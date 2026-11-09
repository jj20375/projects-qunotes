using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Notifications;

/// <summary>站內通知的查詢條件。</summary>
/// <param name="Unread">true 只看未讀；預設全部。</param>
/// <param name="Page">頁碼（預設 1）。</param>
/// <param name="PageSize">每頁筆數（預設 10，最多 50）。</param>
public sealed record NotificationQuery(bool? Unread, int? Page, int? PageSize);

/// <summary>
/// 站內通知控制器（公司範圍）。通知內容依跟進紀錄權限與專案資料範圍過濾，只能存取自己的通知。
/// </summary>
/// <param name="service">通知業務服務。</param>
[Route("api/notifications")]
public sealed class NotificationsController(NotificationService service) : ScopedControllerBase
{
    /// <summary>
    /// 查詢自己的通知。
    /// </summary>
    /// <param name="query">只看未讀與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 { total, unreadCount, page, pageSize, items }。</returns>
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] NotificationQuery query, CancellationToken cancellationToken) =>
        TryCompany("followups", "read", out var scope) ? ToResult(await service.ListAsync(scope, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 把一則通知標為已讀。
    /// </summary>
    /// <param name="id">通知識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>204；不是自己的通知回 404。</returns>
    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        TryCompany("followups", "read", out var scope) ? ToResult(await service.MarkReadAsync(scope, id, cancellationToken)) : Forbidden();

    /// <summary>
    /// 全部標為已讀。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>204。</returns>
    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllRead(CancellationToken cancellationToken) =>
        TryCompany("followups", "read", out var scope) ? ToResult(await service.MarkAllReadAsync(scope, cancellationToken)) : Forbidden();
}
