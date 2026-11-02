using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

public sealed record CreateDepartmentRequest(Guid? ParentId, string? Name, string? Code, int? SortOrder);
public sealed record UpdateDepartmentRequest(Guid? ParentId, string? Name, string? Code, int? SortOrder, bool? IsActive);

/// <summary>目前公司的部門樹（company scope）。</summary>
[Route("api/admin/departments")]
public sealed class DepartmentsController(DepartmentService service) : ScopedControllerBase
{
    /// <summary>部門清單也是帳號管理選部門的來源，因此 company.users:read 也可讀取。</summary>
    [HttpGet]
    public async Task<ActionResult> List(CancellationToken cancellationToken) =>
        TryCompany("company.departments", "read", out _) || TryCompany("company.users", "read", out _)
            ? Ok(await service.ListAsync(cancellationToken))
            : Forbidden();

    [HttpPost]
    public async Task<ActionResult> Create(CreateDepartmentRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.departments", "create", out var scope) ? ToResult(await service.CreateAsync(scope, request, cancellationToken)) : Forbidden();

    [HttpPatch("{departmentId:guid}")]
    public async Task<ActionResult> Update(Guid departmentId, UpdateDepartmentRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.departments", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, departmentId, request, cancellationToken)) : Forbidden();

    [HttpDelete("{departmentId:guid}")]
    public async Task<ActionResult> Delete(Guid departmentId, CancellationToken cancellationToken) =>
        TryCompany("company.departments", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, departmentId, cancellationToken)) : Forbidden();
}

/// <summary>部門 CRUD 與移動；所有查詢經 Global Query Filter 限定在 JWT 公司內。</summary>
public sealed class DepartmentService(QuoteDbContext db, AuditService audit)
{
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
