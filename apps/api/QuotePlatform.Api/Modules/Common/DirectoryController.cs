using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Common;

/// <summary>
/// 公司通訊錄：選專案負責人、成員、部門時使用。只回傳姓名與部門等選單必要欄位，
/// 任何公司成員都可讀取（帳號、Email 等管理資料仍需 <c>company.users</c>）。
/// </summary>
[Route("api/directory")]
public sealed class DirectoryController(QuoteDbContext db) : ScopedControllerBase
{
    /// <summary>
    /// 人員下拉的遠端搜尋：<c>keyword</c> 比對姓名與部門名稱，最多回傳 <c>limit</c> 筆（預設 20、上限 50）；
    /// <c>ids</c> 取回指定人員（顯示已選取的值），<c>excludeIds</c> 排除指定人員（例如專案負責人不必再列為成員）。
    /// 排序：自己部門的人最前面，其次是下層部門，最後才是其他部門，同一層再依姓名；
    /// 這樣沒輸入關鍵字時，選專案成員一打開就能看到同部門的同事。
    /// </summary>
    [HttpGet("users")]
    public async Task<ActionResult> Users([FromQuery] string? keyword, [FromQuery] List<Guid>? ids, [FromQuery] List<Guid>? excludeIds, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (!RequestScope.TryRead(User, out var scope) || !scope.IsCompany) return Forbidden();
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(value => value.Id, value => value.Name, cancellationToken);
        var query = db.CompanyMemberships.AsNoTracking().Where(value => value.IsActive && value.User.Status == "active");
        if (ids is { Count: > 0 }) query = query.Where(value => ids.Contains(value.UserId));
        if (excludeIds is { Count: > 0 }) query = query.Where(value => !excludeIds.Contains(value.UserId));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var text = keyword.Trim();
            var departmentIds = departments.Where(pair => pair.Value.Contains(text, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToList();
            query = query.Where(value => value.User.DisplayName.Contains(text) || (value.DepartmentId != null && departmentIds.Contains(value.DepartmentId.Value)));
        }
        var myDepartmentId = scope.DepartmentId;
        var subtree = myDepartmentId is Guid departmentId
            ? await db.DepartmentClosures.AsNoTracking().Where(value => value.AncestorId == departmentId && value.DescendantId != departmentId).Select(value => value.DescendantId).ToListAsync(cancellationToken)
            : [];
        var members = await query
            .OrderBy(value => value.DepartmentId == myDepartmentId ? 0 : value.DepartmentId != null && subtree.Contains(value.DepartmentId.Value) ? 1 : 2)
            .ThenBy(value => value.User.DisplayName)
            .Take(Math.Clamp(limit ?? 20, 1, 50))
            .Select(value => new { userId = value.UserId, value.User.DisplayName, value.DepartmentId })
            .ToListAsync(cancellationToken);
        return Ok(members.Select(value => new { value.userId, value.DisplayName, value.DepartmentId, departmentName = value.DepartmentId is Guid id ? departments.GetValueOrDefault(id) : null }));
    }

    /// <summary>
    /// 選專案成員用的名單（表格）：<c>keyword</c> 比對姓名，<c>departmentId</c> 篩部門（<c>includeSubDepartments</c> 預設含下層），
    /// <c>roleId</c> 篩角色，<c>excludeIds</c> 排除已在專案裡的人；分頁由後端處理，回傳 <c>{ total, page, pageSize, items }</c>。
    /// 排序同 <see cref="Users"/>：自己部門、下層部門、其他部門，同一層依姓名。
    /// </summary>
    [HttpGet("members")]
    public async Task<ActionResult> Members(
        [FromQuery] ListQuery query,
        [FromQuery] Guid? departmentId,
        [FromQuery] bool? includeSubDepartments,
        [FromQuery] Guid? roleId,
        [FromQuery] List<Guid>? excludeIds,
        CancellationToken cancellationToken)
    {
        if (!RequestScope.TryRead(User, out var scope) || !scope.IsCompany) return Forbidden();
        var members = db.CompanyMemberships.AsNoTracking().Where(value => value.IsActive && value.User.Status == "active");
        if (excludeIds is { Count: > 0 }) members = members.Where(value => !excludeIds.Contains(value.UserId));
        if (query.KeywordText is string keyword) members = members.Where(value => value.User.DisplayName.Contains(keyword));
        if (roleId is Guid role) members = members.Where(value => value.RoleId == role);
        if (departmentId is Guid filterDepartment)
        {
            members = includeSubDepartments ?? true
                ? members.Where(value => db.DepartmentClosures.Any(path => path.AncestorId == filterDepartment && path.DescendantId == value.DepartmentId))
                : members.Where(value => value.DepartmentId == filterDepartment);
        }

        var myDepartmentId = scope.DepartmentId;
        var subtree = myDepartmentId is Guid mine
            ? await db.DepartmentClosures.AsNoTracking().Where(value => value.AncestorId == mine && value.DescendantId != mine).Select(value => value.DescendantId).ToListAsync(cancellationToken)
            : [];
        var total = await members.CountAsync(cancellationToken);
        var rows = await members
            .OrderBy(value => value.DepartmentId == myDepartmentId ? 0 : value.DepartmentId != null && subtree.Contains(value.DepartmentId.Value) ? 1 : 2)
            .ThenBy(value => value.User.DisplayName)
            .Skip(query.Skip)
            .Take(query.PageSizeValue)
            .Select(value => new
            {
                userId = value.UserId,
                value.User.DisplayName,
                value.DepartmentId,
                departmentName = db.Departments.Where(department => department.Id == value.DepartmentId).Select(department => department.Name).FirstOrDefault(),
                value.RoleId,
                roleName = value.Role.Name,
            })
            .ToListAsync(cancellationToken);
        return Ok(new { total, page = query.PageValue, pageSize = query.PageSizeValue, items = rows });
    }

    /// <summary>名單篩選用的角色（系統內建的公司角色與本公司自訂角色），只回傳名稱。</summary>
    [HttpGet("roles")]
    public async Task<ActionResult> Roles(CancellationToken cancellationToken)
    {
        if (!RequestScope.TryRead(User, out var scope) || !scope.IsCompany) return Forbidden();
        return Ok(await db.Roles.AsNoTracking()
            .Where(value => value.Scope == "company" && (value.CompanyId == null || value.CompanyId == scope.CompanyId))
            .OrderByDescending(value => value.IsSystem).ThenBy(value => value.CreatedAt)
            .Select(value => new { value.Id, value.Name })
            .ToListAsync(cancellationToken));
    }

    [HttpGet("departments")]
    public async Task<ActionResult> Departments(CancellationToken cancellationToken)
    {
        if (!RequestScope.TryRead(User, out var scope) || !scope.IsCompany) return Forbidden();
        return Ok(await db.Departments.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.SortOrder).ThenBy(value => value.Name)
            .Select(value => new { value.Id, value.ParentId, value.Name })
            .ToListAsync(cancellationToken));
    }
}
