using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>簽發 access token 時的工作範圍。</summary>
public sealed record TokenScope(
    string Kind,
    Guid? CompanyId = null,
    Guid? MembershipId = null,
    Guid? DepartmentId = null,
    string DataScope = DataScopes.Own,
    bool IsImpersonating = false)
{
    public static readonly TokenScope Platform = new(RequestScope.Platform, DataScope: DataScopes.Company);
}

/// <summary>建立含 scope、auth version 與資源型 grants 的 access token，以及不透明的 refresh token。</summary>
public sealed class JwtTokenService(IOptions<QuoteOptions> options)
{
    private readonly QuoteOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user, TokenScope scope, IReadOnlyCollection<PermissionGrant> grants)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(QuoteClaims.ScopeKind, scope.Kind),
            new(QuoteClaims.AuthVersion, user.AuthVersion.ToString()),
            new(QuoteClaims.DataScope, scope.DataScope),
        };
        if (scope.CompanyId is Guid companyId) claims.Add(new Claim(QuoteClaims.CompanyId, companyId.ToString()));
        if (scope.MembershipId is Guid membershipId) claims.Add(new Claim(QuoteClaims.MembershipId, membershipId.ToString()));
        if (scope.DepartmentId is Guid departmentId) claims.Add(new Claim(QuoteClaims.DepartmentId, departmentId.ToString()));
        if (scope.IsImpersonating) claims.Add(new Claim(QuoteClaims.Impersonating, "true"));
        foreach (var grant in grants)
        {
            claims.AddRange(grant.Actions.Distinct(StringComparer.Ordinal).Select(action => new Claim(QuoteClaims.Permission, $"{grant.Code}:{action}")));
            claims.AddRange(grant.Options.Distinct(StringComparer.Ordinal).Select(option => new Claim(QuoteClaims.PermissionOption, $"{grant.Code}:{option}")));
        }

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.JwtIssuer, _options.JwtAudience, claims, expires: expiresAt.UtcDateTime, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>產生 refresh token 明文（只回給瀏覽器 cookie）與要存進資料庫的雜湊。</summary>
    public static (string Token, string Hash) CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (token, HashRefreshToken(token));
    }

    public static string HashRefreshToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>依角色、個人權限與 company-admin 上限計算 JWT 使用的有效 grants。</summary>
public sealed class PermissionGrantService(QuoteDbContext db)
{
    /// <summary>系統帳號的 platform scope grants。</summary>
    public async Task<IReadOnlyCollection<PermissionGrant>> ForPlatformAsync(User user, CancellationToken cancellationToken) =>
        user.SystemRoleId is Guid roleId ? await ForRolesAsync([roleId], cancellationToken) : [];

    /// <summary>公司成員的有效 grants：(角色 ∪ 個人加權) 展開公司設定後 − 個人禁止，再與 company-admin 交集。</summary>
    public async Task<IReadOnlyCollection<PermissionGrant>> ForMembershipAsync(CompanyMembership membership, Company company, CancellationToken cancellationToken)
    {
        var roleGrants = await ForRolesAsync([membership.RoleId], cancellationToken);
        var now = DateTimeOffset.UtcNow;
        // 登入與切換公司時 JWT 尚未帶 company_id，因此明確忽略 query filter，改以 membership 的公司與使用者篩選。
        var personal = await db.UserPermissions.IgnoreQueryFilters().AsNoTracking()
            .Include(value => value.Permission)
            .Where(value => value.CompanyId == membership.CompanyId && value.UserId == membership.UserId && (value.ExpiresAt == null || value.ExpiresAt > now))
            .ToListAsync(cancellationToken);
        var personalGrants = personal.Where(value => value.Effect == PermissionEffects.Grant).Select(ToGrant);
        var personalDenies = personal.Where(value => value.Effect == PermissionEffects.Deny).Select(ToGrant);

        var ceiling = await CompanyCeilingAsync(company, cancellationToken);
        var optionsByCode = await OptionsByCodeAsync(company, cancellationToken);
        var expanded = PermissionGrantRules.ApplyCompanyConfiguration(
            PermissionGrantRules.Union(roleGrants, personalGrants),
            company.IsRoleCrudConfigurationEnabled,
            company.IsRoleOptionConfigurationEnabled,
            optionsByCode);
        return PermissionGrantRules.Restrict(PermissionGrantRules.Subtract(expanded, personalDenies), ceiling);
    }

    /// <summary>公司角色的權限上限：系統角色 company-admin 經公司設定展開後的 grants。</summary>
    public async Task<IReadOnlyCollection<PermissionGrant>> CompanyCeilingAsync(Company company, CancellationToken cancellationToken)
    {
        var companyAdminRoleId = await db.Roles.AsNoTracking()
            .Where(value => value.CompanyId == null && value.IsSystem && value.Code == PermissionCatalog.CompanyAdmin)
            .Select(value => (Guid?)value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (companyAdminRoleId is not Guid roleId) return [];
        return PermissionGrantRules.ApplyCompanyConfiguration(
            await ForRolesAsync([roleId], cancellationToken),
            company.IsRoleCrudConfigurationEnabled,
            company.IsRoleOptionConfigurationEnabled,
            await OptionsByCodeAsync(company, cancellationToken));
    }

    public async Task<IReadOnlyCollection<PermissionGrant>> ForRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0) return [];
        var links = await db.RolePermissions.AsNoTracking()
            .Include(value => value.Permission)
            .Where(value => roleIds.Contains(value.RoleId))
            .ToListAsync(cancellationToken);
        return PermissionGrantRules.Union(links.Select(value => new PermissionAllowance(
            value.Permission.Code, value.PerCreate, value.PerRead, value.PerUpdate, value.PerDelete, ReadOptions(value.OptionsJson)).ToGrant()));
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> OptionsByCodeAsync(Company company, CancellationToken cancellationToken)
    {
        if (company.IsRoleOptionConfigurationEnabled) return new Dictionary<string, IReadOnlyCollection<string>>();
        var rows = await db.Permissions.AsNoTracking().Select(value => new { value.Code, value.SubFeaturesJson }).ToListAsync(cancellationToken);
        return rows.ToDictionary(value => value.Code, value => ReadSubFeatureKeys(value.SubFeaturesJson), StringComparer.Ordinal);
    }

    private static PermissionGrant ToGrant(UserPermission value) =>
        new PermissionAllowance(value.Permission.Code, value.PerCreate, value.PerRead, value.PerUpdate, value.PerDelete, ReadOptions(value.OptionsJson)).ToGrant();

    /// <summary>讀取 OptionsJson；損毀時回傳空集合（fail-closed）。</summary>
    public static IReadOnlyCollection<string> ReadOptions(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>讀取 SubFeaturesJson 的 key 清單；損毀時回傳空集合（fail-closed）。</summary>
    public static IReadOnlyCollection<string> ReadSubFeatureKeys(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            return document.RootElement.ValueKind != JsonValueKind.Array
                ? []
                : document.RootElement.EnumerateArray()
                    .Select(value => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("key", out var key) ? key.GetString() : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>寫入 audit_logs；與業務異動在同一個 SaveChanges 內提交。</summary>
public sealed class AuditService(QuoteDbContext db, IHttpContextAccessor httpContextAccessor)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Add(Guid? companyId, Guid? actorUserId, string entityType, Guid? entityId, string action, object? before = null, object? after = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            ActorUserId = actorUserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOptions),
            Ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
        });
    }
}

/// <summary>密碼雜湊與長度規則；登入與所有建立／重設密碼流程共用。</summary>
public static class PasswordRules
{
    public const int MinLength = 8;
    private static readonly PasswordHasher<User> Hasher = new();

    public static string? Validate(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinLength ? $"密碼至少需要 {MinLength} 碼。" : null;

    public static string Hash(User user, string password) => Hasher.HashPassword(user, password);

    public static bool Verify(User user, string password) =>
        !string.IsNullOrEmpty(user.PasswordHash) && Hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
