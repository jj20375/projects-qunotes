using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>
/// 虛擬辦公室 REST：部門辦公室列表、公司上線狀態快照與主題設定。
/// 走動、聊天等即時互動走 <see cref="OfficeHub"/>。
/// </summary>
[Route("api/office")]
public sealed class OfficeController(OfficeService service, PresenceTracker tracker) : ScopedControllerBase
{
    /// <summary>公司內所有部門辦公室與成員數、在線人數、在場人數。</summary>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>200 與 <see cref="OfficeRoomSummary"/> 陣列；沒有 <c>office:read</c> 權限回 403。</returns>
    [HttpGet("rooms")]
    public async Task<ActionResult> Rooms(CancellationToken cancellationToken) =>
        TryCompany("office", "read", out var scope) ? Ok(await service.RoomsAsync(scope.CompanyId!.Value, cancellationToken)) : Forbidden();

    /// <summary>公司內目前上線與閒置的成員（以 membershipId 為鍵的對照用，不分頁）。</summary>
    /// <returns>200 與 <see cref="PresenceEntry"/> 陣列；沒有 <c>office:read</c> 權限回 403。</returns>
    [HttpGet("presence")]
    public ActionResult Presence() =>
        TryCompany("office", "read", out var scope) ? Ok(tracker.Snapshot(scope.CompanyId!.Value)) : Forbidden();

    /// <summary>設定部門辦公室主題；需要部門管理的編輯權限。</summary>
    /// <param name="departmentId">要設定的部門。</param>
    /// <param name="request">主題內容。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>204；主題不正確回 400，部門不存在回 404，沒有 <c>company.departments:update</c> 權限回 403。</returns>
    [HttpPut("rooms/{departmentId:guid}/theme")]
    public async Task<ActionResult> UpdateTheme(Guid departmentId, UpdateRoomThemeRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.departments", "update", out var scope) ? ToResult(await service.UpdateThemeAsync(scope.CompanyId!.Value, departmentId, request.Theme, cancellationToken)) : Forbidden();
}
