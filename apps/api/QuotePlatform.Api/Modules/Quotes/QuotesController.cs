using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>
/// 條款文字正規化請求契約。
/// </summary>
/// <param name="Text">待拆分之條款多行文字。</param>
public sealed record TermsNormalizeRequest(string? Text);

/// <summary>
/// 報價單管理控制器（公司租戶範圍）。
/// 報價單可見性與修改權限嚴格繼承所屬專案之資料範圍。
/// </summary>
/// <param name="service">報價核心領域服務實體。</param>
/// <param name="documents">報價文件產生服務實體（PDF、Excel）。</param>
[Route("api")]
public sealed class QuotesController(QuoteService service, QuoteDocumentService documents) : ScopedControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly string[] Actions = ["submit", "approve", "return", "send", "accept", "reject"];

    /// <summary>
    /// 把貼上的條列文字即時正規化拆分為條款字串清單。
    /// </summary>
    /// <param name="request">條款文字輸入內容。</param>
    /// <returns>傳回拆分後之項目清單。</returns>
    [HttpPost("quote-terms/normalize")]
    public ActionResult NormalizeTerms(TermsNormalizeRequest request) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? Ok(new { items = QuoteTerms.Split(request.Text) }) : Forbidden();

    /// <summary>
    /// 條件搜尋跨專案之報價單清單。
    /// </summary>
    /// <param name="query">報價單複合查詢條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁報價單列表與筆數合計。</returns>
    [HttpGet("quotes")]
    public async Task<ActionResult> Search([FromQuery] QuoteSearchQuery query, CancellationToken cancellationToken) =>
        TryCompany("quotes", "read", out var scope) ? ToResult(await service.SearchAsync(scope, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 查詢指定專案底下的全部報價單（含歷史版本），依單號與版次排序。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="query">通用列表分頁與關鍵字條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁報價列表。</returns>
    [HttpGet("projects/{projectId:guid}/quotes")]
    public async Task<ActionResult> ListByProject(Guid projectId, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("quotes", "read", out var scope)
            ? ToResult(await service.ListByProjectAsync(scope, projectId, new QuoteSearchQuery(query.KeywordText, null, projectId, null, null, null, null, null, false, query.PageValue, query.PageSizeValue, "quoteNo"), cancellationToken))
            : Forbidden();

    /// <summary>
    /// 於指定專案建立新報價單（初始為草稿狀態）。
    /// </summary>
    /// <param name="projectId">專案唯一識別碼。</param>
    /// <param name="input">報價單表頭與品項輸入內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立完成之報價單 ID 與單號。</returns>
    [HttpPost("projects/{projectId:guid}/quotes")]
    public async Task<ActionResult> Create(Guid projectId, QuoteInput input, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope) ? ToResult(await service.CreateAsync(scope, projectId, input, QuoteSources.Manual, null, cancellationToken)) : Forbidden();

    /// <summary>
    /// 取得單一報價單之詳細內容（含品項、版本歷史與金額小計）。
    /// </summary>
    /// <param name="quoteId">報價單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回報價單詳細主檔與明細。</returns>
    [HttpGet("quotes/{quoteId:guid}")]
    public async Task<ActionResult> Get(Guid quoteId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "read", out var scope) ? ToResult(await service.GetAsync(scope, quoteId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新草稿狀態之報價單內容。
    /// </summary>
    /// <param name="quoteId">報價單唯一識別碼。</param>
    /// <param name="input">報價單表頭與品項更新內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPut("quotes/{quoteId:guid}")]
    public async Task<ActionResult> Update(Guid quoteId, QuoteInput input, CancellationToken cancellationToken) =>
        TryCompany("quotes", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, quoteId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除指定之草稿報價單。
    /// </summary>
    /// <param name="quoteId">報價單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("quotes/{quoteId:guid}")]
    public async Task<ActionResult> Delete(Guid quoteId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, quoteId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 執行報價單業務狀態流轉動作（submit、approve、return、send、accept、reject）。
    /// </summary>
    /// <param name="quoteId">報價單唯一識別碼。</param>
    /// <param name="operation">流轉動作名稱。</param>
    /// <param name="request">退回或拒絕之原因備註內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回狀態流轉後之報價單主檔。</returns>
    [HttpPost("quotes/{quoteId:guid}/{operation:regex(^(submit|approve|return|send|accept|reject)$)}")]
    public async Task<ActionResult> Transition(Guid quoteId, string operation, [FromBody] QuoteReasonRequest? request, CancellationToken cancellationToken) =>
        Actions.Contains(operation) && TryCompany("quotes", "update", out var scope)
            ? ToResult(await service.TransitionAsync(scope, quoteId, operation, request?.Reason, cancellationToken))
            : Forbidden();

    /// <summary>
    /// 建立既有報價單之新修訂版次（Revision + 1）。
    /// </summary>
    /// <param name="quoteId">被改版之原報價單 ID。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新版次之報價單 ID 與版次編號。</returns>
    [HttpPost("quotes/{quoteId:guid}/revise")]
    public async Task<ActionResult> Revise(Guid quoteId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "update", out var scope) ? ToResult(await service.ReviseAsync(scope, quoteId, cancellationToken)) : Forbidden();

    /// <summary>
    /// 輸出報價單 PDF 文件二進位檔案。
    /// </summary>
    /// <param name="quoteId">報價單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 PDF 二進位串流檔案。</returns>
    [HttpGet("quotes/{quoteId:guid}/pdf")]
    public async Task<ActionResult> Pdf(Guid quoteId, CancellationToken cancellationToken)
    {
        if (!TryCompany("quotes", "read", out var scope, "export_pdf")) return Forbidden();
        var file = await documents.PdfAsync(scope, quoteId, cancellationToken);
        return file is null ? NotFound(new { message = "找不到報價單。" }) : File(file.Value.Bytes, "application/pdf", file.Value.FileName);
    }

    /// <summary>
    /// 匯出報價單 Excel 試算表檔案。
    /// </summary>
    /// <param name="quoteId">報價單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 Excel 試算表二進位串流檔案。</returns>
    [HttpGet("quotes/{quoteId:guid}/excel")]
    public async Task<ActionResult> Excel(Guid quoteId, CancellationToken cancellationToken)
    {
        if (!TryCompany("quotes", "read", out var scope, "export_excel")) return Forbidden();
        var file = await documents.ExcelAsync(scope, quoteId, cancellationToken);
        return file is null ? NotFound(new { message = "找不到報價單。" }) : File(file.Value.Bytes, XlsxContentType, file.Value.FileName);
    }

    /// <summary>
    /// 下載標準空白報價匯入範本 Excel 檔案。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回空白範本 Excel 檔案。</returns>
    [HttpGet("quotes/import-template")]
    public async Task<ActionResult> Template(CancellationToken cancellationToken)
    {
        if (!TryCompany("quotes", "create", out var scope, "import_excel")) return Forbidden();
        return File(await documents.TemplateAsync(scope, cancellationToken), XlsxContentType, "報價匯入範本.xlsx");
    }
}

/// <summary>
/// 報價 PDF 與 Excel 文件產生服務。
/// 可見性嚴格沿用 <see cref="QuoteService.LoadAsync"/> 之資料範圍檢查。
/// </summary>
/// <param name="db">資料庫上下文實體。</param>
/// <param name="quotes">報價核心領域服務實體。</param>
/// <param name="logos">公司 Logo 快取服務實體。</param>
public sealed class QuoteDocumentService(QuoteDbContext db, QuoteService quotes, CompanyLogoService logos)
{
    /// <summary>
    /// 產生報價單 PDF 二進位資料與建議檔名。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="quoteId">報價單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>若存在且具讀取權限則傳回檔案二進位位元組與檔名，否則傳回 null。</returns>
    public async Task<(byte[] Bytes, string FileName)?> PdfAsync(RequestScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        var context = await quotes.LoadAsync(scope, quoteId, track: false, cancellationToken);
        if (context is null) return null;
        var company = await db.Companies.AsNoTracking().SingleAsync(value => value.Id == scope.CompanyId, cancellationToken);
        var logo = await logos.GetBytesAsync(company.LogoFileId, cancellationToken);
        var bytes = QuotePdf.Render(new QuotePdfCompany(company.Name, company.TaxId, company.Phone, company.Address, company.EnglishName, logo), context.Quote);
        return (bytes, $"{FileStem(context.Quote)}.pdf");
    }

    /// <summary>
    /// 產生報價單 Excel 試算表二進位資料與建議檔名。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="quoteId">報價單唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>若存在且具讀取權限則傳回檔案二進位位元組與檔名，否則傳回 null。</returns>
    public async Task<(byte[] Bytes, string FileName)?> ExcelAsync(RequestScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        var context = await quotes.LoadAsync(scope, quoteId, track: false, cancellationToken);
        if (context is null) return null;
        var companyName = await db.Companies.AsNoTracking().Where(value => value.Id == scope.CompanyId).Select(value => value.Name).SingleAsync(cancellationToken);
        return (QuoteExcel.Write(companyName, context.Quote, scope.HasOption("quotes", "view_cost")), $"{FileStem(context.Quote)}.xlsx");
    }

    /// <summary>
    /// 產製空白之報價單匯入範本 Excel 試算表。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回範本 Excel 檔案二進位內容。</returns>
    public async Task<byte[]> TemplateAsync(RequestScope scope, CancellationToken cancellationToken)
    {
        var companyName = await db.Companies.AsNoTracking().Where(value => value.Id == scope.CompanyId).Select(value => value.Name).SingleAsync(cancellationToken);
        return QuoteExcel.Write(companyName, null, includeCost: scope.HasOption("quotes", "view_cost"));
    }

    /// <summary>供 <c>--self-test</c> 確認 QuestPDF 原生元件與中文字型可正常輸出。</summary>
    public static void AssertPdfRenders()
    {
        var quote = new Quote { QuoteNo = "Q202610-0001", ClientName = "測試客戶", QuoteDate = new DateOnly(2026, 10, 29), Items = [new QuoteItem { Name = "伺服器", Quantity = 1, UnitPrice = 1000 }] };
        QuoteCalculator.Apply(quote);
        var bytes = QuotePdf.Render(new QuotePdfCompany("示範公司", "12345678", null, null, "Demo Co., Ltd.", [1, 2, 3]), quote);
        if (bytes.Length < 1000 || bytes[0] != '%' || bytes[1] != 'P') throw new InvalidOperationException("報價 PDF 產生失敗。");
        if (QuotePdf.CountPages(bytes) != 1) throw new InvalidOperationException("報價 PDF 頁數計算不正確。");
        // 60 列明細一定超過一頁：頁數計算要大於 1（緊湊版也排不進一頁）。
        var longQuote = new Quote { QuoteNo = "Q202610-0002", ClientName = "測試客戶", QuoteDate = new DateOnly(2026, 10, 29), Terms = "甲\n乙", Items = [.. Enumerable.Range(1, 60).Select(i => new QuoteItem { SortOrder = i, Name = $"項目 {i}", Quantity = 1, UnitPrice = 100 })] };
        QuoteCalculator.Apply(longQuote);
        if (QuotePdf.CountPages(QuotePdf.Render(new QuotePdfCompany("示範公司", null, null, null), longQuote)) < 2) throw new InvalidOperationException("長報價應該超過一頁。");
    }

    private static string FileStem(Quote quote) =>
        $"報價單_{(quote.RevisionNo > 0 ? $"{quote.QuoteNo}-R{quote.RevisionNo}" : quote.QuoteNo)}_{string.Concat(quote.ClientName.Split(Path.GetInvalidFileNameChars()))}";
}
