using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Quotes;

/// <summary>報價單 API（company scope）。報價看不看得到、能不能改，完全由所屬專案的資料範圍決定。</summary>
public sealed record TermsNormalizeRequest(string? Text);

[Route("api")]
public sealed class QuotesController(QuoteService service, QuoteDocumentService documents) : ScopedControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly string[] Actions = ["submit", "approve", "return", "send", "accept", "reject"];

    /// <summary>把貼上的條列文字拆成條款清單（編輯畫面貼上時即時拆分；儲存時後端也會用同一套規則）。</summary>
    [HttpPost("quote-terms/normalize")]
    public ActionResult NormalizeTerms(TermsNormalizeRequest request) =>
        RequestScope.TryRead(User, out var scope) && scope.IsCompany ? Ok(new { items = QuoteTerms.Split(request.Text) }) : Forbidden();

    [HttpGet("quotes")]
    public async Task<ActionResult> Search([FromQuery] QuoteSearchQuery query, CancellationToken cancellationToken) =>
        TryCompany("quotes", "read", out var scope) ? ToResult(await service.SearchAsync(scope, query, cancellationToken)) : Forbidden();

    /// <summary>專案底下的全部報價（含舊版本），依單號、版次排序；關鍵字搜尋與分頁（keyword、page、pageSize）由後端處理。</summary>
    [HttpGet("projects/{projectId:guid}/quotes")]
    public async Task<ActionResult> ListByProject(Guid projectId, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("quotes", "read", out var scope)
            ? ToResult(await service.ListByProjectAsync(scope, projectId, new QuoteSearchQuery(query.KeywordText, null, projectId, null, null, null, null, null, false, query.PageValue, query.PageSizeValue, "quoteNo"), cancellationToken))
            : Forbidden();

    [HttpPost("projects/{projectId:guid}/quotes")]
    public async Task<ActionResult> Create(Guid projectId, QuoteInput input, CancellationToken cancellationToken) =>
        TryCompany("quotes", "create", out var scope) ? ToResult(await service.CreateAsync(scope, projectId, input, QuoteSources.Manual, null, cancellationToken)) : Forbidden();

    [HttpGet("quotes/{quoteId:guid}")]
    public async Task<ActionResult> Get(Guid quoteId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "read", out var scope) ? ToResult(await service.GetAsync(scope, quoteId, cancellationToken)) : Forbidden();

    [HttpPut("quotes/{quoteId:guid}")]
    public async Task<ActionResult> Update(Guid quoteId, QuoteInput input, CancellationToken cancellationToken) =>
        TryCompany("quotes", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, quoteId, input, cancellationToken)) : Forbidden();

    [HttpDelete("quotes/{quoteId:guid}")]
    public async Task<ActionResult> Delete(Guid quoteId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, quoteId, cancellationToken)) : Forbidden();

    /// <summary>狀態動作：submit、approve、return、send、accept、reject；return 與 reject 需附原因。</summary>
    [HttpPost("quotes/{quoteId:guid}/{operation:regex(^(submit|approve|return|send|accept|reject)$)}")]
    public async Task<ActionResult> Transition(Guid quoteId, string operation, [FromBody] QuoteReasonRequest? request, CancellationToken cancellationToken) =>
        Actions.Contains(operation) && TryCompany("quotes", "update", out var scope)
            ? ToResult(await service.TransitionAsync(scope, quoteId, operation, request?.Reason, cancellationToken))
            : Forbidden();

    [HttpPost("quotes/{quoteId:guid}/revise")]
    public async Task<ActionResult> Revise(Guid quoteId, CancellationToken cancellationToken) =>
        TryCompany("quotes", "update", out var scope) ? ToResult(await service.ReviseAsync(scope, quoteId, cancellationToken)) : Forbidden();

    [HttpGet("quotes/{quoteId:guid}/pdf")]
    public async Task<ActionResult> Pdf(Guid quoteId, CancellationToken cancellationToken)
    {
        if (!TryCompany("quotes", "read", out var scope, "export_pdf")) return Forbidden();
        var file = await documents.PdfAsync(scope, quoteId, cancellationToken);
        return file is null ? NotFound(new { message = "找不到報價單。" }) : File(file.Value.Bytes, "application/pdf", file.Value.FileName);
    }

    [HttpGet("quotes/{quoteId:guid}/excel")]
    public async Task<ActionResult> Excel(Guid quoteId, CancellationToken cancellationToken)
    {
        if (!TryCompany("quotes", "read", out var scope, "export_excel")) return Forbidden();
        var file = await documents.ExcelAsync(scope, quoteId, cancellationToken);
        return file is null ? NotFound(new { message = "找不到報價單。" }) : File(file.Value.Bytes, XlsxContentType, file.Value.FileName);
    }

    /// <summary>空白匯入範本，與匯出版面相同。</summary>
    [HttpGet("quotes/import-template")]
    public async Task<ActionResult> Template(CancellationToken cancellationToken)
    {
        if (!TryCompany("quotes", "create", out var scope, "import_excel")) return Forbidden();
        return File(await documents.TemplateAsync(scope, cancellationToken), XlsxContentType, "報價匯入範本.xlsx");
    }
}

/// <summary>報價 PDF／Excel 輸出；可見性沿用 <see cref="QuoteService.LoadAsync"/>。</summary>
public sealed class QuoteDocumentService(QuoteDbContext db, QuoteService quotes, CompanyLogoService logos)
{
    public async Task<(byte[] Bytes, string FileName)?> PdfAsync(RequestScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        var context = await quotes.LoadAsync(scope, quoteId, track: false, cancellationToken);
        if (context is null) return null;
        var company = await db.Companies.AsNoTracking().SingleAsync(value => value.Id == scope.CompanyId, cancellationToken);
        var logo = await logos.GetBytesAsync(company.LogoFileId, cancellationToken);
        var bytes = QuotePdf.Render(new QuotePdfCompany(company.Name, company.TaxId, company.Phone, company.Address, company.EnglishName, logo), context.Quote);
        return (bytes, $"{FileStem(context.Quote)}.pdf");
    }

    public async Task<(byte[] Bytes, string FileName)?> ExcelAsync(RequestScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        var context = await quotes.LoadAsync(scope, quoteId, track: false, cancellationToken);
        if (context is null) return null;
        var companyName = await db.Companies.AsNoTracking().Where(value => value.Id == scope.CompanyId).Select(value => value.Name).SingleAsync(cancellationToken);
        return (QuoteExcel.Write(companyName, context.Quote, scope.HasOption("quotes", "view_cost")), $"{FileStem(context.Quote)}.xlsx");
    }

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
