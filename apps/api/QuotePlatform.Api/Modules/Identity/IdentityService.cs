using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Identity;

/// <summary>
/// 使用者登入身分驗證請求契約。
/// </summary>
/// <param name="Account">登入帳號名稱。</param>
/// <param name="Password">明文密碼。</param>
/// <param name="CaptchaId">圖形驗證碼識別 ID（若啟用時必填）。</param>
/// <param name="CaptchaCode">使用者輸入之驗證碼文字。</param>
public sealed record LoginRequest(string? Account, string? Password, string? CaptchaId, string? CaptchaCode);

/// <summary>
/// 切換工作租戶或管理範圍請求契約。
/// </summary>
/// <param name="ScopeKind">目標範圍類型（platform 或 company）。</param>
/// <param name="CompanyId">目標公司唯一識別碼（切換平台時為 null）。</param>
public sealed record SelectScopeRequest(string? ScopeKind, Guid? CompanyId);

/// <summary>
/// 驗證成功後簽發之 Access Token 封包。
/// </summary>
/// <param name="AccessToken">經 HMAC-SHA256 簽署完成之 JWT 緊湊字串。</param>
/// <param name="ExpiresAt">權杖過期時間（UTC）。</param>
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// 使用者可切換之租戶工作範圍選項。
/// </summary>
/// <param name="ScopeKind">範圍類型（platform 或 company）。</param>
/// <param name="CompanyId">公司識別碼。</param>
/// <param name="Name">公司或平台顯示名稱。</param>
/// <param name="IsImpersonation">是否為平台管理員模擬進入其他公司。</param>
public sealed record ScopeOption(string ScopeKind, Guid? CompanyId, string Name, bool IsImpersonation);

/// <summary>
/// 導覽選單樹節點資料契約。
/// </summary>
/// <param name="Code">功能模組代碼。</param>
/// <param name="Name">顯示名稱。</param>
/// <param name="RoutePath">前端路由路徑（若是分類父層則為 null）。</param>
/// <param name="Children">子功能節點清單。</param>
public sealed record MenuNode(string Code, string Name, string? RoutePath, IReadOnlyList<MenuNode> Children);

/// <summary>
/// 權杖簽發結果實體；Refresh Token 明文僅供 Controller 寫入 HttpOnly Cookie。
/// </summary>
/// <param name="Response">傳回前端之 Access Token 封包。</param>
/// <param name="RefreshToken">明文 Refresh Token 字串。</param>
/// <param name="RefreshExpiresAt">Refresh Token 到期時間。</param>
public sealed record IssuedSession(AuthResponse Response, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// <summary>登入、refresh token 輪替、工作範圍切換與權限選單。</summary>
public sealed class IdentityService(
    QuoteDbContext db,
    PermissionGrantService grantService,
    JwtTokenService tokenService,
    AuditService audit,
    LoginCaptchaService captchaService,
    LoginAttemptGuard attemptGuard,
    IOptions<QuoteOptions> options)
{
    private const int MaxAccountFailuresBeforeLock = 5;
    private static readonly TimeSpan AccountLockDuration = TimeSpan.FromMinutes(15);
    private const string LoginFailedMessage = "帳號或密碼不正確，或帳號已停用。";

    public bool CaptchaEnabled => options.Value.LoginCaptchaEnabled;

    public async Task<ServiceResult<IssuedSession>> LoginAsync(LoginRequest request, string clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrEmpty(request.Password)) return ServiceResult<IssuedSession>.BadRequest("請輸入帳號與密碼。");
        if (attemptGuard.IsBlocked(clientIp)) return new(StatusCodes.Status429TooManyRequests, null, "登入嘗試次數過於頻繁，請稍後再試。");
        if (CaptchaEnabled && !captchaService.Validate(request.CaptchaId, request.CaptchaCode))
        {
            attemptGuard.RegisterFailure(clientIp);
            return ServiceResult<IssuedSession>.BadRequest("圖形驗證碼不正確或已逾期，請重新輸入。");
        }

        var account = request.Account.Trim();
        var user = await db.Users.SingleOrDefaultAsync(value => value.Account == account, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var locked = user?.LockedUntil > now;
        if (user is null || user.Status != "active" || locked || !PasswordRules.Verify(user, request.Password))
        {
            attemptGuard.RegisterFailure(clientIp);
            if (user is not null && !locked)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= MaxAccountFailuresBeforeLock)
                {
                    user.LockedUntil = now.Add(AccountLockDuration);
                    user.FailedLoginCount = 0;
                }
                await db.SaveChangesAsync(cancellationToken);
            }
            // 不區分帳號不存在、密碼錯誤、停用與鎖定，避免成為帳號枚舉來源。
            return new(StatusCodes.Status401Unauthorized, null, LoginFailedMessage);
        }

        var scope = await DefaultScopeAsync(user, cancellationToken);
        if (scope is null) return ServiceResult<IssuedSession>.Forbidden("此帳號尚未加入任何啟用中的公司。");

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        audit.Add(scope.Value.Token.CompanyId, user.Id, "user", user.Id, "login");
        return ServiceResult<IssuedSession>.Ok(await IssueAsync(user, scope.Value.Token, scope.Value.Grants, clientIp, userAgent, cancellationToken));
    }

    /// <summary>輪替 refresh token；偵測到已撤銷的 token 被重用時撤銷該使用者全部 refresh token。</summary>
    public async Task<ServiceResult<IssuedSession>> RefreshAsync(string? refreshToken, string clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return new(StatusCodes.Status401Unauthorized, null, "登入已逾期，請重新登入。");
        var hash = JwtTokenService.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(value => value.TokenHash == hash, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (stored is null || stored.ExpiresAt <= now) return new(StatusCodes.Status401Unauthorized, null, "登入已逾期，請重新登入。");
        if (stored.RevokedAt is not null)
        {
            await RevokeAllAsync(stored.UserId, cancellationToken);
            audit.Add(stored.CompanyId, stored.UserId, "user", stored.UserId, "refresh_token_reuse_detected", after: new { clientIp });
            await db.SaveChangesAsync(cancellationToken);
            return new(StatusCodes.Status401Unauthorized, null, "登入狀態異常，請重新登入。");
        }

        var user = await db.Users.SingleOrDefaultAsync(value => value.Id == stored.UserId, cancellationToken);
        if (user is null || user.Status != "active") return new(StatusCodes.Status401Unauthorized, null, LoginFailedMessage);

        // 原本的公司身分若已失效（停用、移除），退回預設範圍而不是沿用舊權限。
        var scope = stored.CompanyId is Guid companyId
            ? await CompanyScopeAsync(user, companyId, cancellationToken) ?? await DefaultScopeAsync(user, cancellationToken)
            : user.SystemRoleId is not null ? await PlatformScopeAsync(user, cancellationToken) : await DefaultScopeAsync(user, cancellationToken);
        if (scope is null) return new(StatusCodes.Status401Unauthorized, null, "此帳號已沒有可用的工作範圍。");

        var issued = await IssueAsync(user, scope.Value.Token, scope.Value.Grants, clientIp, userAgent, cancellationToken, replacing: stored);
        return ServiceResult<IssuedSession>.Ok(issued);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        var hash = JwtTokenService.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(value => value.TokenHash == hash, cancellationToken);
        if (stored is null || stored.RevokedAt is not null) return;
        stored.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 可切換的工作範圍：平台（系統帳號）、自己所屬的公司，以及平台管理員可進入的其他公司。
    /// 公司名稱以 <paramref name="keyword"/> 在資料庫比對，最多回傳 <paramref name="limit"/> 間（預設 20、上限 50）；
    /// 目前所在的公司一定包含在結果中，讓切換選單能顯示目前名稱。
    /// </summary>
    public async Task<IReadOnlyList<ScopeOption>> ListScopesAsync(RequestScope current, string? keyword, int? limit, int? offset, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(value => value.Id == current.UserId, cancellationToken);
        var take = Math.Clamp(limit ?? 20, 1, 50);
        var skip = Math.Max(offset ?? 0, 0);
        var text = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var result = new List<ScopeOption>();
        if (user.SystemRoleId is not null && skip == 0 && (text == null || "平台管理".Contains(text))) result.Add(new ScopeOption(RequestScope.Platform, null, "平台管理", false));

        var activeCompanies = db.Companies.AsNoTracking().Where(value => value.Status == "active");
        var memberCompanyIds = db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().Where(value => value.UserId == user.Id && value.IsActive).Select(value => value.CompanyId);
        var canImpersonate = await CanImpersonateAsync(user, cancellationToken);

        // 自己所屬的公司排前面，其次是可用平台身分進入的公司，各自依名稱排序；offset／limit 對整份清單分頁。
        var companies = await activeCompanies
            .Where(value => (text == null || value.Name.Contains(text)) && (canImpersonate || memberCompanyIds.Contains(value.Id)))
            .Select(value => new { value.Id, value.Name, IsMember = memberCompanyIds.Contains(value.Id) })
            .OrderByDescending(value => value.IsMember)
            .ThenBy(value => value.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        result.AddRange(companies.Select(value => new ScopeOption(RequestScope.CompanyScope, value.Id, value.Name, !value.IsMember)));

        // 第一頁一定帶出目前所在的公司，讓選單能顯示已選的項目。
        if (skip == 0 && current.CompanyId is Guid currentId && result.All(value => value.CompanyId != currentId))
        {
            var currentCompany = await activeCompanies.Where(value => value.Id == currentId).Select(value => new { value.Id, value.Name }).SingleOrDefaultAsync(cancellationToken);
            if (currentCompany is not null)
            {
                var isMember = await memberCompanyIds.AnyAsync(value => value == currentId, cancellationToken);
                result.Add(new ScopeOption(RequestScope.CompanyScope, currentCompany.Id, currentCompany.Name, !isMember));
            }
        }
        return result;
    }

    public async Task<ServiceResult<IssuedSession>> SelectScopeAsync(RequestScope current, SelectScopeRequest request, string? currentRefreshToken, string clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(value => value.Id == current.UserId, cancellationToken);
        (TokenScope Token, IReadOnlyCollection<PermissionGrant> Grants)? scope = request.ScopeKind switch
        {
            RequestScope.Platform when user.SystemRoleId is not null => await PlatformScopeAsync(user, cancellationToken),
            RequestScope.CompanyScope when request.CompanyId is Guid companyId => await CompanyScopeAsync(user, companyId, cancellationToken),
            _ => null,
        };
        if (scope is null) return ServiceResult<IssuedSession>.Forbidden("無法切換到指定的工作範圍。");
        if (scope.Value.Token.IsImpersonating) audit.Add(scope.Value.Token.CompanyId, user.Id, "company", scope.Value.Token.CompanyId, "impersonate");

        RefreshToken? replacing = null;
        if (!string.IsNullOrEmpty(currentRefreshToken))
        {
            var hash = JwtTokenService.HashRefreshToken(currentRefreshToken);
            replacing = await db.RefreshTokens.SingleOrDefaultAsync(value => value.TokenHash == hash && value.UserId == user.Id && value.RevokedAt == null, cancellationToken);
        }
        return ServiceResult<IssuedSession>.Ok(await IssueAsync(user, scope.Value.Token, scope.Value.Grants, clientIp, userAgent, cancellationToken, replacing));
    }

    public async Task<object> GetMeAsync(RequestScope scope, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(value => value.Id == scope.UserId, cancellationToken);
        var company = scope.CompanyId is Guid companyId
            ? await db.Companies.AsNoTracking().Where(value => value.Id == companyId).Select(value => new { value.Id, value.Name, value.RootAdminMembershipId }).SingleOrDefaultAsync(cancellationToken)
            : null;
        var departmentName = scope.DepartmentId is Guid departmentId
            ? await db.Departments.AsNoTracking().Where(value => value.Id == departmentId).Select(value => value.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new
        {
            user = new { user.Id, user.Account, user.DisplayName, user.Email, isSystemUser = user.SystemRoleId is not null },
            scope = new
            {
                kind = scope.ScopeKind,
                companyId = company?.Id,
                companyName = company?.Name,
                membershipId = scope.MembershipId,
                departmentId = scope.DepartmentId,
                departmentName,
                dataScope = scope.DataScope,
                isImpersonating = scope.IsImpersonating,
                isRootAdmin = company is not null && scope.MembershipId is not null && company.RootAdminMembershipId == scope.MembershipId,
            },
            grants = scope.Grants.OrderBy(value => value.Code, StringComparer.Ordinal).Select(value => new { code = value.Code, actions = value.Actions, options = value.Options }),
        };
    }

    /// <summary>依目前 scope 的 read grants 組出選單樹；群組節點只在底下有可見項目時出現。</summary>
    public async Task<IReadOnlyList<MenuNode>> ListMenuAsync(RequestScope scope, CancellationToken cancellationToken)
    {
        var scopeName = scope.IsPlatform ? "platform" : "company";
        var permissions = await db.Permissions.AsNoTracking().Where(value => value.Scope == scopeName && value.IsMenu).OrderBy(value => value.SortOrder).ToListAsync(cancellationToken);
        IReadOnlyList<MenuNode> Build(Guid? parentId) => permissions
            .Where(value => value.ParentId == parentId)
            .Select(value => new MenuNode(value.Code, value.Name, value.RoutePath, Build(value.Id)))
            .Where(value => value.RoutePath is not null ? scope.Has(value.Code, "read") : value.Children.Count > 0)
            .ToList();
        return Build(null);
    }

    private async Task<IssuedSession> IssueAsync(User user, TokenScope scope, IReadOnlyCollection<PermissionGrant> grants, string clientIp, string? userAgent, CancellationToken cancellationToken, RefreshToken? replacing = null)
    {
        var (accessToken, expiresAt) = tokenService.CreateAccessToken(user, scope, grants);
        var (refreshToken, refreshHash) = JwtTokenService.CreateRefreshToken();
        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(options.Value.RefreshTokenDays);
        var stored = new RefreshToken
        {
            UserId = user.Id,
            CompanyId = scope.CompanyId,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiresAt,
            CreatedIp = clientIp,
            UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent,
        };
        db.RefreshTokens.Add(stored);
        if (replacing is not null)
        {
            replacing.RevokedAt = DateTimeOffset.UtcNow;
            replacing.ReplacedById = stored.Id;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new IssuedSession(new AuthResponse(accessToken, expiresAt), refreshToken, refreshExpiresAt);
    }

    private async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await db.RefreshTokens.Where(value => value.UserId == userId && value.RevokedAt == null && value.ExpiresAt > now).ToListAsync(cancellationToken);
        foreach (var token in active) token.RevokedAt = now;
    }

    /// <summary>系統帳號預設 platform；一般帳號預設第一個啟用中公司（依名稱排序）。</summary>
    private async Task<(TokenScope Token, IReadOnlyCollection<PermissionGrant> Grants)?> DefaultScopeAsync(User user, CancellationToken cancellationToken)
    {
        if (user.SystemRoleId is not null) return await PlatformScopeAsync(user, cancellationToken);
        var companyId = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(value => value.UserId == user.Id && value.IsActive)
            .Join(db.Companies.Where(value => value.Status == "active"), value => value.CompanyId, value => value.Id, (_, company) => company)
            .OrderBy(value => value.Name)
            .Select(value => (Guid?)value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return companyId is Guid id ? await CompanyScopeAsync(user, id, cancellationToken) : null;
    }

    private async Task<(TokenScope Token, IReadOnlyCollection<PermissionGrant> Grants)?> PlatformScopeAsync(User user, CancellationToken cancellationToken) =>
        (TokenScope.Platform, await grantService.ForPlatformAsync(user, cancellationToken));

    /// <summary>
    /// 公司範圍：優先使用使用者在該公司的 membership；系統帳號具 <c>platform.companies:switch_company</c> 時，
    /// 可用 company-admin 上限的權限進入（標記 impersonating 並寫 audit）。
    /// </summary>
    private async Task<(TokenScope Token, IReadOnlyCollection<PermissionGrant> Grants)?> CompanyScopeAsync(User user, Guid companyId, CancellationToken cancellationToken)
    {
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(value => value.Id == companyId && value.Status == "active", cancellationToken);
        if (company is null) return null;

        var membership = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Include(value => value.Role)
            .SingleOrDefaultAsync(value => value.CompanyId == companyId && value.UserId == user.Id && value.IsActive, cancellationToken);
        if (membership is not null)
        {
            var grants = await grantService.ForMembershipAsync(membership, company, cancellationToken);
            var dataScope = membership.DataScopeOverride ?? membership.Role.DataScope;
            return (new TokenScope(RequestScope.CompanyScope, companyId, membership.Id, membership.DepartmentId, dataScope), grants);
        }

        if (!await CanImpersonateAsync(user, cancellationToken)) return null;
        return (new TokenScope(RequestScope.CompanyScope, companyId, DataScope: DataScopes.Company, IsImpersonating: true),
            await grantService.CompanyCeilingAsync(company, cancellationToken));
    }

    private async Task<bool> CanImpersonateAsync(User user, CancellationToken cancellationToken)
    {
        if (user.SystemRoleId is null) return false;
        var grant = (await grantService.ForPlatformAsync(user, cancellationToken)).SingleOrDefault(value => value.Code == "platform.companies");
        return grant is not null && grant.Actions.Contains("read") && grant.Options.Contains("switch_company");
    }
}
