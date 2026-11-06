using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Dashboard;

/// <summary>
/// 營運戰情 Dashboard 控制器（公司租戶範圍，需具備 dashboard:read 權限）。
/// 數據統計嚴格套用資料範圍過濾；金額欄位受 dashboard:view_amount 權限保護。
/// </summary>
/// <param name="service">戰情統計領域服務實體。</param>
[Route("api/dashboard")]
public sealed class DashboardController(DashboardService service) : ScopedControllerBase
{
    /// <summary>
    /// 取得指定會計年度與衡量口徑之營運戰情彙整指標與趨勢圖表數據。
    /// </summary>
    /// <param name="query">戰情統計查詢條件（年度、指標口徑、部門與業務篩選）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 Dashboard 綜合統計資料物件。</returns>
    [HttpGet("summary")]
    public async Task<ActionResult> Summary([FromQuery] DashboardQuery query, CancellationToken cancellationToken) =>
        TryCompany("dashboard", "read", out var scope) ? ToResult(await service.SummaryAsync(scope, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 取得應收帳款（已開立且未收齊的發票）分頁清單；需另具備 invoices:read。
    /// </summary>
    /// <param name="filter">與 Summary 相同的部門、負責業務篩選。</param>
    /// <param name="list">關鍵字（發票號碼、專案、客戶）與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 { total, page, pageSize, items }。</returns>
    [HttpGet("receivables")]
    public async Task<ActionResult> Receivables([FromQuery] DashboardQuery filter, [FromQuery] ListQuery list, CancellationToken cancellationToken) =>
        TryCompany("dashboard", "read", out var scope) && scope.Has("invoices", "read") ? ToResult(await service.ReceivablesPageAsync(scope, filter, list, cancellationToken)) : Forbidden();

    /// <summary>
    /// 取得 90 天內到期保固的分頁清單；需另具備 warranties:read。
    /// </summary>
    /// <param name="filter">與 Summary 相同的部門、負責業務篩選。</param>
    /// <param name="list">關鍵字（保固主旨、編號、專案、客戶）與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 { total, page, pageSize, items }。</returns>
    [HttpGet("expiring-warranties")]
    public async Task<ActionResult> ExpiringWarranties([FromQuery] DashboardQuery filter, [FromQuery] ListQuery list, CancellationToken cancellationToken) =>
        TryCompany("dashboard", "read", out var scope) && scope.Has("warranties", "read") ? ToResult(await service.ExpiringWarrantiesPageAsync(scope, filter, list, cancellationToken)) : Forbidden();
}
