using Microsoft.EntityFrameworkCore;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 單一專案的存取判斷結果。<see cref="HasWriteRole"/> 只看身分（負責人、manager／member、部門範圍），
/// 不含權限；訂單、發票等子資料以各自的權限（例如 <c>invoices:update</c>）加上 HasWriteRole 判斷。
/// </summary>
public sealed record ProjectAccess(Project Project, string? MemberRole, bool HasWriteRole, bool CanWrite, bool CanManageMembers)
{
    /// <summary>子資料（訂單、保固、發票、附件）的寫入權：該資源的權限 + 專案寫入身分。</summary>
    public bool CanWriteChild(RequestScope scope, string code, string action = "update") => HasWriteRole && scope.Has(code, action);
}

/// <summary>
/// 資料範圍（Data Scope）的唯一實作：專案可見性與可寫入判斷。
/// 報價、訂單、保固、發票、附件都必須透過這裡取得專案，不可各自重寫條件。
/// </summary>
/// <remarks>
/// 可見：own＝負責人或專案成員；department＝再加上專案部門等於自己部門；department_tree＝再加上自己部門的所有下層；company＝全部。
/// 可寫：需 <c>projects:update</c>，且（負責人、manager／member 成員，或不靠成員身分就看得到）。
/// 也就是「只因為被加入為 viewer 才看得到」的人只能讀。
/// </remarks>
public sealed class ProjectAccessService(QuoteDbContext db)
{
    /// <summary>目前使用者可見的專案查詢；公司範圍已由 Global Query Filter 限定。</summary>
    public IQueryable<Project> VisibleProjects(RequestScope scope)
    {
        var userId = scope.UserId;
        var departmentId = scope.DepartmentId;
        var projects = db.Projects.AsQueryable();
        return EffectiveScope(scope) switch
        {
            DataScopes.Company => projects,
            DataScopes.Department => projects.Where(value =>
                value.OwnerUserId == userId
                || value.Members.Any(member => member.UserId == userId)
                || value.DepartmentId == departmentId),
            DataScopes.DepartmentTree => projects.Where(value =>
                value.OwnerUserId == userId
                || value.Members.Any(member => member.UserId == userId)
                || db.DepartmentClosures.Any(path => path.AncestorId == departmentId && path.DescendantId == value.DepartmentId)),
            _ => projects.Where(value => value.OwnerUserId == userId || value.Members.Any(member => member.UserId == userId)),
        };
    }

    /// <summary>取得可見專案與寫入權；看不到時回傳 null（API 一律回 404，不透露專案是否存在）。</summary>
    public async Task<ProjectAccess?> GetAsync(RequestScope scope, Guid projectId, bool track, CancellationToken cancellationToken)
    {
        var query = VisibleProjects(scope).Where(value => value.Id == projectId);
        var project = await (track ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
        if (project is null) return null;

        var memberRole = await db.ProjectMembers.AsNoTracking()
            .Where(value => value.ProjectId == projectId && value.UserId == scope.UserId)
            .Select(value => value.ProjectRole)
            .SingleOrDefaultAsync(cancellationToken);
        var hasWriteRole = project.OwnerUserId == scope.UserId
            || memberRole is ProjectRoles.Manager or ProjectRoles.Member
            || await IsVisibleByScopeAsync(scope, project, cancellationToken);
        var canWrite = scope.Has("projects", "update") && hasWriteRole;
        var canManageMembers = canWrite && scope.HasOption("projects", "assign_member") && (project.OwnerUserId == scope.UserId || memberRole != ProjectRoles.Member);
        return new ProjectAccess(project, memberRole, hasWriteRole, canWrite, canManageMembers);
    }

    /// <summary>建立或移動專案時可選的部門：company 不限；department／own 只能自己部門；department_tree 為自己部門與下層。</summary>
    public async Task<IReadOnlyCollection<Guid>?> AllowedDepartmentIdsAsync(RequestScope scope, CancellationToken cancellationToken)
    {
        var dataScope = EffectiveScope(scope);
        if (dataScope == DataScopes.Company) return null;
        if (scope.DepartmentId is not Guid departmentId) return [];
        if (dataScope != DataScopes.DepartmentTree) return [departmentId];
        return await db.DepartmentClosures.Where(value => value.AncestorId == departmentId).Select(value => value.DescendantId).ToListAsync(cancellationToken);
    }

    /// <summary>沒有部門的帳號（理論上不會發生）退回 own，避免 department 範圍變成「看到部門為空的專案」。</summary>
    private static string EffectiveScope(RequestScope scope) =>
        scope.DataScope != DataScopes.Company && scope.DepartmentId is null ? DataScopes.Own : scope.DataScope;

    /// <summary>不靠成員身分、單憑部門或全公司範圍是否看得到此專案。</summary>
    private async Task<bool> IsVisibleByScopeAsync(RequestScope scope, Project project, CancellationToken cancellationToken) =>
        EffectiveScope(scope) switch
        {
            DataScopes.Company => true,
            DataScopes.Department => project.DepartmentId == scope.DepartmentId,
            DataScopes.DepartmentTree => project.DepartmentId is Guid departmentId
                && await db.DepartmentClosures.AnyAsync(value => value.AncestorId == scope.DepartmentId && value.DescendantId == departmentId, cancellationToken),
            _ => false,
        };
}
