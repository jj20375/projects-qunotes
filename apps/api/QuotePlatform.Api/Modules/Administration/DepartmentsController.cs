using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>
/// 建立組織部門請求契約。
/// </summary>
/// <param name="ParentId">上層部門唯一識別碼（必填，公司開通時已具備根部門）。</param>
/// <param name="Name">部門名稱。</param>
/// <param name="Code">部門代碼（選填，在同公司內具唯一性）。</param>
/// <param name="SortOrder">同層排列排序順序號碼。</param>
public sealed record CreateDepartmentRequest(Guid? ParentId, string? Name, string? Code, int? SortOrder);

/// <summary>
/// 更新組織部門請求契約。
/// </summary>
/// <param name="ParentId">新的上層部門 ID（若需移動階層時傳入）。</param>
/// <param name="Name">部門名稱。</param>
/// <param name="Code">部門代碼。</param>
/// <param name="SortOrder">排序順序。</param>
/// <param name="IsActive">是否啟用。</param>
public sealed record UpdateDepartmentRequest(Guid? ParentId, string? Name, string? Code, int? SortOrder, bool? IsActive);

/// <summary>
/// 公司組織架構部門管理控制器（公司租戶範圍）。
/// </summary>
/// <param name="service">組織部門領域服務實體。</param>
[Route("api/admin/departments")]
public sealed class DepartmentsController(DepartmentService service) : ScopedControllerBase
{
    /// <summary>
    /// 取得當前公司完整組織部門清單（包含階層深度與隸屬成員計數）。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回部門資料陣列。</returns>
    [HttpGet]
    public async Task<ActionResult> List(CancellationToken cancellationToken) =>
        TryCompany("company.departments", "read", out _) || TryCompany("company.users", "read", out _)
            ? Ok(await service.ListAsync(cancellationToken))
            : Forbidden();

    /// <summary>
    /// 建立新部門並自動維護閉包表（Closure Table）之階層關係路徑。
    /// </summary>
    /// <param name="request">部門建立輸入內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立成功之部門 ID 物件。</returns>
    [HttpPost]
    public async Task<ActionResult> Create(CreateDepartmentRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.departments", "create", out var scope) ? ToResult(await service.CreateAsync(scope, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新指定部門資料或調整從屬上層關係（支援跨樹移動）。
    /// </summary>
    /// <param name="departmentId">部門唯一識別碼。</param>
    /// <param name="request">更新資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPatch("{departmentId:guid}")]
    public async Task<ActionResult> Update(Guid departmentId, UpdateDepartmentRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.departments", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, departmentId, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除組織部門（若仍有成員或子部門則拒絕刪除；根部門不可刪除）。
    /// </summary>
    /// <param name="departmentId">部門唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    [HttpDelete("{departmentId:guid}")]
    public async Task<ActionResult> Delete(Guid departmentId, CancellationToken cancellationToken) =>
        TryCompany("company.departments", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, departmentId, cancellationToken)) : Forbidden();
}

/// <summary>
/// 組織部門管理與閉包階層樹業務服務。
/// </summary>
/// <param name="db">資料庫上下文實體。</param>
/// <param name="audit">系統稽核日誌服務實體。</param>
public sealed class DepartmentService(QuoteDbContext db, AuditService audit)
{
    /// <summary>
    /// 取得公司部門樹列表。
    /// </summary>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回包含階層深度與成員統計之部門清單。</returns>
    public async Task<IReadOnlyList<object>> ListAsync(CancellationToken cancellationToken)
    {
        var memberCounts = await db.CompanyMemberships.AsNoTracking()
            .Where(value => value.IsActive && value.DepartmentId != null)
            .GroupBy(value => value.DepartmentId)
            .Select(group => new { DepartmentId = group.Key!.Value, Count = group.Count() })
            .ToDictionaryAsync(value => value.DepartmentId, value => value.Count, cancellationToken);
        var depths = await db.DepartmentClosures.AsNoTracking()
            .GroupBy(value => value.DescendantId)
            .Select(group => new { DepartmentId = group.Key, Depth = group.Max(value => value.Depth) })
            .ToDictionaryAsync(value => value.DepartmentId, value => value.Depth, cancellationToken);
        var departments = await db.Departments.AsNoTracking().OrderBy(value => value.SortOrder).ThenBy(value => value.Name).ToListAsync(cancellationToken);
        return departments.Select(value => (object)new
        {
            value.Id,
            value.ParentId,
            value.Code,
            value.Name,
            value.SortOrder,
            value.IsActive,
            depth = depths.GetValueOrDefault(value.Id),
            memberCount = memberCounts.GetValueOrDefault(value.Id),
        }).ToList();
    }

    /// <summary>
    /// 建立新部門節點。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="request">部門建立請求資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新建立之部門物件。</returns>
    public async Task<ServiceResult<object>> CreateAsync(RequestScope scope, CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return ServiceResult<object>.BadRequest("請輸入部門名稱。");
        if (request.ParentId is not Guid parentId || !await db.Departments.AnyAsync(value => value.Id == parentId, cancellationToken)) return ServiceResult<object>.BadRequest("請選擇上層部門。");
        var code = NullIfBlank(request.Code);
        if (code is not null && await db.Departments.AnyAsync(value => value.Code == code, cancellationToken)) return ServiceResult<object>.Conflict("部門代碼已被使用。");

        var department = await DepartmentTree.AddAsync(db, scope.CompanyId!.Value, parentId, request.Name.Trim(), code, request.SortOrder ?? 0, cancellationToken);
        audit.Add(scope.CompanyId, scope.UserId, "department", department.Id, "create", after: new { department.Name, department.Code, department.ParentId });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new { department.Id });
    }

    /// <summary>
    /// 更新指定部門並重新計算樹狀閉包深度。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="departmentId">部門識別碼。</param>
    /// <param name="request">更新資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    public async Task<ServiceResult<object>> UpdateAsync(RequestScope scope, Guid departmentId, UpdateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var department = await db.Departments.SingleOrDefaultAsync(value => value.Id == departmentId, cancellationToken);
        if (department is null) return ServiceResult<object>.NotFound("找不到部門。");
        var before = new { department.Name, department.Code, department.ParentId, department.SortOrder, department.IsActive };

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return ServiceResult<object>.BadRequest("部門名稱不可空白。");
            department.Name = request.Name.Trim();
        }
        if (request.Code is not null)
        {
            var code = NullIfBlank(request.Code);
            if (code is not null && await db.Departments.AnyAsync(value => value.Code == code && value.Id != departmentId, cancellationToken)) return ServiceResult<object>.Conflict("部門代碼已被使用。");
            department.Code = code;
        }
        if (request.SortOrder is int sortOrder) department.SortOrder = sortOrder;
        if (request.IsActive is bool isActive)
        {
            if (!isActive && department.ParentId is null) return ServiceResult<object>.BadRequest("根部門不可停用。");
            department.IsActive = isActive;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (request.ParentId is Guid newParentId && newParentId != department.ParentId)
        {
            if (await MoveAsync(department, newParentId, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);
        }
        department.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "department", department.Id, "update", before, new { department.Name, department.Code, department.ParentId, department.SortOrder, department.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { department.Id });
    }

    /// <summary>
    /// 刪除組織部門及其在閉包表中的所有路徑。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="departmentId">部門識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除結果。</returns>
    public async Task<ServiceResult<Empty>> DeleteAsync(RequestScope scope, Guid departmentId, CancellationToken cancellationToken)
    {
        var department = await db.Departments.SingleOrDefaultAsync(value => value.Id == departmentId, cancellationToken);
        if (department is null) return ServiceResult<Empty>.NotFound("找不到部門。");
        if (department.ParentId is null) return ServiceResult<Empty>.BadRequest("根部門不可刪除。");
        if (await db.Departments.AnyAsync(value => value.ParentId == departmentId, cancellationToken)) return ServiceResult<Empty>.BadRequest("請先移除下層部門。");
        if (await db.CompanyMemberships.AnyAsync(value => value.DepartmentId == departmentId, cancellationToken)) return ServiceResult<Empty>.BadRequest("部門內仍有成員，請先調整成員部門。");

        db.DepartmentClosures.RemoveRange(await db.DepartmentClosures.Where(value => value.DescendantId == departmentId || value.AncestorId == departmentId).ToListAsync(cancellationToken));
        db.Departments.Remove(department);
        audit.Add(scope.CompanyId, scope.UserId, "department", department.Id, "delete", new { department.Name, department.Code });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    /// <summary>
    /// 移動子樹：刪除「子樹外祖先 → 子樹節點」的路徑，再以新上層的祖先路徑 × 子樹內路徑重建。
    /// </summary>
    private async Task<string?> MoveAsync(Department department, Guid newParentId, CancellationToken cancellationToken)
    {
        if (department.ParentId is null) return "根部門不可移動。";
        if (!await db.Departments.AnyAsync(value => value.Id == newParentId, cancellationToken)) return "找不到新的上層部門。";
        var subtree = await db.DepartmentClosures.Where(value => value.AncestorId == department.Id).ToListAsync(cancellationToken);
        var subtreeIds = subtree.Select(value => value.DescendantId).ToHashSet();
        if (subtreeIds.Contains(newParentId)) return "不可把部門移到自己或下層部門底下。";

        var outerPaths = (await db.DepartmentClosures
                .Where(value => subtreeIds.Contains(value.DescendantId) && !subtreeIds.Contains(value.AncestorId))
                .ToListAsync(cancellationToken))
            .ToDictionary(value => (value.AncestorId, value.DescendantId));

        var newAncestors = await db.DepartmentClosures.Where(value => value.DescendantId == newParentId).ToListAsync(cancellationToken);
        foreach (var ancestor in newAncestors)
        {
            foreach (var inner in subtree)
            {
                var depth = ancestor.Depth + inner.Depth + 1;
                // 新舊路徑共用的祖先（例如根部門）沿用同一列只更新深度；EF 不允許同一 key 先刪除再新增。
                if (outerPaths.Remove((ancestor.AncestorId, inner.DescendantId), out var existing))
                {
                    existing.Depth = depth;
                    continue;
                }
                db.DepartmentClosures.Add(new DepartmentClosure { AncestorId = ancestor.AncestorId, DescendantId = inner.DescendantId, CompanyId = department.CompanyId, Depth = depth });
            }
        }
        db.DepartmentClosures.RemoveRange(outerPaths.Values);
        department.ParentId = newParentId;
        return null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
