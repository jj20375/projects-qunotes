using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Quotes;

public sealed record ConfirmImportRequest(Guid ProjectId, QuoteInput Quote);
public sealed record MappingProfileInput(string? Name, string? SheetName, int? HeaderRow, int? DataStartRow, Dictionary<string, string>? Columns, Dictionary<string, string>? Cells);

/// <summary>
/// 報價匯入（兩階段）：上傳 → 解析成預覽 → 使用者修正 → 確認後建立報價草稿。
/// 解析錯誤不會寫進正式資料；Excel 範本需 <c>quotes:import_excel</c>，舊報價需 <c>quotes:import_legacy</c>，
/// 圖像識別（照片、掃描圖、PDF）需 <c>quotes:import_ocr</c> 且伺服器已設定 Claude 或 Gemini 的金鑰。
/// </summary>
[Route("api")]
public sealed class QuoteImportController(QuoteImportService service, QuoteOcrService ocr) : ScopedControllerBase
{
    private const long MaxFileBytes = 10 * 1024 * 1024;

    [HttpPost("quote-imports")]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    public async Task<ActionResult> Upload([FromForm] IFormFile? file, [FromForm] string? kind, [FromForm] Guid? mappingProfileId, CancellationToken cancellationToken)
    {
        var option = kind switch { ImportKinds.LegacyExcel => "import_legacy", ImportKinds.Ocr => "import_ocr", _ => "import_excel" };
        if (!TryCompany("quotes", "create", out var scope, option)) return Forbidden();
        if (file is null || file.Length == 0) return BadRequest(new { message = "請選擇檔案。" });
        if (file.Length > MaxFileBytes) return BadRequest(new { message = "檔案不可超過 10 MB。" });
        await using var stream = file.OpenReadStream();
        if (kind == ImportKinds.Ocr) return ToResult(await service.UploadOcrAsync(scope, stream, file.FileName, cancellationToken));
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "只支援 .xlsx；舊版 .xls 請先用 Excel 另存為 .xlsx。" });
        return ToResult(await service.UploadAsync(scope, stream, file.FileName, kind ?? ImportKinds.ExcelTemplate, mappingProfileId, cancellationToken));
    }

    /// <summary>前端用來決定要不要顯示「圖像識別」選項，以及告知使用者檔案會送到哪一家。</summary>
    [HttpGet("quote-imports/options")]
    public ActionResult Options() =>
        TryCompany("quotes", "create", out _) ? Ok(new { ocrEnabled = ocr.Enabled, ocrProvider = ocr.Active is { Configured: true } active ? active.Provider : null }) : Forbidden();

    [HttpGet("quote-imports/{jobId:guid}")]
    public async Task<ActionResult> Get(Guid jobId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope) ? ToResult(await service.GetAsync(scope, jobId, cancellationToken)) : Forbidden();

    [HttpPost("quote-imports/{jobId:guid}/confirm")]
    public async Task<ActionResult> Confirm(Guid jobId, ConfirmImportRequest request, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope) ? ToResult(await service.ConfirmAsync(scope, jobId, request, cancellationToken)) : Forbidden();

    [HttpGet("import-mapping-profiles")]
    public async Task<ActionResult> ListProfiles(CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out _, "import_legacy") ? Ok(await service.ListProfilesAsync(cancellationToken)) : Forbidden();

    [HttpPost("import-mapping-profiles")]
    public async Task<ActionResult> CreateProfile(MappingProfileInput input, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope, "import_legacy") ? ToResult(await service.SaveProfileAsync(scope, null, input, cancellationToken)) : Forbidden();

    [HttpPut("import-mapping-profiles/{profileId:guid}")]
    public async Task<ActionResult> UpdateProfile(Guid profileId, MappingProfileInput input, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope, "import_legacy") ? ToResult(await service.SaveProfileAsync(scope, profileId, input, cancellationToken)) : Forbidden();

    [HttpDelete("import-mapping-profiles/{profileId:guid}")]
    public async Task<ActionResult> DeleteProfile(Guid profileId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope, "import_legacy") ? ToResult(await service.DeleteProfileAsync(scope, profileId, cancellationToken)) : Forbidden();
}

/// <summary>匯入工作與舊報價對照設定。</summary>
public sealed class QuoteImportService(QuoteDbContext db, QuoteService quotes, QuoteOcrService ocr, AuditService audit)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ColumnFields = ["ItemCode", "Name", "Spec", "Unit", "Quantity", "UnitPrice", "DiscountRate", "Amount", "UnitCost", "Remark"];
    private static readonly string[] CellFields = ["ClientName", "ClientTaxId", "ClientAddress", "ContactName", "ContactPhone", "ContactEmail", "QuoteDate", "ValidUntil", "Title", "PaymentTerms", "DeliveryTerms", "Terms", "TaxType", "Discount", "SourceQuoteNo"];

    public async Task<ServiceResult<object>> UploadAsync(RequestScope scope, Stream stream, string fileName, string kind, Guid? mappingProfileId, CancellationToken cancellationToken)
    {
        if (kind is not (ImportKinds.ExcelTemplate or ImportKinds.LegacyExcel)) return ServiceResult<object>.BadRequest("匯入類型不正確。");
        MappingProfileSpec? spec = null;
        if (kind == ImportKinds.LegacyExcel && mappingProfileId is Guid profileId)
        {
            var profile = await db.ImportMappingProfiles.AsNoTracking().SingleOrDefaultAsync(value => value.Id == profileId, cancellationToken);
            if (profile is null) return ServiceResult<object>.BadRequest("找不到對照設定。");
            spec = ToSpec(profile);
        }

        var job = new ImportJob { Kind = kind, FileName = Path.GetFileName(fileName), MappingProfileId = spec is null ? null : mappingProfileId, CreatedBy = scope.UserId };
        ParsedQuote? parsed = null;
        try
        {
            // ClosedXML 需要可 seek 的串流。
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            parsed = QuoteExcel.Parse(buffer, spec);
            job.PreviewJson = JsonSerializer.Serialize(parsed, JsonOptions);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or ArgumentException or InvalidOperationException or IOException)
        {
            job.Status = "failed";
            job.ErrorsJson = JsonSerializer.Serialize(new[] { exception is InvalidDataException ? exception.Message : "無法讀取 Excel 檔案，請確認格式是否為 .xlsx。" }, JsonOptions);
        }
        db.ImportJobs.Add(job);
        audit.Add(scope.CompanyId, scope.UserId, "import_job", job.Id, "upload", after: new { job.Kind, job.FileName, job.Status, items = parsed?.Items.Count });
        await db.SaveChangesAsync(cancellationToken);
        return job.Status == "failed"
            ? ServiceResult<object>.BadRequest(JsonSerializer.Deserialize<string[]>(job.ErrorsJson!)![0])
            : ServiceResult<object>.Created(ToDto(job));
    }

    /// <summary>圖像識別：檢查檔頭與大小 → 送 Claude 辨識 → 存成預覽。辨識失敗也留下紀錄（status=failed）。</summary>
    public async Task<ServiceResult<object>> UploadOcrAsync(RequestScope scope, Stream stream, string fileName, CancellationToken cancellationToken)
    {
        // 這次請求固定使用同一家與同一組設定，避免途中設定檔被修改。
        var recognizer = ocr.Active;
        if (recognizer is not { Configured: true }) return ServiceResult<object>.BadRequest("圖像識別尚未啟用，請管理員設定 Quote__AnthropicApiKey 或 Quote__GeminiApiKey。");
        var (model, strength) = recognizer.CurrentSettings;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();
        var mediaType = QuoteOcrService.DetectMediaType(content.AsSpan(0, Math.Min(content.Length, 16)));
        if (mediaType is null) return ServiceResult<object>.BadRequest("只支援 JPG、PNG、WebP、GIF 圖片或 PDF；iPhone 的 HEIC 照片請先轉成 JPG。");
        if (mediaType != "application/pdf" && content.Length > QuoteOcrService.MaxImageBytes) return ServiceResult<object>.BadRequest("圖片不可超過 5 MB，請降低解析度後再上傳。");

        var job = new ImportJob { Kind = ImportKinds.Ocr, FileName = Path.GetFileName(fileName), CreatedBy = scope.UserId };
        var (output, error) = await ocr.RecognizeAsync(recognizer, content, mediaType, cancellationToken);
        ParsedQuote? parsed = output is null ? null : QuoteOcrService.ToParsedQuote(output);
        if (parsed is { Items.Count: 0 }) error = "沒有辨識到任何品項，請確認圖片是報價單且文字清晰。";
        if (error is null) job.PreviewJson = JsonSerializer.Serialize(parsed, JsonOptions);
        else
        {
            job.Status = "failed";
            job.ErrorsJson = JsonSerializer.Serialize(new[] { error }, JsonOptions);
        }
        db.ImportJobs.Add(job);
        audit.Add(scope.CompanyId, scope.UserId, "import_job", job.Id, "upload", after: new { job.Kind, job.FileName, job.Status, mediaType, provider = recognizer.Provider, model, strength, items = parsed?.Items.Count });
        await db.SaveChangesAsync(cancellationToken);
        return error is null ? ServiceResult<object>.Created(ToDto(job)) : ServiceResult<object>.BadRequest(error);
    }

    public async Task<ServiceResult<object>> GetAsync(RequestScope scope, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.AsNoTracking().SingleOrDefaultAsync(value => value.Id == jobId && value.CreatedBy == scope.UserId, cancellationToken);
        return job is null ? ServiceResult<object>.NotFound("找不到匯入工作。") : ServiceResult<object>.Ok(ToDto(job));
    }

    /// <summary>以使用者修正後的內容建立報價草稿；同一份匯入只能確認一次。</summary>
    public async Task<ServiceResult<QuoteCreated>> ConfirmAsync(RequestScope scope, Guid jobId, ConfirmImportRequest request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.SingleOrDefaultAsync(value => value.Id == jobId && value.CreatedBy == scope.UserId, cancellationToken);
        if (job is null) return ServiceResult<QuoteCreated>.NotFound("找不到匯入工作。");
        if (job.Status != "preview") return ServiceResult<QuoteCreated>.BadRequest("這份匯入已經處理過了。");
        var source = job.Kind switch { ImportKinds.LegacyExcel => QuoteSources.LegacyExcel, ImportKinds.Ocr => QuoteSources.Ocr, _ => QuoteSources.ExcelTemplate };
        var created = await quotes.CreateAsync(scope, request.ProjectId, request.Quote, source, job.Id, cancellationToken);
        if (created.Value is not QuoteCreated quote) return created;

        job.Status = "confirmed";
        job.TargetProjectId = request.ProjectId;
        job.CreatedQuoteId = quote.Id;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task<IReadOnlyList<object>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await db.ImportMappingProfiles.AsNoTracking().OrderBy(value => value.Name).ToListAsync(cancellationToken);
        return profiles.Select(value => (object)new { value.Id, value.Name, value.SheetName, value.HeaderRow, value.DataStartRow, columns = ToSpec(value).Columns, cells = ToSpec(value).Cells }).ToList();
    }

    public async Task<ServiceResult<object>> SaveProfileAsync(RequestScope scope, Guid? profileId, MappingProfileInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return ServiceResult<object>.BadRequest("請輸入設定名稱。");
        var columns = (input.Columns ?? []).Where(value => !string.IsNullOrWhiteSpace(value.Value)).ToDictionary(value => value.Key, value => value.Value.Trim().ToUpperInvariant());
        var cells = (input.Cells ?? []).Where(value => !string.IsNullOrWhiteSpace(value.Value)).ToDictionary(value => value.Key, value => value.Value.Trim().ToUpperInvariant());
        if (!columns.ContainsKey("Name")) return ServiceResult<object>.BadRequest("至少要指定品名欄。");
        if (columns.Keys.Any(key => !ColumnFields.Contains(key)) || columns.Values.Any(value => !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Z]{1,3}$"))) return ServiceResult<object>.BadRequest("欄位對照必須是欄字母，例如 B。");
        if (cells.Keys.Any(key => !CellFields.Contains(key)) || cells.Values.Any(value => !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Z]{1,3}[1-9][0-9]{0,5}$"))) return ServiceResult<object>.BadRequest("表頭對照必須是儲存格位置，例如 C3。");
        var headerRow = input.HeaderRow ?? 1;
        var dataStartRow = input.DataStartRow ?? headerRow + 1;
        if (headerRow < 1 || dataStartRow <= headerRow) return ServiceResult<object>.BadRequest("資料起始列必須在表頭列之後。");

        var name = input.Name.Trim();
        if (await db.ImportMappingProfiles.AnyAsync(value => value.Name == name && value.Id != profileId, cancellationToken)) return ServiceResult<object>.Conflict("已有同名的對照設定。");
        var profile = profileId is Guid id ? await db.ImportMappingProfiles.SingleOrDefaultAsync(value => value.Id == id, cancellationToken) : null;
        if (profileId is not null && profile is null) return ServiceResult<object>.NotFound("找不到對照設定。");
        if (profile is null)
        {
            profile = new ImportMappingProfile { CreatedBy = scope.UserId };
            db.ImportMappingProfiles.Add(profile);
        }
        profile.Name = name;
        profile.SheetName = string.IsNullOrWhiteSpace(input.SheetName) ? null : input.SheetName.Trim();
        profile.HeaderRow = headerRow;
        profile.DataStartRow = dataStartRow;
        profile.ColumnMapJson = JsonSerializer.Serialize(columns);
        profile.CellMapJson = JsonSerializer.Serialize(cells);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "import_mapping_profile", profile.Id, profileId is null ? "create" : "update", after: new { name, columns, cells });
        await db.SaveChangesAsync(cancellationToken);
        return profileId is null ? ServiceResult<object>.Created(new { profile.Id }) : ServiceResult<object>.Ok(new { profile.Id });
    }

    public async Task<ServiceResult<Empty>> DeleteProfileAsync(RequestScope scope, Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await db.ImportMappingProfiles.SingleOrDefaultAsync(value => value.Id == profileId, cancellationToken);
        if (profile is null) return ServiceResult<Empty>.NotFound("找不到對照設定。");
        db.ImportMappingProfiles.Remove(profile);
        audit.Add(scope.CompanyId, scope.UserId, "import_mapping_profile", profileId, "delete", new { profile.Name });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    private static MappingProfileSpec ToSpec(ImportMappingProfile profile) => new(
        profile.SheetName,
        profile.HeaderRow,
        profile.DataStartRow,
        JsonSerializer.Deserialize<Dictionary<string, string>>(profile.ColumnMapJson) ?? [],
        JsonSerializer.Deserialize<Dictionary<string, string>>(profile.CellMapJson) ?? []);

    private static object ToDto(ImportJob job) => new
    {
        job.Id,
        job.Kind,
        job.FileName,
        job.Status,
        job.CreatedQuoteId,
        preview = job.PreviewJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(job.PreviewJson),
    };
}
