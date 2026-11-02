using System.Security.Claims;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>JWT claim 名稱；簽發與讀取共用，避免拼字不一致。</summary>
public static class QuoteClaims
{
    public const string ScopeKind = "scope_kind";
    public const string CompanyId = "company_id";
    public const string MembershipId = "membership_id";
    public const string DepartmentId = "department_id";
    public const string DataScope = "data_scope";
    public const string AuthVersion = "auth_version";
    public const string Permission = "permission";
    public const string PermissionOption = "permission_option";
    public const string Impersonating = "impersonating";
}

/// <summary>從已驗證 JWT 唯一解析出的使用者、公司與權限範圍；API 內部唯一可信的 scope 來源。</summary>
public sealed record RequestScope(
    Guid UserId,
    string ScopeKind,
    Guid? CompanyId,
    Guid? MembershipId,
    Guid? DepartmentId,
    string DataScope,
    IReadOnlySet<string> Permissions,
    IReadOnlySet<string> Options,
    int AuthVersion,
    bool IsImpersonating)
{
    public const string Platform = "platform";
    public const string CompanyScope = "company";

    public bool IsPlatform => ScopeKind == Platform;
    public bool IsCompany => ScopeKind == CompanyScope && CompanyId is not null;

    /// <summary>讀取 JWT；company scope 缺少 company_id 時視為無效，fail-closed。</summary>
    public static bool TryRead(ClaimsPrincipal principal, out RequestScope scope)
    {
        scope = null!;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return false;
        var scopeKind = principal.FindFirstValue(QuoteClaims.ScopeKind) ?? string.Empty;
        if (scopeKind is not (Platform or CompanyScope)) return false;
        Guid? companyId = Guid.TryParse(principal.FindFirstValue(QuoteClaims.CompanyId), out var parsedCompanyId) ? parsedCompanyId : null;
        if (scopeKind == CompanyScope && companyId is null) return false;
        Guid? membershipId = Guid.TryParse(principal.FindFirstValue(QuoteClaims.MembershipId), out var parsedMembershipId) ? parsedMembershipId : null;
        Guid? departmentId = Guid.TryParse(principal.FindFirstValue(QuoteClaims.DepartmentId), out var parsedDepartmentId) ? parsedDepartmentId : null;
        _ = int.TryParse(principal.FindFirstValue(QuoteClaims.AuthVersion), out var authVersion);
        scope = new RequestScope(
            userId,
            scopeKind,
            companyId,
            membershipId,
            departmentId,
            principal.FindFirstValue(QuoteClaims.DataScope) ?? DataScopes.Own,
            principal.FindAll(QuoteClaims.Permission).Select(value => value.Value).ToHashSet(StringComparer.Ordinal),
            principal.FindAll(QuoteClaims.PermissionOption).Select(value => value.Value).ToHashSet(StringComparer.Ordinal),
            authVersion,
            principal.FindFirstValue(QuoteClaims.Impersonating) == "true");
        return true;
    }

    public bool Has(string code, string action) => Permissions.Contains($"{code}:{action}");

    public bool HasOption(string code, string option) => Options.Contains($"{code}:{option}");

    /// <summary>目前 scope 的有效 grants，供權限下放時檢查「不得給出自己沒有的權限」。</summary>
    public IReadOnlyCollection<PermissionGrant> Grants =>
        Permissions.Select(value => value.Split(':', 2))
            .Concat(Options.Select(value => value.Split(':', 2)))
            .Where(value => value.Length == 2)
            .Select(value => value[0])
            .Distinct(StringComparer.Ordinal)
            .Select(code => new PermissionGrant(
                code,
                Permissions.Where(value => value.StartsWith(code + ":", StringComparison.Ordinal)).Select(value => value[(code.Length + 1)..]).ToArray(),
                Options.Where(value => value.StartsWith(code + ":", StringComparison.Ordinal)).Select(value => value[(code.Length + 1)..]).ToArray()))
            .ToArray();
}

/// <summary>
/// 目前請求所屬公司；<see cref="QuoteDbContext"/> 以它做 Global Query Filter。
/// </summary>
/// <remarks>
/// 預設由 JWT 的 company_id 決定。平台開通公司這類跨公司流程必須明確呼叫 <see cref="UseCompany"/>，
/// 讓「越過 JWT 範圍」在程式碼中一眼可見。
/// </remarks>
public sealed class CompanyContext(IHttpContextAccessor httpContextAccessor)
{
    private Guid? _override;
    private bool _resolved;
    private Guid? _fromToken;

    public Guid? CompanyId
    {
        get
        {
            if (_override is not null) return _override;
            if (!_resolved)
            {
                _resolved = true;
                var user = httpContextAccessor.HttpContext?.User;
                _fromToken = user is not null && RequestScope.TryRead(user, out var scope) && scope.IsCompany ? scope.CompanyId : null;
            }
            return _fromToken;
        }
    }

    /// <summary>在目前請求內改用指定公司；只能用於平台開通公司、Seeder 等受控流程。</summary>
    public void UseCompany(Guid companyId) => _override = companyId;
}
