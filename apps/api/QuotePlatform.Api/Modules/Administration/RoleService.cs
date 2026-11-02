using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>
/// 角色與角色權限。公司只能管理自己的自訂角色；系統角色由平台調整，完整存取角色（platform-admin、company-admin）由 Seeder 維護不可修改。
/// </summary>
public sealed partial class RoleService(QuoteDbContext db, PermissionGrantService grantService, AuditService audit)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,49}$")]
    private static partial Regex CodePattern();

    public async Task<IReadOnlyList<object>> ListCompanyRolesAsync(RequestScope scope, CancellationToken cancellationToken)
    {
        var memberCounts = await db.CompanyMemberships.AsNoTracking()
            .GroupBy(value => value.RoleId)
            .Select(group => new { RoleId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.RoleId, value => value.Count, cancellationToken);
        var roles = await db.Roles.AsNoTracking()
            .Where(value => value.Scope == "company" && (value.CompanyId == null || value.CompanyId == scope.CompanyId))
            .OrderByDescending(value => value.IsSystem).ThenBy(value => value.CreatedAt)
            .ToListAsync(cancellationToken);
        return roles.Select(value => (object)new
        {
            value.Id,
            value.Code,
            value.Name,
            value.DataScope,
            value.IsSystem,
            value.IsFullAccess,
            isEditable = value.CompanyId == scope.CompanyId,
            memberCount = memberCounts.GetValueOrDefault(value.Id),
        }).ToList();
    }

    public async Task<IReadOnlyList<object>> ListSystemRolesAsync(CancellationToken cancellationToken)
    {
        // 平台檢視系統角色在所有公司的使用人數，必須明確忽略公司 filter。
        var memberCounts = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(value => value.RoleId)
            .Select(group => new { RoleId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.RoleId, value => value.Count, cancellationToken);
        var systemUserCounts = await db.Users.AsNoTracking()
            .Where(value => value.SystemRoleId != null)
            .GroupBy(value => value.SystemRoleId!.Value)
            .Select(group => new { RoleId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.RoleId, value => value.Count, cancellationToken);
        var roles = await db.Roles.AsNoTracking().Where(value => value.CompanyId == null).OrderBy(value => value.Scope).ThenBy(value => value.CreatedAt).ToListAsync(cancellationToken);
        return roles.Select(value => (object)new
        {
            value.Id,
            value.Code,
            value.Name,
            value.Scope,
            value.DataScope,
            value.IsSystem,
            value.IsFullAccess,
            isEditable = !value.IsFullAccess,
            memberCount = memberCounts.GetValueOrDefault(value.Id) + systemUserCounts.GetValueOrDefault(value.Id),
        }).ToList();
    }

    public async Task<ServiceResult<object>> GetPermissionsAsync(RequestScope scope, Guid roleId, CancellationToken cancellationToken)
    {
        if (await FindVisibleRoleAsync(scope, roleId, cancellationToken) is null) return ServiceResult<object>.NotFound("找不到角色。");
        var links = await db.RolePermissions.AsNoTracking().Include(value => value.Permission).Where(value => value.RoleId == roleId).OrderBy(value => value.Permission.SortOrder).ToListAsync(cancellationToken);
        return ServiceResult<object>.Ok(links.Select(value => new
        {
            code = value.Permission.Code,
            value.PerCreate,
            value.PerRead,
            value.PerUpdate,
            value.PerDelete,
            options = PermissionGrantService.ReadOptions(value.OptionsJson),
        }));
    }

    public async Task<ServiceResult<object>> CreateCompanyRoleAsync(RequestScope scope, CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!CodePattern().IsMatch(code)) return ServiceResult<object>.BadRequest("角色代碼只能使用小寫英數與連字號，長度 2～50。");
        if (string.IsNullOrWhiteSpace(request.Name)) return ServiceResult<object>.BadRequest("請輸入角色名稱。");
        if (!DataScopes.IsValid(request.DataScope)) return ServiceResult<object>.BadRequest("資料範圍不正確。");
        if (await db.Roles.AnyAsync(value => value.Code == code && (value.CompanyId == null || value.CompanyId == scope.CompanyId), cancellationToken)) return ServiceResult<object>.Conflict("角色代碼已被使用。");

        var permissions = request.Permissions ?? [];
        if (await ValidateCompanyPermissionsAsync(scope, permissions, cancellationToken) is { } error) return error;

        var role = new Role { CompanyId = scope.CompanyId, Scope = "company", Code = code, Name = request.Name.Trim(), DataScope = request.DataScope! };
        db.Roles.Add(role);
        await WritePermissionsAsync(role.Id, permissions, "company", cancellationToken);
        audit.Add(scope.CompanyId, scope.UserId, "role", role.Id, "create", after: new { role.Code, role.Name, role.DataScope, permissions });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new { role.Id });
    }

    public async Task<ServiceResult<object>> UpdateRoleAsync(RequestScope scope, Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await FindEditableRoleAsync(scope, roleId, cancellationToken);
        if (role is null) return ServiceResult<object>.NotFound("找不到可修改的角色。");
        if (request.DataScope is not null && !DataScopes.IsValid(request.DataScope)) return ServiceResult<object>.BadRequest("資料範圍不正確。");
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name)) return ServiceResult<object>.BadRequest("角色名稱不可空白。");

        var before = new { role.Name, role.DataScope };
        var scopeChanged = request.DataScope is not null && request.DataScope != role.DataScope;
        role.Name = request.Name?.Trim() ?? role.Name;
        role.DataScope = request.DataScope ?? role.DataScope;
        role.UpdatedAt = DateTimeOffset.UtcNow;
        if (scopeChanged) await BumpRoleMembersAsync(role, cancellationToken);
        audit.Add(scope.CompanyId, scope.UserId, "role", role.Id, "update", before, new { role.Name, role.DataScope });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { role.Id });
    }

    public async Task<ServiceResult<object>> ReplacePermissionsAsync(RequestScope scope, Guid roleId, ReplaceRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        var role = await FindEditableRoleAsync(scope, roleId, cancellationToken);
        if (role is null) return ServiceResult<object>.NotFound("找不到可修改的角色。");
        var permissions = request.Permissions ?? [];

        if (role.Scope == "company")
        {
            if (scope.IsCompany)
            {
                if (await ValidateCompanyPermissionsAsync(scope, permissions, cancellationToken) is { } error) return error;
            }
            else
            {
                // 平台調整公司類系統角色：上限是 company-admin 的原始權限（不套任何公司的簡化設定）。
                var (error, _) = await PermissionInputs.ValidateAsync(db, permissions, "company", cancellationToken);
                if (error is not null) return ServiceResult<object>.BadRequest(error);
                var companyAdmin = await db.Roles.AsNoTracking().SingleAsync(value => value.CompanyId == null && value.Code == PermissionCatalog.CompanyAdmin, cancellationToken);
                if (!PermissionGrantRules.IsSubsetOf(permissions.Select(value => value.ToAllowance().ToGrant()), await grantService.ForRolesAsync([companyAdmin.Id], cancellationToken)))
                {
                    return ServiceResult<object>.BadRequest("公司角色的權限不可超過公司管理員。");
                }
            }
        }
        else
        {
            var (error, _) = await PermissionInputs.ValidateAsync(db, permissions, "platform", cancellationToken);
            if (error is not null) return ServiceResult<object>.BadRequest(error);
        }

        var before = await db.RolePermissions.AsNoTracking().Include(value => value.Permission).Where(value => value.RoleId == role.Id)
            .Select(value => new { value.Permission.Code, value.PerCreate, value.PerRead, value.PerUpdate, value.PerDelete, value.OptionsJson }).ToListAsync(cancellationToken);
        // role_permissions 以 (RoleId, PermissionId) 為主鍵，EF 不允許同一 key 先刪後加，因此分兩次儲存並包在同一交易。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(value => value.RoleId == role.Id).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await WritePermissionsAsync(role.Id, permissions, role.Scope, cancellationToken);
        role.UpdatedAt = DateTimeOffset.UtcNow;
        await BumpRoleMembersAsync(role, cancellationToken);
        audit.Add(scope.CompanyId, scope.UserId, "role", role.Id, "replace_permissions", before, permissions);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { role.Id });
    }

    public async Task<ServiceResult<Empty>> DeleteCompanyRoleAsync(RequestScope scope, Guid roleId, CancellationToken cancellationToken)
    {
        var role = await FindEditableRoleAsync(scope, roleId, cancellationToken);
        if (role is null) return ServiceResult<Empty>.NotFound("找不到可刪除的角色。");
        if (await db.CompanyMemberships.AnyAsync(value => value.RoleId == roleId, cancellationToken)) return ServiceResult<Empty>.BadRequest("仍有成員使用此角色，請先改派。");
        db.Roles.Remove(role);
        audit.Add(scope.CompanyId, scope.UserId, "role", role.Id, "delete", new { role.Code, role.Name });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    /// <summary>公司自訂角色必須同時落在 company-admin 上限與操作者自己的有效權限之內。</summary>
    private async Task<ServiceResult<object>?> ValidateCompanyPermissionsAsync(RequestScope scope, IReadOnlyCollection<PermissionAllowanceInput> permissions, CancellationToken cancellationToken)
    {
        var (error, _) = await PermissionInputs.ValidateAsync(db, permissions, "company", cancellationToken);
        if (error is not null) return ServiceResult<object>.BadRequest(error);
        var company = await db.Companies.AsNoTracking().SingleAsync(value => value.Id == scope.CompanyId, cancellationToken);
        var grants = permissions.Select(value => value.ToAllowance().ToGrant()).ToList();
        if (!PermissionGrantRules.IsSubsetOf(grants, await grantService.CompanyCeilingAsync(company, cancellationToken))) return ServiceResult<object>.BadRequest("角色權限不可超過公司管理員。");
        if (!PermissionGrantRules.IsSubsetOf(grants, scope.Grants)) return ServiceResult<object>.Forbidden("不可設定超過自己權限範圍的角色權限。");
        return null;
    }

    private async Task WritePermissionsAsync(Guid roleId, IEnumerable<PermissionAllowanceInput> permissions, string scopeName, CancellationToken cancellationToken)
    {
        var ids = await db.Permissions.AsNoTracking().Where(value => value.Scope == scopeName).ToDictionaryAsync(value => value.Code, value => value.Id, StringComparer.Ordinal, cancellationToken);
        db.RolePermissions.AddRange(permissions
            .Where(value => value.PerCreate || value.PerRead || value.PerUpdate || value.PerDelete || (value.Options?.Length ?? 0) > 0)
            .Select(value => new RolePermission
            {
                RoleId = roleId,
                PermissionId = ids[value.Code],
                PerCreate = value.PerCreate,
                PerRead = value.PerRead,
                PerUpdate = value.PerUpdate,
                PerDelete = value.PerDelete,
                OptionsJson = PermissionInputs.SerializeOptions(value.Options ?? []),
            }));
    }

    /// <summary>角色權限或資料範圍異動後，使用此角色的所有帳號舊 token 立即失效。</summary>
    private async Task BumpRoleMembersAsync(Role role, CancellationToken cancellationToken)
    {
        var memberIds = await db.CompanyMemberships.IgnoreQueryFilters()
            .Where(value => value.RoleId == role.Id && (role.CompanyId == null || value.CompanyId == role.CompanyId))
            .Select(value => value.UserId)
            .ToListAsync(cancellationToken);
        memberIds.AddRange(await db.Users.Where(value => value.SystemRoleId == role.Id).Select(value => value.Id).ToListAsync(cancellationToken));
        await PermissionInputs.BumpAuthVersionAsync(db, memberIds, cancellationToken);
    }

    private Task<Role?> FindVisibleRoleAsync(RequestScope scope, Guid roleId, CancellationToken cancellationToken) =>
        scope.IsPlatform
            ? db.Roles.SingleOrDefaultAsync(value => value.Id == roleId && value.CompanyId == null, cancellationToken)
            : db.Roles.SingleOrDefaultAsync(value => value.Id == roleId && value.Scope == "company" && (value.CompanyId == null || value.CompanyId == scope.CompanyId), cancellationToken);

    /// <summary>公司只能改自己的自訂角色；平台只能改非完整存取的系統角色。</summary>
    private Task<Role?> FindEditableRoleAsync(RequestScope scope, Guid roleId, CancellationToken cancellationToken) =>
        scope.IsPlatform
            ? db.Roles.SingleOrDefaultAsync(value => value.Id == roleId && value.CompanyId == null && !value.IsFullAccess, cancellationToken)
            : db.Roles.SingleOrDefaultAsync(value => value.Id == roleId && value.CompanyId == scope.CompanyId, cancellationToken);
}
