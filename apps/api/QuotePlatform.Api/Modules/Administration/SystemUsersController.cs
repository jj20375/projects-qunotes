using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

public sealed record CreateSystemUserRequest(string? Account, string? DisplayName, string? Email, string? Password, Guid? RoleId);
public sealed record UpdateSystemUserRequest(string? DisplayName, string? Email, Guid? RoleId, string? Status);

/// <summary>系統帳號（platform scope）：只列出 SystemRoleId 非空的帳號。</summary>
[Route("api/platform/system-users")]
public sealed class SystemUsersController(SystemUserService service) : ScopedControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryPlatform("platform.system_users", "read", out _) ? Ok(await service.ListAsync(query, cancellationToken)) : Forbidden();

    [HttpPost]
    public async Task<ActionResult> Create(CreateSystemUserRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.system_users", "create", out var scope) ? ToResult(await service.CreateAsync(scope, request, cancellationToken)) : Forbidden();

    [HttpPatch("{userId:guid}")]
    public async Task<ActionResult> Update(Guid userId, UpdateSystemUserRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.system_users", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, userId, request, cancellationToken)) : Forbidden();

    [HttpPost("{userId:guid}/reset-password")]
    public async Task<ActionResult> ResetPassword(Guid userId, ResetPasswordRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.system_users", "update", out var scope, "reset_password") ? ToResult(await service.ResetPasswordAsync(scope, userId, request, cancellationToken)) : Forbidden();
}

/// <summary>系統帳號管理。</summary>
public sealed class SystemUserService(QuoteDbContext db, AuditService audit)
{
    /// <summary>系統帳號列表：關鍵字比對帳號、姓名、Email，分頁由資料庫處理。</summary>
    public async Task<object> ListAsync(ListQuery query, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking().Where(value => value.SystemRoleId != null);
        if (query.KeywordText is string keyword)
        {
            users = users.Where(value => value.Account.Contains(keyword) || value.DisplayName.Contains(keyword) || (value.Email != null && value.Email.Contains(keyword)));
        }
        var total = await users.CountAsync(cancellationToken);
        var items = await users
            .OrderBy(value => value.Account)
            .Skip(query.Skip).Take(query.PageSizeValue)
            .Select(value => new { value.Id, value.Account, value.DisplayName, value.Email, value.Status, value.SystemRoleId, roleName = value.SystemRole!.Name, value.LastLoginAt, value.LockedUntil })
            .ToListAsync(cancellationToken);
        return new { total, page = query.PageValue, pageSize = query.PageSizeValue, items };
    }

    public async Task<ServiceResult<object>> CreateAsync(RequestScope scope, CreateSystemUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.DisplayName)) return ServiceResult<object>.BadRequest("請輸入帳號與姓名。");
        if (PasswordRules.Validate(request.Password) is string passwordError) return ServiceResult<object>.BadRequest(passwordError);
        if (!await IsPlatformRoleAsync(request.RoleId, cancellationToken)) return ServiceResult<object>.BadRequest("請選擇平台角色。");
        var account = request.Account.Trim();
        if (await db.Users.AnyAsync(value => value.Account == account, cancellationToken)) return ServiceResult<object>.Conflict("帳號已存在。");

        var user = new User { Account = account, DisplayName = request.DisplayName.Trim(), Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(), SystemRoleId = request.RoleId };
        user.PasswordHash = PasswordRules.Hash(user, request.Password!);
        db.Users.Add(user);
        audit.Add(null, scope.UserId, "system_user", user.Id, "create", after: new { account, request.RoleId });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Created(new { user.Id });
    }

    public async Task<ServiceResult<object>> UpdateAsync(RequestScope scope, Guid userId, UpdateSystemUserRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(value => value.Id == userId && value.SystemRoleId != null, cancellationToken);
        if (user is null) return ServiceResult<object>.NotFound("找不到系統帳號。");
        if (request.Status is not (null or "active" or "disabled")) return ServiceResult<object>.BadRequest("狀態只能是 active 或 disabled。");
        var changesAccess = (request.RoleId is Guid roleId && roleId != user.SystemRoleId) || (request.Status is not null && request.Status != user.Status);
        if (changesAccess && userId == scope.UserId) return ServiceResult<object>.BadRequest("不可變更自己的角色或啟用狀態。");
        if (request.RoleId is not null && !await IsPlatformRoleAsync(request.RoleId, cancellationToken)) return ServiceResult<object>.BadRequest("請選擇平台角色。");

        var before = new { user.DisplayName, user.Email, user.SystemRoleId, user.Status };
        if (!string.IsNullOrWhiteSpace(request.DisplayName)) user.DisplayName = request.DisplayName.Trim();
        if (request.Email is not null) user.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        user.SystemRoleId = request.RoleId ?? user.SystemRoleId;
        user.Status = request.Status ?? user.Status;
        if (changesAccess) user.AuthVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(null, scope.UserId, "system_user", user.Id, "update", before, new { user.DisplayName, user.Email, user.SystemRoleId, user.Status });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { user.Id });
    }

    public async Task<ServiceResult<Empty>> ResetPasswordAsync(RequestScope scope, Guid userId, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(value => value.Id == userId && value.SystemRoleId != null, cancellationToken);
        if (user is null) return ServiceResult<Empty>.NotFound("找不到系統帳號。");
        if (PasswordRules.Validate(request.Password) is string passwordError) return ServiceResult<Empty>.BadRequest(passwordError);
        user.PasswordHash = PasswordRules.Hash(user, request.Password!);
        user.AuthVersion++;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        foreach (var token in await db.RefreshTokens.Where(value => value.UserId == userId && value.RevokedAt == null).ToListAsync(cancellationToken)) token.RevokedAt = now;
        audit.Add(null, scope.UserId, "system_user", user.Id, "reset_password");
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    private async Task<bool> IsPlatformRoleAsync(Guid? roleId, CancellationToken cancellationToken) =>
        roleId is Guid id && await db.Roles.AnyAsync(value => value.Id == id && value.CompanyId == null && value.Scope == "platform", cancellationToken);
}
