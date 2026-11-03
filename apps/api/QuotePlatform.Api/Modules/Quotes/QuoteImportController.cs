using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>
/// 確認匯入結果並正式產生報價單草稿請求契約。
/// </summary>
/// <param name="ProjectId">目標歸屬專案識別碼。</param>
/// <param name="Quote">確認後之報價單表頭與品項明細資料。</param>
public sealed record ConfirmImportRequest(Guid ProjectId, QuoteInput Quote);

/// <summary>
/// 自訂 Excel 欄位與儲存格對照範本輸入契約。
/// </summary>
/// <param name="Name">對照範本名稱。</param>
/// <param name="SheetName">指定工作表名稱（選填）。</param>
/// <param name="HeaderRow">表頭所在列號。</param>
/// <param name="DataStartRow">品項資料起始列號。</param>
/// <param name="Columns">品項明細欄位對照字典（欄位名對映 Excel 欄位字母）。</param>
/// <param name="Cells">表頭儲存格對照字典（欄位名對映儲存格位置如 C3）。</param>
public sealed record MappingProfileInput(string? Name, string? SheetName, int? HeaderRow, int? DataStartRow, Dictionary<string, string>? Columns, Dictionary<string, string>? Cells);

/// <summary>
/// 報價試算表匯入與 OCR 圖文辨識管理控制器。
/// 流程採兩階段設計：上傳檔案解析成預覽 → 畫面檢視修正 → 確認後正式建立報價單草稿。
/// </summary>
/// <param name="service">報價匯入解析業務服務實體。</param>
/// <param name="ocr">報價 OCR 圖文辨識服務實體。</param>
[Route("api")]
public sealed class QuoteImportController(QuoteImportService service, QuoteOcrService ocr) : ScopedControllerBase
{
    private const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>
    /// 上傳試算表或掃描圖片並啟動第一階段解析，產生預覽工作資料。
    /// </summary>
    /// <param name="file">上傳之檔案物件（.xlsx 或 圖片/PDF）。</param>
    /// <param name="kind">匯入類型（excel_template、legacy_excel、ocr）。</param>
    /// <param name="mappingProfileId">自訂對照範本 ID（legacy_excel 專用）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回匯入預覽工作物件與解析結果。</returns>
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

    /// <summary>
    /// 查詢後端 OCR 圖文識別組態設定與啟用狀態。
    /// </summary>
    /// <returns>傳回 OCR 是否啟用與目前作用中之模型提供者（claude 或 gemini）。</returns>
    [HttpGet("quote-imports/options")]
    public ActionResult Options() =>
        TryCompany("quotes", "create", out _) ? Ok(new { ocrEnabled = ocr.Enabled, ocrProvider = ocr.Active is { Configured: true } active ? active.Provider : null }) : Forbidden();

    /// <summary>
    /// 取得指定匯入工作階段之預覽資料與解析明細。
    /// </summary>
    /// <param name="jobId">匯入工作識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回匯入工作狀態與預覽結構。</returns>
    [HttpGet("quote-imports/{jobId:guid}")]
    public async Task<ActionResult> Get(Guid jobId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope) ? ToResult(await service.GetAsync(scope, jobId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 第二階段：確認預覽修正結果並正式於目標專案建立報價單草稿。
    /// </summary>
    /// <param name="jobId">匯入工作識別碼。</param>
    /// <param name="request">確認輸入資料與目標專案 ID。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立成功之報價單 ID 與單號。</returns>
    [HttpPost("quote-imports/{jobId:guid}/confirm")]
    public async Task<ActionResult> Confirm(Guid jobId, ConfirmImportRequest request, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope) ? ToResult(await service.ConfirmAsync(scope, jobId, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 查詢當前公司設定之自訂 Excel 對照範本清單。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回對照範本陣列。</returns>
    [HttpGet("import-mapping-profiles")]
    public async Task<ActionResult> ListProfiles(CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out _, "import_legacy") ? Ok(await service.ListProfilesAsync(cancellationToken)) : Forbidden();

    /// <summary>
    /// 建立自訂 Excel 對照範本。
    /// </summary>
    /// <param name="input">對照範本資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立結果。</returns>
    [HttpPost("import-mapping-profiles")]
    public async Task<ActionResult> CreateProfile(MappingProfileInput input, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope, "import_legacy") ? ToResult(await service.SaveProfileAsync(scope, null, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新自訂 Excel 對照範本。
    /// </summary>
    /// <param name="profileId">對照範本識別碼。</param>
    /// <param name="input">更新內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPut("import-mapping-profiles/{profileId:guid}")]
    public async Task<ActionResult> UpdateProfile(Guid profileId, MappingProfileInput input, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope, "import_legacy") ? ToResult(await service.SaveProfileAsync(scope, profileId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除自訂 Excel 對照範本。
    /// </summary>
    /// <param name="profileId">對照範本識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("import-mapping-profiles/{profileId:guid}")]
    public async Task<ActionResult> DeleteProfile(Guid profileId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope, "import_legacy") ? ToResult(await service.DeleteProfileAsync(scope, profileId, cancellationToken)) : Forbidden();
}

/// <summary>
/// 報價匯入解析工作階段與舊報價對照範本管理領域服務。
/// </summary>
/// <param name="db">資料庫上下文實體。</param>
/// <param name="quotes">報價核心領域服務實體。</param>
/// <param name="ocr">OCR 圖文識別服務實體。</param>
/// <param name="audit">系統稽核日誌服務實體。</param>
public sealed class QuoteImportService(QuoteDbContext db, QuoteService quotes, QuoteOcrService ocr, AuditService audit)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ColumnFields = ["ItemCode", "Name", "Spec", "Unit", "Quantity", "UnitPrice", "DiscountRate", "Amount", "UnitCost", "Remark"];
    private static readonly string[] CellFields = ["ClientName", "ClientTaxId", "ClientAddress", "ContactName", "ContactPhone", "ContactEmail", "QuoteDate", "ValidUntil", "Title", "PaymentTerms", "DeliveryTerms", "Terms", "TaxType", "Discount", "SourceQuoteNo"];

    /// <summary>
    /// 處理上傳之 Excel 試算表串流並轉換為預覽結構。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="stream">檔案二進位串流。</param>
    /// <param name="fileName">原始檔案名稱。</param>
    /// <param name="kind">匯入類型。</param>
    /// <param name="mappingProfileId">對照範本 ID。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立之匯入工作 DTO 結果。</returns>
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

    /// <summary>
    /// 處理上傳之照片、圖片或 PDF 並執行 AI OCR 報價辨識。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="stream">檔案二進位串流。</param>
    /// <param name="fileName">原始檔案名稱。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 OCR 辨識匯入工作 DTO 結果。</returns>
    public async Task<ServiceResult<object>> UploadOcrAsync(RequestScope scope, Stream stream, string fileName, CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// 取得匯入工作詳細預覽內容。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="jobId">匯入工作識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回匯入工作狀態物件。</returns>
    public async Task<ServiceResult<object>> GetAsync(RequestScope scope, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.AsNoTracking().SingleOrDefaultAsync(value => value.Id == jobId && value.CreatedBy == scope.UserId, cancellationToken);
        return job is null ? ServiceResult<object>.NotFound("找不到匯入工作。") : ServiceResult<object>.Ok(ToDto(job));
    }

    /// <summary>
    /// 確認匯入結果並正式建立報價單草稿。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="jobId">匯入工作識別碼。</param>
    /// <param name="request">確認內容契約。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立成功之報價單資料。</returns>
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

    /// <summary>
    /// 查詢所有自訂 Excel 對照範本。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回範本清單。</returns>
    public async Task<IReadOnlyList<object>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await db.ImportMappingProfiles.AsNoTracking().OrderBy(value => value.Name).ToListAsync(cancellationToken);
        return profiles.Select(value => (object)new { value.Id, value.Name, value.SheetName, value.HeaderRow, value.DataStartRow, columns = ToSpec(value).Columns, cells = ToSpec(value).Cells }).ToList();
    }

    /// <summary>
    /// 新增或儲存自訂 Excel 對照範本。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="profileId">範本 ID（新增時為 null）。</param>
    /// <param name="input">對照設定輸入資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回儲存後結果。</returns>
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

    /// <summary>
    /// 刪除自訂 Excel 對照範本。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="profileId">範本唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
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
