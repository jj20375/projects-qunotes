namespace QuotePlatform.Api.Infrastructure;

/// <summary>屬於單一公司的資料；由 <see cref="QuoteDbContext"/> 依 JWT company_id 自動過濾並在寫入時強制填值。</summary>
public interface ICompanyOwned
{
    /// <summary>所屬公司識別碼。</summary>
    Guid CompanyId { get; set; }
}

/// <summary>登入帳號。系統帳號以 <see cref="SystemRoleId"/> 指向 platform 角色；公司身分由 <see cref="CompanyMembership"/> 決定。</summary>
public sealed class User
{
    /// <summary>使用者唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>登入帳號名稱；全系統唯一。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>電子郵件信箱。</summary>
    public string? Email { get; set; }

    /// <summary>使用者顯示姓名。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>BCrypt 密碼雜湊值。</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>帳號狀態：active（正常啟用）／disabled（停用）。</summary>
    public string Status { get; set; } = "active";

    /// <summary>全系統角色識別碼；非 null 表示為系統管理員或具備平台管理權限帳號。</summary>
    public Guid? SystemRoleId { get; set; }

    /// <summary>全系統角色實體。</summary>
    public Role? SystemRole { get; set; }

    /// <summary>Token 撤銷版本；權限、身分或密碼異動時遞增，舊 access token 立即失效。</summary>
    public int AuthVersion { get; set; } = 1;

    /// <summary>登入連續失敗次數。</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>帳號鎖定截止時間戳記；null 表示未被鎖定。</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>最後成功登入時間戳記。</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>帳號建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>可撤銷、可輪替的 refresh token；資料庫只保存 SHA-256 雜湊。</summary>
public sealed class RefreshToken
{
    /// <summary>Token 紀錄唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬使用者識別碼。</summary>
    public Guid UserId { get; set; }

    /// <summary>簽發時的工作範圍公司識別碼；null 表示 platform 系統層級範圍。</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>Refresh Token 明文計算之 SHA-256 雜湊碼。</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>權杖有效截止時間戳記（UTC）。</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>權杖撤銷時間戳記（UTC）；null 表示未撤銷。</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>若已輪替，記錄接替本 Token 之新 Token 識別碼。</summary>
    public Guid? ReplacedById { get; set; }

    /// <summary>建立 Token 時的客戶端 IP 位址。</summary>
    public string? CreatedIp { get; set; }

    /// <summary>建立 Token 時的客戶端 User-Agent。</summary>
    public string? UserAgent { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>公司：登入租戶與資料隔離單位（美達特的商家）。</summary>
public sealed class Company
{
    /// <summary>公司唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>公司名稱。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>公司英文縮寫／網址代稱；全系統唯一。</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>公司統一編號。</summary>
    public string? TaxId { get; set; }

    /// <summary>公司代表電話。</summary>
    public string? Phone { get; set; }

    /// <summary>公司營業登記地址。</summary>
    public string? Address { get; set; }

    /// <summary>英文名稱，印在報價單抬頭公司名稱下方。</summary>
    public string? EnglishName { get; set; }

    /// <summary>公司 Logo（files 的 Id，只接受圖片），印在報價單抬頭。</summary>
    public Guid? LogoFileId { get; set; }

    /// <summary>公司營運狀態：active（營運中）／suspended（停權凍結）。</summary>
    public string Status { get; set; } = "active";

    /// <summary>受保護的主管理員 membership 識別碼；不可降級、停用或改角色。</summary>
    public Guid? RootAdminMembershipId { get; set; }

    /// <summary>是否允許此公司管理員自訂角色之 CRUD 權限。</summary>
    public bool IsRoleCrudConfigurationEnabled { get; set; } = true;

    /// <summary>是否允許此公司管理員配置角色之進階子功能選項。</summary>
    public bool IsRoleOptionConfigurationEnabled { get; set; } = true;

    /// <summary>公司建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>公司層級設定（一對一）。</summary>
public sealed class CompanySetting : ICompanyOwned
{
    /// <summary>所屬公司識別碼（主鍵）。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>預設營業稅率百分比（例如 5.00 代表 5%）。</summary>
    public decimal DefaultTaxRate { get; set; } = 5.00m;

    /// <summary>預設計價幣別代碼（例如 TWD、USD）。</summary>
    public string DefaultCurrency { get; set; } = "TWD";

    /// <summary>報價單預設有效天數。</summary>
    public int QuoteValidDays { get; set; } = 30;

    /// <summary>報價單預設條款與說明文字。</summary>
    public string? DefaultTerms { get; set; }

    /// <summary>匯出報價單 PDF 時之抬頭自訂設定 JSON。</summary>
    public string PdfHeaderJson { get; set; } = "{}";

    /// <summary>會計年度起始月份（1~12），供 Dashboard 財務季報計算使用。</summary>
    public int FiscalYearStartMonth { get; set; } = 1;

    /// <summary>報價單號規則，例如 <c>Q{yyyy}{MM}-{seq:4}</c>；見 <see cref="DocumentNumberPattern"/>。</summary>
    public string QuoteNumberPattern { get; set; } = DocumentNumberPattern.DefaultQuote;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>部門樹節點（美達特的 tenants），決定資料範圍。</summary>
public sealed class Department : ICompanyOwned
{
    /// <summary>部門唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>上層父部門識別碼；null 表示為公司一級頂層部門。</summary>
    public Guid? ParentId { get; set; }

    /// <summary>部門代碼。</summary>
    public string? Code { get; set; }

    /// <summary>部門名稱。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>排序順序編號。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否啟用運作中。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>部門 closure table；<see cref="Depth"/> = 0 為節點自身。</summary>
public sealed class DepartmentClosure : ICompanyOwned
{
    /// <summary>祖先部門識別碼。</summary>
    public Guid AncestorId { get; set; }

    /// <summary>後代部門識別碼。</summary>
    public Guid DescendantId { get; set; }

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>樹階層深度差；0 表示本部門節點本身，1 表示直接子部門。</summary>
    public int Depth { get; set; }
}

/// <summary>使用者在某公司的身分：部門、角色與資料範圍覆寫。</summary>
public sealed class CompanyMembership : ICompanyOwned
{
    /// <summary>成員身分關係唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>關聯使用者識別碼。</summary>
    public Guid UserId { get; set; }

    /// <summary>使用者主檔實體。</summary>
    public User User { get; set; } = null!;

    /// <summary>所屬部門識別碼。</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>指派角色識別碼。</summary>
    public Guid RoleId { get; set; }

    /// <summary>角色實體。</summary>
    public Role Role { get; set; } = null!;

    /// <summary>個別資料範圍覆寫；null 表示採用角色的 <see cref="Role.DataScope"/>。</summary>
    public string? DataScopeOverride { get; set; }

    /// <summary>此公司成員身分是否啟用生效。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>角色。<see cref="CompanyId"/> 為 null 表示系統內建角色，各公司共用。</summary>
public sealed class Role
{
    /// <summary>角色唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼；null 表示為全系統共用角色。</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>適用範圍：platform（平台層）／company（公司層）。</summary>
    public string Scope { get; set; } = "company";

    /// <summary>角色代碼（例如 admin、sales、manager）。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>角色顯示名稱。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>預設資料可見性範圍（own／department／department_tree／company）；參見 <see cref="DataScopes"/>。</summary>
    public string DataScope { get; set; } = DataScopes.Own;

    /// <summary>是否為系統預設內建角色（內建角色不可刪除代碼）。</summary>
    public bool IsSystem { get; set; }

    /// <summary>是否具備全功能存取權限（超級管理員標記）。</summary>
    public bool IsFullAccess { get; set; }

    /// <summary>自訂角色的建立者；自訂角色只有建立者能在成員表單中選用。系統角色與舊資料為 NULL（不限制）。</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>角色建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>權限目錄節點（同美達特 PlatformPermission）。</summary>
public sealed class Permission
{
    /// <summary>權限唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>權限資源代碼（例如 quotes、projects、companies）。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>功能或資源名稱。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>功能描述說明。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>權限範圍層級：platform（平台功能）／company（公司業務功能）。</summary>
    public string Scope { get; set; } = "company";

    /// <summary>上層選單／功能節點識別碼；null 表示為頂層模組。</summary>
    public Guid? ParentId { get; set; }

    /// <summary>選單或清單排序順序編號。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否作為左側導覽選單項展示。</summary>
    public bool IsMenu { get; set; }

    /// <summary>前端路由路徑（若是選單項）。</summary>
    public string? RoutePath { get; set; }

    /// <summary>支援之進階子功能選項定義 JSON（例如 [{ "key": "approve", "name": "核准" }]）。</summary>
    public string SubFeaturesJson { get; set; } = "[]";

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>角色對單一資源的 CRUD 與子功能授予。</summary>
public sealed class RolePermission
{
    /// <summary>角色識別碼。</summary>
    public Guid RoleId { get; set; }

    /// <summary>權限資源識別碼。</summary>
    public Guid PermissionId { get; set; }

    /// <summary>關聯權限定義實體。</summary>
    public Permission Permission { get; set; } = null!;

    /// <summary>是否具備建立權限（Create）。</summary>
    public bool PerCreate { get; set; }

    /// <summary>是否具備讀取檢視權限（Read）。</summary>
    public bool PerRead { get; set; }

    /// <summary>是否具備編輯修改權限（Update）。</summary>
    public bool PerUpdate { get; set; }

    /// <summary>是否具備刪除權限（Delete）。</summary>
    public bool PerDelete { get; set; }

    /// <summary>已啟用的進階子功能項目 JSON 陣列（例如 ["approve", "export"]）。</summary>
    public string OptionsJson { get; set; } = "[]";
}

/// <summary>個人權限：在角色之外對單一資源加權（grant）或禁止（deny）。</summary>
public sealed class UserPermission : ICompanyOwned
{
    /// <summary>個人特例權限唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>目標使用者識別碼。</summary>
    public Guid UserId { get; set; }

    /// <summary>權限資源識別碼。</summary>
    public Guid PermissionId { get; set; }

    /// <summary>關聯權限定義實體。</summary>
    public Permission Permission { get; set; } = null!;

    /// <summary>授權效果：grant（額外授權）／deny（特別禁止）；參見 <see cref="PermissionEffects"/>。</summary>
    public string Effect { get; set; } = PermissionEffects.Grant;

    /// <summary>是否具備建立權限（Create）。</summary>
    public bool PerCreate { get; set; }

    /// <summary>是否具備讀取檢視權限（Read）。</summary>
    public bool PerRead { get; set; }

    /// <summary>是否具備編輯修改權限（Update）。</summary>
    public bool PerUpdate { get; set; }

    /// <summary>是否具備刪除權限（Delete）。</summary>
    public bool PerDelete { get; set; }

    /// <summary>已設定之進階子功能選項 JSON 陣列。</summary>
    public string OptionsJson { get; set; } = "[]";

    /// <summary>特例授權原因備註。</summary>
    public string? Reason { get; set; }

    /// <summary>權限有效截止時間戳記；到期後不再計入有效權限，可做臨時授權。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>授權操作者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>稽核紀錄；不建 FK，主檔刪除後仍保留。</summary>
public sealed class AuditLog
{
    /// <summary>稽核紀錄唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>觸發操作當下所屬公司識別碼；null 表示平台層級操作。</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>執行操作之使用者識別碼；null 表示系統排程或匿名未驗證操作。</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>異動對象資料實體類型名稱（如 Quote、Project）。</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>異動對象實體識別碼。</summary>
    public Guid? EntityId { get; set; }

    /// <summary>操作動作代碼（如 create、update、delete、login、void）。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>異動前快照資料 JSON；新增時為 null。</summary>
    public string? BeforeJson { get; set; }

    /// <summary>異動後快照資料 JSON；刪除時為 null。</summary>
    public string? AfterJson { get; set; }

    /// <summary>操作來源客戶端 IP 位址。</summary>
    public string? Ip { get; set; }

    /// <summary>稽核事件發生時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>資料範圍常數定義與比較輔助。</summary>
public static class DataScopes
{
    /// <summary>僅限本人：自己負責或被加入專案成員的專案。</summary>
    public const string Own = "own";

    /// <summary>本部門：歸屬於自己所屬部門的專案與客戶。</summary>
    public const string Department = "department";

    /// <summary>本部門與所有下層子部門之專案與客戶。</summary>
    public const string DepartmentTree = "department_tree";

    /// <summary>全公司：可存取全公司所有部門之專案與客戶。</summary>
    public const string Company = "company";

    /// <summary>所有定義的資料範圍常數陣列。</summary>
    public static readonly string[] All = [Own, Department, DepartmentTree, Company];

    /// <summary>
    /// 驗證給定之字串是否為合法的資料範圍常數。
    /// </summary>
    /// <param name="value">待驗證的字串值。</param>
    /// <returns>若合法回傳 true；否則為 false。</returns>
    public static bool IsValid(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);

    /// <summary>範圍大小排序：own &lt; department &lt; department_tree &lt; company；不認得的值當成最大，避免被拿來繞過比較。</summary>
    /// <param name="value">資料範圍名稱。</param>
    /// <returns>排序數值（越小範圍越窄）。</returns>
    public static int Rank(string? value) => Array.IndexOf(All, value) is var index and >= 0 ? index : All.Length;

    /// <summary>建立或指派的資料範圍不可大於操作者自己的資料範圍，否則「建一個全公司範圍的角色再指派給自己」就能繞過資料範圍。</summary>
    /// <param name="value">目標資料範圍。</param>
    /// <param name="limit">操作者上限資料範圍。</param>
    /// <returns>若目標範圍小於或等於上限則回傳 true；否則為 false。</returns>
    public static bool IsWithin(string? value, string? limit) => Rank(value) <= Rank(limit);

    /// <summary>
    /// 取得資料範圍的繁體中文友善標籤說明。
    /// </summary>
    /// <param name="value">資料範圍常數。</param>
    /// <returns>對應的中文標籤文字。</returns>
    public static string Label(string? value) => value switch
    {
        Own => "自己負責／參與的專案",
        Department => "本部門",
        DepartmentTree => "本部門與下層部門",
        Company => "全公司",
        _ => value ?? string.Empty,
    };
}

/// <summary>個人權限效果常數。</summary>
public static class PermissionEffects
{
    /// <summary>加權授予（Grant）：額外開放該項權限。</summary>
    public const string Grant = "grant";

    /// <summary>特別禁止（Deny）：即使角色擁有也強制禁止。</summary>
    public const string Deny = "deny";
}
