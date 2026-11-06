using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 可重複執行的 Seeder：權限目錄、系統角色、admin 系統帳號，以及（開發／展示環境）smoke 測試用的 demo 公司與對客戶展示用的 showcase 公司。
/// </summary>
/// <remarks>
/// 與美達特不同，admin 密碼只在帳號不存在時設定，之後重跑 Seeder 不會把密碼改回預設值。
/// </remarks>
public sealed class QuoteSeeder(QuoteDbContext db, CompanyProvisioner provisioner, CompanyContext companyContext, AuditService audit, ShowcaseSeeder showcase, IOptions<QuoteOptions> options, ILogger<QuoteSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedPermissionsAsync(cancellationToken);
        await SeedSystemRolesAsync(cancellationToken);
        await GrantNewPermissionDefaultsAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
        if (!options.Value.SeedDemoData) return;
        await SeedDemoCompanyAsync(cancellationToken);
        await showcase.SeedAsync(cancellationToken);
    }

    private async Task SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Permissions.ToDictionaryAsync(value => value.Code, StringComparer.Ordinal, cancellationToken);
        // 先建立全部節點再補 ParentId，避免定義順序影響外鍵。
        foreach (var (definition, index) in PermissionCatalog.Definitions.Select((value, index) => (value, index)))
        {
            if (!existing.TryGetValue(definition.Code, out var permission))
            {
                permission = new Permission { Code = definition.Code };
                db.Permissions.Add(permission);
                existing[definition.Code] = permission;
            }
            permission.Name = definition.Name;
            permission.Scope = definition.Scope;
            permission.SortOrder = (index + 1) * 10;
            permission.RoutePath = definition.RoutePath;
            permission.IsMenu = definition.RoutePath is not null || PermissionCatalog.IsGroup(definition.Code);
            permission.SubFeaturesJson = JsonSerializer.Serialize(definition.SubFeatures.Select(value => new { key = value.Key, name = value.Name }));
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach (var definition in PermissionCatalog.Definitions)
        {
            existing[definition.Code].ParentId = definition.Parent is null ? null : existing[definition.Parent].Id;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSystemRolesAsync(CancellationToken cancellationToken)
    {
        var permissionIds = await db.Permissions.ToDictionaryAsync(value => value.Code, value => value.Id, StringComparer.Ordinal, cancellationToken);
        foreach (var definition in PermissionCatalog.SystemRoles)
        {
            var role = await db.Roles.SingleOrDefaultAsync(value => value.CompanyId == null && value.Code == definition.Code, cancellationToken);
            var isNew = role is null;
            role ??= new Role { Code = definition.Code, IsSystem = true };
            role.Name = definition.Name;
            role.Scope = definition.Scope;
            role.IsFullAccess = definition.IsFullAccess;
            if (isNew)
            {
                role.DataScope = definition.DataScope;
                db.Roles.Add(role);
            }

            // 完整存取角色每次都同步到全部權限，新增的資源自動納入；其他系統角色只在首次建立時寫入基線，保留平台管理員後續調整。
            if (isNew || definition.IsFullAccess)
            {
                // 主鍵 (RoleId, PermissionId) 不能在同一次 SaveChanges 先刪後加。
                db.RolePermissions.RemoveRange(await db.RolePermissions.Where(value => value.RoleId == role.Id).ToListAsync(cancellationToken));
                await db.SaveChangesAsync(cancellationToken);
                db.RolePermissions.AddRange(definition.Permissions.Select(value => new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permissionIds[value.Code],
                    PerCreate = value.PerCreate,
                    PerRead = value.PerRead,
                    PerUpdate = value.PerUpdate,
                    PerDelete = value.PerDelete,
                    OptionsJson = JsonSerializer.Serialize(value.Options),
                }));
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>新功能第一次出現時，要替既有公司角色補上預設授權的權限代碼；內容由 <see cref="DefaultForExistingRole"/> 決定。</summary>
    private static readonly string[] NewPermissionDefaultCodes = ["office", "followups"];

    private const string DefaultGrantedAction = "permission.default_granted";

    /// <summary>
    /// 決定既有角色對新權限的預設授權。
    /// </summary>
    /// <param name="code">新權限代碼。</param>
    /// <param name="roleCode">角色代碼。</param>
    /// <param name="isSystem">是否為系統內建角色。</param>
    /// <param name="projects">該角色目前對 <c>projects</c> 的授權；沒有則為 null。</param>
    /// <returns>要補上的授權；null 表示不授權。</returns>
    /// <remarks>
    /// followups：系統角色依 <see cref="PermissionCatalog.SystemRoles"/>（會計沒有）；自訂角色比照它對專案的權限，
    /// 因為跟進紀錄寫在專案上，能看、能改專案的自訂角色（例如部門管理者）通常也要能看、能寫紀錄。
    /// </remarks>
    internal static PermissionAllowance? DefaultForExistingRole(string code, string roleCode, bool isSystem, RolePermission? projects) => code switch
    {
        "office" => new PermissionAllowance("office", false, true, false, false, []),
        "followups" when isSystem => PermissionCatalog.SystemRoles.SingleOrDefault(role => role.Code == roleCode)?.Permissions.SingleOrDefault(value => value.Code == "followups"),
        "followups" => projects is { PerRead: true } ? new PermissionAllowance("followups", projects.PerCreate, true, projects.PerUpdate, projects.PerDelete, []) : null,
        _ => throw new InvalidOperationException($"{code} 沒有定義既有角色的預設授權。"),
    };

    /// <summary>
    /// 新功能第一次出現時，替既有的公司角色補上預設授權；之後管理員收回也不會再被補回。
    /// </summary>
    /// <remarks>
    /// 以 audit_logs 的 <c>permission.default_granted</c> 紀錄當一次性標記，而不是「這次執行才新建權限列」：
    /// 權限列在 <see cref="SeedPermissionsAsync"/> 就已提交，若授權在後面失敗，重跑時仍會補上。
    /// 沒有任何角色需要補授權（例如全新資料庫）時也寫入標記，管理員日後收回的授權才不會被補回。
    /// 授權、AuthVersion 遞增與標記在同一個交易，要嘛全部完成、要嘛全部重來。
    /// 完整存取角色由 <see cref="SeedSystemRolesAsync"/> 同步；這裡只處理其他公司角色（系統與自訂）。
    /// Seeder 沒有公司範圍，查 membership 必須 IgnoreQueryFilters；audit_logs 不受公司過濾。
    /// </remarks>
    private async Task GrantNewPermissionDefaultsAsync(CancellationToken cancellationToken)
    {
        var projectsPermissionId = await db.Permissions.Where(value => value.Code == "projects").Select(value => value.Id).SingleAsync(cancellationToken);
        foreach (var code in NewPermissionDefaultCodes)
        {
            var permissionId = await db.Permissions.Where(value => value.Code == code).Select(value => value.Id).SingleAsync(cancellationToken);
            if (await db.AuditLogs.AnyAsync(value => value.EntityType == "permission" && value.EntityId == permissionId && value.Action == DefaultGrantedAction, cancellationToken)) continue;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var roles = await db.Roles
                .Where(role => role.Scope == "company" && !role.IsFullAccess && !db.RolePermissions.Any(value => value.RoleId == role.Id && value.PermissionId == permissionId))
                .Select(role => new
                {
                    role.Id,
                    role.Code,
                    role.IsSystem,
                    Projects = db.RolePermissions.FirstOrDefault(value => value.RoleId == role.Id && value.PermissionId == projectsPermissionId),
                })
                .ToListAsync(cancellationToken);
            var grants = roles
                .Select(role => (role.Id, Allowance: DefaultForExistingRole(code, role.Code, role.IsSystem, role.Projects)))
                .Where(value => value.Allowance is not null)
                .ToList();
            var roleIds = grants.Select(value => value.Id).ToList();
            if (grants.Count > 0)
            {
                db.RolePermissions.AddRange(grants.Select(value => new RolePermission
                {
                    RoleId = value.Id,
                    PermissionId = permissionId,
                    PerCreate = value.Allowance!.PerCreate,
                    PerRead = value.Allowance.PerRead,
                    PerUpdate = value.Allowance.PerUpdate,
                    PerDelete = value.Allowance.PerDelete,
                    OptionsJson = "[]",
                }));
                await db.SaveChangesAsync(cancellationToken);
                var userIds = db.CompanyMemberships.IgnoreQueryFilters().Where(value => roleIds.Contains(value.RoleId)).Select(value => value.UserId);
                await db.Users.Where(user => userIds.Contains(user.Id))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.AuthVersion, user => user.AuthVersion + 1), cancellationToken);
            }
            audit.Add(null, null, "permission", permissionId, DefaultGrantedAction, after: new { code, roleIds });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("已記錄 {Code} 的預設授權，替 {Count} 個既有角色開放。", code, roleIds.Count);
        }
    }

    /// <summary>供 <c>--self-test</c> 驗證既有角色對新權限的預設授權。</summary>
    public static void AssertExistingRoleDefaults()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"既有角色預設授權：{message}");
        }
        static bool Is(PermissionAllowance? value, bool create, bool read, bool update, bool delete) =>
            value is not null && value.PerCreate == create && value.PerRead == read && value.PerUpdate == update && value.PerDelete == delete;

        Check(Is(DefaultForExistingRole("office", "sales", true, null), false, true, false, false), "office 一律只補 read");
        Check(Is(DefaultForExistingRole("followups", "sales", true, null), true, true, true, true), "業務補 CRUD");
        Check(Is(DefaultForExistingRole("followups", "sales-manager", true, null), true, true, true, true), "業務主管補 CRUD");
        Check(Is(DefaultForExistingRole("followups", "viewer", true, null), false, true, false, false), "唯讀只補 read");
        Check(DefaultForExistingRole("followups", "accountant", true, new RolePermission { PerRead = true }) is null, "會計不補");
        Check(Is(DefaultForExistingRole("followups", "dept-manager", false, new RolePermission { PerCreate = true, PerRead = true, PerUpdate = true }), true, true, true, false), "自訂角色比照專案權限");
        Check(DefaultForExistingRole("followups", "custom", false, null) is null, "沒有專案權限的自訂角色不補");
        Check(DefaultForExistingRole("followups", "custom", false, new RolePermission { PerRead = false }) is null, "沒有 projects:read 的自訂角色不補");
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(value => value.Account == "admin", cancellationToken)) return;
        var password = options.Value.BootstrapAdminPassword;
        if (PasswordRules.Validate(password) is string error)
        {
            logger.LogWarning("未建立 admin 系統帳號：Quote__BootstrapAdminPassword {Error}", error);
            return;
        }

        var platformAdmin = await db.Roles.SingleAsync(value => value.CompanyId == null && value.Code == PermissionCatalog.PlatformAdmin, cancellationToken);
        var admin = new User { Account = "admin", DisplayName = "平台管理員", SystemRoleId = platformAdmin.Id };
        admin.PasswordHash = PasswordRules.Hash(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("已建立 admin 系統帳號。");
    }

    /// <summary>smoke 測試用的 demo 公司：部門三層、公司管理員、業務主管與業務各一位，密碼同 admin 初始密碼。只放測試需要的基本資料，業務資料由測試自己建立。</summary>
    private async Task SeedDemoCompanyAsync(CancellationToken cancellationToken)
    {
        if (await db.Companies.AnyAsync(value => value.Slug == "demo", cancellationToken)) return;
        var password = options.Value.BootstrapAdminPassword;
        if (PasswordRules.Validate(password) is not null) return;

        var (company, _) = await provisioner.CreateAsync(
            new CompanyOnboarding("示範工程股份有限公司", "demo", "12345678", null, null, "demo-admin", "示範公司管理員", null, password),
            cancellationToken);
        companyContext.UseCompany(company.Id);

        var root = await db.Departments.SingleAsync(value => value.ParentId == null, cancellationToken);
        var sales = await DepartmentTree.AddAsync(db, company.Id, root.Id, "業務部", "SALES", 10, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var salesTeam = await DepartmentTree.AddAsync(db, company.Id, sales.Id, "業務一課", "SALES-1", 10, cancellationToken);
        await DepartmentTree.AddAsync(db, company.Id, root.Id, "財務部", "FIN", 20, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var roles = await db.Roles.Where(value => value.CompanyId == null).ToDictionaryAsync(value => value.Code, cancellationToken);
        AddMember("demo-manager", "王業務主管", sales.Id, roles["sales-manager"].Id);
        AddMember("demo-sales", "林業務", salesTeam.Id, roles["sales"].Id);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("已建立示範公司 demo。");

        void AddMember(string account, string displayName, Guid departmentId, Guid roleId)
        {
            var user = new User { Account = account, DisplayName = displayName };
            user.PasswordHash = PasswordRules.Hash(user, password);
            db.Users.Add(user);
            db.CompanyMemberships.Add(new CompanyMembership { CompanyId = company.Id, UserId = user.Id, DepartmentId = departmentId, RoleId = roleId });
        }
    }
}
