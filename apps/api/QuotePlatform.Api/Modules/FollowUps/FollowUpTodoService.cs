using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.FollowUps;

/// <summary>今日待辦：下次聯絡日已到或快到的專案清單。</summary>
/// <remarks>
/// 範圍一律經 <see cref="ProjectAccessService"/>；「待跟進」規則與專案列表相同（見 <c>WithPendingFollowUp</c>）。
/// 「我的」是自己負責的專案，加上最新一筆跟進紀錄（也就是排定下次聯絡的那一筆）由自己寫的專案，
/// 與到期通知的收件人相同，所以通知裡提到的案子一定出現在自己的待辦裡。
/// </remarks>
public sealed class FollowUpTodoService(QuoteDbContext db, ProjectAccessService access)
{
    /// <summary>「即將到期」涵蓋明天起算的天數。</summary>
    public const int UpcomingDays = 7;

    /// <summary>
    /// 查詢待辦清單與三個區間的件數。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="query">區間、是否只看自己的與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ total, page, pageSize, counts, items }</c>；counts 為各區間件數，不受 range 影響。</returns>
    public async Task<ServiceResult<object>> ListAsync(RequestScope scope, FollowUpTodoQuery query, CancellationToken cancellationToken)
    {
        var range = FollowUpRules.Clean(query.Range) ?? FollowUpTodoRanges.Overdue;
        if (!FollowUpTodoRanges.All.Contains(range)) return ServiceResult<object>.BadRequest("待辦區間不正確。");

        var today = BusinessTime.Today;
        var userId = scope.UserId;
        // 關聯子查詢裡不能呼叫 LatestFirst()（EF 無法翻譯自訂方法），排序條件必須與它相同：發生時間、建立時間、Id 由新到舊。
        var pending = access.VisibleProjects(scope).AsNoTracking().WithPendingFollowUp();
        if (query.Mine ?? true)
        {
            pending = pending.Where(project => project.OwnerUserId == userId
                || db.ProjectFollowUps.Where(row => row.ProjectId == project.Id).OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).Select(row => row.CreatedBy).FirstOrDefault() == userId);
        }

        var counts = new
        {
            overdue = await InRange(pending, FollowUpTodoRanges.Overdue, today).CountAsync(cancellationToken),
            today = await InRange(pending, FollowUpTodoRanges.Today, today).CountAsync(cancellationToken),
            upcoming = await InRange(pending, FollowUpTodoRanges.Upcoming, today).CountAsync(cancellationToken),
        };
        var rows = InRange(pending, range, today);
        var pageSize = Math.Clamp(query.PageSize ?? 10, 1, 100);
        var page = Math.Max(query.Page ?? 1, 1);
        var total = range switch { FollowUpTodoRanges.Overdue => counts.overdue, FollowUpTodoRanges.Today => counts.today, _ => counts.upcoming };
        var items = await rows
            .OrderBy(project => project.NextFollowUpOn).ThenBy(project => project.ProjectNo)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(project => new
            {
                project.Id,
                project.ProjectNo,
                project.Name,
                project.Status,
                ClientName = project.Client.ShortName ?? project.Client.Name,
                project.NextFollowUpOn,
                project.LastFollowUpAt,
                NextAction = db.ProjectFollowUps.Where(row => row.ProjectId == project.Id).OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).Select(row => row.NextAction).FirstOrDefault(),
                Owner = new FollowUpUser(project.OwnerUserId, db.Users.Where(user => user.Id == project.OwnerUserId).Select(user => user.DisplayName).FirstOrDefault()),
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<object>.Ok(new { total, page, pageSize, counts, items });
    }

    /// <summary>區間篩選：逾期＝今天以前；今天；即將到期＝明天起算 <see cref="UpcomingDays"/> 天內。</summary>
    private static IQueryable<Project> InRange(IQueryable<Project> projects, string range, DateOnly today)
    {
        var upcomingEnd = today.AddDays(UpcomingDays);
        return range switch
        {
            FollowUpTodoRanges.Overdue => projects.Where(project => project.NextFollowUpOn < today),
            FollowUpTodoRanges.Today => projects.Where(project => project.NextFollowUpOn == today),
            _ => projects.Where(project => project.NextFollowUpOn > today && project.NextFollowUpOn <= upcomingEnd),
        };
    }
}

/// <summary>今日待辦的區間代碼。</summary>
public static class FollowUpTodoRanges
{
    /// <summary>已逾期：下次聯絡日早於今天。</summary>
    public const string Overdue = "overdue";

    /// <summary>今天：下次聯絡日就是今天。</summary>
    public const string Today = "today";

    /// <summary>即將到期：明天起算 <see cref="FollowUpTodoService.UpcomingDays"/> 天內。</summary>
    public const string Upcoming = "upcoming";

    /// <summary>所有合法區間。</summary>
    public static readonly IReadOnlyList<string> All = [Overdue, Today, Upcoming];
}
