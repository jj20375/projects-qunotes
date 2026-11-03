using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Common;

/// <summary>
/// 公司通訊錄控制器（提供專案負責人、成員選擇彈窗與下拉選單使用）。
/// 僅回傳姓名、部門等必要選單欄位，公司全體成員皆可讀取。
/// </summary>
/// <param name="db">資料庫上下文實體。</param>
[Route("api/directory")]
public sealed class DirectoryController(QuoteDbContext db) : ScopedControllerBase
{
    /// <summary>
    /// 人員下拉選單遠端即時搜尋。
    /// 排序依據：本部門成員置頂，其次為下層部門，其餘部門置後，同層依姓名排序。
    /// </summary>
    /// <param name="keyword">搜尋關鍵字（比對成員姓名或部門名稱）。</param>
    /// <param name="ids">欲強制包含的使用者 ID 清單（用於顯示已選取值）。</param>
    /// <param name="excludeIds">欲排除之使用者 ID 清單（如排除專案負責人）。</param>
    /// <param name="limit">回傳筆數上限（預設 20，最多 50）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回人員清單物件陣列。</returns>
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
    /// 條件分頁查詢專案成員選擇名單表格。
    /// 支援依部門、下層部門樹與公司內部角色組合篩選。
    /// </summary>
    /// <param name="query">分頁與關鍵字查詢條件。</param>
    /// <param name="departmentId">部門篩選 ID。</param>
    /// <param name="includeSubDepartments">是否遞迴包含下層子部門（預設 true）。</param>
    /// <param name="roleId">角色篩選 ID。</param>
    /// <param name="excludeIds">欲排除之成員 ID 清單。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁成員列表與筆數合計。</returns>
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

    /// <summary>
    /// 取得公司通訊錄可供篩選之角色清單（僅回傳名稱與代碼）。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回角色清單陣列。</returns>
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

    /// <summary>
    /// 取得公司組織架構部門清單（下拉選單用）。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回部門陣列。</returns>
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
