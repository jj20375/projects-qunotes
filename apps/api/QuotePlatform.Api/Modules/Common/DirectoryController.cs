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
        var members = await query
            .OrderBy(value => value.User.DisplayName)
            .Take(Math.Clamp(limit ?? 20, 1, 50))
            .Select(value => new { userId = value.UserId, value.User.DisplayName, value.DepartmentId })
            .ToListAsync(cancellationToken);
        return Ok(members.Select(value => new { value.userId, value.DisplayName, value.DepartmentId, departmentName = value.DepartmentId is Guid id ? departments.GetValueOrDefault(id) : null }));
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
