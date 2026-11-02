namespace QuotePlatform.Api.Infrastructure;

/// <summary>屬於單一公司的資料；由 <see cref="QuoteDbContext"/> 依 JWT company_id 自動過濾並在寫入時強制填值。</summary>
public interface ICompanyOwned
{
    Guid CompanyId { get; set; }
}

/// <summary>登入帳號。系統帳號以 <see cref="SystemRoleId"/> 指向 platform 角色；公司身分由 <see cref="CompanyMembership"/> 決定。</summary>
public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Account { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>active／disabled。</summary>
    public string Status { get; set; } = "active";
    public Guid? SystemRoleId { get; set; }
    public Role? SystemRole { get; set; }
    /// <summary>Token 撤銷版本；權限、身分或密碼異動時遞增，舊 access token 立即失效。</summary>
    public int AuthVersion { get; set; } = 1;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>可撤銷、可輪替的 refresh token；資料庫只保存 SHA-256 雜湊。</summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    /// <summary>簽發時的工作範圍；null 表示 platform scope。</summary>
    public Guid? CompanyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public string? CreatedIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>公司：登入租戶與資料隔離單位（美達特的商家）。</summary>
public sealed class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? TaxId { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    /// <summary>英文名稱，印在報價單抬頭公司名稱下方。</summary>
    public string? EnglishName { get; set; }
    /// <summary>公司 Logo（files 的 Id，只接受圖片），印在報價單抬頭。</summary>
    public Guid? LogoFileId { get; set; }
    /// <summary>active／suspended。</summary>
    public string Status { get; set; } = "active";
    /// <summary>受保護的主管理員 membership；不可降級、停用或改角色。</summary>
    public Guid? RootAdminMembershipId { get; set; }
    public bool IsRoleCrudConfigurationEnabled { get; set; } = true;
    public bool IsRoleOptionConfigurationEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>公司層級設定（一對一）。</summary>
public sealed class CompanySetting : ICompanyOwned
{
    public Guid CompanyId { get; set; }
    public decimal DefaultTaxRate { get; set; } = 5.00m;
    public string DefaultCurrency { get; set; } = "TWD";
    public int QuoteValidDays { get; set; } = 30;
    public string? DefaultTerms { get; set; }
    public string PdfHeaderJson { get; set; } = "{}";
    /// <summary>會計年度起始月，Dashboard 季度計算使用。</summary>
    public int FiscalYearStartMonth { get; set; } = 1;
    /// <summary>報價單號規則，例如 <c>Q{yyyy}{MM}-{seq:4}</c>；見 <see cref="DocumentNumberPattern"/>。</summary>
    public string QuoteNumberPattern { get; set; } = DocumentNumberPattern.DefaultQuote;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>部門樹節點（美達特的 tenants），決定資料範圍。</summary>
public sealed class Department : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid? ParentId { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>部門 closure table；<see cref="Depth"/> = 0 為節點自身。</summary>
public sealed class DepartmentClosure : ICompanyOwned
{
    public Guid AncestorId { get; set; }
    public Guid DescendantId { get; set; }
    public Guid CompanyId { get; set; }
    public int Depth { get; set; }
}

/// <summary>使用者在某公司的身分：部門、角色與資料範圍覆寫。</summary>
public sealed class CompanyMembership : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? DepartmentId { get; set; }
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    /// <summary>null 表示採用角色的 <see cref="Role.DataScope"/>。</summary>
    public string? DataScopeOverride { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>角色。<see cref="CompanyId"/> 為 null 表示系統內建角色，各公司共用。</summary>
public sealed class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CompanyId { get; set; }
    /// <summary>platform／company。</summary>
    public string Scope { get; set; } = "company";
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>own／department／department_tree／company。</summary>
    public string DataScope { get; set; } = DataScopes.Own;
    public bool IsSystem { get; set; }
    public bool IsFullAccess { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>權限目錄節點（同美達特 PlatformPermission）。</summary>
public sealed class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Scope { get; set; } = "company";
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsMenu { get; set; }
    public string? RoutePath { get; set; }
    /// <summary>[{ "key": "approve", "name": "核准" }]</summary>
    public string SubFeaturesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>角色對單一資源的 CRUD 與子功能授予。</summary>
public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
    public bool PerCreate { get; set; }
    public bool PerRead { get; set; }
    public bool PerUpdate { get; set; }
    public bool PerDelete { get; set; }
    public string OptionsJson { get; set; } = "[]";
}

/// <summary>個人權限：在角色之外對單一資源加權（grant）或禁止（deny）。</summary>
public sealed class UserPermission : ICompanyOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
    /// <summary>grant／deny。</summary>
    public string Effect { get; set; } = PermissionEffects.Grant;
    public bool PerCreate { get; set; }
    public bool PerRead { get; set; }
    public bool PerUpdate { get; set; }
    public bool PerDelete { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public string? Reason { get; set; }
    /// <summary>到期後不再計入有效權限，可做臨時授權。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>稽核紀錄；不建 FK，主檔刪除後仍保留。</summary>
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CompanyId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Ip { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>資料範圍常數。</summary>
public static class DataScopes
{
    public const string Own = "own";
    public const string Department = "department";
    public const string DepartmentTree = "department_tree";
    public const string Company = "company";
    public static readonly string[] All = [Own, Department, DepartmentTree, Company];
    public static bool IsValid(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>個人權限效果常數。</summary>
public static class PermissionEffects
{
    public const string Grant = "grant";
    public const string Deny = "deny";
}
