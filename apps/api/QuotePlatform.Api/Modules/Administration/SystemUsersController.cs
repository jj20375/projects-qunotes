using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Administration;

/// <summary>
/// 建立平台系統管理員帳號輸入契約。
/// </summary>
/// <param name="Account">登入帳號名稱。</param>
/// <param name="DisplayName">使用者姓名。</param>
/// <param name="Email">電子郵件信箱。</param>
/// <param name="Password">初始密碼。</param>
/// <param name="RoleId">指派之平台角色識別碼。</param>
public sealed record CreateSystemUserRequest(string? Account, string? DisplayName, string? Email, string? Password, Guid? RoleId);

/// <summary>
/// 更新平台系統管理員帳號輸入契約。
/// </summary>
/// <param name="DisplayName">使用者姓名。</param>
/// <param name="Email">電子郵件信箱。</param>
/// <param name="RoleId">指派之平台角色識別碼。</param>
/// <param name="Status">帳號狀態（active 或 disabled）。</param>
public sealed record UpdateSystemUserRequest(string? DisplayName, string? Email, Guid? RoleId, string? Status);

/// <summary>
/// 平台系統管理員帳號管理控制器（平台管理員專屬 platform scope）。
/// 僅管理 SystemRoleId 非空之跨租戶系統維運帳號。
/// </summary>
/// <param name="service">系統帳號核心業務服務實體。</param>
[Route("api/platform/system-users")]
public sealed class SystemUsersController(SystemUserService service) : ScopedControllerBase
{
    /// <summary>
    /// 分頁查詢所有平台系統管理員帳號清單。
    /// </summary>
    /// <param name="query">分頁與關鍵字查詢條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁系統使用者列表與總筆數。</returns>
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryPlatform("platform.system_users", "read", out _) ? Ok(await service.ListAsync(query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 建立新平台系統管理員帳號。
    /// </summary>
    /// <param name="request">帳號建立內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立成功之使用者 ID 物件。</returns>
    [HttpPost]
    public async Task<ActionResult> Create(CreateSystemUserRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.system_users", "create", out var scope) ? ToResult(await service.CreateAsync(scope, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新指定系統管理員帳號之資料、角色或啟用狀態。
    /// </summary>
    /// <param name="userId">系統使用者識別碼。</param>
    /// <param name="request">更新資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPatch("{userId:guid}")]
    public async Task<ActionResult> Update(Guid userId, UpdateSystemUserRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.system_users", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, userId, request, cancellationToken)) : Forbidden();

    /// <summary>
    /// 重設系統管理員之密碼，並註銷其所有現存 Refresh Token。
    /// </summary>
    /// <param name="userId">系統使用者識別碼。</param>
    /// <param name="request">新密碼請求物件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回重設結果。</returns>
    [HttpPost("{userId:guid}/reset-password")]
    public async Task<ActionResult> ResetPassword(Guid userId, ResetPasswordRequest request, CancellationToken cancellationToken) =>
        TryPlatform("platform.system_users", "update", out var scope, "reset_password") ? ToResult(await service.ResetPasswordAsync(scope, userId, request, cancellationToken)) : Forbidden();
}

/// <summary>
/// 平台系統管理員帳號管理業務服務。
/// </summary>
/// <param name="db">資料庫上下文實體。</param>
/// <param name="audit">系統稽核日誌服務實體。</param>
public sealed class SystemUserService(QuoteDbContext db, AuditService audit)
{
    /// <summary>
    /// 條件分頁查詢系統帳號清單。
    /// </summary>
    /// <param name="query">分頁與關鍵字條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁資料物件。</returns>
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

    /// <summary>
    /// 建立新系統帳號並雜湊密碼。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="request">帳號資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回新建立之使用者物件結果。</returns>
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

    /// <summary>
    /// 更新指定系統帳號資料或角色。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="userId">使用者識別碼。</param>
    /// <param name="request">更新資料內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
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

    /// <summary>
    /// 重設指定系統帳號之登入密碼並撤銷其所有現存 Token。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="userId">使用者識別碼。</param>
    /// <param name="request">重設密碼內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回執行結果。</returns>
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
