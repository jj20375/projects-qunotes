using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;
using QuotePlatform.Api.Modules.FollowUps;

namespace QuotePlatform.Api.Modules.Notifications;

/// <summary>站內通知的查詢與已讀；使用者只能看到與操作自己的通知。</summary>
/// <remarks>
/// 列表只包含「收件人目前看得到的專案」的通知（經 <see cref="ProjectAccessService"/>），
/// 資料範圍或專案成員異動後，不再看得到的專案不會從通知洩漏名稱。
/// </remarks>
public sealed class NotificationService(QuoteDbContext db, ProjectAccessService access)
{
    /// <summary>
    /// 查詢目前使用者的通知。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="query">只看未讀與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ total, unreadCount, page, pageSize, items }</c>；未讀排前面，其次依產生時間新到舊。</returns>
    public async Task<ServiceResult<object>> ListAsync(RequestScope scope, NotificationQuery query, CancellationToken cancellationToken)
    {
        var visible = access.VisibleProjects(scope);
        var mine = db.Notifications.AsNoTracking().Where(value => value.UserId == scope.UserId && visible.Any(project => project.Id == value.ProjectId));
        var unreadCount = await mine.CountAsync(value => value.ReadAt == null, cancellationToken);
        var rows = query.Unread == true ? mine.Where(value => value.ReadAt == null) : mine;

        var pageSize = Math.Clamp(query.PageSize ?? 10, 1, 50);
        var page = Math.Max(query.Page ?? 1, 1);
        var total = query.Unread == true ? unreadCount : await rows.CountAsync(cancellationToken);
        var inactive = FollowUpRules.InactiveProjectStatuses.ToList();
        var items = await rows
            .OrderBy(value => value.ReadAt != null).ThenByDescending(value => value.CreatedAt).ThenBy(value => value.DueOn).ThenBy(value => value.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(value => new
            {
                value.Id,
                value.Type,
                value.ProjectId,
                value.DueOn,
                value.CreatedAt,
                value.ReadAt,
                Project = db.Projects.Where(project => project.Id == value.ProjectId).Select(project => new
                {
                    project.ProjectNo,
                    project.Name,
                    project.Status,
                    project.NextFollowUpOn,
                    ClientName = project.Client.ShortName ?? project.Client.Name,
                }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<object>.Ok(new
        {
            total,
            unreadCount,
            page,
            pageSize,
            items = items.Select(item => new
            {
                item.Id,
                item.Type,
                item.ProjectId,
                item.DueOn,
                item.CreatedAt,
                item.ReadAt,
                item.Project,
                // 已處理：專案已改排下次聯絡、清掉下次聯絡，或專案已停止進行；畫面把它淡化，提醒人不用再處理。
                Resolved = item.Project is null || item.Project.NextFollowUpOn != item.DueOn || inactive.Contains(item.Project.Status),
            }),
        });
    }

    /// <summary>
    /// 把一則通知標為已讀。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="id">通知識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>成功回 204；不是自己的通知一律回 404。</returns>
    public async Task<ServiceResult<Empty>> MarkReadAsync(RequestScope scope, Guid id, CancellationToken cancellationToken)
    {
        var mine = db.Notifications.Where(value => value.Id == id && value.UserId == scope.UserId);
        if (!await mine.AnyAsync(cancellationToken)) return ServiceResult<Empty>.NotFound("找不到通知。");
        var now = DateTimeOffset.UtcNow;
        await mine.Where(value => value.ReadAt == null).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ReadAt, now), cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    /// <summary>
    /// 把目前使用者所有未讀通知標為已讀。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>回 204。</returns>
    public async Task<ServiceResult<Empty>> MarkAllReadAsync(RequestScope scope, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await db.Notifications.Where(value => value.UserId == scope.UserId && value.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ReadAt, now), cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }
}
