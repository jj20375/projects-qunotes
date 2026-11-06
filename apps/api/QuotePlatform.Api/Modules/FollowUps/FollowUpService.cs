using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.FollowUps;

/// <summary>專案跟進紀錄：專案內列表、聯絡人、新增修改刪除、不用跟進，以及專案跟進狀態的重算。</summary>
/// <remarks>
/// 可見範圍一律經 <see cref="ProjectAccessService"/>，看不到專案回 404。
/// 專案的 LastFollowUpAt／NextFollowUpOn 只由 <see cref="SaveAndRecalculateAsync"/> 寫入。
/// </remarks>
public sealed class FollowUpService(QuoteDbContext db, ProjectAccessService access, AuditService audit)
{
    /// <summary>
    /// 查詢專案內的跟進紀錄（最新一筆在前）。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="type">類型篩選；null 表示全部。</param>
    /// <param name="query">關鍵字（主旨、內容、對象）與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>分頁結果、寫入旗標與專案目前的跟進狀態。</returns>
    public async Task<ServiceResult<object>> ListByProjectAsync(RequestScope scope, Guid projectId, string? type, ListQuery query, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        type = FollowUpRules.Clean(type);
        if (type is not null && !FollowUpTypes.All.Contains(type)) return ServiceResult<object>.BadRequest("跟進類型不正確。");

        var rows = db.ProjectFollowUps.AsNoTracking().Where(value => value.ProjectId == projectId);
        if (type is not null) rows = rows.Where(value => value.Type == type);
        if (query.KeywordText is string keyword)
        {
            rows = rows.Where(value => value.Summary.Contains(keyword)
                || (value.Content != null && value.Content.Contains(keyword))
                || (value.ContactName != null && value.ContactName.Contains(keyword)));
        }
        var total = await rows.CountAsync(cancellationToken);
        var items = await ToItems(rows.LatestFirst().Skip(query.Skip).Take(query.PageSizeValue)).ToListAsync(cancellationToken);
        var writable = project.Project.Status != ProjectStatuses.Closed;
        return ServiceResult<object>.Ok(new
        {
            canCreate = writable && project.CanWriteChild(scope, "followups", "create"),
            canUpdate = writable && project.CanWriteChild(scope, "followups"),
            canDelete = writable && project.CanWriteChild(scope, "followups", "delete"),
            total,
            page = query.PageValue,
            pageSize = query.PageSizeValue,
            items,
            project = await StateAsync(projectId, cancellationToken),
        });
    }

    /// <summary>
    /// 跨專案跟進紀錄總表：目前使用者看得到的專案內的所有紀錄。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="query">篩選、排序與分頁條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ total, page, pageSize, items }</c>；每筆附專案摘要與客戶名稱。</returns>
    /// <remarks>部門篩選含下層部門，且仍在資料範圍內（先套 VisibleProjects 再篩部門），與 Dashboard 相同。</remarks>
    public async Task<ServiceResult<object>> SearchAsync(RequestScope scope, FollowUpSearchQuery query, CancellationToken cancellationToken)
    {
        if (query.From is DateOnly from && query.To is DateOnly to && from > to) return ServiceResult<object>.BadRequest("起始日期不可晚於結束日期。");
        var type = FollowUpRules.Clean(query.Type);
        if (type is not null && !FollowUpTypes.All.Contains(type)) return ServiceResult<object>.BadRequest("跟進類型不正確。");

        var projects = access.VisibleProjects(scope);
        if (query.DepartmentId is Guid departmentId)
        {
            projects = projects.Where(project => db.DepartmentClosures.Any(path => path.AncestorId == departmentId && path.DescendantId == project.DepartmentId));
        }
        if (query.ProjectId is Guid projectId) projects = projects.Where(project => project.Id == projectId);

        var rows = db.ProjectFollowUps.AsNoTracking().Where(value => projects.Any(project => project.Id == value.ProjectId));
        if (type is not null) rows = rows.Where(value => value.Type == type);
        if (query.CreatedBy is Guid createdBy) rows = rows.Where(value => value.CreatedBy == createdBy);
        if (query.From is DateOnly fromDate)
        {
            var fromAt = BusinessTime.StartOfDay(fromDate);
            rows = rows.Where(value => value.OccurredAt >= fromAt);
        }
        if (query.To is DateOnly toDate)
        {
            var toAt = BusinessTime.StartOfDay(toDate.AddDays(1));
            rows = rows.Where(value => value.OccurredAt < toAt);
        }
        if (FollowUpRules.Clean(query.Keyword) is string keyword)
        {
            rows = rows.Where(value => value.Summary.Contains(keyword)
                || (value.Content != null && value.Content.Contains(keyword))
                || (value.ContactName != null && value.ContactName.Contains(keyword))
                || db.Projects.Any(project => project.Id == value.ProjectId
                    && (project.ProjectNo.Contains(keyword) || project.Name.Contains(keyword) || project.Client.Name.Contains(keyword)
                        || (project.Client.ShortName != null && project.Client.ShortName.Contains(keyword)))));
        }

        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 200);
        var page = Math.Max(query.Page ?? 1, 1);
        var total = await rows.CountAsync(cancellationToken);
        var sorted = (query.Sort == "createdAt", query.Desc ?? true) switch
        {
            (true, true) => rows.OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id),
            (true, false) => rows.OrderBy(value => value.CreatedAt).ThenBy(value => value.Id),
            (false, false) => rows.OrderBy(value => value.OccurredAt).ThenBy(value => value.CreatedAt).ThenBy(value => value.Id),
            _ => rows.LatestFirst(),
        };
        var items = await ToItems(sorted.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);

        var projectIds = items.Select(value => value.ProjectId).Distinct().ToList();
        var info = await db.Projects.AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .Select(project => new { project.Id, project.ProjectNo, project.Name, project.Status, project.NextFollowUpOn, ClientName = project.Client.Name })
            .ToDictionaryAsync(value => value.Id, cancellationToken);

        return ServiceResult<object>.Ok(new
        {
            total,
            page,
            pageSize,
            items = items.Select(item =>
            {
                var project = info.GetValueOrDefault(item.ProjectId);
                return new
                {
                    item.Id,
                    item.ProjectId,
                    item.Type,
                    item.OccurredAt,
                    item.ContactId,
                    item.ContactName,
                    item.Summary,
                    item.Content,
                    item.NextFollowUpOn,
                    item.NextAction,
                    item.NextDismissedAt,
                    item.CreatedBy,
                    item.CreatedAt,
                    item.UpdatedBy,
                    item.UpdatedAt,
                    project = project is null ? null : new { project.Id, project.ProjectNo, project.Name, project.Status, project.NextFollowUpOn },
                    clientName = project?.ClientName,
                };
            }),
        });
    }

    /// <summary>
    /// 專案客戶的聯絡人，供表單下拉選單遠端查詢。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="keyword">比對姓名與職稱；null 表示不篩選。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>最多 20 筆聯絡人（id、name、title）。</returns>
    /// <remarks>不呼叫客戶 API：客戶依客戶部門判斷可見範圍，專案成員可能看得到專案卻看不到客戶。</remarks>
    public async Task<ServiceResult<object>> ContactsAsync(RequestScope scope, Guid projectId, string? keyword, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        var contacts = db.ClientContacts.AsNoTracking().Where(value => value.ClientId == project.Project.ClientId);
        if (FollowUpRules.Clean(keyword) is string text) contacts = contacts.Where(value => value.Name.Contains(text) || (value.Title != null && value.Title.Contains(text)));
        return ServiceResult<object>.Ok(await contacts
            .OrderByDescending(value => value.IsPrimary).ThenBy(value => value.SortOrder).ThenBy(value => value.Name)
            .Take(20)
            .Select(value => new { value.Id, value.Name, value.Title })
            .ToListAsync(cancellationToken));
    }

    /// <summary>
    /// 新增跟進紀錄並重算專案的跟進狀態。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="input">紀錄內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>201 與 <c>{ item, project }</c>。</returns>
    public async Task<ServiceResult<object>> CreateAsync(RequestScope scope, Guid projectId, FollowUpInput input, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (Denied(scope, project, "create") is { } denied) return denied;
        if (await ValidateAsync(project.Project, input, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);

        var row = new ProjectFollowUp { ProjectId = projectId, CreatedBy = scope.UserId };
        await ApplyAsync(row, input, cancellationToken);
        db.ProjectFollowUps.Add(row);
        audit.Add(scope.CompanyId, scope.UserId, "project_followup", row.Id, "create", after: Snapshot(row));
        var state = await SaveAndRecalculateAsync(projectId, cancellationToken);
        return ServiceResult<object>.Created(new { item = await ItemAsync(row.Id, cancellationToken), project = state });
    }

    /// <summary>
    /// 修改跟進紀錄並重算專案的跟進狀態；改了下次聯絡日會清掉「不用跟進」。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="id">跟進紀錄識別碼。</param>
    /// <param name="input">紀錄內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ item, project }</c>。</returns>
    public async Task<ServiceResult<object>> UpdateAsync(RequestScope scope, Guid id, FollowUpInput input, CancellationToken cancellationToken)
    {
        var row = await db.ProjectFollowUps.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null) return ServiceResult<object>.NotFound("找不到跟進紀錄。");
        var project = await access.GetAsync(scope, row.ProjectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到跟進紀錄。");
        if (Denied(scope, project, "update") is { } denied) return denied;
        if (await ValidateAsync(project.Project, input, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);

        var before = Snapshot(row);
        await ApplyAsync(row, input, cancellationToken);
        row.UpdatedBy = scope.UserId;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "project_followup", row.Id, "update", before, Snapshot(row));
        var state = await SaveAndRecalculateAsync(row.ProjectId, cancellationToken);
        return ServiceResult<object>.Ok(new { item = await ItemAsync(row.Id, cancellationToken), project = state });
    }

    /// <summary>
    /// 刪除（軟刪除）跟進紀錄；刪掉最新一筆時，狀態倒回前一筆。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="id">跟進紀錄識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ project }</c>。</returns>
    public async Task<ServiceResult<object>> DeleteAsync(RequestScope scope, Guid id, CancellationToken cancellationToken)
    {
        var row = await db.ProjectFollowUps.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null) return ServiceResult<object>.NotFound("找不到跟進紀錄。");
        var project = await access.GetAsync(scope, row.ProjectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到跟進紀錄。");
        if (Denied(scope, project, "delete") is { } denied) return denied;

        row.IsDeleted = true;
        row.DeletedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "project_followup", row.Id, "delete", Snapshot(row));
        return ServiceResult<object>.Ok(new { project = await SaveAndRecalculateAsync(row.ProjectId, cancellationToken) });
    }

    /// <summary>
    /// 「不用跟進」：清掉最新一筆的下次聯絡日。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ project }</c>；目前沒有排定時回 400。</returns>
    public async Task<ServiceResult<object>> DismissNextAsync(RequestScope scope, Guid projectId, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (Denied(scope, project, "update") is { } denied) return denied;

        var latest = await db.ProjectFollowUps.Where(value => value.ProjectId == projectId).LatestFirst().FirstOrDefaultAsync(cancellationToken);
        if (latest is not { NextFollowUpOn: not null, NextDismissedAt: null }) return ServiceResult<object>.BadRequest("目前沒有排定下次聯絡。");
        latest.NextDismissedAt = DateTimeOffset.UtcNow;
        latest.NextDismissedBy = scope.UserId;
        audit.Add(scope.CompanyId, scope.UserId, "project_followup", latest.Id, "dismiss_next", after: new { latest.NextFollowUpOn });
        return ServiceResult<object>.Ok(new { project = await SaveAndRecalculateAsync(projectId, cancellationToken) });
    }

    /// <summary>
    /// 取得專案目前的「下次要做的事」。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>最新一筆的下次要做的事；呼叫端應只在專案的 NextFollowUpOn 有值時使用。</returns>
    public Task<string?> NextActionAsync(Guid projectId, CancellationToken cancellationToken) =>
        db.ProjectFollowUps.AsNoTracking().Where(value => value.ProjectId == projectId).LatestFirst().Select(value => value.NextAction).FirstOrDefaultAsync(cancellationToken);

    private static ServiceResult<object>? Denied(RequestScope scope, ProjectAccess project, string action)
    {
        if (!project.CanWriteChild(scope, "followups", action)) return ServiceResult<object>.Forbidden("您對此專案的跟進紀錄只有檢視權限。");
        return project.Project.Status == ProjectStatuses.Closed ? ServiceResult<object>.BadRequest("已結案的專案不可修改。") : null;
    }

    private async Task<string?> ValidateAsync(Project project, FollowUpInput input, CancellationToken cancellationToken)
    {
        if (FollowUpRules.Validate(input, DateTimeOffset.UtcNow) is string error) return error;
        if (input.ContactId is Guid contactId && !await db.ClientContacts.AnyAsync(value => value.Id == contactId && value.ClientId == project.ClientId, cancellationToken))
        {
            return "聯絡人不屬於此專案的客戶。";
        }
        return null;
    }

    private async Task ApplyAsync(ProjectFollowUp row, FollowUpInput input, CancellationToken cancellationToken)
    {
        row.Type = input.Type!;
        row.OccurredAt = input.OccurredAt!.Value.ToUniversalTime();
        row.ContactId = input.ContactId;
        // 選了聯絡人就存它當下的姓名：聯絡人日後改名或刪除，紀錄仍保留當時的對象。
        row.ContactName = input.ContactId is Guid contactId
            ? await db.ClientContacts.Where(value => value.Id == contactId).Select(value => value.Name).SingleAsync(cancellationToken)
            : FollowUpRules.Clean(input.ContactName);
        row.Summary = input.Summary!.Trim();
        row.Content = FollowUpRules.Clean(input.Content);
        // 下次聯絡日改了代表重新排定，先前按的「不用跟進」不再適用。
        if (row.NextFollowUpOn != input.NextFollowUpOn)
        {
            row.NextDismissedAt = null;
            row.NextDismissedBy = null;
        }
        row.NextFollowUpOn = input.NextFollowUpOn;
        row.NextAction = input.NextFollowUpOn is null ? null : FollowUpRules.Clean(input.NextAction);
    }

    /// <summary>寫入紀錄後在同一個交易內重算專案的跟進狀態。</summary>
    /// <remarks>
    /// 先以不改值的 UPDATE 鎖住專案列，同一專案的並行寫入會在這裡排隊；否則兩邊各自插入紀錄後，
    /// 又互等對方未提交的列與專案列鎖，在 REPEATABLE READ 下會死結（MariaDB 1213）。
    /// 重算是單一 UPDATE 加子查詢，子查詢的排序必須與 LatestFirst 相同。
    /// </remarks>
    private async Task<FollowUpProjectState> SaveAndRecalculateAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Projects.Where(value => value.Id == projectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.LastFollowUpAt, value => value.LastFollowUpAt), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var latest = db.ProjectFollowUps.Where(value => value.ProjectId == projectId).LatestFirst();
        await db.Projects.Where(value => value.Id == projectId).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.LastFollowUpAt, value => latest.Select(row => (DateTimeOffset?)row.OccurredAt).FirstOrDefault())
            .SetProperty(value => value.NextFollowUpOn, value => latest.Select(row => row.NextDismissedAt == null ? row.NextFollowUpOn : null).FirstOrDefault()),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await StateAsync(projectId, cancellationToken);
    }

    private async Task<FollowUpProjectState> StateAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking().Where(value => value.Id == projectId).Select(value => new { value.LastFollowUpAt, value.NextFollowUpOn }).SingleAsync(cancellationToken);
        // NextFollowUpOn 有值代表最新一筆的下次聯絡仍有效，下次要做的事就取那一筆。
        var nextAction = project.NextFollowUpOn is null ? null : await NextActionAsync(projectId, cancellationToken);
        return new FollowUpProjectState(project.LastFollowUpAt, project.NextFollowUpOn, nextAction);
    }

    private Task<FollowUpItem> ItemAsync(Guid id, CancellationToken cancellationToken) =>
        ToItems(db.ProjectFollowUps.AsNoTracking().Where(value => value.Id == id)).SingleAsync(cancellationToken);

    /// <summary>轉成回傳格式；撰寫人與修改人的姓名以子查詢取得（users 不受公司過濾）。</summary>
    internal IQueryable<FollowUpItem> ToItems(IQueryable<ProjectFollowUp> rows) => rows.Select(value => new FollowUpItem(
        value.Id,
        value.ProjectId,
        value.Type,
        value.OccurredAt,
        value.ContactId,
        value.ContactName,
        value.Summary,
        value.Content,
        value.NextFollowUpOn,
        value.NextAction,
        value.NextDismissedAt,
        new FollowUpUser(value.CreatedBy, db.Users.Where(user => user.Id == value.CreatedBy).Select(user => user.DisplayName).FirstOrDefault()),
        value.CreatedAt,
        value.UpdatedBy == null ? null : new FollowUpUser(value.UpdatedBy.Value, db.Users.Where(user => user.Id == value.UpdatedBy).Select(user => user.DisplayName).FirstOrDefault()),
        value.UpdatedAt));

    private static object Snapshot(ProjectFollowUp row) => new
    {
        row.Type,
        row.OccurredAt,
        row.ContactId,
        row.ContactName,
        row.Summary,
        row.Content,
        row.NextFollowUpOn,
        row.NextAction,
        row.NextDismissedAt,
    };
}
