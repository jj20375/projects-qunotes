using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.FollowUps;

namespace QuotePlatform.Api.Modules.Notifications;

/// <summary>
/// 產生一家公司的跟進到期通知：下次聯絡日已到或已逾期的專案，通知負責人，以及排定下次聯絡的那位撰寫人。
/// </summary>
/// <remarks>
/// 背景工作沒有 JWT，因此以 <see cref="CompanyContext.UseCompany"/> 明確指定公司，再為每位收件人重建他登入時的
/// <see cref="RequestScope"/>（有效權限與資料範圍），用同一個 <see cref="ProjectAccessService"/> 判斷看不看得到專案、
/// 有沒有跟進紀錄的讀取權；沒有權限或看不到的人不會收到通知。
/// 同一收件人、專案、到期日有唯一索引，所以重複執行或多個實例同時執行都不會重複通知。
/// </remarks>
public sealed class FollowUpNotificationGenerator(
    QuoteDbContext db,
    CompanyContext companyContext,
    ProjectAccessService access,
    PermissionGrantService grants,
    ILogger<FollowUpNotificationGenerator> logger)
{
    private const int Batch = 500;

    /// <summary>
    /// 為指定公司產生通知。
    /// </summary>
    /// <param name="companyId">公司識別碼；非啟用中的公司不處理。</param>
    /// <param name="today">台灣日期的今天。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>這次新增的通知數。</returns>
    public async Task<int> GenerateAsync(Guid companyId, DateOnly today, CancellationToken cancellationToken)
    {
        companyContext.UseCompany(companyId);
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(value => value.Id == companyId && value.Status == "active", cancellationToken);
        if (company is null) return 0;

        // 兩個實例同時寫入時其中一個會撞到唯一索引；重試一次，這次會先查到對方寫入的通知而略過。
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await GenerateOnceAsync(company, today, cancellationToken);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<int> GenerateOnceAsync(Company company, DateOnly today, CancellationToken cancellationToken)
    {
        var due = await db.Projects.AsNoTracking().WithPendingFollowUp()
            .Where(project => project.NextFollowUpOn <= today)
            .Select(project => new
            {
                project.Id,
                project.OwnerUserId,
                DueOn = project.NextFollowUpOn!.Value,
                // 排序與 LatestFirst() 相同；關聯子查詢裡不能呼叫自訂方法。
                Author = db.ProjectFollowUps.Where(row => row.ProjectId == project.Id).OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).Select(row => row.CreatedBy).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        if (due.Count == 0) return 0;

        var existing = new HashSet<(Guid UserId, Guid ProjectId, DateOnly DueOn)>();
        // 用 List 而不是陣列：.NET 10 對陣列的 Contains 會改走 ReadOnlySpan 的擴充方法，EF Core 無法翻譯。
        foreach (var ids in due.Select(value => value.Id).Chunk(Batch).Select(chunk => chunk.ToList()))
        {
            var rows = await db.Notifications.AsNoTracking()
                .Where(value => value.Type == NotificationTypes.FollowUpDue && ids.Contains(value.ProjectId))
                .Select(value => new { value.UserId, value.ProjectId, value.DueOn })
                .ToListAsync(cancellationToken);
            foreach (var row in rows) existing.Add((row.UserId, row.ProjectId, row.DueOn));
        }

        var wanted = due
            .SelectMany(project => new[] { project.OwnerUserId, project.Author }.Where(id => id != Guid.Empty).Distinct().Select(userId => (UserId: userId, Project: project)))
            .Where(value => !existing.Contains((value.UserId, value.Project.Id, value.Project.DueOn)))
            .GroupBy(value => value.UserId);

        var created = 0;
        foreach (var recipient in wanted)
        {
            var scope = await ScopeOfAsync(company, recipient.Key, cancellationToken);
            if (scope is null || !scope.Has("followups", "read")) continue;
            var candidateIds = recipient.Select(value => value.Project.Id).ToList();
            var visible = new HashSet<Guid>();
            foreach (var ids in candidateIds.Chunk(Batch).Select(chunk => chunk.ToList()))
            {
                visible.UnionWith(await access.VisibleProjects(scope).Where(project => ids.Contains(project.Id)).Select(project => project.Id).ToListAsync(cancellationToken));
            }
            foreach (var (_, project) in recipient.Where(value => visible.Contains(value.Project.Id)))
            {
                db.Notifications.Add(new Notification { CompanyId = company.Id, UserId = recipient.Key, ProjectId = project.Id, DueOn = project.DueOn });
                created++;
            }
        }
        if (created > 0) await db.SaveChangesAsync(cancellationToken);
        if (created > 0) logger.LogInformation("公司 {CompanyId} 產生 {Count} 則跟進到期通知。", company.Id, created);
        return created;
    }

    /// <summary>重建收件人目前的請求範圍；帳號或成員身分已停用時回傳 null。</summary>
    private async Task<RequestScope?> ScopeOfAsync(Company company, Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().Where(value => value.Id == userId && value.Status == "active").Select(value => new { value.Id, value.AuthVersion }).SingleOrDefaultAsync(cancellationToken);
        if (user is null) return null;
        // 與登入時相同：背景工作沒有 JWT 公司範圍，成員身分改用明確的公司與使用者條件查詢。
        var membership = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Include(value => value.Role)
            .SingleOrDefaultAsync(value => value.CompanyId == company.Id && value.UserId == userId && value.IsActive, cancellationToken);
        if (membership is null) return null;

        var effective = await grants.ForMembershipAsync(membership, company, cancellationToken);
        return new RequestScope(
            userId,
            RequestScope.CompanyScope,
            company.Id,
            membership.Id,
            membership.DepartmentId,
            membership.DataScopeOverride ?? membership.Role.DataScope,
            effective.SelectMany(grant => grant.Actions.Select(action => $"{grant.Code}:{action}")).ToHashSet(StringComparer.Ordinal),
            effective.SelectMany(grant => grant.Options.Select(option => $"{grant.Code}:{option}")).ToHashSet(StringComparer.Ordinal),
            user.AuthVersion,
            IsImpersonating: false);
    }
}
