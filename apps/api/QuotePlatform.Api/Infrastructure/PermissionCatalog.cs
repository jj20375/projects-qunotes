namespace QuotePlatform.Api.Infrastructure;

/// <summary>權限目錄定義。<paramref name="Parent"/> 為 null 表示頂層；沒有路由的節點是選單群組。</summary>
public sealed record PermissionDefinition(
    string Code,
    string Name,
    string Scope,
    string? Parent,
    string? RoutePath,
    IReadOnlyList<(string Key, string Name)> SubFeatures);

/// <summary>系統內建角色定義。</summary>
public sealed record SystemRoleDefinition(
    string Code,
    string Name,
    string Scope,
    string DataScope,
    bool IsFullAccess,
    IReadOnlyList<PermissionAllowance> Permissions);

/// <summary>
/// 權限目錄與系統角色的唯一來源；Seeder、self-test 與文件（docs/permissions-and-data-scope.md）必須一致。
/// </summary>
/// <remarks>前端 <c>src/shared/types/permissions.ts</c> 的 <c>PermissionCode</c> 也要同步更新。</remarks>
public static class PermissionCatalog
{
    public const string PlatformAdmin = "platform-admin";
    public const string CompanyAdmin = "company-admin";

    public static readonly IReadOnlyList<PermissionDefinition> Definitions =
    [
        new("platform.system_manager", "系統管理", "platform", null, null, []),
        new("platform.companies", "公司管理", "platform", "platform.system_manager", "/platform/companies", [("switch_company", "切換公司")]),
        new("platform.system_users", "系統帳號", "platform", "platform.system_manager", "/platform/system-users", [("reset_password", "重設密碼")]),
        new("platform.roles", "系統角色", "platform", "platform.system_manager", "/platform/roles", []),
        new("platform.permissions", "權限目錄", "platform", "platform.system_manager", "/platform/permissions", []),

        new("dashboard", "戰情 Dashboard", "company", null, "/", [("view_amount", "檢視金額"), ("view_all_staff", "檢視全員排行")]),

        new("business", "業務管理", "company", null, null, []),
        new("clients", "客戶", "company", "business", "/clients", [("import", "匯入"), ("export", "匯出")]),
        new("products", "品項價目", "company", "business", "/products", [("import", "匯入")]),
        new("projects", "專案", "company", "business", "/projects", [("assign_member", "指定成員"), ("close", "結案"), ("export", "匯出")]),
        new("quotes", "報價單", "company", "business", "/quotes",
        [
            ("import_excel", "Excel 範本匯入"), ("import_legacy", "舊報價匯入"), ("import_ocr", "圖像識別匯入"),
            ("export_pdf", "PDF"), ("export_excel", "Excel 匯出"), ("approve", "核准"), ("view_cost", "檢視成本與毛利"),
        ]),
        new("orders", "訂單", "company", "business", null, [("download", "下載附件")]),
        new("warranties", "保固單", "company", "business", null, [("download", "下載附件")]),
        new("invoices", "發票", "company", "business", null, [("download", "下載附件"), ("mark_paid", "登記收款"), ("void", "作廢")]),

        new("company.manager", "公司設定", "company", null, null, []),
        new("company.departments", "部門", "company", "company.manager", "/admin/departments", []),
        new("company.users", "帳號與成員", "company", "company.manager", "/admin/users", [("assign_role", "指派角色"), ("assign_permission", "設定個人權限"), ("reset_password", "重設密碼")]),
        new("company.roles", "角色權限", "company", "company.manager", "/admin/roles", []),
        new("company.settings", "公司參數", "company", "company.manager", "/admin/settings", [("numbering", "單號規則"), ("quote_template", "報價範本")]),
    ];

    /// <summary>可授予的資源（排除純群組節點）。</summary>
    public static IEnumerable<PermissionDefinition> Grantable(string scope) =>
        Definitions.Where(value => value.Scope == scope && !IsGroup(value.Code));

    public static bool IsGroup(string code) => code is "platform.system_manager" or "business" or "company.manager";

    private static PermissionAllowance Full(string code) =>
        new(code, true, true, true, true, Definitions.Single(value => value.Code == code).SubFeatures.Select(value => value.Key).ToArray());

    private static PermissionAllowance Allow(string code, string crud, params string[] options) =>
        new(code, crud.Contains('C'), crud.Contains('R'), crud.Contains('U'), crud.Contains('D'), options);

    public static readonly IReadOnlyList<SystemRoleDefinition> SystemRoles =
    [
        new(PlatformAdmin, "平台管理員", "platform", DataScopes.Company, true, Grantable("platform").Select(value => Full(value.Code)).ToArray()),
        new(CompanyAdmin, "公司管理員", "company", DataScopes.Company, true, Grantable("company").Select(value => Full(value.Code)).ToArray()),
        new("sales-manager", "業務主管", "company", DataScopes.DepartmentTree, false,
        [
            Allow("dashboard", "R", "view_amount", "view_all_staff"),
            Allow("clients", "CRUD", "import", "export"),
            Allow("products", "R"),
            Allow("projects", "CRUD", "assign_member", "close", "export"),
            Full("quotes"),
            Allow("orders", "CRU", "download"),
            Allow("warranties", "CRU", "download"),
            Allow("invoices", "R", "download"),
            Allow("company.users", "R"),
        ]),
        new("sales", "業務", "company", DataScopes.Own, false,
        [
            Allow("dashboard", "R", "view_amount"),
            Allow("clients", "CRU"),
            Allow("products", "R"),
            Allow("projects", "CRU", "assign_member"),
            Allow("quotes", "CRU", "import_excel", "import_legacy", "import_ocr", "export_pdf", "export_excel"),
            Allow("orders", "CRU", "download"),
            Allow("warranties", "CRU", "download"),
            Allow("invoices", "R", "download"),
        ]),
        new("accountant", "會計", "company", DataScopes.Company, false,
        [
            Allow("dashboard", "R", "view_amount", "view_all_staff"),
            Allow("clients", "R"),
            Allow("projects", "R"),
            Allow("quotes", "R", "export_pdf", "view_cost"),
            Allow("orders", "CRUD", "download"),
            Allow("warranties", "R", "download"),
            Allow("invoices", "CRUD", "download", "mark_paid", "void"),
        ]),
        new("viewer", "唯讀", "company", DataScopes.Own, false,
            new[] { "dashboard", "clients", "products", "projects", "quotes", "orders", "warranties", "invoices" }.Select(code => Allow(code, "R")).ToArray()),
    ];

    /// <summary>供 <c>--self-test</c> 驗證目錄與角色定義的一致性。</summary>
    public static void AssertCatalog()
    {
        var codes = Definitions.Select(value => value.Code).ToList();
        if (codes.Count != codes.Distinct(StringComparer.Ordinal).Count()) throw new InvalidOperationException("權限代碼重複。");
        foreach (var definition in Definitions.Where(value => value.Parent is not null))
        {
            var parent = Definitions.SingleOrDefault(value => value.Code == definition.Parent) ?? throw new InvalidOperationException($"{definition.Code} 的上層不存在。");
            if (parent.Scope != definition.Scope) throw new InvalidOperationException($"{definition.Code} 與上層 scope 不一致。");
        }

        var companyAdmin = SystemRoles.Single(value => value.Code == CompanyAdmin).Permissions.Select(value => value.ToGrant()).ToArray();
        foreach (var role in SystemRoles)
        {
            foreach (var allowance in role.Permissions)
            {
                var definition = Definitions.SingleOrDefault(value => value.Code == allowance.Code) ?? throw new InvalidOperationException($"{role.Code} 引用不存在的權限 {allowance.Code}。");
                if (definition.Scope != role.Scope) throw new InvalidOperationException($"{role.Code} 不可授予 {definition.Scope} 權限 {allowance.Code}。");
                if (allowance.Options.Any(option => definition.SubFeatures.All(value => value.Key != option))) throw new InvalidOperationException($"{role.Code} 的 {allowance.Code} 含未定義子功能。");
            }
            if (role.Scope == "company" && !PermissionGrantRules.IsSubsetOf(role.Permissions.Select(value => value.ToGrant()), companyAdmin))
            {
                throw new InvalidOperationException($"{role.Code} 超出 company-admin 上限。");
            }
        }
    }
}
