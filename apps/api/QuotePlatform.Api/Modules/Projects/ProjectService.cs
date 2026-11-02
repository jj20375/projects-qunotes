using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Projects;

/// <summary>專案搜尋、CRUD、狀態流程與成員。</summary>
public sealed class ProjectService(QuoteDbContext db, ProjectAccessService access, NumberSequenceService numbers, AuditService audit)
{
    private static readonly string[] SortFields = ["createdAt", "projectNo", "name", "startDate", "endDate", "expectedAmount", "contractAmount", "status"];

    public async Task<ServiceResult<object>> SearchAsync(RequestScope scope, ProjectSearchQuery query, CancellationToken cancellationToken)
    {
        if (query.From is DateOnly from && query.To is DateOnly to && from > to) return ServiceResult<object>.BadRequest("起始日期不可晚於結束日期。");
        if (query.AmountMin > query.AmountMax) return ServiceResult<object>.BadRequest("金額下限不可大於上限。");
        // 用 List 而非陣列：C# 14 會把 array.Contains 綁到 ReadOnlySpan 多載，EF Core 9 無法轉成 SQL IN。
        var statuses = (query.Status ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
        if (statuses.Any(value => !ProjectStatuses.All.Contains(value))) return ServiceResult<object>.BadRequest("專案狀態不正確。");

        // 除了狀態以外的條件先套用，供狀態分頁籤計數；再套狀態取得列表。
        var filtered = ApplyFilters(access.VisibleProjects(scope).AsNoTracking(), scope, query);
        var statusCounts = await filtered.GroupBy(value => value.Status).Select(group => new { Status = group.Key, Count = group.Count() }).ToDictionaryAsync(value => value.Status, value => value.Count, cancellationToken);
        if (statuses.Count > 0) filtered = filtered.Where(value => statuses.Contains(value.Status));

        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 200);
        var page = Math.Max(query.Page ?? 1, 1);
        var total = await filtered.CountAsync(cancellationToken);
        var sumExpected = await filtered.SumAsync(value => value.ExpectedAmount ?? 0, cancellationToken);
        var sumContract = await filtered.SumAsync(value => value.ContractAmount ?? 0, cancellationToken);

        var items = await Sort(filtered, query.Sort, query.Desc ?? true)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(value => new
            {
                value.Id,
                value.ProjectNo,
                value.Name,
                value.Status,
                client = new { value.Client.Id, value.Client.Code, value.Client.Name },
                value.DepartmentId,
                departmentName = db.Departments.Where(department => department.Id == value.DepartmentId).Select(department => department.Name).FirstOrDefault(),
                owner = new { value.Owner.Id, value.Owner.DisplayName },
                value.StartDate,
                value.EndDate,
                value.ExpectedAmount,
                value.ContractAmount,
                value.ContractTotal,
                value.Currency,
                value.CreatedAt,
                memberCount = value.Members.Count,
            })
            .ToListAsync(cancellationToken);
        return ServiceResult<object>.Ok(new
        {
            total,
            page,
            pageSize,
            statusCounts,
            sumExpectedAmount = sumExpected,
            sumContractAmount = sumContract,
            items,
        });
    }

    public async Task<ServiceResult<object>> GetAsync(RequestScope scope, Guid projectId, CancellationToken cancellationToken)
    {
        var projectAccess = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (projectAccess is null) return ServiceResult<object>.NotFound("找不到專案。");
        var project = projectAccess.Project;
        var client = await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(value => value.Id == project.ClientId).Select(value => new { value.Id, value.Code, value.Name, value.IsDeleted }).SingleAsync(cancellationToken);
        var owner = await db.Users.AsNoTracking().Where(value => value.Id == project.OwnerUserId).Select(value => new { value.Id, value.DisplayName }).SingleAsync(cancellationToken);
        var departmentName = project.DepartmentId is Guid departmentId ? await db.Departments.Where(value => value.Id == departmentId).Select(value => value.Name).SingleOrDefaultAsync(cancellationToken) : null;
        var members = await db.ProjectMembers.AsNoTracking()
            .Where(value => value.ProjectId == projectId)
            .OrderBy(value => value.AddedAt)
            .Select(value => new { value.UserId, value.User.DisplayName, value.ProjectRole, value.AddedAt })
            .ToListAsync(cancellationToken);
        var history = await db.ProjectStatusHistories.AsNoTracking()
            .Where(value => value.ProjectId == projectId)
            .OrderByDescending(value => value.ChangedAt)
            .Select(value => new { value.FromStatus, value.ToStatus, value.Reason, value.ChangedAt, changedBy = db.Users.Where(user => user.Id == value.ChangedBy).Select(user => user.DisplayName).FirstOrDefault() })
            .ToListAsync(cancellationToken);

        // 專案金額摘要：只回傳使用者有讀取權的資源，沒有權限的欄位為 null。
        var today = BusinessTime.Today;
        var summary = new
        {
            orderTotal = scope.Has("orders", "read") ? await db.ProjectOrders.Where(value => value.ProjectId == projectId && value.Status != "cancelled").SumAsync(value => (decimal?)value.Total, cancellationToken) ?? 0 : (decimal?)null,
            invoicedTotal = scope.Has("invoices", "read") ? await db.ProjectInvoices.Where(value => value.ProjectId == projectId && value.Status == "issued").SumAsync(value => (decimal?)value.TotalAmount, cancellationToken) ?? 0 : (decimal?)null,
            paidTotal = scope.Has("invoices", "read") ? await db.ProjectInvoices.Where(value => value.ProjectId == projectId && value.Status == "issued").SumAsync(value => (decimal?)value.PaidAmount, cancellationToken) ?? 0 : (decimal?)null,
            activeWarranties = scope.Has("warranties", "read") ? await db.ProjectWarranties.CountAsync(value => value.ProjectId == projectId && value.EndDate >= today, cancellationToken) : (int?)null,
        };

        var nextStatuses = projectAccess.CanWrite
            ? ProjectStatuses.Transitions[project.Status].Where(value => value != ProjectStatuses.Closed || scope.HasOption("projects", "close")).ToArray()
            : [];
        return ServiceResult<object>.Ok(new
        {
            project.Id,
            project.ProjectNo,
            project.Name,
            project.Status,
            client,
            project.DepartmentId,
            departmentName,
            owner,
            project.StartDate,
            project.EndDate,
            project.ExpectedAmount,
            project.ContractAmount,
            project.ContractTotal,
            project.Currency,
            project.Description,
            project.WonAt,
            project.ClosedAt,
            project.CreatedAt,
            project.UpdatedAt,
            myRole = project.OwnerUserId == scope.UserId ? "owner" : projectAccess.MemberRole,
            canWrite = projectAccess.CanWrite,
            canManageMembers = projectAccess.CanManageMembers,
            canDelete = projectAccess.CanWrite && scope.Has("projects", "delete") && project.Status is ProjectStatuses.Draft or ProjectStatuses.Cancelled,
            nextStatuses,
            summary,
            members,
            history,
        });
    }

    public async Task<ServiceResult<object>> CreateAsync(RequestScope scope, ProjectInput input, CancellationToken cancellationToken)
    {
        var departmentId = input.DepartmentId ?? scope.DepartmentId;
        var ownerUserId = input.OwnerUserId ?? scope.UserId;
        if (await ValidateAsync(scope, input, departmentId, ownerUserId, null, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);
        var members = input.Members ?? [];
        if (members.Length > 0 && !scope.HasOption("projects", "assign_member")) return ServiceResult<object>.Forbidden("沒有指定專案成員的權限。");
        if (await ValidateMembersAsync(members, ownerUserId, cancellationToken) is string memberError) return ServiceResult<object>.BadRequest(memberError);

        var project = new Project
        {
            ProjectNo = await numbers.NextProjectNoAsync(scope.CompanyId!.Value, cancellationToken),
            CreatedBy = scope.UserId,
        };
        Apply(project, input, departmentId, ownerUserId);
        db.Projects.Add(project);
        db.ProjectMembers.AddRange(members.Select(value => new ProjectMember { ProjectId = project.Id, UserId = value.UserId, ProjectRole = value.ProjectRole ?? ProjectRoles.Member, AddedBy = scope.UserId }));
        db.ProjectStatusHistories.Add(new ProjectStatusHistory { ProjectId = project.Id, ToStatus = project.Status, ChangedBy = scope.UserId });
        audit.Add(scope.CompanyId, scope.UserId, "project", project.Id, "create", after: new { project.ProjectNo, project.Name, project.ClientId, project.DepartmentId, project.OwnerUserId, members });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new { project.Id, project.ProjectNo });
    }

    public async Task<ServiceResult<object>> UpdateAsync(RequestScope scope, Guid projectId, ProjectInput input, CancellationToken cancellationToken)
    {
        var projectAccess = await access.GetAsync(scope, projectId, track: true, cancellationToken);
        if (projectAccess is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (!projectAccess.CanWrite) return ServiceResult<object>.Forbidden("您對此專案只有檢視權限。");
        var project = projectAccess.Project;
        if (project.Status == ProjectStatuses.Closed) return ServiceResult<object>.BadRequest("已結案的專案不可修改。");

        var departmentId = input.DepartmentId ?? project.DepartmentId;
        var ownerUserId = input.OwnerUserId ?? project.OwnerUserId;
        if (await ValidateAsync(scope, input, departmentId, ownerUserId, project, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);

        var before = Snapshot(project);
        Apply(project, input, departmentId, ownerUserId);
        project.UpdatedBy = scope.UserId;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        // 負責人一定有完整權限，不需要同時是成員。
        db.ProjectMembers.RemoveRange(await db.ProjectMembers.Where(value => value.ProjectId == projectId && value.UserId == ownerUserId).ToListAsync(cancellationToken));
        audit.Add(scope.CompanyId, scope.UserId, "project", project.Id, "update", before, Snapshot(project));
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { project.Id });
    }

    public async Task<ServiceResult<object>> ChangeStatusAsync(RequestScope scope, Guid projectId, ChangeStatusRequest request, CancellationToken cancellationToken)
    {
        var projectAccess = await access.GetAsync(scope, projectId, track: true, cancellationToken);
        if (projectAccess is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (!projectAccess.CanWrite) return ServiceResult<object>.Forbidden("您對此專案只有檢視權限。");
        var project = projectAccess.Project;
        var target = request.Status ?? string.Empty;
        if (!ProjectStatuses.CanTransition(project.Status, target)) return ServiceResult<object>.BadRequest($"專案狀態不可從 {project.Status} 變更為 {target}。");
        if (target == ProjectStatuses.Closed && !scope.HasOption("projects", "close")) return ServiceResult<object>.Forbidden("沒有結案的權限。");
        if (target is ProjectStatuses.Lost or ProjectStatuses.Cancelled && string.IsNullOrWhiteSpace(request.Reason)) return ServiceResult<object>.BadRequest("請填寫未得標或取消的原因。");

        var now = DateTimeOffset.UtcNow;
        db.ProjectStatusHistories.Add(new ProjectStatusHistory { ProjectId = projectId, FromStatus = project.Status, ToStatus = target, Reason = Clean(request.Reason), ChangedBy = scope.UserId, ChangedAt = now });
        project.Status = target;
        if (target == ProjectStatuses.Won) project.WonAt = now;
        project.ClosedAt = target == ProjectStatuses.Closed ? now : null;
        project.UpdatedBy = scope.UserId;
        project.UpdatedAt = now;
        audit.Add(scope.CompanyId, scope.UserId, "project", projectId, "change_status", after: new { target, request.Reason });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { project.Id, project.Status });
    }

    /// <summary>只有草稿或已取消的專案可刪除（軟刪除）；進入報價後的專案必須保留紀錄。</summary>
    public async Task<ServiceResult<Empty>> DeleteAsync(RequestScope scope, Guid projectId, CancellationToken cancellationToken)
    {
        var projectAccess = await access.GetAsync(scope, projectId, track: true, cancellationToken);
        if (projectAccess is null) return ServiceResult<Empty>.NotFound("找不到專案。");
        if (!projectAccess.CanWrite) return ServiceResult<Empty>.Forbidden("您對此專案只有檢視權限。");
        var project = projectAccess.Project;
        if (project.Status is not (ProjectStatuses.Draft or ProjectStatuses.Cancelled)) return ServiceResult<Empty>.BadRequest("只有草稿或已取消的專案可以刪除。");
        project.IsDeleted = true;
        project.DeletedAt = DateTimeOffset.UtcNow;
        project.UpdatedBy = scope.UserId;
        audit.Add(scope.CompanyId, scope.UserId, "project", projectId, "delete", Snapshot(project));
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    public async Task<ServiceResult<object>> ReplaceMembersAsync(RequestScope scope, Guid projectId, ProjectMemberInput[] members, CancellationToken cancellationToken)
    {
        var projectAccess = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (projectAccess is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (!projectAccess.CanManageMembers) return ServiceResult<object>.Forbidden("沒有管理此專案成員的權限。");
        if (await ValidateMembersAsync(members, projectAccess.Project.OwnerUserId, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.ProjectMembers.Where(value => value.ProjectId == projectId).ToListAsync(cancellationToken);
        var before = existing.Select(value => new { value.UserId, value.ProjectRole }).ToList();
        var requested = members.ToDictionary(value => value.UserId, value => value.ProjectRole ?? ProjectRoles.Member);
        foreach (var member in existing)
        {
            if (requested.Remove(member.UserId, out var role)) member.ProjectRole = role;
            else db.ProjectMembers.Remove(member);
        }
        db.ProjectMembers.AddRange(requested.Select(value => new ProjectMember { ProjectId = projectId, UserId = value.Key, ProjectRole = value.Value, AddedBy = scope.UserId }));
        audit.Add(scope.CompanyId, scope.UserId, "project", projectId, "replace_members", before, members);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { count = members.Length });
    }

    private IQueryable<Project> ApplyFilters(IQueryable<Project> projects, RequestScope scope, ProjectSearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            projects = projects.Where(value => value.Name.Contains(keyword) || value.ProjectNo.Contains(keyword) || value.Client.Name.Contains(keyword));
        }
        if (query.ClientId is Guid clientId) projects = projects.Where(value => value.ClientId == clientId);
        if (query.OwnerUserId is Guid ownerUserId) projects = projects.Where(value => value.OwnerUserId == ownerUserId);
        if (query.DepartmentId is Guid departmentId) projects = projects.Where(value => value.DepartmentId == departmentId);
        if (query.Mine == true) projects = projects.Where(value => value.OwnerUserId == scope.UserId || value.Members.Any(member => member.UserId == scope.UserId));

        // 日期：created 以台灣時間切日換算成 UTC 區間；start／end 為 DATE 欄位直接比較。
        switch (query.DateField ?? "created")
        {
            case "start":
                if (query.From is DateOnly startFrom) projects = projects.Where(value => value.StartDate >= startFrom);
                if (query.To is DateOnly startTo) projects = projects.Where(value => value.StartDate <= startTo);
                break;
            case "end":
                if (query.From is DateOnly endFrom) projects = projects.Where(value => value.EndDate >= endFrom);
                if (query.To is DateOnly endTo) projects = projects.Where(value => value.EndDate <= endTo);
                break;
            default:
                if (query.From is DateOnly createdFrom)
                {
                    var fromAt = BusinessTime.StartOfDay(createdFrom);
                    projects = projects.Where(value => value.CreatedAt >= fromAt);
                }
                if (query.To is DateOnly createdTo)
                {
                    var toAt = BusinessTime.StartOfDay(createdTo.AddDays(1));
                    projects = projects.Where(value => value.CreatedAt < toAt);
                }
                break;
        }

        if (query.AmountField == "contract")
        {
            if (query.AmountMin is decimal min) projects = projects.Where(value => value.ContractAmount >= min);
            if (query.AmountMax is decimal max) projects = projects.Where(value => value.ContractAmount <= max);
        }
        else
        {
            if (query.AmountMin is decimal min) projects = projects.Where(value => value.ExpectedAmount >= min);
            if (query.AmountMax is decimal max) projects = projects.Where(value => value.ExpectedAmount <= max);
        }
        return projects;
    }

    private static IQueryable<Project> Sort(IQueryable<Project> projects, string? sort, bool desc)
    {
        var field = SortFields.Contains(sort) ? sort : "createdAt";
        return (field, desc) switch
        {
            ("projectNo", false) => projects.OrderBy(value => value.ProjectNo),
            ("projectNo", true) => projects.OrderByDescending(value => value.ProjectNo),
            ("name", false) => projects.OrderBy(value => value.Name),
            ("name", true) => projects.OrderByDescending(value => value.Name),
            ("startDate", false) => projects.OrderBy(value => value.StartDate),
            ("startDate", true) => projects.OrderByDescending(value => value.StartDate),
            ("endDate", false) => projects.OrderBy(value => value.EndDate),
            ("endDate", true) => projects.OrderByDescending(value => value.EndDate),
            ("expectedAmount", false) => projects.OrderBy(value => value.ExpectedAmount),
            ("expectedAmount", true) => projects.OrderByDescending(value => value.ExpectedAmount),
            ("contractAmount", false) => projects.OrderBy(value => value.ContractAmount),
            ("contractAmount", true) => projects.OrderByDescending(value => value.ContractAmount),
            ("status", false) => projects.OrderBy(value => value.Status),
            ("status", true) => projects.OrderByDescending(value => value.Status),
            (_, false) => projects.OrderBy(value => value.CreatedAt),
            _ => projects.OrderByDescending(value => value.CreatedAt),
        };
    }

    private async Task<string?> ValidateAsync(RequestScope scope, ProjectInput input, Guid? departmentId, Guid ownerUserId, Project? current, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "請輸入專案名稱。";
        if (input.ClientId is not Guid clientId) return "請選擇客戶。";
        // 停用的客戶不能開新專案，但既有專案仍可維持原客戶。
        if (clientId != current?.ClientId && !await db.Clients.AnyAsync(value => value.Id == clientId && value.Status == "active", cancellationToken)) return "客戶不存在或已停用。";
        if (input.StartDate is DateOnly start && input.EndDate is DateOnly end && start > end) return "開始日期不可晚於結束日期。";
        if (input.ExpectedAmount < 0 || input.ContractAmount < 0 || input.ContractTotal < 0) return "金額不可為負數。";
        if (!await db.CompanyMemberships.AnyAsync(value => value.UserId == ownerUserId && value.IsActive, cancellationToken)) return "負責人必須是公司內啟用中的成員。";

        if (departmentId != current?.DepartmentId)
        {
            if (departmentId is not Guid id || !await db.Departments.AnyAsync(value => value.Id == id && value.IsActive, cancellationToken)) return "請選擇啟用中的部門。";
            // 不能把專案建立或移到自己資料範圍以外的部門，否則會產生自己看不到的資料。
            var allowed = await access.AllowedDepartmentIdsAsync(scope, cancellationToken);
            if (allowed is not null && !allowed.Contains(id)) return "只能選擇自己資料範圍內的部門。";
        }
        return null;
    }

    private async Task<string?> ValidateMembersAsync(IReadOnlyCollection<ProjectMemberInput> members, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (members.Select(value => value.UserId).Distinct().Count() != members.Count) return "成員不可重複。";
        if (members.Any(value => value.UserId == ownerUserId)) return "負責人不需要加入成員清單。";
        if (members.Any(value => value.ProjectRole is not null && !ProjectRoles.All.Contains(value.ProjectRole))) return "專案角色只能是 manager、member 或 viewer。";
        var ids = members.Select(value => value.UserId).ToList();
        var activeCount = await db.CompanyMemberships.CountAsync(value => ids.Contains(value.UserId) && value.IsActive, cancellationToken);
        return activeCount == ids.Count ? null : "專案成員必須是公司內啟用中的成員。";
    }

    private static void Apply(Project project, ProjectInput input, Guid? departmentId, Guid ownerUserId)
    {
        project.Name = input.Name!.Trim();
        project.ClientId = input.ClientId!.Value;
        project.DepartmentId = departmentId;
        project.OwnerUserId = ownerUserId;
        project.StartDate = input.StartDate;
        project.EndDate = input.EndDate;
        project.ExpectedAmount = input.ExpectedAmount;
        project.ContractAmount = input.ContractAmount;
        project.ContractTotal = input.ContractTotal;
        project.Description = Clean(input.Description);
    }

    private static object Snapshot(Project value) => new { value.Name, value.ClientId, value.DepartmentId, value.OwnerUserId, value.Status, value.StartDate, value.EndDate, value.ExpectedAmount, value.ContractAmount, value.ContractTotal };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
