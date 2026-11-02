using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>
/// 成員帳號、角色指派、資料範圍與個人權限。
/// </summary>
/// <remarks>
/// 防止越權的三條規則：不能指派或授予超過自己有效權限的角色／權限；不能修改自己的角色、範圍、啟用狀態與個人權限；
/// 受保護的主管理員不可降級、停用或被他人重設密碼。
/// </remarks>
public sealed class MemberService(QuoteDbContext db, PermissionGrantService grantService, AuditService audit)
{
    /// <summary>成員列表：關鍵字比對姓名、帳號、Email，分頁由資料庫處理。</summary>
    public async Task<object> ListAsync(RequestScope scope, ListQuery query, CancellationToken cancellationToken)
    {
        var rootAdminId = await RootAdminMembershipIdAsync(scope, cancellationToken);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(value => value.Id, value => value.Name, cancellationToken);
        var memberships = db.CompanyMemberships.AsNoTracking();
        if (query.KeywordText is string keyword)
        {
            memberships = memberships.Where(value => value.User.DisplayName.Contains(keyword) || value.User.Account.Contains(keyword) || (value.User.Email != null && value.User.Email.Contains(keyword)));
        }
        var total = await memberships.CountAsync(cancellationToken);
        var members = await memberships
            .Include(value => value.User)
            .Include(value => value.Role)
            .OrderBy(value => value.User.DisplayName).ThenBy(value => value.User.Account)
            .Skip(query.Skip).Take(query.PageSizeValue)
            .ToListAsync(cancellationToken);
        var userIds = members.Select(value => value.UserId).ToList();
        var overrideCounts = await db.UserPermissions.AsNoTracking()
            .Where(value => userIds.Contains(value.UserId))
            .GroupBy(value => value.UserId)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.UserId, value => value.Count, cancellationToken);
        var items = members.Select(value => new
        {
            membershipId = value.Id,
            userId = value.UserId,
            value.User.Account,
            value.User.DisplayName,
            value.User.Email,
            userStatus = value.User.Status,
            value.User.LastLoginAt,
            value.DepartmentId,
            departmentName = value.DepartmentId is Guid departmentId ? departments.GetValueOrDefault(departmentId) : null,
            value.RoleId,
            roleName = value.Role.Name,
            roleDataScope = value.Role.DataScope,
            value.DataScopeOverride,
            effectiveDataScope = value.DataScopeOverride ?? value.Role.DataScope,
            value.IsActive,
            isRootAdmin = value.Id == rootAdminId,
            isSelf = value.UserId == scope.UserId,
            personalPermissionCount = overrideCounts.GetValueOrDefault(value.UserId),
        }).ToList();
        return new { total, page = query.PageValue, pageSize = query.PageSizeValue, items };
    }

    public async Task<ServiceResult<object>> CreateAsync(RequestScope scope, CreateMemberRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.DisplayName)) return ServiceResult<object>.BadRequest("請輸入帳號與姓名。");
        if (PasswordRules.Validate(request.Password) is string passwordError) return ServiceResult<object>.BadRequest(passwordError);
        var account = request.Account.Trim();
        if (await db.Users.AnyAsync(value => value.Account == account, cancellationToken)) return ServiceResult<object>.Conflict("帳號已存在。");
        if (request.DepartmentId is not Guid departmentId || !await db.Departments.AnyAsync(value => value.Id == departmentId && value.IsActive, cancellationToken))
        {
            return ServiceResult<object>.BadRequest("請選擇啟用中的部門。");
        }

        // 沒有 assign_role 的人只能建立唯讀成員，避免以「建立帳號」繞過角色指派權限。
        var canAssignRole = scope.HasOption("company.users", "assign_role");
        var roleId = request.RoleId;
        if (!canAssignRole || roleId is null)
        {
            if (roleId is not null || request.DataScopeOverride is not null) return ServiceResult<object>.Forbidden("沒有指派角色的權限。");
            roleId = await db.Roles.Where(value => value.CompanyId == null && value.Code == "viewer").Select(value => (Guid?)value.Id).SingleAsync(cancellationToken);
        }
        if (await CheckAssignableRoleAsync(scope, roleId!.Value, cancellationToken) is string roleError) return ServiceResult<object>.BadRequest(roleError);
        if (request.DataScopeOverride is not null && !DataScopes.IsValid(request.DataScopeOverride)) return ServiceResult<object>.BadRequest("資料範圍不正確。");

        var user = new User { Account = account, DisplayName = request.DisplayName.Trim(), Email = NullIfBlank(request.Email) };
        user.PasswordHash = PasswordRules.Hash(user, request.Password!);
        db.Users.Add(user);
        var membership = new CompanyMembership
        {
            UserId = user.Id,
            DepartmentId = departmentId,
            RoleId = roleId.Value,
            DataScopeOverride = request.DataScopeOverride,
        };
        db.CompanyMemberships.Add(membership);
        audit.Add(scope.CompanyId, scope.UserId, "membership", membership.Id, "create", after: new { account, user.DisplayName, departmentId, roleId, request.DataScopeOverride });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new { membershipId = membership.Id, userId = user.Id });
    }

    public async Task<ServiceResult<object>> UpdateAsync(RequestScope scope, Guid membershipId, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        var membership = await db.CompanyMemberships.Include(value => value.User).SingleOrDefaultAsync(value => value.Id == membershipId, cancellationToken);
        if (membership is null) return ServiceResult<object>.NotFound("找不到成員。");
        var isRootAdmin = membership.Id == await RootAdminMembershipIdAsync(scope, cancellationToken);
        var isSelf = membership.UserId == scope.UserId;
        var before = Snapshot(membership);

        var changesRole = request.RoleId is Guid newRoleId && newRoleId != membership.RoleId;
        var changesScope = request.ClearDataScopeOverride == true ? membership.DataScopeOverride is not null : request.DataScopeOverride is not null && request.DataScopeOverride != membership.DataScopeOverride;
        var changesActive = request.IsActive is bool active && active != membership.IsActive;
        if ((changesRole || changesScope || changesActive) && (isRootAdmin || isSelf))
        {
            return ServiceResult<object>.BadRequest(isRootAdmin ? "主管理員的角色、資料範圍與啟用狀態不可變更。" : "不可變更自己的角色、資料範圍或啟用狀態。");
        }
        if ((changesRole || changesScope) && !scope.HasOption("company.users", "assign_role")) return ServiceResult<object>.Forbidden("沒有指派角色的權限。");

        if (changesRole)
        {
            if (await CheckAssignableRoleAsync(scope, request.RoleId!.Value, cancellationToken) is string roleError) return ServiceResult<object>.BadRequest(roleError);
            membership.RoleId = request.RoleId.Value;
        }
        if (changesScope)
        {
            if (request.ClearDataScopeOverride != true && !DataScopes.IsValid(request.DataScopeOverride)) return ServiceResult<object>.BadRequest("資料範圍不正確。");
            membership.DataScopeOverride = request.ClearDataScopeOverride == true ? null : request.DataScopeOverride;
        }
        if (changesActive) membership.IsActive = request.IsActive!.Value;

        var changesDepartment = request.DepartmentId is Guid departmentId && departmentId != membership.DepartmentId;
        if (changesDepartment)
        {
            if (!await db.Departments.AnyAsync(value => value.Id == request.DepartmentId && value.IsActive, cancellationToken)) return ServiceResult<object>.BadRequest("請選擇啟用中的部門。");
            membership.DepartmentId = request.DepartmentId;
        }
        if (request.DisplayName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName)) return ServiceResult<object>.BadRequest("姓名不可空白。");
            membership.User.DisplayName = request.DisplayName.Trim();
        }
        if (request.Email is not null) membership.User.Email = NullIfBlank(request.Email);

        membership.UpdatedAt = DateTimeOffset.UtcNow;
        membership.User.UpdatedAt = DateTimeOffset.UtcNow;
        // 部門也寫在 JWT（department_id），所以同樣要讓舊 token 失效。
        if (changesRole || changesScope || changesActive || changesDepartment) membership.User.AuthVersion++;
        audit.Add(scope.CompanyId, scope.UserId, "membership", membership.Id, "update", before, Snapshot(membership));
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { membershipId = membership.Id });
    }

    public async Task<ServiceResult<Empty>> ResetPasswordAsync(RequestScope scope, Guid membershipId, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var membership = await db.CompanyMemberships.Include(value => value.User).SingleOrDefaultAsync(value => value.Id == membershipId, cancellationToken);
        if (membership is null) return ServiceResult<Empty>.NotFound("找不到成員。");
        if (membership.Id == await RootAdminMembershipIdAsync(scope, cancellationToken) && membership.UserId != scope.UserId)
        {
            return ServiceResult<Empty>.Forbidden("主管理員的密碼只能由本人或平台管理員重設。");
        }
        if (PasswordRules.Validate(request.Password) is string passwordError) return ServiceResult<Empty>.BadRequest(passwordError);

        // 帳號可能同時屬於多間公司，重設密碼影響所有公司，因此一併撤銷所有 refresh token。
        membership.User.PasswordHash = PasswordRules.Hash(membership.User, request.Password!);
        membership.User.AuthVersion++;
        membership.User.FailedLoginCount = 0;
        membership.User.LockedUntil = null;
        membership.User.UpdatedAt = DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        foreach (var token in await db.RefreshTokens.Where(value => value.UserId == membership.UserId && value.RevokedAt == null).ToListAsync(cancellationToken)) token.RevokedAt = now;
        audit.Add(scope.CompanyId, scope.UserId, "membership", membership.Id, "reset_password");
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    public async Task<ServiceResult<object>> GetPermissionsAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        var membership = await db.CompanyMemberships.AsNoTracking().SingleOrDefaultAsync(value => value.Id == membershipId, cancellationToken);
        if (membership is null) return ServiceResult<object>.NotFound("找不到成員。");
        var company = await db.Companies.AsNoTracking().SingleAsync(value => value.Id == membership.CompanyId, cancellationToken);
        var overrides = await db.UserPermissions.AsNoTracking()
            .Include(value => value.Permission)
            .Where(value => value.UserId == membership.UserId)
            .OrderBy(value => value.Permission.SortOrder)
            .ToListAsync(cancellationToken);
        var roleGrants = await grantService.ForRolesAsync([membership.RoleId], cancellationToken);
        var effective = await grantService.ForMembershipAsync(membership, company, cancellationToken);
        return ServiceResult<object>.Ok(new
        {
            roleGrants = roleGrants.Select(PermissionInputs.ToDto),
            overrides = overrides.Select(value => new
            {
                code = value.Permission.Code,
                value.Effect,
                value.PerCreate,
                value.PerRead,
                value.PerUpdate,
                value.PerDelete,
                options = PermissionGrantService.ReadOptions(value.OptionsJson),
                value.Reason,
                value.ExpiresAt,
                isExpired = value.ExpiresAt <= DateTimeOffset.UtcNow,
            }),
            effective = effective.Select(PermissionInputs.ToDto),
        });
    }

    public async Task<ServiceResult<object>> ReplacePermissionsAsync(RequestScope scope, Guid membershipId, ReplaceUserPermissionsRequest request, CancellationToken cancellationToken)
    {
        var membership = await db.CompanyMemberships.Include(value => value.User).SingleOrDefaultAsync(value => value.Id == membershipId, cancellationToken);
        if (membership is null) return ServiceResult<object>.NotFound("找不到成員。");
        if (membership.UserId == scope.UserId) return ServiceResult<object>.BadRequest("不可設定自己的個人權限。");

        var items = request.Items ?? [];
        if (items.Any(value => value.Effect is not (PermissionEffects.Grant or PermissionEffects.Deny))) return ServiceResult<object>.BadRequest("個人權限只能是加權（grant）或禁止（deny）。");
        if (items.Any(value => value.ExpiresAt is DateTimeOffset expiresAt && expiresAt <= DateTimeOffset.UtcNow)) return ServiceResult<object>.BadRequest("到期時間必須晚於現在。");

        var permissionIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var effect in new[] { PermissionEffects.Grant, PermissionEffects.Deny })
        {
            var inputs = items.Where(value => value.Effect == effect)
                .Select(value => new PermissionAllowanceInput(value.Code, value.PerCreate, value.PerRead, value.PerUpdate, value.PerDelete, value.Options))
                .ToList();
            var (error, ids) = await PermissionInputs.ValidateAsync(db, inputs, "company", cancellationToken);
            if (error is not null) return ServiceResult<object>.BadRequest(error);
            foreach (var (code, id) in ids) permissionIds[code] = id;
            // 加權與禁止都只能針對自己擁有的權限，避免成員管理者把自己沒有的權限給別人。
            if (!PermissionGrantRules.IsSubsetOf(inputs.Select(value => value.ToAllowance().ToGrant()), scope.Grants))
            {
                return ServiceResult<object>.Forbidden("不可設定超過自己權限範圍的個人權限。");
            }
        }

        // 先刪後加會碰到 (CompanyId, UserId, PermissionId, Effect) 唯一索引，分兩次 SaveChanges 並包在同一交易內。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.UserPermissions.Include(value => value.Permission).Where(value => value.UserId == membership.UserId).ToListAsync(cancellationToken);
        var before = existing.Select(value => new { value.Permission.Code, value.Effect, value.PerCreate, value.PerRead, value.PerUpdate, value.PerDelete, value.OptionsJson, value.ExpiresAt }).ToList();
        db.UserPermissions.RemoveRange(existing);
        await db.SaveChangesAsync(cancellationToken);

        var rows = items
            .Where(value => value.PerCreate || value.PerRead || value.PerUpdate || value.PerDelete || (value.Options?.Length ?? 0) > 0)
            .Select(value => new UserPermission
            {
                UserId = membership.UserId,
                PermissionId = permissionIds[value.Code],
                Effect = value.Effect,
                PerCreate = value.PerCreate,
                PerRead = value.PerRead,
                PerUpdate = value.PerUpdate,
                PerDelete = value.PerDelete,
                OptionsJson = PermissionInputs.SerializeOptions(value.Options ?? []),
                Reason = string.IsNullOrWhiteSpace(value.Reason) ? null : value.Reason.Trim(),
                ExpiresAt = value.ExpiresAt,
                CreatedBy = scope.UserId,
            })
            .ToList();
        db.UserPermissions.AddRange(rows);
        membership.User.AuthVersion++;
        audit.Add(scope.CompanyId, scope.UserId, "membership", membership.Id, "replace_permissions", before, items);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { count = rows.Count });
    }

    /// <summary>角色必須是公司角色（系統或本公司自訂），且權限不得超過操作者自己的有效權限。</summary>
    private async Task<string?> CheckAssignableRoleAsync(RequestScope scope, Guid roleId, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(value => value.Id == roleId && value.Scope == "company" && (value.CompanyId == null || value.CompanyId == scope.CompanyId), cancellationToken);
        if (role is null) return "角色不存在。";
        var roleGrants = await grantService.ForRolesAsync([roleId], cancellationToken);
        return PermissionGrantRules.IsSubsetOf(roleGrants, scope.Grants) ? null : $"不可指派權限比自己高的角色「{role.Name}」。";
    }

    private Task<Guid?> RootAdminMembershipIdAsync(RequestScope scope, CancellationToken cancellationToken) =>
        db.Companies.AsNoTracking().Where(value => value.Id == scope.CompanyId).Select(value => value.RootAdminMembershipId).SingleAsync(cancellationToken);

    private static object Snapshot(CompanyMembership value) => new { value.User.DisplayName, value.User.Email, value.DepartmentId, value.RoleId, value.DataScopeOverride, value.IsActive };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
