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
}
