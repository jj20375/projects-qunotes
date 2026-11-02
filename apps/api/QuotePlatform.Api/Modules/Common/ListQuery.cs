namespace QuotePlatform.Api.Modules.Common;

/// <summary>
/// 一般列表的查詢條件：關鍵字與分頁。所有列表的搜尋、篩選、排序、分頁都由後端處理，前端只送參數、不自行過濾。
/// </summary>
public sealed record ListQuery(string? Keyword, int? Page, int? PageSize)
{
    public string? KeywordText => string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim();
    public int PageSizeValue => Math.Clamp(PageSize ?? 20, 1, 200);
    public int PageValue => Math.Max(Page ?? 1, 1);
    public int Skip => (PageValue - 1) * PageSizeValue;
}
