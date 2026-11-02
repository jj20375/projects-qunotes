using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Dashboard;

/// <summary>戰情 Dashboard（company scope，需 dashboard:read）。</summary>
[Route("api/dashboard")]
public sealed class DashboardController(DashboardService service) : ScopedControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult> Summary([FromQuery] DashboardQuery query, CancellationToken cancellationToken) =>
        TryCompany("dashboard", "read", out var scope) ? ToResult(await service.SummaryAsync(scope, query, cancellationToken)) : Forbidden();
}
