# 部門虛擬辦公室 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 每個部門一間依成員自動產生的 2D 像素辦公室，成員以角色走動、表情、靠近聊天、敲門，並在後台任何頁面維持上線狀態。

**Architecture:** API 內新增 `Modules/Office/`：純邏輯（地圖產生、規則、上線狀態追蹤）可在 `--self-test` 驗證；SignalR `OfficeHub` 只做驗證與轉發，狀態放記憶體；背景 `OfficeSweeper` 負責閒置／離線轉換與 AuthVersion 巡檢。前端 `AdminShell` 建立全域 hub 連線（`use-presence`），`/office` 頁以動態載入的 Phaser 3 繪製地圖。

**Tech Stack:** ASP.NET Core 10 SignalR（框架內建）、EF Core + MariaDB、Vue 3 + Astro、`@microsoft/signalr`、`phaser@3`、Kenney CC0 素材（Task 11）。

**Spec:** `docs/superpowers/specs/2026-11-03-virtual-office-design.md`

## Global Constraints

- 所有程式碼註解、錯誤文案、文件使用繁體中文；技術名稱保留原文。
- Commit 使用 Conventional Commits，subject 用繁體中文、單一目的；結尾加 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- 單一程式碼檔案不超過 1000 行。
- 公司隔離：範圍一律取自已驗證 JWT 的 `company_id`；業務 entity 實作 `ICompanyOwned`；hub 與背景工作沒有可靠的 HttpContext，資料庫存取前必須以 `CompanyContext.UseCompany` 明確指定公司。
- 不連線、不寫入任何 `metat_*` 資料庫；本機 MariaDB 為 `shared-mariadb`（9529）。
- 權限雙重檢查：前端 `route-manifest` 只負責 UX，API／hub 才是安全邊界。
- 角色權限異動須遞增 `users.AuthVersion` 並寫 `audit_logs`。
- 前端 `package.json` 的相依套件一律鎖定精確版本（不帶 `^`）。
- 驗證最低要求：API 變更跑 `npm run build:api`（0 警告）與 `npm run test:api`；涉及授權跑 `npm run test:smoke`；前端跑 `npm run check:web` 與 `npm run build:web`（Node 24）；Schema 變更產生 migration 並同步 `docs/database-planning.md`、`docs/database-schema.sql`。
- 權限目錄異動同步：`Infrastructure/PermissionCatalog.cs`、`src/shared/types/permissions.ts`、`src/app/route-manifest.ts`、`docs/permissions-and-data-scope.md`。
- 數值常數（照 spec）：閒置 5 分鐘、離線寬限 15 秒、AuthVersion 巡檢預設 60 秒、移動每秒 10 次、聊天每秒 2 則且最多 500 字、表情每秒 1 次、敲門同一對象 30 秒一次、狀態文字最多 40 字、聊天距離切比雪夫 ≤ 2 格、畫面上限 30 fps。

## 檔案地圖

| 檔案 | 職責 |
|---|---|
| `apps/api/QuotePlatform.Api/Infrastructure/OfficeEntities.cs`（新） | `OfficeRoom`、`OfficeProfile` entity |
| `apps/api/QuotePlatform.Api/Modules/Office/OfficeMap.cs`（新） | 地圖資料型別與 `OfficeMapGenerator`（純函式） |
| `apps/api/QuotePlatform.Api/Modules/Office/OfficeRules.cs`（新） | 合法值清單、移動驗證、聊天收訊對象、`NearestFree`、`RateGate`、測試用 `ManualClock` |
| `apps/api/QuotePlatform.Api/Modules/Office/PresenceTracker.cs`（新） | 記憶體中的上線狀態與辦公室位置 |
| `apps/api/QuotePlatform.Api/Modules/Office/OfficeService.cs`（新） | 資料庫存取：房間列表、地圖載入與快取、個人設定 |
| `apps/api/QuotePlatform.Api/Modules/Office/OfficeController.cs`（新） | REST：`/api/office/rooms`、`/presence`、主題設定 |
| `apps/api/QuotePlatform.Api/Modules/Office/OfficeContracts.cs`（新） | `IOfficeClient`、hub DTO、群組名稱 |
| `apps/api/QuotePlatform.Api/Modules/Office/OfficeHub.cs`（新） | SignalR hub |
| `apps/api/QuotePlatform.Api/Modules/Office/OfficeSweeper.cs`（新） | `OfficeConnections` 與背景巡檢 |
| `apps/web/src/shared/services/office-service.ts`（新） | 型別、REST 呼叫、hub 連線單例 |
| `apps/web/src/app/composables/use-presence.ts`（新） | 全域上線狀態、活動回報、敲門通知 |
| `apps/web/src/app/components/PresenceDot.vue`（新） | 上線狀態圓點 |
| `apps/web/src/pages/office/_components/office-grid.ts`（新） | 前端尋路與聊天對象提示（純函式） |
| `apps/web/src/pages/office/_components/office-textures.ts`（新） | 程式繪製的圖塊與角色（素材備援） |
| `apps/web/src/pages/office/_components/office-scene.ts`（新） | Phaser 場景 |
| `apps/web/src/pages/office/_components/OfficeView.vue`（新） | 頁面外框與 hub 事件 |
| `apps/web/src/pages/office/_components/OfficeSidePanel.vue`（新） | 成員清單與聊天 |
| `tests/office-smoke.mjs`（新） | 端對端驗證 |

---

### Task 1: 權限 `office` 與既有角色預設授權

**Files:**
- Modify: `docs/superpowers/specs/2026-11-03-virtual-office-design.md`
- Modify: `apps/api/QuotePlatform.Api/Infrastructure/PermissionCatalog.cs`
- Modify: `apps/api/QuotePlatform.Api/Infrastructure/QuoteSeeder.cs`
- Modify: `apps/web/src/shared/types/permissions.ts`
- Modify: `docs/permissions-and-data-scope.md`
- Modify: `tests/api-smoke.mjs`

**Interfaces:**
- Produces: 權限碼 `office`（scope `company`，route `/office`，無子功能）；API 端以 `scope.Has("office", "read")` 判斷；前端 `PermissionCode` 含 `'office'`。

- [ ] **Step 1: 把 spec 同步成實作採用的細節**

在 `docs/superpowers/specs/2026-11-03-virtual-office-design.md` 修改以下內容（其他段落不動）：

1. 2.4 節：「新增權限碼 `office.view`」改為「新增權限碼 `office`（只用 read），選單路徑 `/office`」；並補一句「既有角色由 Seeder 在 `office` 第一次寫入權限目錄時補上 read，同時遞增相關使用者的 `AuthVersion` 並寫 audit；正式環境部署此版本時必須執行一次 Seeder（`--seed-only` 或 `BOOTSTRAP_ON_START=true`）」。
2. 2.2 節 `office_rooms` 說明：「第一次有人進入時建立」改為「管理員第一次設定主題時建立；沒有資料列時主題視為 `wood`」。
3. 2.1 節 `PresenceSweeper`：「每 30 秒巡檢」改為「每 5 秒巡檢（純記憶體運算）」，並與 `OfficeAuthSweeper` 合併為 `OfficeSweeper`；AuthVersion 巡檢間隔可由 `Quote__OfficeAuthSweepSeconds` 調整（預設 60）。
4. 3.3 節輸出格式改為：
   ```json
   {
     "width": 17, "height": 15, "tileSize": 16, "theme": "wood",
     "grid": ["#################", "#.....#........#", "…"],
     "furniture": [{ "kind": "desk", "x": 7, "y": 10 }],
     "zones": [{ "id": "meeting", "type": "meeting", "x": 8, "y": 1, "w": 8, "h": 6 }],
     "seats": [{ "membershipId": "…", "name": "王小明", "x": 7, "y": 9, "facing": "down", "lastSeenAt": null }],
     "spawn": { "x": 3, "y": 12 }
   }
   ```
   `grid` 每列一個字串：`#` 牆、`.` 可走、`f` 家具（不可走）。
5. 3.2 節「桌上顯示名牌」改為「座位主人不在場時，空椅子上淡淡顯示名字」。
6. 4.1 節：新增 `SetAvatar(avatarKey)`（寫入 `office_profiles` 並廣播 `AvatarChanged`）；`Knock` 要求敲門者已在辦公室內，對象不存在、不同公司、離線一律回「找不到此成員或對方不在線上。」；`AnswerKnock` 必須對應 5 分鐘內的敲門，否則回「沒有待回應的敲門，可能已經逾時。」。
7. 4.2 節：移除 `StatusChanged`、`OperationRejected`；狀態變更併入 `PresenceChanged`；新增 `AvatarChanged`、`OfficeSessionReplaced`（同一人在另一個分頁進入辦公室時，舊分頁收到）。
8. 6.2 節：「以 `OperationRejected` 回傳」改為「以 `HubException` 回傳繁體中文原因，前端取出訊息顯示」。

- [ ] **Step 2: 權限目錄加入 `office`**

`PermissionCatalog.cs` 的 `Definitions`，在 `dashboard` 那一行後面加：

```csharp
        new("office", "虛擬辦公室", "company", null, "/office", []),
```

`SystemRoles` 裡：`sales-manager`、`sales`、`accountant` 三個角色的權限清單各加一行 `Allow("office", "R"),`（放在 `Allow("dashboard", ...)` 之後）；`viewer` 的陣列改為：

```csharp
            new[] { "dashboard", "office", "clients", "products", "projects", "quotes", "orders", "warranties", "invoices" }.Select(code => Allow(code, "R")).ToArray()),
```

- [ ] **Step 3: 跑 self-test 確認目錄一致**

Run: `npm run test:api`
Expected: 最後一行 `self-test passed`

- [ ] **Step 4: Seeder 替既有角色補上預設權限**

`QuoteSeeder.cs`：

1. 建構式加入 `AuditService audit`：
   ```csharp
   public sealed class QuoteSeeder(QuoteDbContext db, CompanyProvisioner provisioner, CompanyContext companyContext, AuditService audit, IOptions<QuoteOptions> options, ILogger<QuoteSeeder> logger)
   ```
2. `SeedAsync` 改為：
   ```csharp
       public async Task SeedAsync(CancellationToken cancellationToken)
       {
           var createdCodes = await SeedPermissionsAsync(cancellationToken);
           await SeedSystemRolesAsync(cancellationToken);
           await GrantNewPermissionDefaultsAsync(createdCodes, cancellationToken);
           await SeedAdminAsync(cancellationToken);
           if (options.Value.SeedDemoData) await SeedDemoCompanyAsync(cancellationToken);
       }
   ```
3. `SeedPermissionsAsync` 回傳本次新建立的代碼：簽章改為 `private async Task<IReadOnlySet<string>> SeedPermissionsAsync(CancellationToken cancellationToken)`；在方法開頭宣告 `var created = new HashSet<string>(StringComparer.Ordinal);`，在 `permission = new Permission { Code = definition.Code };` 那個 if 區塊內加 `created.Add(definition.Code);`，方法最後 `return created;`。
4. 新增方法（放在 `SeedSystemRolesAsync` 之後）：
   ```csharp
       /// <summary>權限第一次寫入目錄時，替既有公司角色預設開放 read 的功能。</summary>
       private static readonly string[] ReadByDefaultForExistingRoles = ["office"];

       /// <summary>
       /// 新功能第一次出現在權限目錄時，替既有的公司角色補上 read；之後管理員收回也不會再被補回。
       /// </summary>
       /// <remarks>
       /// 完整存取角色由 <see cref="SeedSystemRolesAsync"/> 同步；這裡只處理其他公司角色（系統與自訂）。
       /// 依權限異動規則遞增相關使用者的 AuthVersion 並寫 audit，使用者下次換發 token 即取得新權限。
       /// Seeder 沒有公司範圍，查 membership 必須 IgnoreQueryFilters。
       /// </remarks>
       private async Task GrantNewPermissionDefaultsAsync(IReadOnlySet<string> createdCodes, CancellationToken cancellationToken)
       {
           foreach (var code in ReadByDefaultForExistingRoles.Where(createdCodes.Contains))
           {
               var permissionId = await db.Permissions.Where(value => value.Code == code).Select(value => value.Id).SingleAsync(cancellationToken);
               var roleIds = await db.Roles
                   .Where(role => role.Scope == "company" && !role.IsFullAccess && !db.RolePermissions.Any(value => value.RoleId == role.Id && value.PermissionId == permissionId))
                   .Select(role => role.Id)
                   .ToListAsync(cancellationToken);
               if (roleIds.Count == 0) continue;
               db.RolePermissions.AddRange(roleIds.Select(roleId => new RolePermission { RoleId = roleId, PermissionId = permissionId, PerRead = true, OptionsJson = "[]" }));
               var userIds = db.CompanyMemberships.IgnoreQueryFilters().Where(value => roleIds.Contains(value.RoleId)).Select(value => value.UserId);
               await db.Users.Where(user => userIds.Contains(user.Id))
                   .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.AuthVersion, user => user.AuthVersion + 1), cancellationToken);
               audit.Add(null, null, "permission", permissionId, "permission.default_granted", after: new { code, roleIds });
               await db.SaveChangesAsync(cancellationToken);
               logger.LogInformation("已替 {Count} 個既有角色開放 {Code}:read。", roleIds.Count, code);
           }
       }
   ```

- [ ] **Step 5: 前端權限型別**

`apps/web/src/shared/types/permissions.ts` 的 `PermissionCode` 在 `| 'dashboard'` 後加一行 `| 'office'`。

- [ ] **Step 6: 權限文件**

`docs/permissions-and-data-scope.md` 權限目錄表格中，在 `dashboard` 那一列後面加：

```markdown
| company | `office` | `/office` | — |
```

並在該表格下方 Dashboard 說明之後加一段：

```markdown
`/office` 虛擬辦公室只用 `office:read`：同公司成員都能進入任何部門的辦公室，不套資料範圍（辦公室不含業務資料）；hub 連線時亦檢查此權限。既有角色由 Seeder 在 `office` 第一次寫入目錄時補上 read，並遞增相關使用者的 `AuthVersion`。
```

- [ ] **Step 7: api-smoke 驗證既有角色取得權限**

`tests/api-smoke.mjs`，在 `admin 登入為 platform scope，選單只有平台功能` 這個 step 之後插入：

```js
await step('既有業務角色預設取得虛擬辦公室 office:read', async () => {
  const sales = await login('demo-sales');
  const me = (await call('GET', '/api/auth/me', { token: sales.token })).data;
  assert.ok(hasGrant(me, 'office', 'read'));
});
```

- [ ] **Step 8: 建置、套用 Seeder、跑 smoke**

Run: `npm run build:api` → Expected: `0 Warning(s)`、`0 Error(s)`
Run: `npm run dev:api:init` → Expected: log 出現 `已替 N 個既有角色開放 office:read。`（再跑一次不應再出現）
另開終端以 `Quote__LoginCaptchaEnabled=false` 啟動 API（`npm run dev:api`，本機 env 檔已設定時直接啟動），再跑：
Run: `npm run test:smoke` → Expected: 全部通過，含新 step

- [ ] **Step 9: Commit**

```bash
git add docs/superpowers/specs/2026-11-03-virtual-office-design.md apps/api/QuotePlatform.Api/Infrastructure/PermissionCatalog.cs apps/api/QuotePlatform.Api/Infrastructure/QuoteSeeder.cs apps/web/src/shared/types/permissions.ts docs/permissions-and-data-scope.md tests/api-smoke.mjs
git commit -m "feat: 新增虛擬辦公室權限並替既有角色預設開放

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 資料表 `office_rooms`、`office_profiles`

**Files:**
- Create: `apps/api/QuotePlatform.Api/Infrastructure/OfficeEntities.cs`
- Modify: `apps/api/QuotePlatform.Api/Infrastructure/QuoteDbContext.cs`
- Create: `apps/api/QuotePlatform.Api/Migrations/*_AddVirtualOffice.cs`（由工具產生）
- Modify: `docs/database-planning.md`、`docs/database-schema.sql`

**Interfaces:**
- Produces: `QuoteDbContext.OfficeRooms`（`DbSet<OfficeRoom>`）、`QuoteDbContext.OfficeProfiles`（`DbSet<OfficeProfile>`）；`OfficeRoom { Id, CompanyId, DepartmentId, Theme, CreatedAt, UpdatedAt }`；`OfficeProfile { Id, CompanyId, MembershipId, AvatarKey?, Status, StatusText?, LastSeenAt?, UpdatedAt }`。

- [ ] **Step 1: 建立 entity**

`Infrastructure/OfficeEntities.cs`：

```csharp
namespace QuotePlatform.Api.Infrastructure;

/// <summary>部門虛擬辦公室的房間設定；管理員第一次設定主題時建立，沒有資料列時主題視為 wood。</summary>
public sealed class OfficeRoom : ICompanyOwned
{
    /// <summary>房間設定唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬部門識別碼；一個部門一間辦公室。</summary>
    public Guid DepartmentId { get; set; }

    /// <summary>地圖主題：wood（木地板）／carpet（地毯）／dark（深色）。</summary>
    public string Theme { get; set; } = "wood";

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>成員在虛擬辦公室的個人設定；第一次修改狀態、角色或轉為離線時建立。</summary>
public sealed class OfficeProfile : ICompanyOwned
{
    /// <summary>個人設定唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬成員身分識別碼。</summary>
    public Guid MembershipId { get; set; }

    /// <summary>角色外觀鍵；null 表示依 userId 雜湊選出的預設角色。</summary>
    public string? AvatarKey { get; set; }

    /// <summary>手動狀態：available／busy／meeting／break／away。</summary>
    public string Status { get; set; } = "available";

    /// <summary>自訂狀態文字，最多 40 字。</summary>
    public string? StatusText { get; set; }

    /// <summary>最後在線時間（轉為離線時寫入，UTC）。</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 2: DbContext 設定**

`QuoteDbContext.cs`：

1. DbSet 區塊最後加：
   ```csharp
       public DbSet<OfficeRoom> OfficeRooms => Set<OfficeRoom>();
       public DbSet<OfficeProfile> OfficeProfiles => Set<OfficeProfile>();
   ```
2. `OnModelCreating` 中 `ConfigureDocuments(modelBuilder);` 後面加 `ConfigureOffice(modelBuilder);`（必須在套用 Global Query Filter 的 foreach 之前）。
3. 新增方法（放在 `ConfigureDocuments` 之後，與其他 Configure 方法同一個 static／instance 修飾，參照 `ConfigureDocuments` 的宣告）：
   ```csharp
       private static void ConfigureOffice(ModelBuilder modelBuilder)
       {
           modelBuilder.Entity<OfficeRoom>(entity =>
           {
               entity.ToTable("office_rooms");
               entity.HasKey(value => value.Id);
               entity.Property(value => value.Theme).HasMaxLength(20).IsRequired().HasDefaultValue("wood");
               entity.HasIndex(value => new { value.CompanyId, value.DepartmentId }).IsUnique();
               entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
               entity.HasOne<Department>().WithMany().HasForeignKey(value => value.DepartmentId).OnDelete(DeleteBehavior.Cascade);
           });

           modelBuilder.Entity<OfficeProfile>(entity =>
           {
               entity.ToTable("office_profiles", table => table.HasCheckConstraint("CK_office_profiles_Status", "Status IN ('available', 'busy', 'meeting', 'break', 'away')"));
               entity.HasKey(value => value.Id);
               entity.Property(value => value.AvatarKey).HasMaxLength(20);
               entity.Property(value => value.Status).HasMaxLength(20).IsRequired().HasDefaultValue("available");
               entity.Property(value => value.StatusText).HasMaxLength(40);
               entity.HasIndex(value => value.MembershipId).IsUnique();
               entity.HasIndex(value => value.CompanyId);
               entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
               entity.HasOne<CompanyMembership>().WithMany().HasForeignKey(value => value.MembershipId).OnDelete(DeleteBehavior.Cascade);
           });
       }
   ```

- [ ] **Step 3: 產生 migration**

Run: `npm run migrations:add -- AddVirtualOffice`
Expected: `Migrations/` 下新增 `*_AddVirtualOffice.cs` 與 Designer；打開確認只建立 `office_rooms`、`office_profiles` 兩張表與其索引、FK、CHECK，沒有動到其他表。

- [ ] **Step 4: 在既有資料庫套用**

Run: `npm run dev:api:init`
Expected: 成功結束、log 為 `初始化完成。`

- [ ] **Step 5: 空資料庫驗證**

Run: `dotnet ef migrations script 0 --project apps/api/QuotePlatform.Api --idempotent --output /tmp/quote-full-schema.sql`（輸出到 scratchpad 目錄亦可）
Expected: 產生成功，搜尋 `office_profiles` 能找到 `CREATE TABLE`。若使用者有可拋棄的空資料庫，詢問後以該連線字串跑一次 `--seed-only`；`quote_app` 帳號沒有建庫權限時不要自行嘗試 root。

- [ ] **Step 6: 資料庫文件**

`docs/database-planning.md` 在 `### 4.6 檔案` 小節之後新增：

```markdown
### 4.7 虛擬辦公室

| 資料表 | 用途與主要約束 |
|---|---|
| `office_rooms` | 部門辦公室設定（目前只有主題）；unique(`CompanyId`,`DepartmentId`)；管理員第一次設定主題時建立，沒有資料列時主題視為 `wood` |
| `office_profiles` | 成員的角色外觀、手動狀態、狀態文字與最後在線時間；unique(`MembershipId`)；`Status` CHECK 限五種值。走動、表情、聊天、上線狀態本身只存在 API 記憶體，不寫資料庫 |
```

`docs/database-schema.sql`：在 `-- 7. 循環關聯` 區段之前插入下列區段，並把原本的 `7. 循環關聯` 改為 `8. 循環關聯`。索引與 FK 名稱以 Step 3 產生的 migration 為準（若與下方不同，改成 migration 的名稱）：

```sql
-- =========================================================
-- 7. 虛擬辦公室
-- =========================================================

CREATE TABLE office_rooms (
  Id           CHAR(36)     NOT NULL COMMENT '房間設定唯一識別碼',
  CompanyId    CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  DepartmentId CHAR(36)     NOT NULL COMMENT '所屬部門識別碼（一個部門一間辦公室）',
  Theme        VARCHAR(20)  NOT NULL DEFAULT 'wood' COMMENT '地圖主題（wood / carpet / dark）',
  CreatedAt    DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt    DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY IX_office_rooms_CompanyId_DepartmentId (CompanyId, DepartmentId),
  KEY IX_office_rooms_DepartmentId (DepartmentId),
  CONSTRAINT FK_office_rooms_companies_CompanyId FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_office_rooms_departments_DepartmentId FOREIGN KEY (DepartmentId) REFERENCES departments (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='部門虛擬辦公室房間設定檔';

CREATE TABLE office_profiles (
  Id           CHAR(36)     NOT NULL COMMENT '個人設定唯一識別碼',
  CompanyId    CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  MembershipId CHAR(36)     NOT NULL COMMENT '所屬成員身分識別碼',
  AvatarKey    VARCHAR(20)  NULL COMMENT '角色外觀鍵；NULL 表示依使用者雜湊的預設角色',
  Status       VARCHAR(20)  NOT NULL DEFAULT 'available' COMMENT '手動狀態（available / busy / meeting / break / away）',
  StatusText   VARCHAR(40)  NULL COMMENT '自訂狀態文字',
  LastSeenAt   DATETIME(6)  NULL COMMENT '最後在線時間（UTC）',
  UpdatedAt    DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY IX_office_profiles_MembershipId (MembershipId),
  KEY IX_office_profiles_CompanyId (CompanyId),
  CONSTRAINT FK_office_profiles_companies_CompanyId FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_office_profiles_company_memberships_MembershipId FOREIGN KEY (MembershipId) REFERENCES company_memberships (Id) ON DELETE CASCADE,
  CONSTRAINT CK_office_profiles_Status CHECK (Status IN ('available', 'busy', 'meeting', 'break', 'away'))
) ENGINE=InnoDB COMMENT='成員虛擬辦公室個人設定檔';
```

- [ ] **Step 7: 建置**

Run: `npm run build:api` → Expected: 0 警告 0 錯誤
Run: `npm run test:api` → Expected: `self-test passed`

- [ ] **Step 8: Commit**

```bash
git add apps/api/QuotePlatform.Api/Infrastructure/OfficeEntities.cs apps/api/QuotePlatform.Api/Infrastructure/QuoteDbContext.cs apps/api/QuotePlatform.Api/Migrations docs/database-planning.md docs/database-schema.sql
git commit -m "feat: 新增虛擬辦公室房間與個人設定資料表

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 地圖自動產生器

**Files:**
- Create: `apps/api/QuotePlatform.Api/Modules/Office/OfficeMap.cs`
- Modify: `apps/api/QuotePlatform.Api/Program.cs`（self-test 區塊）

**Interfaces:**
- Produces:
  - `record OfficePoint(int X, int Y)`
  - `record OfficeZone(string Id, string Type, int X, int Y, int W, int H)`，`bool Contains(int x, int y)`；`Type` 為 `desk`／`meeting`／`pantry`／`entrance`
  - `record OfficeFurniture(string Kind, int X, int Y)`；`Kind` 為 `desk`、`chair`、`meeting-table`、`coffee`、`fridge`、`counter`、`table`、`plant`、`whiteboard`、`rug`
  - `record OfficeSeat(Guid MembershipId, string Name, int X, int Y, string Facing, DateTimeOffset? LastSeenAt)`
  - `record OfficeSeatOwner(Guid MembershipId, string Name, DateTimeOffset? LastSeenAt)`
  - `record OfficeMap(int Width, int Height, int TileSize, string Theme, IReadOnlyList<string> Grid, IReadOnlyList<OfficeFurniture> Furniture, IReadOnlyList<OfficeZone> Zones, IReadOnlyList<OfficeSeat> Seats, OfficePoint Spawn)`，方法 `bool IsWalkable(int x, int y)`、`OfficeZone? ZoneAt(int x, int y)`、`IReadOnlyDictionary<OfficePoint, int> Distances(OfficePoint from)`、`static IEnumerable<OfficePoint> Neighbors(OfficePoint point)`
  - `static class OfficeMapGenerator`：`OfficeMap Generate(IReadOnlyList<OfficeSeatOwner> owners, string theme)`、`void AssertExamples()`

- [ ] **Step 1: 先寫測試（AssertExamples）並接上 self-test**

建立 `Modules/Office/OfficeMap.cs`，先只放型別、`Generate` 的空殼與測試：

```csharp
using System.Text.Json;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>地圖上的格子座標。</summary>
public sealed record OfficePoint(int X, int Y);

/// <summary>地圖區塊；會議室與茶水間是「同區塊即聊」的房間。</summary>
public sealed record OfficeZone(string Id, string Type, int X, int Y, int W, int H)
{
    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>家具；只有 chair、rug、whiteboard 不擋路（whiteboard 掛在牆上）。</summary>
public sealed record OfficeFurniture(string Kind, int X, int Y);

/// <summary>部門成員的專屬座位。</summary>
public sealed record OfficeSeat(Guid MembershipId, string Name, int X, int Y, string Facing, DateTimeOffset? LastSeenAt);

/// <summary>產生地圖的輸入：依加入部門的先後排序。</summary>
public sealed record OfficeSeatOwner(Guid MembershipId, string Name, DateTimeOffset? LastSeenAt);

/// <summary>
/// 一間部門辦公室；<see cref="Grid"/> 每列一個字串：<c>#</c> 牆、<c>.</c> 可走、<c>f</c> 家具。
/// </summary>
public sealed record OfficeMap(
    int Width,
    int Height,
    int TileSize,
    string Theme,
    IReadOnlyList<string> Grid,
    IReadOnlyList<OfficeFurniture> Furniture,
    IReadOnlyList<OfficeZone> Zones,
    IReadOnlyList<OfficeSeat> Seats,
    OfficePoint Spawn)
{
    public bool IsWalkable(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Grid[y][x] == '.';

    public OfficeZone? ZoneAt(int x, int y) => Zones.FirstOrDefault(zone => zone.Contains(x, y));

    /// <summary>從起點出發的四方向步數；起點不可走時回傳空集合。</summary>
    public IReadOnlyDictionary<OfficePoint, int> Distances(OfficePoint from)
    {
        var distances = new Dictionary<OfficePoint, int>();
        if (!IsWalkable(from.X, from.Y)) return distances;
        distances[from] = 0;
        var queue = new Queue<OfficePoint>([from]);
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            foreach (var next in Neighbors(point))
            {
                if (!IsWalkable(next.X, next.Y) || distances.ContainsKey(next)) continue;
                distances[next] = distances[point] + 1;
                queue.Enqueue(next);
            }
        }
        return distances;
    }

    /// <summary>上、右、下、左四個鄰格。</summary>
    public static IEnumerable<OfficePoint> Neighbors(OfficePoint point) =>
        [new(point.X, point.Y - 1), new(point.X + 1, point.Y), new(point.X, point.Y + 1), new(point.X - 1, point.Y)];
}

/// <summary>依部門成員自動產生辦公室；同樣的輸入必定產生同樣的地圖，不另存地圖檔。</summary>
public static class OfficeMapGenerator
{
    public const int TileSize = 16;

    public static OfficeMap Generate(IReadOnlyList<OfficeSeatOwner> owners, string theme) =>
        throw new NotImplementedException();

    /// <summary>供 <c>--self-test</c> 驗證地圖產生規則。</summary>
    public static void AssertExamples()
    {
        var owners = Enumerable.Range(1, 30).Select(index => new OfficeSeatOwner(new Guid(index, 0, 0, new byte[8]), $"成員{index}", null)).ToArray();
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException($"虛擬辦公室地圖：{message}");
        }

        Check(JsonSerializer.Serialize(Generate(owners[..5], "wood")) == JsonSerializer.Serialize(Generate(owners[..5], "wood")), "同樣的成員必須產生同樣的地圖");
        foreach (var count in new[] { 0, 1, 8, 9, 12, 13, 30 })
        {
            var map = Generate(owners[..count], "wood");
            Check(map.Grid.Count == map.Height && map.Grid.All(row => row.Length == map.Width), $"{count} 人：地圖尺寸不一致");
            Check(map.Seats.Count == count, $"{count} 人：座位數與成員數不符");
            Check(map.Seats.Select(seat => (seat.X, seat.Y)).Distinct().Count() == count, $"{count} 人：座位重複");
            var reachable = map.Distances(map.Spawn);
            Check(map.Seats.All(seat => reachable.ContainsKey(new OfficePoint(seat.X, seat.Y))), $"{count} 人：有座位從入口走不到");
            Check(map.Zones.All(zone => reachable.Keys.Any(point => zone.Contains(point.X, point.Y))), $"{count} 人：有區塊從入口走不到");
        }
        foreach (var (before, after) in new[] { (8, 9), (12, 13) })
        {
            var earlier = Generate(owners[..before], "wood").Seats;
            var later = Generate(owners[..after], "wood").Seats;
            Check(earlier.Select((seat, index) => seat.X == later[index].X && seat.Y == later[index].Y).All(same => same), $"{before}→{after} 人：新增成員後原有座位被移動");
        }
    }
}
```

`Program.cs` 的 self-test 區塊，在 `AiSettings.AssertExamples();` 之後加：

```csharp
    OfficeMapGenerator.AssertExamples();
```

並在檔案上方 using 區加 `using QuotePlatform.Api.Modules.Office;`。

- [ ] **Step 2: 跑測試確認失敗**

Run: `npm run test:api`
Expected: FAIL，例外為 `NotImplementedException`

- [ ] **Step 3: 實作 Generate**

把 `Generate` 換成下列實作（常數放在 `TileSize` 之後）：

```csharp
    private const int ClustersPerBand = 3;
    private const int SeatsPerCluster = 4;
    private const int SeatsPerBand = ClustersPerBand * SeatsPerCluster;
    private const int PantryWidth = 6;
    private const int RoomHeight = 6;
    private const int EntranceWidth = 5;
    private const int BandHeight = 5;
    // 上牆(0)、茶水間與會議室(1..6)、隔牆(7)、走道(8) 之後才是第一排桌位。
    private const int FirstBandY = RoomHeight + 3;

    /// <remarks>
    /// 版面：上方左茶水間、右會議室（≤8 人寬 8 格，否則 12 格），中間一道有門的隔牆；
    /// 下方左側入口，右側桌位以 4 人一組（兩兩對坐）排列，一排固定 3 組，人多往下加排。
    /// 每排組數固定，所以會議室變寬或加排時，原有成員的座位不會移動。
    /// </remarks>
    public static OfficeMap Generate(IReadOnlyList<OfficeSeatOwner> owners, string theme)
    {
        var meetingWidth = owners.Count <= 8 ? 8 : 12;
        var width = PantryWidth + meetingWidth + 3;
        var bands = Math.Max(1, (owners.Count + SeatsPerBand - 1) / SeatsPerBand);
        var height = FirstBandY + bands * BandHeight + 1;
        var meetingX = PantryWidth + 2;
        var dividerY = RoomHeight + 1;

        var grid = new char[height][];
        for (var y = 0; y < height; y++)
        {
            grid[y] = new char[width];
            for (var x = 0; x < width; x++) grid[y][x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? '#' : '.';
        }
        for (var x = 1; x < width - 1; x++) grid[dividerY][x] = '#';
        for (var y = 1; y < dividerY; y++) grid[y][PantryWidth + 1] = '#';
        grid[dividerY][3] = '.';
        grid[dividerY][meetingX + meetingWidth / 2] = '.';

        var furniture = new List<OfficeFurniture>();
        void Place(string kind, int x, int y, bool blocking = true)
        {
            furniture.Add(new OfficeFurniture(kind, x, y));
            if (blocking) grid[y][x] = 'f';
        }

        Place("coffee", 1, 1);
        Place("fridge", 2, 1);
        Place("counter", 3, 1);
        Place("counter", 4, 1);
        Place("plant", PantryWidth, 1);
        Place("table", 3, 4);
        Place("table", 4, 4);

        Place("whiteboard", meetingX + meetingWidth / 2, 0, blocking: false);
        Place("plant", meetingX + meetingWidth - 1, 1);
        for (var x = meetingX + 2; x <= meetingX + meetingWidth - 3; x++)
        {
            Place("meeting-table", x, 3);
            Place("meeting-table", x, 4);
        }

        var spawn = new OfficePoint(3, height - 3);
        Place("rug", spawn.X, spawn.Y, blocking: false);
        Place("plant", 1, height - 2);

        var usedClusters = (owners.Count + SeatsPerCluster - 1) / SeatsPerCluster;
        for (var cluster = 0; cluster < usedClusters; cluster++)
        {
            var (left, top) = ClusterOrigin(cluster);
            for (var column = 0; column < 2; column++)
            {
                Place("desk", left + column, top + 1);
                Place("desk", left + column, top + 2);
            }
        }

        var seats = new List<OfficeSeat>(owners.Count);
        for (var index = 0; index < owners.Count; index++)
        {
            var (left, top) = ClusterOrigin(index / SeatsPerCluster);
            var slot = index % SeatsPerCluster;
            var upper = slot < 2;
            var x = left + slot % 2;
            var y = upper ? top : top + 3;
            Place("chair", x, y, blocking: false);
            seats.Add(new OfficeSeat(owners[index].MembershipId, owners[index].Name, x, y, upper ? "down" : "up", owners[index].LastSeenAt));
        }

        var bottomHeight = height - dividerY - 2;
        OfficeZone[] zones =
        [
            new("pantry", "pantry", 1, 1, PantryWidth, RoomHeight),
            new("meeting", "meeting", meetingX, 1, meetingWidth, RoomHeight),
            new("entrance", "entrance", 1, dividerY + 1, EntranceWidth, bottomHeight),
            new("desk", "desk", EntranceWidth + 1, dividerY + 1, width - EntranceWidth - 2, bottomHeight),
        ];
        return new OfficeMap(width, height, TileSize, theme, grid.Select(row => new string(row)).ToArray(), furniture, zones, seats, spawn);

        static (int Left, int Top) ClusterOrigin(int cluster) =>
            (EntranceWidth + 2 + cluster % ClustersPerBand * 3, FirstBandY + cluster / ClustersPerBand * BandHeight);
    }
```

- [ ] **Step 4: 跑測試確認通過**

Run: `npm run test:api` → Expected: `self-test passed`
Run: `npm run build:api` → Expected: 0 警告

- [ ] **Step 5: Commit**

```bash
git add apps/api/QuotePlatform.Api/Modules/Office/OfficeMap.cs apps/api/QuotePlatform.Api/Program.cs
git commit -m "feat: 依部門成員自動產生虛擬辦公室地圖

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 辦公室規則與頻率限制

**Files:**
- Create: `apps/api/QuotePlatform.Api/Modules/Office/OfficeRules.cs`
- Modify: `apps/api/QuotePlatform.Api/Program.cs`（self-test 區塊）

**Interfaces:**
- Consumes: Task 3 的 `OfficeMap`、`OfficePoint`、`OfficeZone`、`OfficeMapGenerator.Generate`
- Produces:
  - `static class OfficeRules`：清單 `Statuses`、`Emotes`、`Avatars`、`Themes`、`Facings`（皆 `IReadOnlyList<string>`）；常數 `StatusTextMaxLength = 40`、`ChatMaxLength = 500`、`KnockMessageMaxLength = 100`、`ChatRange = 2`；`string DefaultAvatar(Guid userId)`、`bool IsValidStep(OfficeMap map, OfficePoint from, OfficePoint to)`、`IReadOnlyList<Guid> ChatRecipients(OfficeMap map, Guid sender, IReadOnlyDictionary<Guid, OfficePoint> positions)`、`OfficePoint NearestFree(OfficeMap map, OfficePoint target, IReadOnlySet<OfficePoint> occupied)`、`void AssertExamples()`
  - `sealed class RateGate(TimeProvider clock)`：`bool TryPass(string key, int limit, TimeSpan window)`、`void Forget(string keyPrefix)`
  - `sealed class ManualClock(DateTimeOffset start) : TimeProvider`（internal，測試用）：`void Advance(TimeSpan by)`

- [ ] **Step 1: 先寫測試**

建立 `Modules/Office/OfficeRules.cs`，函式本體先 `throw new NotImplementedException()`，只把 `AssertExamples`、`ManualClock` 寫完整：

```csharp
using System.Collections.Concurrent;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>虛擬辦公室的合法值與判斷規則；hub 與前端提示共用同一套定義（前端在 office-grid.ts 有對應實作）。</summary>
public static class OfficeRules
{
    public static readonly IReadOnlyList<string> Statuses = ["available", "busy", "meeting", "break", "away"];
    public static readonly IReadOnlyList<string> Emotes = ["wave", "coffee", "thumbsup", "party", "question"];
    public static readonly IReadOnlyList<string> Avatars = ["a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8"];
    public static readonly IReadOnlyList<string> Themes = ["wood", "carpet", "dark"];
    public static readonly IReadOnlyList<string> Facings = ["up", "down", "left", "right"];
    public const int StatusTextMaxLength = 40;
    public const int ChatMaxLength = 500;
    public const int KnockMessageMaxLength = 100;
    public const int ChatRange = 2;

    public static string DefaultAvatar(Guid userId) => throw new NotImplementedException();
    public static bool IsValidStep(OfficeMap map, OfficePoint from, OfficePoint to) => throw new NotImplementedException();
    public static IReadOnlyList<Guid> ChatRecipients(OfficeMap map, Guid sender, IReadOnlyDictionary<Guid, OfficePoint> positions) => throw new NotImplementedException();
    public static OfficePoint NearestFree(OfficeMap map, OfficePoint target, IReadOnlySet<OfficePoint> occupied) => throw new NotImplementedException();

    /// <summary>供 <c>--self-test</c> 驗證移動、聊天範圍、空位與頻率限制。</summary>
    public static void AssertExamples()
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException($"虛擬辦公室規則：{message}");
        }
        Guid Id(int value) => new(value, 0, 0, new byte[8]);

        // 0 人的地圖：入口 (3,12)，會議室 x 8..15、y 1..6，冰箱在 (2,1)。
        var map = OfficeMapGenerator.Generate([], "wood");
        var spawn = map.Spawn;
        Check(IsValidStep(map, spawn, new(spawn.X + 1, spawn.Y)), "相鄰可走的格子被拒");
        Check(!IsValidStep(map, spawn, new(spawn.X + 1, spawn.Y + 1)), "斜走沒有被擋下");
        Check(!IsValidStep(map, spawn, new(spawn.X + 2, spawn.Y)), "跳格沒有被擋下");
        Check(!IsValidStep(map, new(1, 2), new(0, 2)), "走進外牆沒有被擋下");
        Check(!IsValidStep(map, new(2, 2), new(2, 1)), "走進家具沒有被擋下");

        var near = new Dictionary<Guid, OfficePoint> { [Id(1)] = spawn, [Id(2)] = new(spawn.X + 2, spawn.Y), [Id(3)] = new(spawn.X + 3, spawn.Y) };
        Check(ChatRecipients(map, Id(1), near).SequenceEqual([Id(2)]), "一般區域只有 2 格內收得到聊天");

        var room = new Dictionary<Guid, OfficePoint> { [Id(1)] = new(9, 2), [Id(2)] = new(14, 5), [Id(3)] = new(9, 8), [Id(4)] = new(9, 6) };
        Check(ChatRecipients(map, Id(1), room).SequenceEqual([Id(2), Id(4)]), "會議室內的人都要收到、室外收不到");
        Check(ChatRecipients(map, Id(3), room).Count == 0, "室外的人不能收到會議室內的人");

        var free = NearestFree(map, spawn, new HashSet<OfficePoint> { spawn });
        Check(free != spawn && Math.Abs(free.X - spawn.X) + Math.Abs(free.Y - spawn.Y) == 1 && map.IsWalkable(free.X, free.Y), "入口被占時要放到旁邊的空格");
        var besideFridge = NearestFree(map, new(2, 1), new HashSet<OfficePoint>());
        Check(besideFridge == new OfficePoint(2, 2), "目標是家具時要放到旁邊可走的格子");

        var avatar = DefaultAvatar(Id(7));
        Check(Avatars.Contains(avatar) && avatar == DefaultAvatar(Id(7)), "預設角色必須固定且在清單內");

        var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var gate = new RateGate(clock);
        Check(gate.TryPass("c:move", 2, TimeSpan.FromSeconds(1)) && gate.TryPass("c:move", 2, TimeSpan.FromSeconds(1)), "限制內要放行");
        Check(!gate.TryPass("c:move", 2, TimeSpan.FromSeconds(1)), "超過限制要擋下");
        clock.Advance(TimeSpan.FromSeconds(1));
        Check(gate.TryPass("c:move", 2, TimeSpan.FromSeconds(1)), "時間窗過後要恢復");
        gate.Forget("c:");
        Check(gate.TryPass("c:move", 1, TimeSpan.FromSeconds(1)), "Forget 後重新計算");
    }
}

/// <summary>以時間窗計數的頻率限制；key 慣例為 <c>{connectionId}:{動作}</c>，斷線時以 <see cref="Forget"/> 清除。</summary>
public sealed class RateGate(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new(StringComparer.Ordinal);

    public bool TryPass(string key, int limit, TimeSpan window) => throw new NotImplementedException();

    public void Forget(string keyPrefix) => throw new NotImplementedException();
}

/// <summary>self-test 用的可控時鐘。</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
```

`Program.cs` self-test 區塊在 `OfficeMapGenerator.AssertExamples();` 之後加 `OfficeRules.AssertExamples();`。

- [ ] **Step 2: 跑測試確認失敗**

Run: `npm run test:api` → Expected: FAIL（`NotImplementedException`）

- [ ] **Step 3: 實作**

替換 `OfficeRules` 的四個方法：

```csharp
    public static string DefaultAvatar(Guid userId) =>
        Avatars[(int)(BitConverter.ToUInt32(userId.ToByteArray(), 0) % (uint)Avatars.Count)];

    /// <summary>只能往上下左右走一格，且目的地可通行。</summary>
    public static bool IsValidStep(OfficeMap map, OfficePoint from, OfficePoint to) =>
        Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y) == 1 && map.IsWalkable(to.X, to.Y);

    /// <summary>
    /// 聊天收訊對象：發送者在會議室或茶水間時是同一區塊的所有人；在其他區域時是 <see cref="ChatRange"/> 格內、且不在房間裡的人。
    /// </summary>
    public static IReadOnlyList<Guid> ChatRecipients(OfficeMap map, Guid sender, IReadOnlyDictionary<Guid, OfficePoint> positions)
    {
        if (!positions.TryGetValue(sender, out var origin)) return [];
        var room = RoomAt(map, origin);
        return positions
            .Where(pair => pair.Key != sender && (room is not null
                ? room.Contains(pair.Value.X, pair.Value.Y)
                : RoomAt(map, pair.Value) is null && Math.Max(Math.Abs(pair.Value.X - origin.X), Math.Abs(pair.Value.Y - origin.Y)) <= ChatRange))
            .Select(pair => pair.Key)
            .Order()
            .ToList();
    }

    /// <summary>離目標最近、可走且沒人站的格子；目標本身是家具時從它旁邊開始找。都找不到時回到入口。</summary>
    public static OfficePoint NearestFree(OfficeMap map, OfficePoint target, IReadOnlySet<OfficePoint> occupied)
    {
        var visited = new HashSet<OfficePoint>();
        var queue = new Queue<OfficePoint>();
        IEnumerable<OfficePoint> starts = map.IsWalkable(target.X, target.Y) ? new[] { target } : OfficeMap.Neighbors(target);
        foreach (var point in starts.Where(point => map.IsWalkable(point.X, point.Y) && visited.Add(point))) queue.Enqueue(point);
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            if (!occupied.Contains(point)) return point;
            foreach (var next in OfficeMap.Neighbors(point).Where(next => map.IsWalkable(next.X, next.Y) && visited.Add(next))) queue.Enqueue(next);
        }
        return map.Spawn;
    }

    private static OfficeZone? RoomAt(OfficeMap map, OfficePoint point) =>
        map.ZoneAt(point.X, point.Y) is { Type: "meeting" or "pantry" } zone ? zone : null;
```

替換 `RateGate` 的兩個方法：

```csharp
    public bool TryPass(string key, int limit, TimeSpan window)
    {
        var now = clock.GetUtcNow();
        var hits = _hits.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (hits)
        {
            while (hits.Count > 0 && now - hits.Peek() >= window) hits.Dequeue();
            if (hits.Count >= limit) return false;
            hits.Enqueue(now);
            return true;
        }
    }

    public void Forget(string keyPrefix)
    {
        foreach (var key in _hits.Keys.Where(key => key.StartsWith(keyPrefix, StringComparison.Ordinal))) _hits.TryRemove(key, out _);
    }
```

- [ ] **Step 4: 跑測試確認通過**

Run: `npm run test:api` → Expected: `self-test passed`
Run: `npm run build:api` → Expected: 0 警告

- [ ] **Step 5: Commit**

```bash
git add apps/api/QuotePlatform.Api/Modules/Office/OfficeRules.cs apps/api/QuotePlatform.Api/Program.cs
git commit -m "feat: 新增虛擬辦公室移動、聊天範圍與頻率限制規則

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 上線狀態追蹤 PresenceTracker

**Files:**
- Create: `apps/api/QuotePlatform.Api/Modules/Office/PresenceTracker.cs`
- Modify: `apps/api/QuotePlatform.Api/Program.cs`（self-test 區塊）

**Interfaces:**
- Consumes: Task 3 `OfficeMap`、`OfficePoint`、`OfficeSeatOwner`、`OfficeMapGenerator`；Task 4 `OfficeRules.IsValidStep`、`OfficeRules.NearestFree`、`ManualClock`
- Produces（全部在 `QuotePlatform.Api.Modules.Office`）：
  - `record ConnectionIdentity(Guid CompanyId, Guid MembershipId, Guid UserId, Guid? DepartmentId, string Name, int AuthVersion)`
  - `record MemberProfile(string AvatarKey, string Status, string? StatusText)`
  - `record PresenceEntry(Guid MembershipId, string Presence, string Status, string? StatusText, Guid? HomeDepartmentId, Guid? OfficeDepartmentId)`；`Presence` 為 `online`／`idle`／`offline`
  - `record OfficeOccupant(Guid MembershipId, string Name, string AvatarKey, int X, int Y, string Facing, string Status, string? StatusText, Guid? HomeDepartmentId)`
  - `record OfficeJoin(OfficeOccupant Me, Guid? PreviousDepartmentId, string? ReplacedConnectionId, bool AlreadyPresent, IReadOnlyList<OfficeOccupant> Occupants)`
  - `record OfficeMove(bool Accepted, Guid DepartmentId, OfficeOccupant Occupant)`
  - `record OfficePresence(Guid DepartmentId, OfficeMap Map, IReadOnlyDictionary<Guid, OfficePoint> Positions)`
  - `abstract record PresenceEvent(Guid CompanyId)`；`record PresenceChangedEvent(Guid CompanyId, PresenceEntry Entry, DateTimeOffset? LastSeenAt)`；`record OccupantLeftEvent(Guid CompanyId, Guid DepartmentId, Guid MembershipId)`
  - `sealed class PresenceTracker(TimeProvider clock)`：常數 `IdleAfter`、`Grace`、`KnockExpiry`；方法 `PresenceChangedEvent? Connect(string connectionId, ConnectionIdentity identity, MemberProfile profile)`、`void Disconnect(string connectionId)`、`PresenceChangedEvent? Touch(string connectionId)`、`IReadOnlyList<PresenceEvent> Sweep()`、`ConnectionIdentity? Identity(string connectionId)`、`ConnectionIdentity? OnlineMember(Guid membershipId)`、`IReadOnlyList<string> ConnectionsOf(Guid membershipId)`、`string? OfficeConnectionOf(Guid membershipId)`、`IReadOnlyList<PresenceEntry> Snapshot(Guid companyId)`、`IReadOnlyDictionary<string, ConnectionIdentity> AllConnections()`、`OfficeJoin? JoinOffice(string connectionId, Guid departmentId, OfficeMap map)`、`OccupantLeftEvent? LeaveOffice(string connectionId)`、`OfficeMove? TryMove(string connectionId, int x, int y, string facing)`、`OfficeOccupant? Place(Guid membershipId, OfficePoint target, string facing)`、`OfficePresence? OfficeOf(Guid membershipId)`、`PresenceChangedEvent? SetStatus(Guid membershipId, string status, string? statusText)`、`bool SetAvatar(Guid membershipId, string avatarKey)`、`void RecordKnock(Guid from, Guid to)`、`bool ConsumeKnock(Guid from, Guid to)`、`static void AssertExamples()`

- [ ] **Step 1: 先寫測試**

建立 `Modules/Office/PresenceTracker.cs`，放入全部 record、類別欄位與 `AssertExamples`；所有公開方法先 `throw new NotImplementedException()`：

```csharp
namespace QuotePlatform.Api.Modules.Office;

/// <summary>hub 連線時從 JWT 與資料庫取得的身分；AuthVersion 供背景巡檢比對。</summary>
public sealed record ConnectionIdentity(Guid CompanyId, Guid MembershipId, Guid UserId, Guid? DepartmentId, string Name, int AuthVersion);

/// <summary>會跟著人走的個人設定（角色外觀與手動狀態）。</summary>
public sealed record MemberProfile(string AvatarKey, string Status, string? StatusText);

/// <summary>給全公司看的上線狀態。</summary>
public sealed record PresenceEntry(Guid MembershipId, string Presence, string Status, string? StatusText, Guid? HomeDepartmentId, Guid? OfficeDepartmentId);

/// <summary>辦公室裡的一個角色。</summary>
public sealed record OfficeOccupant(Guid MembershipId, string Name, string AvatarKey, int X, int Y, string Facing, string Status, string? StatusText, Guid? HomeDepartmentId);

/// <summary>進入辦公室的結果；<see cref="ReplacedConnectionId"/> 為被取代的舊分頁。</summary>
public sealed record OfficeJoin(OfficeOccupant Me, Guid? PreviousDepartmentId, string? ReplacedConnectionId, bool AlreadyPresent, IReadOnlyList<OfficeOccupant> Occupants);

/// <summary>移動的結果；不合法時 <see cref="Occupant"/> 為原位置，供 PositionCorrected 使用。</summary>
public sealed record OfficeMove(bool Accepted, Guid DepartmentId, OfficeOccupant Occupant);

/// <summary>某人所在辦公室與其中所有人的位置（聊天、敲門用）。</summary>
public sealed record OfficePresence(Guid DepartmentId, OfficeMap Map, IReadOnlyDictionary<Guid, OfficePoint> Positions);

/// <summary>需要廣播的狀態變化。</summary>
public abstract record PresenceEvent(Guid CompanyId);

/// <summary>上線、閒置、離線或手動狀態變化；離線時帶最後在線時間。</summary>
public sealed record PresenceChangedEvent(Guid CompanyId, PresenceEntry Entry, DateTimeOffset? LastSeenAt) : PresenceEvent(CompanyId);

/// <summary>有人離開辦公室（寬限期過後才發出）。</summary>
public sealed record OccupantLeftEvent(Guid CompanyId, Guid DepartmentId, Guid MembershipId) : PresenceEvent(CompanyId);

/// <summary>
/// 記憶體中的上線狀態與辦公室位置；以 membership 為單位，多個分頁算同一人。
/// </summary>
/// <remarks>
/// 單一 API 實例時有效；改為多實例時需換成 SignalR Redis backplane 與共用儲存。
/// 斷線後有 <see cref="Grace"/> 寬限期，重新整理頁面不會讓狀態或辦公室位置閃爍。
/// 全部操作以同一把鎖保護；人數在數百以內，O(n) 掃描足夠。
/// </remarks>
public sealed class PresenceTracker(TimeProvider clock)
{
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan KnockExpiry = TimeSpan.FromMinutes(5);

    private sealed class Member(ConnectionIdentity identity, MemberProfile profile, DateTimeOffset now)
    {
        public ConnectionIdentity Identity { get; set; } = identity;
        public MemberProfile Profile { get; set; } = profile;
        public HashSet<string> Connections { get; } = new(StringComparer.Ordinal);
        public DateTimeOffset LastActivity { get; set; } = now;
        public DateTimeOffset? DisconnectedAt { get; set; }
        public bool Idle { get; set; }
        public Guid? OfficeDepartmentId { get; set; }
        public OfficeMap? Map { get; set; }
        public string? OfficeConnectionId { get; set; }
        public DateTimeOffset? OfficeLeftAt { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Facing { get; set; } = "down";
    }

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Member> _members = [];
    private readonly Dictionary<string, Guid> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<(Guid From, Guid To), DateTimeOffset> _knocks = [];

    // ……公開方法（Step 3 實作）……

    /// <summary>供 <c>--self-test</c> 驗證上線、閒置、寬限期、辦公室進出與敲門。</summary>
    public static void AssertExamples()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var tracker = new PresenceTracker(clock);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var department = Guid.NewGuid();
        ConnectionIdentity Identity(Guid companyId, string name) => new(companyId, Guid.NewGuid(), Guid.NewGuid(), department, name, 1);
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException($"上線狀態：{message}");
        }
        var profile = new MemberProfile("a1", "available", null);
        var alice = Identity(companyA, "Alice");
        var bob = Identity(companyA, "Bob");
        var carol = Identity(companyB, "Carol");

        Check(tracker.Connect("a1", alice, profile)?.Entry.Presence == "online", "第一條連線要廣播上線");
        Check(tracker.Connect("a2", alice, profile) is null, "第二個分頁不重複廣播");
        tracker.Disconnect("a1");
        Check(tracker.Snapshot(companyA).Single().Presence == "online", "還有分頁時維持上線");
        tracker.Disconnect("a2");
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(tracker.Sweep().Count == 0, "寬限期內不轉離線");
        Check(tracker.Connect("a3", alice, profile) is null, "寬限期內重連不廣播");
        tracker.Disconnect("a3");
        clock.Advance(TimeSpan.FromSeconds(16));
        var offline = tracker.Sweep().OfType<PresenceChangedEvent>().Single();
        Check(offline.Entry.Presence == "offline" && offline.LastSeenAt is not null, "超過寬限期轉離線並帶最後在線時間");
        Check(tracker.Snapshot(companyA).Count == 0, "離線後不在快照中");

        tracker.Connect("b1", bob, profile);
        tracker.Connect("c1", carol, profile);
        Check(tracker.Snapshot(companyA).All(entry => entry.MembershipId == bob.MembershipId), "快照不含其他公司");
        clock.Advance(PresenceTracker.IdleAfter);
        Check(tracker.Sweep().OfType<PresenceChangedEvent>().Count(change => change.Entry.Presence == "idle") == 2, "5 分鐘沒操作轉閒置");
        Check(tracker.Touch("b1")?.Entry.Presence == "online", "有操作就恢復上線");

        var map = OfficeMapGenerator.Generate([new OfficeSeatOwner(bob.MembershipId, "Bob", null)], "wood");
        var join = tracker.JoinOffice("b1", department, map)!;
        Check(join.Me.X == map.Spawn.X && join.Me.Y == map.Spawn.Y && !join.AlreadyPresent, "進入辦公室出現在入口");
        Check(tracker.TryMove("b1", map.Spawn.X + 1, map.Spawn.Y, "right")!.Accepted, "相鄰格可以走");
        Check(!tracker.TryMove("b1", map.Spawn.X + 2, map.Spawn.Y + 1, "right")!.Accepted, "斜走或跳格要被拒絕");
        tracker.Connect("b2", bob, profile);
        Check(tracker.JoinOffice("b2", department, map)!.ReplacedConnectionId == "b1", "另一個分頁進入時取代原分頁");
        Check(tracker.TryMove("b1", map.Spawn.X, map.Spawn.Y, "left") is null, "被取代的分頁不能再移動");
        tracker.Disconnect("b2");
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(!tracker.Sweep().OfType<OccupantLeftEvent>().Any(), "辦公室分頁寬限期內不離場");
        tracker.Connect("b3", bob, profile);
        var rejoin = tracker.JoinOffice("b3", department, map)!;
        Check(rejoin.AlreadyPresent && rejoin.Me.X == map.Spawn.X + 1, "寬限期內重回辦公室保留位置");
        var seat = map.Seats.Single();
        Check(tracker.Place(bob.MembershipId, new OfficePoint(seat.X, seat.Y), seat.Facing) is { } placed && placed.X == seat.X && placed.Y == seat.Y, "回座位要放到自己的座位");
        tracker.Disconnect("b3");
        clock.Advance(TimeSpan.FromSeconds(16));
        Check(tracker.Sweep().OfType<OccupantLeftEvent>().Count() == 1, "超過寬限期離開辦公室");

        tracker.RecordKnock(alice.MembershipId, bob.MembershipId);
        Check(tracker.ConsumeKnock(alice.MembershipId, bob.MembershipId), "敲門後可以回應");
        Check(!tracker.ConsumeKnock(alice.MembershipId, bob.MembershipId), "同一次敲門只能回應一次");
    }
}
```

`Program.cs` self-test 區塊在 `OfficeRules.AssertExamples();` 之後加 `PresenceTracker.AssertExamples();`。

- [ ] **Step 2: 跑測試確認失敗**

Run: `npm run test:api` → Expected: FAIL（`NotImplementedException`）

- [ ] **Step 3: 實作公開方法與私有工具**

在 `// ……公開方法（Step 3 實作）……` 位置放入：

```csharp
    /// <summary>新連線；第一次上線或從閒置恢復時回傳要廣播的事件。</summary>
    public PresenceChangedEvent? Connect(string connectionId, ConnectionIdentity identity, MemberProfile profile)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            var isNew = !_members.TryGetValue(identity.MembershipId, out var member);
            if (member is null)
            {
                member = new Member(identity, profile, now);
                _members[identity.MembershipId] = member;
            }
            var wasIdle = member.Idle;
            member.Identity = identity;
            member.Connections.Add(connectionId);
            member.DisconnectedAt = null;
            member.LastActivity = now;
            member.Idle = false;
            _connections[connectionId] = identity.MembershipId;
            return isNew || wasIdle ? Changed(member) : null;
        }
    }

    /// <summary>斷線只記下時間；離線與離開辦公室由 <see cref="Sweep"/> 在寬限期後處理。</summary>
    public void Disconnect(string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connectionId, out var membershipId) || !_members.TryGetValue(membershipId, out var member)) return;
            var now = clock.GetUtcNow();
            member.Connections.Remove(connectionId);
            if (member.OfficeConnectionId == connectionId)
            {
                member.OfficeConnectionId = null;
                member.OfficeLeftAt = now;
            }
            if (member.Connections.Count == 0) member.DisconnectedAt = now;
        }
    }

    /// <summary>前端回報有操作；從閒置恢復時回傳事件。</summary>
    public PresenceChangedEvent? Touch(string connectionId)
    {
        lock (_gate)
        {
            if (Find(connectionId) is not { } member) return null;
            member.LastActivity = clock.GetUtcNow();
            if (!member.Idle) return null;
            member.Idle = false;
            return Changed(member);
        }
    }

    /// <summary>背景巡檢：寬限期過後離開辦公室、轉離線，以及 5 分鐘沒操作轉閒置。</summary>
    public IReadOnlyList<PresenceEvent> Sweep()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            var events = new List<PresenceEvent>();
            foreach (var member in _members.Values.ToList())
            {
                if (member.OfficeDepartmentId is Guid departmentId && member.OfficeConnectionId is null && now - member.OfficeLeftAt >= Grace)
                {
                    events.Add(new OccupantLeftEvent(member.Identity.CompanyId, departmentId, member.Identity.MembershipId));
                    ClearOffice(member);
                }
                if (member.Connections.Count == 0 && now - member.DisconnectedAt >= Grace)
                {
                    _members.Remove(member.Identity.MembershipId);
                    events.Add(new PresenceChangedEvent(member.Identity.CompanyId, Entry(member) with { Presence = "offline", OfficeDepartmentId = null }, member.DisconnectedAt));
                }
                else if (member.Connections.Count > 0 && !member.Idle && now - member.LastActivity >= IdleAfter)
                {
                    member.Idle = true;
                    events.Add(Changed(member));
                }
            }
            foreach (var key in _knocks.Where(pair => now - pair.Value >= KnockExpiry).Select(pair => pair.Key).ToList()) _knocks.Remove(key);
            return events;
        }
    }

    public ConnectionIdentity? Identity(string connectionId)
    {
        lock (_gate) return Find(connectionId)?.Identity;
    }

    /// <summary>目前至少有一條連線的成員；寬限期中的人不算在線（不能被敲門）。</summary>
    public ConnectionIdentity? OnlineMember(Guid membershipId)
    {
        lock (_gate) return _members.TryGetValue(membershipId, out var member) && member.Connections.Count > 0 ? member.Identity : null;
    }

    public IReadOnlyList<string> ConnectionsOf(Guid membershipId)
    {
        lock (_gate) return _members.TryGetValue(membershipId, out var member) ? member.Connections.ToList() : [];
    }

    /// <summary>正在顯示辦公室的那個分頁；聊天只送到這個分頁。</summary>
    public string? OfficeConnectionOf(Guid membershipId)
    {
        lock (_gate) return _members.TryGetValue(membershipId, out var member) ? member.OfficeConnectionId : null;
    }

    public IReadOnlyList<PresenceEntry> Snapshot(Guid companyId)
    {
        lock (_gate) return _members.Values.Where(member => member.Identity.CompanyId == companyId).Select(Entry).ToList();
    }

    /// <summary>所有連線與其身分，供 AuthVersion 巡檢。</summary>
    public IReadOnlyDictionary<string, ConnectionIdentity> AllConnections()
    {
        lock (_gate) return _connections.ToDictionary(pair => pair.Key, pair => _members[pair.Value].Identity, StringComparer.Ordinal);
    }

    /// <summary>
    /// 進入部門辦公室；同一人已在這間（寬限期或另一個分頁）時保留位置，否則從入口旁的空格出現。
    /// 由目前這個分頁接手辦公室畫面，舊分頁回傳於 <see cref="OfficeJoin.ReplacedConnectionId"/>。
    /// </summary>
    public OfficeJoin? JoinOffice(string connectionId, Guid departmentId, OfficeMap map)
    {
        lock (_gate)
        {
            if (Find(connectionId) is not { } member) return null;
            var previous = member.OfficeDepartmentId;
            var replaced = member.OfficeConnectionId is string old && old != connectionId ? old : null;
            var alreadyPresent = previous == departmentId;
            if (!alreadyPresent || !map.IsWalkable(member.X, member.Y))
            {
                var spot = OfficeRules.NearestFree(map, map.Spawn, Occupied(member, departmentId));
                (member.X, member.Y, member.Facing) = (spot.X, spot.Y, "down");
            }
            member.OfficeDepartmentId = departmentId;
            member.Map = map;
            member.OfficeConnectionId = connectionId;
            member.OfficeLeftAt = null;
            var occupants = InOffice(member.Identity.CompanyId, departmentId).Select(Occupant).ToList();
            return new OfficeJoin(Occupant(member), previous, replaced, alreadyPresent, occupants);
        }
    }

    /// <summary>主動離開（換頁）；只有目前的辦公室分頁可以離開。</summary>
    public OccupantLeftEvent? LeaveOffice(string connectionId)
    {
        lock (_gate)
        {
            if (Find(connectionId) is not { } member || member.OfficeConnectionId != connectionId || member.OfficeDepartmentId is not Guid departmentId) return null;
            ClearOffice(member);
            return new OccupantLeftEvent(member.Identity.CompanyId, departmentId, member.Identity.MembershipId);
        }
    }

    /// <summary>走一格或原地轉向；不是目前的辦公室分頁時回傳 null。</summary>
    public OfficeMove? TryMove(string connectionId, int x, int y, string facing)
    {
        lock (_gate)
        {
            if (Find(connectionId) is not { } member || member.OfficeConnectionId != connectionId || member is not { OfficeDepartmentId: Guid departmentId, Map: { } map }) return null;
            var accepted = (x == member.X && y == member.Y) || OfficeRules.IsValidStep(map, new OfficePoint(member.X, member.Y), new OfficePoint(x, y));
            if (accepted) (member.X, member.Y, member.Facing) = (x, y, facing);
            return new OfficeMove(accepted, departmentId, Occupant(member));
        }
    }

    /// <summary>瞬移到目標附近的空格（回座位、接受敲門）。</summary>
    public OfficeOccupant? Place(Guid membershipId, OfficePoint target, string facing)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(membershipId, out var member) || member is not { OfficeDepartmentId: Guid departmentId, Map: { } map }) return null;
            var spot = OfficeRules.NearestFree(map, target, Occupied(member, departmentId));
            (member.X, member.Y, member.Facing) = (spot.X, spot.Y, facing);
            return Occupant(member);
        }
    }

    public OfficePresence? OfficeOf(Guid membershipId)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(membershipId, out var member) || member is not { OfficeDepartmentId: Guid departmentId, Map: { } map }) return null;
            var positions = InOffice(member.Identity.CompanyId, departmentId).ToDictionary(value => value.Identity.MembershipId, value => new OfficePoint(value.X, value.Y));
            return new OfficePresence(departmentId, map, positions);
        }
    }

    public PresenceChangedEvent? SetStatus(Guid membershipId, string status, string? statusText)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(membershipId, out var member)) return null;
            member.Profile = member.Profile with { Status = status, StatusText = statusText };
            return Changed(member);
        }
    }

    public bool SetAvatar(Guid membershipId, string avatarKey)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(membershipId, out var member)) return false;
            member.Profile = member.Profile with { AvatarKey = avatarKey };
            return true;
        }
    }

    public void RecordKnock(Guid from, Guid to)
    {
        lock (_gate) _knocks[(from, to)] = clock.GetUtcNow();
    }

    /// <summary>回應敲門前必須有 <see cref="KnockExpiry"/> 內的敲門紀錄；每次敲門只能回應一次。</summary>
    public bool ConsumeKnock(Guid from, Guid to)
    {
        lock (_gate) return _knocks.Remove((from, to), out var at) && clock.GetUtcNow() - at < KnockExpiry;
    }

    private Member? Find(string connectionId) =>
        _connections.TryGetValue(connectionId, out var membershipId) ? _members.GetValueOrDefault(membershipId) : null;

    private IEnumerable<Member> InOffice(Guid companyId, Guid departmentId) =>
        _members.Values.Where(member => member.Identity.CompanyId == companyId && member.OfficeDepartmentId == departmentId);

    private HashSet<OfficePoint> Occupied(Member self, Guid departmentId) =>
        InOffice(self.Identity.CompanyId, departmentId).Where(member => member != self).Select(member => new OfficePoint(member.X, member.Y)).ToHashSet();

    private static void ClearOffice(Member member)
    {
        member.OfficeDepartmentId = null;
        member.Map = null;
        member.OfficeConnectionId = null;
        member.OfficeLeftAt = null;
    }

    private static PresenceEntry Entry(Member member) =>
        new(member.Identity.MembershipId, member.Idle ? "idle" : "online", member.Profile.Status, member.Profile.StatusText, member.Identity.DepartmentId, member.OfficeDepartmentId);

    private static PresenceChangedEvent Changed(Member member) => new(member.Identity.CompanyId, Entry(member), null);

    private static OfficeOccupant Occupant(Member member) =>
        new(member.Identity.MembershipId, member.Identity.Name, member.Profile.AvatarKey, member.X, member.Y, member.Facing, member.Profile.Status, member.Profile.StatusText, member.Identity.DepartmentId);
```

- [ ] **Step 4: 跑測試確認通過**

Run: `npm run test:api` → Expected: `self-test passed`
Run: `npm run build:api` → Expected: 0 警告

- [ ] **Step 5: Commit**

```bash
git add apps/api/QuotePlatform.Api/Modules/Office/PresenceTracker.cs apps/api/QuotePlatform.Api/Program.cs
git commit -m "feat: 新增虛擬辦公室上線狀態與位置追蹤

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: OfficeService 與 REST API

**Files:**
- Create: `apps/api/QuotePlatform.Api/Modules/Office/OfficeService.cs`
- Create: `apps/api/QuotePlatform.Api/Modules/Office/OfficeController.cs`
- Modify: `apps/api/QuotePlatform.Api/Program.cs`（服務註冊）

**Interfaces:**
- Consumes: Task 2 `OfficeRoom`、`OfficeProfile`、DbSet；Task 3 `OfficeMapGenerator`、`OfficeSeatOwner`、`OfficeMap`；Task 4 `OfficeRules.Themes`、`OfficeRules.DefaultAvatar`；Task 5 `PresenceTracker.Snapshot`、`MemberProfile`
- Produces:
  - `record OfficeRoomSummary(Guid DepartmentId, Guid? ParentId, string Name, int SortOrder, int MemberCount, int OnlineCount, int OccupantCount)`
  - `record LoadedOffice(Guid DepartmentId, string Name, OfficeMap Map)`
  - `record UpdateRoomThemeRequest(string? Theme)`
  - `sealed class OfficeService`：`void UseCompany(Guid companyId)`、`Task<IReadOnlyList<OfficeRoomSummary>> RoomsAsync(Guid companyId, CancellationToken)`、`Task<LoadedOffice?> LoadAsync(Guid companyId, Guid departmentId, CancellationToken)`、`Task<(MemberProfile Profile, string Name)> ProfileAsync(Guid membershipId, Guid userId, CancellationToken)`、`Task SaveStatusAsync(Guid membershipId, string status, string? statusText, CancellationToken)`、`Task SaveAvatarAsync(Guid membershipId, string avatarKey, CancellationToken)`、`Task WriteLastSeenAsync(IReadOnlyList<(Guid MembershipId, DateTimeOffset At)> items, CancellationToken)`、`Task<ServiceResult<Empty>> UpdateThemeAsync(Guid companyId, Guid departmentId, string? theme, CancellationToken)`
  - REST：`GET /api/office/rooms`（`office:read`）→ `OfficeRoomSummary[]`；`GET /api/office/presence`（`office:read`）→ `PresenceEntry[]`；`PUT /api/office/rooms/{departmentId}/theme`（`company.departments:update`）body `{ theme }` → 204

- [ ] **Step 1: 建立 OfficeService**

`Modules/Office/OfficeService.cs`：

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>部門辦公室列表的一列。</summary>
public sealed record OfficeRoomSummary(Guid DepartmentId, Guid? ParentId, string Name, int SortOrder, int MemberCount, int OnlineCount, int OccupantCount);

/// <summary>已載入的辦公室（快取 30 秒）。</summary>
public sealed record LoadedOffice(Guid DepartmentId, string Name, OfficeMap Map);

/// <summary>設定部門辦公室主題。</summary>
public sealed record UpdateRoomThemeRequest(string? Theme);

/// <summary>虛擬辦公室的資料庫存取；走動、聊天等即時資料不經過這裡。</summary>
public sealed class OfficeService(QuoteDbContext db, CompanyContext companyContext, IMemoryCache cache, PresenceTracker tracker)
{
    private static readonly TimeSpan MapCacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>hub 與背景巡檢沒有可靠的 HttpContext，由呼叫端以 JWT（或連線身分）的公司明確指定範圍。</summary>
    public void UseCompany(Guid companyId) => companyContext.UseCompany(companyId);

    /// <summary>公司內所有啟用中的部門；部門樹是完整載入的小型資料，不分頁。</summary>
    public async Task<IReadOnlyList<OfficeRoomSummary>> RoomsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var departments = await db.Departments.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.SortOrder).ThenBy(value => value.Name)
            .Select(value => new { value.Id, value.ParentId, value.Name, value.SortOrder })
            .ToListAsync(cancellationToken);
        var memberCounts = await ActiveMembers()
            .Where(value => value.DepartmentId != null)
            .GroupBy(value => value.DepartmentId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.Key, value => value.Count, cancellationToken);
        var presence = tracker.Snapshot(companyId);
        return departments
            .Select(value => new OfficeRoomSummary(
                value.Id, value.ParentId, value.Name, value.SortOrder,
                memberCounts.GetValueOrDefault(value.Id),
                presence.Count(entry => entry.HomeDepartmentId == value.Id),
                presence.Count(entry => entry.OfficeDepartmentId == value.Id)))
            .ToList();
    }

    /// <summary>載入部門辦公室；部門不存在、停用或不屬於目前公司時回傳 null。成員異動最多 30 秒後反映在地圖上。</summary>
    public async Task<LoadedOffice?> LoadAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken)
    {
        var key = MapCacheKey(companyId, departmentId);
        if (cache.TryGetValue(key, out LoadedOffice? cached)) return cached;
        var department = await db.Departments.AsNoTracking()
            .Where(value => value.Id == departmentId && value.IsActive)
            .Select(value => new { value.Id, value.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (department is null) return null;
        var theme = await db.OfficeRooms.AsNoTracking().Where(value => value.DepartmentId == departmentId).Select(value => value.Theme).SingleOrDefaultAsync(cancellationToken) ?? "wood";
        var owners = await ActiveMembers()
            .Where(value => value.DepartmentId == departmentId)
            .OrderBy(value => value.CreatedAt).ThenBy(value => value.Id)
            .Select(value => new OfficeSeatOwner(
                value.Id,
                value.User.DisplayName,
                db.OfficeProfiles.Where(profile => profile.MembershipId == value.Id).Select(profile => profile.LastSeenAt).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        var loaded = new LoadedOffice(department.Id, department.Name, OfficeMapGenerator.Generate(owners, theme));
        cache.Set(key, loaded, MapCacheDuration);
        return loaded;
    }

    /// <summary>連線時讀取姓名與個人設定；沒有設定時用預設角色與 available。</summary>
    public async Task<(MemberProfile Profile, string Name)> ProfileAsync(Guid membershipId, Guid userId, CancellationToken cancellationToken)
    {
        var name = await db.CompanyMemberships.AsNoTracking().Where(value => value.Id == membershipId).Select(value => value.User.DisplayName).SingleAsync(cancellationToken);
        var profile = await db.OfficeProfiles.AsNoTracking().SingleOrDefaultAsync(value => value.MembershipId == membershipId, cancellationToken);
        return (new MemberProfile(profile?.AvatarKey ?? OfficeRules.DefaultAvatar(userId), profile?.Status ?? "available", profile?.StatusText), name);
    }

    public async Task SaveStatusAsync(Guid membershipId, string status, string? statusText, CancellationToken cancellationToken)
    {
        var profile = await EditableProfileAsync(membershipId, cancellationToken);
        profile.Status = status;
        profile.StatusText = statusText;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAvatarAsync(Guid membershipId, string avatarKey, CancellationToken cancellationToken)
    {
        var profile = await EditableProfileAsync(membershipId, cancellationToken);
        profile.AvatarKey = avatarKey;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>同一家公司一批轉為離線的成員；呼叫前必須 <see cref="UseCompany"/>。</summary>
    public async Task WriteLastSeenAsync(IReadOnlyList<(Guid MembershipId, DateTimeOffset At)> items, CancellationToken cancellationToken)
    {
        foreach (var (membershipId, at) in items)
        {
            var profile = await EditableProfileAsync(membershipId, cancellationToken);
            profile.LastSeenAt = at;
            profile.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ServiceResult<Empty>> UpdateThemeAsync(Guid companyId, Guid departmentId, string? theme, CancellationToken cancellationToken)
    {
        if (theme is null || !OfficeRules.Themes.Contains(theme)) return ServiceResult<Empty>.BadRequest("主題不正確。");
        if (!await db.Departments.AnyAsync(value => value.Id == departmentId, cancellationToken)) return ServiceResult<Empty>.NotFound("找不到部門。");
        var room = await db.OfficeRooms.SingleOrDefaultAsync(value => value.DepartmentId == departmentId, cancellationToken);
        if (room is null)
        {
            room = new OfficeRoom { DepartmentId = departmentId };
            db.OfficeRooms.Add(room);
        }
        room.Theme = theme;
        room.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(MapCacheKey(companyId, departmentId));
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    private async Task<OfficeProfile> EditableProfileAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        var profile = db.OfficeProfiles.Local.FirstOrDefault(value => value.MembershipId == membershipId)
            ?? await db.OfficeProfiles.SingleOrDefaultAsync(value => value.MembershipId == membershipId, cancellationToken);
        if (profile is not null) return profile;
        profile = new OfficeProfile { MembershipId = membershipId };
        db.OfficeProfiles.Add(profile);
        return profile;
    }

    private IQueryable<CompanyMembership> ActiveMembers() =>
        db.CompanyMemberships.AsNoTracking().Where(value => value.IsActive && value.User.Status == "active");

    private static string MapCacheKey(Guid companyId, Guid departmentId) => $"office-map:{companyId}:{departmentId}";
}
```

- [ ] **Step 2: 建立 OfficeController**

`Modules/Office/OfficeController.cs`：

```csharp
using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>
/// 虛擬辦公室 REST：部門辦公室列表、公司上線狀態快照與主題設定。
/// 走動、聊天等即時互動走 <see cref="OfficeHub"/>。
/// </summary>
[Route("api/office")]
public sealed class OfficeController(OfficeService service, PresenceTracker tracker) : ScopedControllerBase
{
    /// <summary>公司內所有部門辦公室與成員數、在線人數、在場人數。</summary>
    [HttpGet("rooms")]
    public async Task<ActionResult> Rooms(CancellationToken cancellationToken) =>
        TryCompany("office", "read", out var scope) ? Ok(await service.RoomsAsync(scope.CompanyId!.Value, cancellationToken)) : Forbidden();

    /// <summary>公司內目前上線與閒置的成員（以 membershipId 為鍵的對照用，不分頁）。</summary>
    [HttpGet("presence")]
    public ActionResult Presence() =>
        TryCompany("office", "read", out var scope) ? Ok(tracker.Snapshot(scope.CompanyId!.Value)) : Forbidden();

    /// <summary>設定部門辦公室主題；需要部門管理的編輯權限。</summary>
    [HttpPut("rooms/{departmentId:guid}/theme")]
    public async Task<ActionResult> UpdateTheme(Guid departmentId, UpdateRoomThemeRequest request, CancellationToken cancellationToken) =>
        TryCompany("company.departments", "update", out var scope) ? ToResult(await service.UpdateThemeAsync(scope.CompanyId!.Value, departmentId, request.Theme, cancellationToken)) : Forbidden();
}
```

- [ ] **Step 3: 註冊服務**

`Program.cs` 在 `builder.Services.AddScoped<DashboardService>();` 之後加：

```csharp
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<RateGate>();
builder.Services.AddScoped<OfficeService>();
```

- [ ] **Step 4: 建置並手動驗證 REST**

Run: `npm run build:api` → Expected: 0 警告
重啟 API（`Quote__LoginCaptchaEnabled=false`），執行：

```bash
node -e "const fs=require('fs');const p=fs.readFileSync('.env.development-api.local','utf8').match(/^Quote__BootstrapAdminPassword=(.+)$/m)[1].trim();(async()=>{const b='http://127.0.0.1:5280';const l=await (await fetch(b+'/api/auth/login',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({account:'demo-admin',password:p})})).json();const r=await fetch(b+'/api/office/rooms',{headers:{authorization:'Bearer '+l.accessToken}});console.log(r.status,JSON.stringify(await r.json()).slice(0,300));})()"
```

Expected: `200 [{"departmentId":…,"name":"示範工程股份有限公司"…},{…"name":"業務部"…}]`

- [ ] **Step 5: Commit**

```bash
git add apps/api/QuotePlatform.Api/Modules/Office/OfficeService.cs apps/api/QuotePlatform.Api/Modules/Office/OfficeController.cs apps/api/QuotePlatform.Api/Program.cs
git commit -m "feat: 新增虛擬辦公室房間列表與上線狀態 API

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: OfficeHub、背景巡檢與部署設定

**Files:**
- Create: `apps/api/QuotePlatform.Api/Modules/Office/OfficeContracts.cs`
- Create: `apps/api/QuotePlatform.Api/Modules/Office/OfficeHub.cs`
- Create: `apps/api/QuotePlatform.Api/Modules/Office/OfficeSweeper.cs`
- Modify: `apps/api/QuotePlatform.Api/Infrastructure/QuoteOptions.cs`
- Modify: `apps/api/QuotePlatform.Api/Program.cs`
- Modify: `infra/nginx/default.conf`
- Modify: `apps/web/astro.config.mjs`
- Modify: `docs/system-architecture.md`

**Interfaces:**
- Consumes: Task 5 全部 tracker API 與 record；Task 6 `OfficeService`、`LoadedOffice`；Task 4 `OfficeRules`、`RateGate`
- Produces:
  - Hub 路徑 `OfficeHub.Path = "/api/hubs/office"`；前端可呼叫的方法：`Activity()`、`JoinOffice(Guid departmentId) → OfficeSnapshot`、`LeaveOffice()`、`Move(int x, int y, string facing)`、`GoToSeat()`、`SetStatus(string status, string? statusText)`、`SetAvatar(string avatarKey)`、`Emote(string key)`、`SendChat(string text)`、`Knock(Guid targetMembershipId, string? message)`、`AnswerKnock(Guid fromMembershipId, bool accept, string? message)`
  - 伺服器事件（`IOfficeClient`）：`PresenceChanged(PresenceEntry entry, DateTimeOffset? lastSeenAt)`、`OccupantJoined(OfficeOccupant occupant)`、`OccupantLeft(Guid membershipId)`、`Moved(Guid membershipId, int x, int y, string facing)`、`PositionCorrected(int x, int y, string facing)`、`Emoted(Guid membershipId, string key)`、`AvatarChanged(Guid membershipId, string avatarKey)`、`ChatReceived(OfficeChatMessage message)`、`Knocked(OfficeKnock knock)`、`KnockAnswered(Guid membershipId, string name, bool accepted, string? message)`、`OfficeSessionReplaced()`、`ForceDisconnect(string reason)`
  - `record OfficeSnapshot(OfficeMap Map, string DepartmentName, OfficeOccupant Me, IReadOnlyList<OfficeOccupant> Occupants)`、`record OfficeChatMessage(Guid FromMembershipId, string FromName, string Text, IReadOnlyList<Guid> Recipients, DateTimeOffset SentAt)`、`record OfficeKnock(Guid FromMembershipId, string FromName, Guid DepartmentId, string DepartmentName, string? Message)`
  - 設定 `Quote__OfficeAuthSweepSeconds`（預設 60）

- [ ] **Step 1: 契約與群組**

`Modules/Office/OfficeContracts.cs`：

```csharp
namespace QuotePlatform.Api.Modules.Office;

/// <summary>進入辦公室時回傳的快照。</summary>
public sealed record OfficeSnapshot(OfficeMap Map, string DepartmentName, OfficeOccupant Me, IReadOnlyList<OfficeOccupant> Occupants);

/// <summary>靠近聊天的一則訊息；<see cref="Recipients"/> 由後端計算，不含發送者。</summary>
public sealed record OfficeChatMessage(Guid FromMembershipId, string FromName, string Text, IReadOnlyList<Guid> Recipients, DateTimeOffset SentAt);

/// <summary>敲門通知。</summary>
public sealed record OfficeKnock(Guid FromMembershipId, string FromName, Guid DepartmentId, string DepartmentName, string? Message);

/// <summary>伺服器推給瀏覽器的事件；方法名稱即前端 <c>hub.on</c> 的事件名稱。</summary>
public interface IOfficeClient
{
    Task PresenceChanged(PresenceEntry entry, DateTimeOffset? lastSeenAt);
    Task OccupantJoined(OfficeOccupant occupant);
    Task OccupantLeft(Guid membershipId);
    Task Moved(Guid membershipId, int x, int y, string facing);
    Task PositionCorrected(int x, int y, string facing);
    Task Emoted(Guid membershipId, string key);
    Task AvatarChanged(Guid membershipId, string avatarKey);
    Task ChatReceived(OfficeChatMessage message);
    Task Knocked(OfficeKnock knock);
    Task KnockAnswered(Guid membershipId, string name, bool accepted, string? message);
    Task OfficeSessionReplaced();
    Task ForceDisconnect(string reason);
}

/// <summary>SignalR 群組名稱；公司群組收上線狀態，辦公室群組收走動與表情。</summary>
public static class OfficeGroups
{
    public static string Company(Guid companyId) => $"company:{companyId}";

    public static string Office(Guid companyId, Guid departmentId) => $"office:{companyId}:{departmentId}";
}
```

- [ ] **Step 2: OfficeHub**

`Modules/Office/OfficeHub.cs`：

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>
/// 虛擬辦公室的即時通道：上線狀態、走動、表情、靠近聊天與敲門。
/// </summary>
/// <remarks>
/// 前端只送意圖，座標、對象與文字一律在這裡驗證後才轉發；錯誤以 <see cref="HubException"/> 回傳繁體中文訊息。
/// 長連線不會在每個請求重新驗證 JWT，帳號停用與權限異動由 <see cref="OfficeSweeper"/> 定期比對 AuthVersion 後斷線。
/// hub 沒有可靠的 HttpContext，存取資料庫前以 <see cref="OfficeService.UseCompany"/> 明確指定連線身分的公司。
/// </remarks>
[Authorize]
public sealed class OfficeHub(PresenceTracker tracker, RateGate gate, OfficeService service, OfficeConnections connections, TimeProvider clock) : Hub<IOfficeClient>
{
    public const string Path = "/api/hubs/office";

    /// <summary>只接受公司範圍、有 membership、非平台代入且具備 office:read 的連線。</summary>
    public override async Task OnConnectedAsync()
    {
        if (Context.User is null
            || !RequestScope.TryRead(Context.User, out var scope)
            || !scope.IsCompany
            || scope.MembershipId is not Guid membershipId
            || scope.IsImpersonating
            || !scope.Has("office", "read"))
        {
            Context.Abort();
            return;
        }
        var companyId = scope.CompanyId!.Value;
        service.UseCompany(companyId);
        var (profile, name) = await service.ProfileAsync(membershipId, scope.UserId, Context.ConnectionAborted);
        connections.Register(Context);
        await Groups.AddToGroupAsync(Context.ConnectionId, OfficeGroups.Company(companyId));
        var changed = tracker.Connect(Context.ConnectionId, new ConnectionIdentity(companyId, membershipId, scope.UserId, scope.DepartmentId, name, scope.AuthVersion), profile);
        if (changed is not null) await Clients.Group(OfficeGroups.Company(companyId)).PresenceChanged(changed.Entry, null);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        tracker.Disconnect(Context.ConnectionId);
        connections.Unregister(Context.ConnectionId);
        gate.Forget(Context.ConnectionId + ":");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>前端偵測到操作時最多每 60 秒送一次。</summary>
    public async Task Activity()
    {
        if (!Allow("activity", 5, TimeSpan.FromMinutes(1))) return;
        if (tracker.Touch(Context.ConnectionId) is { } changed) await Clients.Group(OfficeGroups.Company(changed.CompanyId)).PresenceChanged(changed.Entry, null);
    }

    public async Task<OfficeSnapshot> JoinOffice(Guid departmentId)
    {
        var me = Me();
        Limit("join", 10, TimeSpan.FromMinutes(1));
        service.UseCompany(me.CompanyId);
        var office = await service.LoadAsync(me.CompanyId, departmentId, Context.ConnectionAborted) ?? throw new HubException("找不到此部門的辦公室。");
        var join = tracker.JoinOffice(Context.ConnectionId, departmentId, office.Map) ?? throw new HubException("連線已失效，請重新整理頁面。");
        if (join.PreviousDepartmentId is Guid previous)
        {
            if (join.ReplacedConnectionId is string replaced)
            {
                await Groups.RemoveFromGroupAsync(replaced, OfficeGroups.Office(me.CompanyId, previous));
                await Clients.Client(replaced).OfficeSessionReplaced();
            }
            if (previous != departmentId)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, OfficeGroups.Office(me.CompanyId, previous));
                await Clients.Group(OfficeGroups.Office(me.CompanyId, previous)).OccupantLeft(me.MembershipId);
            }
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, OfficeGroups.Office(me.CompanyId, departmentId));
        if (!join.AlreadyPresent) await Clients.OthersInGroup(OfficeGroups.Office(me.CompanyId, departmentId)).OccupantJoined(join.Me);
        return new OfficeSnapshot(office.Map, office.Name, join.Me, join.Occupants);
    }

    public async Task LeaveOffice()
    {
        if (tracker.LeaveOffice(Context.ConnectionId) is not { } left) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, OfficeGroups.Office(left.CompanyId, left.DepartmentId));
        await Clients.Group(OfficeGroups.Office(left.CompanyId, left.DepartmentId)).OccupantLeft(left.MembershipId);
    }

    /// <summary>走一格；不合法時只回給自己 PositionCorrected，超量的訊息直接丟棄。</summary>
    public async Task Move(int x, int y, string facing)
    {
        if (!OfficeRules.Facings.Contains(facing) || !Allow("move", 10, TimeSpan.FromSeconds(1))) return;
        if (tracker.TryMove(Context.ConnectionId, x, y, facing) is not { } move) return;
        var occupant = move.Occupant;
        if (!move.Accepted)
        {
            CountRejection();
            await Clients.Caller.PositionCorrected(occupant.X, occupant.Y, occupant.Facing);
            return;
        }
        await Clients.OthersInGroup(OfficeGroups.Office(Me().CompanyId, move.DepartmentId)).Moved(occupant.MembershipId, x, y, facing);
    }

    public async Task GoToSeat()
    {
        var me = Me();
        Limit("seat", 5, TimeSpan.FromSeconds(10));
        var office = tracker.OfficeOf(me.MembershipId) ?? throw new HubException("請先進入辦公室。");
        var seat = office.Map.Seats.FirstOrDefault(value => value.MembershipId == me.MembershipId) ?? throw new HubException("你在這間辦公室沒有座位。");
        var placed = tracker.Place(me.MembershipId, new OfficePoint(seat.X, seat.Y), seat.Facing) ?? throw new HubException("請先進入辦公室。");
        await Clients.Group(OfficeGroups.Office(me.CompanyId, office.DepartmentId)).Moved(placed.MembershipId, placed.X, placed.Y, placed.Facing);
    }

    public async Task SetStatus(string status, string? statusText)
    {
        var me = Me();
        if (!OfficeRules.Statuses.Contains(status)) throw new HubException("狀態不正確。");
        statusText = string.IsNullOrWhiteSpace(statusText) ? null : statusText.Trim();
        if (statusText?.Length > OfficeRules.StatusTextMaxLength) throw new HubException($"狀態文字最多 {OfficeRules.StatusTextMaxLength} 字。");
        Limit("status", 5, TimeSpan.FromSeconds(10));
        service.UseCompany(me.CompanyId);
        await service.SaveStatusAsync(me.MembershipId, status, statusText, Context.ConnectionAborted);
        if (tracker.SetStatus(me.MembershipId, status, statusText) is { } changed) await Clients.Group(OfficeGroups.Company(me.CompanyId)).PresenceChanged(changed.Entry, null);
    }

    public async Task SetAvatar(string avatarKey)
    {
        var me = Me();
        if (!OfficeRules.Avatars.Contains(avatarKey)) throw new HubException("角色不正確。");
        Limit("avatar", 5, TimeSpan.FromSeconds(10));
        service.UseCompany(me.CompanyId);
        await service.SaveAvatarAsync(me.MembershipId, avatarKey, Context.ConnectionAborted);
        if (tracker.SetAvatar(me.MembershipId, avatarKey) && tracker.OfficeOf(me.MembershipId) is { } office)
        {
            await Clients.Group(OfficeGroups.Office(me.CompanyId, office.DepartmentId)).AvatarChanged(me.MembershipId, avatarKey);
        }
    }

    public async Task Emote(string key)
    {
        var me = Me();
        if (!OfficeRules.Emotes.Contains(key)) throw new HubException("表情不正確。");
        Limit("emote", 1, TimeSpan.FromSeconds(1));
        var office = tracker.OfficeOf(me.MembershipId) ?? throw new HubException("請先進入辦公室。");
        await Clients.Group(OfficeGroups.Office(me.CompanyId, office.DepartmentId)).Emoted(me.MembershipId, key);
    }

    /// <summary>收訊對象由後端依位置計算；只送到對方正在顯示辦公室的分頁，不存資料庫。</summary>
    public async Task SendChat(string text)
    {
        var me = Me();
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0) return;
        if (text.Length > OfficeRules.ChatMaxLength) throw new HubException($"訊息最多 {OfficeRules.ChatMaxLength} 字。");
        Limit("chat", 2, TimeSpan.FromSeconds(1));
        var office = tracker.OfficeOf(me.MembershipId) ?? throw new HubException("請先進入辦公室。");
        var recipients = OfficeRules.ChatRecipients(office.Map, me.MembershipId, office.Positions);
        if (recipients.Count == 0) throw new HubException("附近沒有人，走近同事再聊天。");
        var message = new OfficeChatMessage(me.MembershipId, me.Name, text, recipients, clock.GetUtcNow());
        var targets = recipients.Select(tracker.OfficeConnectionOf).OfType<string>().Append(Context.ConnectionId).Distinct().ToList();
        await Clients.Clients(targets).ChatReceived(message);
    }

    /// <summary>敲門送到對方所有分頁；不同公司、不存在與離線回同一句話，避免透露其他公司的成員。</summary>
    public async Task Knock(Guid targetMembershipId, string? message)
    {
        var me = Me();
        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (message?.Length > OfficeRules.KnockMessageMaxLength) throw new HubException($"附言最多 {OfficeRules.KnockMessageMaxLength} 字。");
        if (targetMembershipId == me.MembershipId) throw new HubException("不能敲自己的門。");
        Limit("knock", 5, TimeSpan.FromMinutes(1));
        if (tracker.OnlineMember(targetMembershipId) is not { } target || target.CompanyId != me.CompanyId) throw new HubException("找不到此成員或對方不在線上。");
        var office = tracker.OfficeOf(me.MembershipId) ?? throw new HubException("請先進入辦公室再敲門。");
        if (!gate.TryPass($"knock:{me.MembershipId}:{targetMembershipId}", 1, TimeSpan.FromSeconds(30))) throw new HubException("請稍候 30 秒再敲門。");
        service.UseCompany(me.CompanyId);
        var room = await service.LoadAsync(me.CompanyId, office.DepartmentId, Context.ConnectionAborted);
        tracker.RecordKnock(me.MembershipId, targetMembershipId);
        await Clients.Clients(tracker.ConnectionsOf(targetMembershipId)).Knocked(new OfficeKnock(me.MembershipId, me.Name, office.DepartmentId, room?.Name ?? string.Empty, message));
    }

    /// <summary>接受時把自己放到敲門者旁邊；必須已進入對方所在的辦公室（前端會先 JoinOffice）。</summary>
    public async Task AnswerKnock(Guid fromMembershipId, bool accept, string? message)
    {
        var me = Me();
        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (message?.Length > OfficeRules.KnockMessageMaxLength) throw new HubException($"附言最多 {OfficeRules.KnockMessageMaxLength} 字。");
        Limit("answer", 10, TimeSpan.FromMinutes(1));
        if (!tracker.ConsumeKnock(fromMembershipId, me.MembershipId)) throw new HubException("沒有待回應的敲門，可能已經逾時。");
        if (accept)
        {
            var theirs = tracker.OfficeOf(fromMembershipId) ?? throw new HubException("對方已離開辦公室。");
            if (tracker.OfficeOf(me.MembershipId)?.DepartmentId != theirs.DepartmentId) throw new HubException("請先進入對方所在的辦公室。");
            var placed = tracker.Place(me.MembershipId, theirs.Positions[fromMembershipId], "down") ?? throw new HubException("請先進入對方所在的辦公室。");
            await Clients.Group(OfficeGroups.Office(me.CompanyId, theirs.DepartmentId)).Moved(placed.MembershipId, placed.X, placed.Y, placed.Facing);
        }
        await Clients.Clients(tracker.ConnectionsOf(fromMembershipId)).KnockAnswered(me.MembershipId, me.Name, accept, message);
    }

    private ConnectionIdentity Me() =>
        tracker.Identity(Context.ConnectionId) ?? throw new HubException("連線已失效，請重新整理頁面。");

    /// <summary>超過頻率回傳 false 並累計；同一連線 10 秒內被擋 50 次即斷開。</summary>
    private bool Allow(string action, int limit, TimeSpan window)
    {
        if (gate.TryPass($"{Context.ConnectionId}:{action}", limit, window)) return true;
        CountRejection();
        return false;
    }

    private void Limit(string action, int limit, TimeSpan window)
    {
        if (!Allow(action, limit, window)) throw new HubException("操作太頻繁，請稍候再試。");
    }

    private void CountRejection()
    {
        if (!gate.TryPass($"{Context.ConnectionId}:rejected", 50, TimeSpan.FromSeconds(10))) Context.Abort();
    }
}
```

- [ ] **Step 3: 連線登記與背景巡檢**

`QuoteOptions.cs` 在 `SeedDemoData` 之後加：

```csharp
    /// <summary>虛擬辦公室巡檢 AuthVersion 的間隔秒數（最少 5）；smoke 測試時調短以加快驗證。</summary>
    public int OfficeAuthSweepSeconds { get; init; } = 60;
```

`Modules/Office/OfficeSweeper.cs`：

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>記錄每條 hub 連線的 context，讓背景工作可以主動斷線（IHubContext 做不到）。</summary>
public sealed class OfficeConnections
{
    private readonly ConcurrentDictionary<string, HubCallerContext> _contexts = new(StringComparer.Ordinal);

    public void Register(HubCallerContext context) => _contexts[context.ConnectionId] = context;

    public void Unregister(string connectionId) => _contexts.TryRemove(connectionId, out _);

    public void Abort(string connectionId)
    {
        if (_contexts.TryGetValue(connectionId, out var context)) context.Abort();
    }
}

/// <summary>
/// 每 5 秒處理閒置、離線與離開辦公室（寬限期後）並廣播；每 <see cref="QuoteOptions.OfficeAuthSweepSeconds"/> 秒比對一次
/// AuthVersion、帳號與公司狀態，不一致的連線先送 ForceDisconnect 再斷線。
/// </summary>
/// <remarks>AuthVersion 巡檢把所有連線合成一次查詢；轉離線時依公司分批寫入 office_profiles.LastSeenAt。</remarks>
public sealed class OfficeSweeper(
    PresenceTracker tracker,
    OfficeConnections connections,
    IHubContext<OfficeHub, IOfficeClient> hub,
    IServiceScopeFactory scopes,
    IOptions<QuoteOptions> options,
    ILogger<OfficeSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var authInterval = TimeSpan.FromSeconds(Math.Max(5, options.Value.OfficeAuthSweepSeconds));
        var lastAuthSweep = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepPresenceAsync(stoppingToken);
                if (DateTimeOffset.UtcNow - lastAuthSweep >= authInterval)
                {
                    lastAuthSweep = DateTimeOffset.UtcNow;
                    await SweepAuthAsync(stoppingToken);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "虛擬辦公室巡檢失敗。");
            }
        }
    }

    private async Task SweepPresenceAsync(CancellationToken cancellationToken)
    {
        var events = tracker.Sweep();
        foreach (var change in events)
        {
            switch (change)
            {
                case OccupantLeftEvent left:
                    await hub.Clients.Group(OfficeGroups.Office(left.CompanyId, left.DepartmentId)).OccupantLeft(left.MembershipId);
                    break;
                case PresenceChangedEvent changed:
                    await hub.Clients.Group(OfficeGroups.Company(changed.CompanyId)).PresenceChanged(changed.Entry, changed.LastSeenAt);
                    break;
            }
        }
        foreach (var company in events.OfType<PresenceChangedEvent>().Where(value => value.Entry.Presence == "offline" && value.LastSeenAt is not null).GroupBy(value => value.CompanyId))
        {
            using var scope = scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<OfficeService>();
            service.UseCompany(company.Key);
            await service.WriteLastSeenAsync(company.Select(value => (value.Entry.MembershipId, value.LastSeenAt!.Value)).ToList(), cancellationToken);
        }
    }

    private async Task SweepAuthAsync(CancellationToken cancellationToken)
    {
        var all = tracker.AllConnections();
        if (all.Count == 0) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuoteDbContext>();
        var userIds = all.Values.Select(value => value.UserId).Distinct().ToList();
        var companyIds = all.Values.Select(value => value.CompanyId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(value => userIds.Contains(value.Id))
            .Select(value => new { value.Id, value.Status, value.AuthVersion })
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var activeCompanies = (await db.Companies.AsNoTracking()
            .Where(value => companyIds.Contains(value.Id) && value.Status == "active")
            .Select(value => value.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        foreach (var (connectionId, identity) in all)
        {
            var valid = users.TryGetValue(identity.UserId, out var user)
                && user.Status == "active"
                && user.AuthVersion == identity.AuthVersion
                && activeCompanies.Contains(identity.CompanyId);
            if (valid) continue;
            await hub.Clients.Client(connectionId).ForceDisconnect("登入權限已更新，請重新登入。");
            connections.Abort(connectionId);
        }
    }
}
```

- [ ] **Step 4: Program.cs 接線**

1. 服務註冊（接在 Task 6 加的 `AddScoped<OfficeService>()` 之後）：
   ```csharp
   builder.Services.AddSingleton<OfficeConnections>();
   builder.Services.AddHostedService<OfficeSweeper>();
   builder.Services.AddSignalR(options =>
   {
       // 走動、聊天訊息都很小；限制單則大小避免被灌大封包。
       options.MaximumReceiveMessageSize = 8 * 1024;
       options.KeepAliveInterval = TimeSpan.FromSeconds(15);
       options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
   });
   ```
2. `options.Events = new JwtBearerEvents { … }` 內、`OnTokenValidated` 之前加：
   ```csharp
               // WebSocket 無法帶 Authorization header；只有虛擬辦公室 hub 接受 query 的 access_token。
               OnMessageReceived = context =>
               {
                   if (context.HttpContext.Request.Path.StartsWithSegments(OfficeHub.Path)
                       && context.Request.Query.TryGetValue("access_token", out var token))
                   {
                       context.Token = token;
                   }
                   return Task.CompletedTask;
               },
   ```
3. `app.MapControllers();` 之後加 `app.MapHub<OfficeHub>(OfficeHub.Path);`

- [ ] **Step 5: Nginx 與 Vite 代理**

`infra/nginx/default.conf` 在 `location /api/ {` 之前加（更精確的前綴優先比對）：

```nginx
    # 虛擬辦公室 SignalR：需要 WebSocket upgrade；token 在 query string，關閉 access log 避免寫進日誌。
    location /api/hubs/ {
        proxy_pass http://api:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 120s;
        access_log off;
    }
```

`apps/web/astro.config.mjs` 的 proxy 改為：

```js
        '/api': { target: apiTarget, changeOrigin: false, ws: true },
```

- [ ] **Step 6: 架構文件**

`docs/system-architecture.md`：
1. `## 3. 執行元件` 的 API 說明處補一句「API 另提供 SignalR hub `/api/hubs/office`（虛擬辦公室），上線狀態只存在記憶體，限單一實例；多實例時需加 Redis backplane。」
2. 在 `### 6.7` 小節之後新增：
   ```markdown
   ### 6.8 部門虛擬辦公室

   - 規格：`docs/superpowers/specs/2026-11-03-virtual-office-design.md`。
   - `Modules/Office/`：`OfficeMapGenerator`（依部門成員產生地圖）、`OfficeRules`（移動、聊天範圍、頻率限制）、`PresenceTracker`（記憶體上線狀態）、`OfficeHub`（SignalR）、`OfficeSweeper`（每 5 秒處理閒置／離線，每 `Quote__OfficeAuthSweepSeconds` 秒比對 AuthVersion）。
   - hub 以 query `access_token` 驗證（僅此路徑），連線時要求 `office:read`；公司隔離靠群組 `company:{id}`、`office:{companyId}:{departmentId}`，資料庫存取前以 `CompanyContext.UseCompany` 指定連線身分的公司。
   - 走動、表情、聊天、上線狀態不寫資料庫；`office_rooms` 存主題、`office_profiles` 存角色、手動狀態與最後在線時間。
   - Nginx 的 `/api/hubs/` 需 WebSocket upgrade 並關閉 access log（token 在 query string）。
   ```

- [ ] **Step 7: 建置與快速連線驗證**

Run: `npm run build:api` → Expected: 0 警告
Run: `npm run test:api` → Expected: `self-test passed`
重啟 API，確認 log 沒有例外；完整行為由 Task 8 驗證。

- [ ] **Step 8: Commit**

```bash
git add apps/api/QuotePlatform.Api/Modules/Office apps/api/QuotePlatform.Api/Infrastructure/QuoteOptions.cs apps/api/QuotePlatform.Api/Program.cs infra/nginx/default.conf apps/web/astro.config.mjs docs/system-architecture.md
git commit -m "feat: 新增虛擬辦公室即時通道與權限巡檢

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 端對端 smoke 測試

**Files:**
- Create: `tests/office-smoke.mjs`
- Modify: `package.json`（根目錄 scripts）

**Interfaces:**
- Consumes: Task 6 REST、Task 7 hub 方法與事件名稱、既有 `/api/admin/users`、`/api/admin/roles`、`/api/admin/departments`、`/api/platform/companies`

- [ ] **Step 1: 建立測試檔**

`tests/office-smoke.mjs`：

```js
/**
 * 虛擬辦公室端對端驗證：hub 連線權限、公司隔離、走動、靠近聊天、敲門、表情、狀態、
 * 停用帳號斷線與離線寬限期。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false、Quote__OfficeAuthSweepSeconds=5 啟動，且已執行 Seeder（SeedDemoData=true）。
 * 用法：node tests/office-smoke.mjs [baseUrl]
 * 密碼讀自 .env.development-api.local 的 Quote__BootstrapAdminPassword，不會輸出到畫面。
 * 以 Node 24 內建 WebSocket 與 SignalR JSON 協定直接連線（skipNegotiation），不需要額外套件。
 */
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { setTimeout as sleep } from 'node:timers/promises';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5280';
const wsBase = baseUrl.replace(/^http/, 'ws');
const RS = '\u001e';
const sweepWaitMs = Number(process.env.OFFICE_SWEEP_WAIT_MS ?? 15_000);
const envText = await readFile(new URL('../.env.development-api.local', import.meta.url), 'utf8');
const seedPassword = envText.match(/^Quote__BootstrapAdminPassword=(.+)$/m)?.[1]?.trim();
assert.ok(seedPassword, '找不到 Quote__BootstrapAdminPassword');

const suffix = Date.now().toString(36);
let passed = 0;

async function call(method, path, { token, body } = {}) {
  const headers = { 'content-type': 'application/json' };
  if (token) headers.authorization = `Bearer ${token}`;
  const response = await fetch(`${baseUrl}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  return { status: response.status, data: text ? JSON.parse(text) : null };
}

async function login(account, password = seedPassword) {
  const result = await call('POST', '/api/auth/login', { body: { account, password } });
  assert.equal(result.status, 200, `${account} 登入失敗：${JSON.stringify(result.data)}`);
  return { token: result.data.accessToken };
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

/** 最小的 SignalR JSON 協定客戶端：握手、呼叫、接收伺服器事件。 */
class HubClient {
  constructor(token) {
    this.events = [];
    this.pending = new Map();
    this.nextId = 1;
    this.closed = false;
    this.ws = new WebSocket(`${wsBase}/api/hubs/office?access_token=${encodeURIComponent(token)}`);
    this.ready = new Promise((resolve, reject) => {
      this.resolveReady = resolve;
      this.ws.addEventListener('open', () => this.ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS));
      this.ws.addEventListener('error', () => reject(new Error('WebSocket 連線失敗')));
    });
    this.ws.addEventListener('message', (event) => this.receive(String(event.data)));
    this.ws.addEventListener('close', () => {
      this.closed = true;
      for (const { reject } of this.pending.values()) reject(new Error('連線已關閉'));
      this.pending.clear();
    });
  }

  /** 握手完成後稍等，讓伺服器跑完 OnConnectedAsync（加入群組、登記上線）。 */
  static async open(token) {
    const client = new HubClient(token);
    await client.ready;
    await sleep(200);
    return client;
  }

  receive(data) {
    for (const frame of data.split(RS).filter(Boolean)) {
      const message = JSON.parse(frame);
      if (message.type === undefined) this.resolveReady();
      else if (message.type === 1) this.events.push({ target: message.target, args: message.arguments });
      else if (message.type === 3) {
        const waiter = this.pending.get(message.invocationId);
        this.pending.delete(message.invocationId);
        if (message.error) waiter?.reject(new Error(message.error));
        else waiter?.resolve(message.result);
      } else if (message.type === 7) this.closed = true;
    }
  }

  invoke(target, ...args) {
    const invocationId = String(this.nextId++);
    return new Promise((resolve, reject) => {
      this.pending.set(invocationId, { resolve, reject });
      this.ws.send(JSON.stringify({ type: 1, invocationId, target, arguments: args }) + RS);
    });
  }

  send(target, ...args) {
    this.ws.send(JSON.stringify({ type: 1, target, arguments: args }) + RS);
  }

  async waitFor(target, predicate = () => true, timeout = 3000) {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
      const index = this.events.findIndex((event) => event.target === target && predicate(...event.args));
      if (index >= 0) return this.events.splice(index, 1)[0].args;
      await sleep(25);
    }
    throw new Error(`等待 ${target} 逾時`);
  }

  async expectNone(target, predicate = () => true, wait = 600) {
    await sleep(wait);
    assert.ok(!this.events.some((event) => event.target === target && predicate(...event.args)), `不應收到 ${target}`);
  }

  async waitClosed(timeout) {
    const end = Date.now() + timeout;
    while (!this.closed) {
      if (Date.now() > end) throw new Error('連線沒有被關閉');
      await sleep(100);
    }
  }

  close() {
    this.ws.close();
  }
}

const createdMembers = [];
let demoAdmin;
let sales;
let memberA;
let memberB;
let outsider;
let hubA;
let hubB;
let hubX;
let snapshotA;

await step('準備：業務部兩位新成員與另一家公司', async () => {
  demoAdmin = await login('demo-admin');
  const departments = (await call('GET', '/api/admin/departments', { token: demoAdmin.token })).data;
  sales = departments.find((department) => department.name === '業務部');
  assert.ok(sales, '找不到業務部');
  const roles = (await call('GET', '/api/admin/roles', { token: demoAdmin.token })).data;
  const salesRole = roles.find((role) => role.code === 'sales');
  const createMember = async (prefix, roleId) => {
    const account = `${prefix}-${suffix}`;
    const created = await call('POST', '/api/admin/users', { token: demoAdmin.token, body: { account, displayName: account, password: seedPassword, departmentId: sales.id, roleId } });
    assert.equal(created.status, 201, JSON.stringify(created.data));
    createdMembers.push(created.data.membershipId);
    return { ...(await login(account)), membershipId: created.data.membershipId };
  };
  memberA = await createMember('oa', salesRole.id);
  memberB = await createMember('ob', salesRole.id);

  const admin = await login('admin');
  const company = await call('POST', '/api/platform/companies', {
    token: admin.token,
    body: { name: `辦公室測試 ${suffix}`, slug: `o-${suffix}`, adminAccount: `o-admin-${suffix}`, adminDisplayName: '外部管理員', adminPassword: seedPassword },
  });
  assert.equal(company.status, 201, JSON.stringify(company.data));
  outsider = await login(`o-admin-${suffix}`);
});

await step('同公司成員進入業務部辦公室：地圖含兩人座位，後到的人不疊在同一格', async () => {
  hubA = await HubClient.open(memberA.token);
  hubB = await HubClient.open(memberB.token);
  snapshotA = await hubA.invoke('JoinOffice', sales.id);
  assert.deepEqual([snapshotA.me.x, snapshotA.me.y], [snapshotA.map.spawn.x, snapshotA.map.spawn.y]);
  const seatIds = snapshotA.map.seats.map((seat) => seat.membershipId);
  assert.ok(seatIds.includes(memberA.membershipId) && seatIds.includes(memberB.membershipId));
  await hubB.invoke('JoinOffice', sales.id);
  const [joined] = await hubA.waitFor('OccupantJoined', (occupant) => occupant.membershipId === memberB.membershipId);
  assert.notDeepEqual([joined.x, joined.y], [snapshotA.me.x, snapshotA.me.y]);
  const rooms = (await call('GET', '/api/office/rooms', { token: memberA.token })).data;
  const room = rooms.find((value) => value.departmentId === sales.id);
  assert.ok(room.memberCount >= 2 && room.onlineCount >= 2 && room.occupantCount >= 2, JSON.stringify(room));
});

await step('公司隔離：另一家公司進不了這間辦公室，也看不到兩人的上線狀態', async () => {
  hubX = await HubClient.open(outsider.token);
  await assert.rejects(hubX.invoke('JoinOffice', sales.id), /找不到此部門的辦公室/);
  const outsiderRoot = (await call('GET', '/api/admin/departments', { token: outsider.token })).data[0];
  await hubX.invoke('JoinOffice', outsiderRoot.id);
  const seen = (await call('GET', '/api/office/presence', { token: outsider.token })).data;
  assert.ok(!seen.some((entry) => [memberA.membershipId, memberB.membershipId].includes(entry.membershipId)));
  const mine = (await call('GET', '/api/office/presence', { token: demoAdmin.token })).data;
  assert.ok([memberA.membershipId, memberB.membershipId].every((id) => mine.some((entry) => entry.membershipId === id && entry.presence === 'online')));
});

await step('走動：相鄰格廣播給同辦公室、斜走被拉回、其他公司收不到', async () => {
  const { spawn } = snapshotA.map;
  hubA.send('Move', spawn.x + 1, spawn.y, 'right');
  await hubB.waitFor('Moved', (id, x, y) => id === memberA.membershipId && x === spawn.x + 1 && y === spawn.y);
  hubA.send('Move', spawn.x + 2, spawn.y + 1, 'right');
  await hubA.waitFor('PositionCorrected', (x, y) => x === spawn.x + 1 && y === spawn.y);
  await hubX.expectNone('Moved');
});

await step('靠近即聊：附近的人收到，對方回座位後無人可聊', async () => {
  await hubA.invoke('SendChat', '哈囉');
  const [message] = await hubB.waitFor('ChatReceived', (value) => value.text === '哈囉');
  assert.deepEqual(message.recipients, [memberB.membershipId]);
  await hubX.expectNone('ChatReceived');
  await hubB.invoke('GoToSeat');
  await hubA.waitFor('Moved', (id) => id === memberB.membershipId);
  await assert.rejects(hubA.invoke('SendChat', '還在嗎'), /附近沒有人/);
});

await step('敲門：送達對方、30 秒內不能重複、接受後被放到敲門者旁邊；其他公司敲不到', async () => {
  const { spawn } = snapshotA.map;
  await assert.rejects(hubB.invoke('AnswerKnock', memberA.membershipId, true, null), /沒有待回應的敲門/);
  await hubA.invoke('Knock', memberB.membershipId, '來一下');
  const [knock] = await hubB.waitFor('Knocked');
  assert.equal(knock.fromMembershipId, memberA.membershipId);
  assert.equal(knock.departmentId, sales.id);
  assert.equal(knock.message, '來一下');
  await assert.rejects(hubA.invoke('Knock', memberB.membershipId, null), /30 秒/);
  await hubB.invoke('AnswerKnock', memberA.membershipId, true, null);
  await hubA.waitFor('KnockAnswered', (id, _name, accepted) => id === memberB.membershipId && accepted === true);
  const [, x, y] = await hubA.waitFor('Moved', (id) => id === memberB.membershipId);
  assert.equal(Math.abs(x - (spawn.x + 1)) + Math.abs(y - spawn.y), 1);
  await assert.rejects(hubX.invoke('Knock', memberA.membershipId, null), /找不到此成員或對方不在線上/);
});

await step('表情與狀態：只接受定義的值並廣播給同公司', async () => {
  await assert.rejects(hubA.invoke('Emote', 'skull'), /表情不正確/);
  await hubA.invoke('Emote', 'wave');
  await hubB.waitFor('Emoted', (id, key) => id === memberA.membershipId && key === 'wave');
  await assert.rejects(hubA.invoke('SetStatus', 'sleeping', null), /狀態不正確/);
  await hubA.invoke('SetStatus', 'meeting', '週會');
  await hubB.waitFor('PresenceChanged', (entry) => entry.membershipId === memberA.membershipId && entry.status === 'meeting' && entry.statusText === '週會');
  await hubX.expectNone('PresenceChanged', (entry) => entry.membershipId === memberA.membershipId);
});

await step('沒有 office 權限的角色無法建立連線，也不能讀房間列表', async () => {
  const role = await call('POST', '/api/admin/roles', { token: demoAdmin.token, body: { code: `no-office-${suffix}`, name: '無辦公室', dataScope: 'own', permissions: [] } });
  assert.equal(role.status, 201, JSON.stringify(role.data));
  const account = `oc-${suffix}`;
  const created = await call('POST', '/api/admin/users', { token: demoAdmin.token, body: { account, displayName: account, password: seedPassword, departmentId: sales.id, roleId: role.data.id } });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  createdMembers.push(created.data.membershipId);
  const memberC = await login(account);
  const hubC = new HubClient(memberC.token);
  await hubC.ready.catch(() => undefined);
  await hubC.waitClosed(5000);
  assert.equal((await call('GET', '/api/office/rooms', { token: memberC.token })).status, 403);
});

await step('停用帳號後，連線在巡檢週期內被斷開並離開辦公室', async () => {
  assert.equal((await call('PATCH', `/api/admin/users/${memberB.membershipId}`, { token: demoAdmin.token, body: { isActive: false } })).status, 200);
  await hubB.waitClosed(sweepWaitMs);
  await hubA.waitFor('OccupantLeft', (id) => id === memberB.membershipId, 30_000);
});

await step('離線寬限期：斷線 15 秒內仍在線，之後轉離線', async () => {
  hubA.close();
  await sleep(3000);
  const during = (await call('GET', '/api/office/presence', { token: demoAdmin.token })).data;
  assert.ok(during.some((entry) => entry.membershipId === memberA.membershipId), '寬限期內應仍在線');
  await sleep(20_000);
  const after = (await call('GET', '/api/office/presence', { token: demoAdmin.token })).data;
  assert.ok(!after.some((entry) => entry.membershipId === memberA.membershipId), '超過寬限期應轉離線');
});

hubX.close();
for (const membershipId of createdMembers) {
  await call('PATCH', `/api/admin/users/${membershipId}`, { token: demoAdmin.token, body: { isActive: false } });
}
console.log(`\n全部通過（${passed} 項）`);
```

- [ ] **Step 2: 加入 npm script**

根目錄 `package.json` 的 scripts，在 `"test:smoke:quote-format"` 之後加：

```json
    "test:smoke:office": "node tests/office-smoke.mjs",
```

- [ ] **Step 3: 執行**

以 `Quote__LoginCaptchaEnabled=false Quote__OfficeAuthSweepSeconds=5` 重啟 API（在背景執行），然後：
Run: `npm run test:smoke:office`
Expected: 十個 `✓` 加上 `全部通過（10 項）`；總時間約 1 分鐘。若某步失敗，依錯誤修正對應 Task 的程式（測試是規格，不改測試迎合實作，除非測試本身寫錯 API 欄位名稱——例如 `/api/admin/departments` 回傳欄位與假設不同時，以實際回傳修正測試並在 commit 說明）。

Run: `npm run test:smoke` → Expected: 全部通過（確認既有授權流程不受影響）

- [ ] **Step 4: Commit**

```bash
git add tests/office-smoke.mjs package.json
git commit -m "test: 新增虛擬辦公室端對端 smoke 測試

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: 前端連線層與上線符號

**Files:**
- Modify: `apps/web/package.json`（新增 `@microsoft/signalr`）
- Modify: `apps/web/src/shared/api/http-client.ts`
- Create: `apps/web/src/shared/services/office-service.ts`
- Create: `apps/web/src/app/composables/use-presence.ts`
- Create: `apps/web/src/app/components/PresenceDot.vue`
- Modify: `apps/web/src/app/components/AdminShell.vue`

**Interfaces:**
- Consumes: Task 6 REST、Task 7 hub 方法與事件
- Produces:
  - `http-client.ts`：`ensureFreshAccessToken(): Promise<string | null>`
  - `office-service.ts`：型別 `PresenceState`、`OfficeStatus`、`Facing`、`PresenceEntry`、`OfficeZone`、`OfficeSeat`、`OfficeFurniture`、`OfficeMap`、`OfficeOccupant`、`OfficeSnapshot`、`OfficeChatMessage`、`OfficeKnock`、`OfficeRoom`；常數 `OFFICE_STATUS_LABELS`、`OFFICE_EMOTES`、`OFFICE_AVATARS`、`OFFICE_THEMES`；函式 `listOfficeRoomsApi()`、`getPresenceApi()`、`updateRoomThemeApi(departmentId, theme)`、`officeConnection(): HubConnection`、`hubErrorMessage(error: unknown): string`
  - `use-presence.ts`：`connectionState`（`Ref`）、`startPresence(me: Me)`、`presenceOf(membershipId)`、`lastSeenOf(membershipId)`、`waitForOfficeConnection()`、`onOfficeReconnected(handler): () => void`、`setOfficeKnockHandler(handler | null)`、`answerKnock(from, accept, reply?)`
  - `PresenceDot.vue`：props `membershipId: string`、`lastSeenAt?: string | null`

- [ ] **Step 1: 安裝套件**

Run: `npm --prefix apps/web install --save-exact @microsoft/signalr`
Expected: `apps/web/package.json` 的 dependencies 出現 `"@microsoft/signalr": "x.y.z"`（不帶 `^`）

- [ ] **Step 2: http-client 提供給 WebSocket 用的 token**

`apps/web/src/shared/api/http-client.ts` 在 `isExpiringSoon` 函式之後加：

```ts
/**
 * 給 WebSocket 等不經 apiRequest 的連線使用：快過期就先換發。
 * refresh 被拒（401）時 token 已被清除而回傳 null；網路錯誤時回傳舊 token，讓連線稍後自行重試。
 */
export async function ensureFreshAccessToken(): Promise<string | null> {
  const token = readAccessToken();
  if (token && !isExpiringSoon(token)) return token;
  await refreshAccessToken();
  return readAccessToken();
}
```

- [ ] **Step 3: office-service**

`apps/web/src/shared/services/office-service.ts`：

```ts
/**
 * @file office-service.ts
 * 虛擬辦公室的型別、REST 呼叫與全後台共用的 SignalR 連線。
 *
 * hub 的方法與事件名稱必須與後端 OfficeHub／IOfficeClient 一致；所有驗證都在後端，前端只送意圖。
 */
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { apiRequest, ensureFreshAccessToken, redirectToLogin } from '@/shared/api/http-client';

export type PresenceState = 'online' | 'idle' | 'offline';
export type OfficeStatus = 'available' | 'busy' | 'meeting' | 'break' | 'away';
export type Facing = 'up' | 'down' | 'left' | 'right';

/** 全公司可見的上線狀態 */
export interface PresenceEntry {
  membershipId: string;
  presence: PresenceState;
  status: OfficeStatus;
  statusText: string | null;
  /** 所屬部門 */
  homeDepartmentId: string | null;
  /** 目前所在的辦公室；不在任何辦公室時為 null */
  officeDepartmentId: string | null;
}

export interface OfficeZone {
  id: string;
  type: 'desk' | 'meeting' | 'pantry' | 'entrance';
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface OfficeSeat {
  membershipId: string;
  name: string;
  x: number;
  y: number;
  facing: Facing;
  lastSeenAt: string | null;
}

export interface OfficeFurniture {
  kind: string;
  x: number;
  y: number;
}

/** 後端產生的地圖；grid 每列一個字串：# 牆、. 可走、f 家具 */
export interface OfficeMap {
  width: number;
  height: number;
  tileSize: number;
  theme: string;
  grid: string[];
  furniture: OfficeFurniture[];
  zones: OfficeZone[];
  seats: OfficeSeat[];
  spawn: { x: number; y: number };
}

export interface OfficeOccupant {
  membershipId: string;
  name: string;
  avatarKey: string;
  x: number;
  y: number;
  facing: Facing;
  status: OfficeStatus;
  statusText: string | null;
  homeDepartmentId: string | null;
}

export interface OfficeSnapshot {
  map: OfficeMap;
  departmentName: string;
  me: OfficeOccupant;
  occupants: OfficeOccupant[];
}

export interface OfficeChatMessage {
  fromMembershipId: string;
  fromName: string;
  text: string;
  recipients: string[];
  sentAt: string;
}

export interface OfficeKnock {
  fromMembershipId: string;
  fromName: string;
  departmentId: string;
  departmentName: string;
  message: string | null;
}

export interface OfficeRoom {
  departmentId: string;
  parentId: string | null;
  name: string;
  sortOrder: number;
  memberCount: number;
  onlineCount: number;
  occupantCount: number;
}

export const OFFICE_STATUS_LABELS: Record<OfficeStatus, string> = {
  available: '有空',
  busy: '忙碌',
  meeting: '開會中',
  break: '休息中',
  away: '外出',
};

export const OFFICE_EMOTES = [
  { key: 'wave', icon: '👋' },
  { key: 'coffee', icon: '☕' },
  { key: 'thumbsup', icon: '👍' },
  { key: 'party', icon: '🎉' },
  { key: 'question', icon: '❓' },
] as const;

export const OFFICE_AVATARS = ['a1', 'a2', 'a3', 'a4', 'a5', 'a6', 'a7', 'a8'] as const;

export const OFFICE_THEMES = [
  { value: 'wood', label: '木地板' },
  { value: 'carpet', label: '地毯' },
  { value: 'dark', label: '深色' },
] as const;

export const listOfficeRoomsApi = () => apiRequest<OfficeRoom[]>('/api/office/rooms');

export const getPresenceApi = () => apiRequest<PresenceEntry[]>('/api/office/presence');

export const updateRoomThemeApi = (departmentId: string, theme: string) =>
  apiRequest<void>(`/api/office/rooms/${departmentId}/theme`, { method: 'PUT', body: { theme } });

/** 重連間隔：0、2、5、10 秒，之後每 30 秒一次，不放棄。 */
const RETRY_DELAYS = [0, 2_000, 5_000, 10_000];

let connection: HubConnection | null = null;

/** 全後台共用一條 hub 連線（多頁靜態站，每次換頁重建，由後端的 15 秒寬限期吸收）。 */
export function officeConnection(): HubConnection {
  connection ??= new HubConnectionBuilder()
    .withUrl('/api/hubs/office', {
      accessTokenFactory: async () => {
        const token = await ensureFreshAccessToken();
        if (!token) {
          redirectToLogin();
          throw new Error('登入已逾期，請重新登入。');
        }
        return token;
      },
    })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => RETRY_DELAYS[context.previousRetryCount] ?? 30_000 })
    .configureLogging(LogLevel.Warning)
    .build();
  return connection;
}

/** SignalR 把 HubException 包成「An unexpected error occurred invoking 'X' on the server. HubException: 訊息」，只取訊息。 */
export function hubErrorMessage(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const marker = 'HubException: ';
  const index = text.indexOf(marker);
  return index >= 0 ? text.slice(index + marker.length) : '連線發生問題，請稍後再試。';
}
```

- [ ] **Step 4: use-presence**

`apps/web/src/app/composables/use-presence.ts`：

```ts
/**
 * @file use-presence.ts
 * 全後台共用的上線狀態：AdminShell 登入後呼叫 startPresence 建立唯一一條 hub 連線，
 * 任何元件以 presenceOf(membershipId) 讀取狀態；敲門通知也在這裡顯示，所以在後台任何頁面都收得到。
 * 前端只負責顯示，是否在線、能否進入辦公室都由後端判斷。
 */
import { h, reactive, ref } from 'vue';
import { Button, notification, Space } from 'ant-design-vue';
import { HubConnectionState } from '@microsoft/signalr';
import { readAccessToken, redirectToLogin, refreshAccessToken, storeAccessToken } from '@/shared/api/http-client';
import { getPresenceApi, officeConnection, type OfficeKnock, type PresenceEntry } from '@/shared/services/office-service';
import type { Me } from '@/shared/types/auth';
import { hasPermission } from '@/shared/types/permissions';

const ACTIVITY_INTERVAL_MS = 60_000;
const RESTART_DELAY_MS = 30_000;

const presence = reactive(new Map<string, PresenceEntry>());
const lastSeen = reactive(new Map<string, string>());
const reconnectedHandlers = new Set<() => void>();
const connectedWaiters: (() => void)[] = [];
let knockHandler: ((knock: OfficeKnock) => void) | null = null;
let started = false;
let forced = false;
let everConnected = false;

export const connectionState = ref<'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected'>('idle');

export function presenceOf(membershipId: string): PresenceEntry | undefined {
  return presence.get(membershipId);
}

export function lastSeenOf(membershipId: string): string | undefined {
  return lastSeen.get(membershipId);
}

/** 連線建立後才 resolve；辦公室頁在 JoinOffice 前等待。 */
export function waitForOfficeConnection(): Promise<void> {
  if (officeConnection().state === HubConnectionState.Connected) return Promise.resolve();
  return new Promise((resolve) => connectedWaiters.push(resolve));
}

/** 斷線重連成功後要做的事（辦公室頁用來重新 JoinOffice）；回傳取消註冊函式。 */
export function onOfficeReconnected(handler: () => void): () => void {
  reconnectedHandlers.add(handler);
  return () => reconnectedHandlers.delete(handler);
}

/** 辦公室頁開著時由它處理「前往」，不必整頁跳轉。 */
export function setOfficeKnockHandler(handler: ((knock: OfficeKnock) => void) | null): void {
  knockHandler = handler;
}

export function answerKnock(from: string, accept: boolean, reply?: string): Promise<void> {
  return officeConnection().invoke('AnswerKnock', from, accept, reply ?? null);
}

/** AdminShell 取得 me 後呼叫；平台範圍、代入公司或沒有 office 權限時不連線。 */
export async function startPresence(me: Me): Promise<void> {
  if (started || me.scope.kind !== 'company' || me.scope.isImpersonating || !hasPermission(me.grants, 'office')) return;
  started = true;
  const hub = officeConnection();
  hub.on('PresenceChanged', applyPresence);
  hub.on('Knocked', showKnock);
  hub.on('ForceDisconnect', () => {
    forced = true;
    void hub.stop();
    storeAccessToken(null);
    redirectToLogin();
  });
  hub.onreconnecting(() => (connectionState.value = 'reconnecting'));
  hub.onreconnected(async () => {
    markConnected();
    await loadSnapshot();
    reconnectedHandlers.forEach((handler) => handler());
  });
  hub.onclose(() => {
    connectionState.value = 'disconnected';
    if (!forced) setTimeout(() => void connect(), RESTART_DELAY_MS);
  });
  watchActivity();
  await connect();
}

async function connect(): Promise<void> {
  const hub = officeConnection();
  if (hub.state !== HubConnectionState.Disconnected) return;
  connectionState.value = 'connecting';
  try {
    await hub.start();
    markConnected();
    await loadSnapshot();
    if (everConnected) reconnectedHandlers.forEach((handler) => handler());
    everConnected = true;
  } catch {
    connectionState.value = 'disconnected';
    // refresh 被拒時 token 會被清除，代表真的登出了；網路錯誤則保留 token 稍後重試。
    if (!(await refreshAccessToken()) && !readAccessToken()) {
      redirectToLogin();
      return;
    }
    setTimeout(() => void connect(), RESTART_DELAY_MS);
  }
}

function markConnected(): void {
  connectionState.value = 'connected';
  connectedWaiters.splice(0).forEach((resolve) => resolve());
}

async function loadSnapshot(): Promise<void> {
  try {
    const entries = await getPresenceApi();
    presence.clear();
    entries.forEach((entry) => presence.set(entry.membershipId, entry));
  } catch {
    // 快照失敗時保留目前狀態，後續事件仍會更新。
  }
}

function applyPresence(entry: PresenceEntry, seenAt: string | null): void {
  if (entry.presence === 'offline') {
    presence.delete(entry.membershipId);
    if (seenAt) lastSeen.set(entry.membershipId, seenAt);
    return;
  }
  presence.set(entry.membershipId, entry);
}

/** 有操作時最多每 60 秒回報一次；後端 5 分鐘沒收到就轉閒置。 */
function watchActivity(): void {
  let lastReport = 0;
  const report = () => {
    const hub = officeConnection();
    if (Date.now() - lastReport < ACTIVITY_INTERVAL_MS || hub.state !== HubConnectionState.Connected) return;
    lastReport = Date.now();
    void hub.send('Activity');
  };
  for (const type of ['pointerdown', 'pointermove', 'keydown', 'wheel']) window.addEventListener(type, report, { passive: true });
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') report();
  });
}

function showKnock(knock: OfficeKnock): void {
  const key = `office-knock-${knock.fromMembershipId}`;
  notification.open({
    key,
    duration: 0,
    placement: 'bottomRight',
    message: `${knock.fromName} 在敲你的門`,
    description: `在${knock.departmentName}辦公室${knock.message ? `：「${knock.message}」` : ''}`,
    btn: () =>
      h(Space, null, () => [
        h(Button, { size: 'small', onClick: () => { notification.close(key); void answerKnock(knock.fromMembershipId, false).catch(() => undefined); } }, () => '稍後'),
        h(Button, { size: 'small', type: 'primary', onClick: () => { notification.close(key); goToKnock(knock); } }, () => '前往'),
      ]),
  });
}

function goToKnock(knock: OfficeKnock): void {
  if (knockHandler) knockHandler(knock);
  else window.location.assign(`/office?dept=${encodeURIComponent(knock.departmentId)}&answer=${encodeURIComponent(knock.fromMembershipId)}`);
}
```

- [ ] **Step 5: PresenceDot**

`apps/web/src/app/components/PresenceDot.vue`：

```vue
<script setup lang="ts">
/**
 * @component PresenceDot
 * 成員上線狀態圓點：綠＝上線、黃＝閒置／休息／外出、紅＝忙碌／開會、灰＝離線（附最後在線時間）。
 * 任何列出成員的地方放上這個元件即可顯示上線符號；資料來自 use-presence 的全域連線。
 */
import { computed } from 'vue';
import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';
import { lastSeenOf, presenceOf } from '@/app/composables/use-presence';
import { OFFICE_STATUS_LABELS } from '@/shared/services/office-service';

dayjs.extend(relativeTime);

const props = defineProps<{ membershipId: string; lastSeenAt?: string | null }>();

const entry = computed(() => presenceOf(props.membershipId));
const tone = computed(() => {
  const value = entry.value;
  if (!value || value.presence === 'offline') return 'offline';
  if (value.status === 'busy' || value.status === 'meeting') return 'busy';
  if (value.presence === 'idle' || value.status === 'break' || value.status === 'away') return 'idle';
  return 'online';
});
const title = computed(() => {
  const value = entry.value;
  if (!value || value.presence === 'offline') {
    const seen = lastSeenOf(props.membershipId) ?? props.lastSeenAt;
    return seen ? `離線 · ${dayjs(seen).fromNow()}在線` : '離線';
  }
  const base = value.presence === 'idle' ? '閒置' : '上線';
  const status = value.status === 'available' ? '' : ` · ${OFFICE_STATUS_LABELS[value.status]}`;
  return `${base}${status}${value.statusText ? `：${value.statusText}` : ''}`;
});
</script>

<template>
  <a-tooltip :title="title">
    <span class="presence-dot" :class="`presence-dot--${tone}`" role="img" :aria-label="title" />
  </a-tooltip>
</template>

<style scoped>
.presence-dot {
  display: inline-block;
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: 50%;
}
.presence-dot--online { background: #52c41a; }
.presence-dot--idle { background: #faad14; }
.presence-dot--busy { background: #ff4d4f; }
.presence-dot--offline { background: #bfbfbf; }
</style>
```

- [ ] **Step 6: AdminShell 建立全域連線**

`AdminShell.vue`：
1. import 區加 `import { startPresence } from '@/app/composables/use-presence';`
2. `onMounted` 最後一行（`forbidden.value = …` 之後）加：
   ```ts
     // 上線狀態與敲門通知在後台任何頁面都要有；權限不足時 startPresence 自己會略過。
     void startPresence(meResult);
   ```

- [ ] **Step 7: 型別檢查與建置**

Run: `npm run check:web` → Expected: 0 errors
Run: `npm run build:web` → Expected: 建置成功

- [ ] **Step 8: 瀏覽器驗證**

啟動 API 與 `npm run dev:web`，用內建瀏覽器開 `http://127.0.0.1:4341/login`，以 `demo-sales` 登入（密碼取自 `.env.development-api.local` 的 `Quote__BootstrapAdminPassword`，不要在對話中顯示）。在任一頁面用 `read_network_requests` 確認 `/api/hubs/office` 以 WebSocket 連線成功（101），並以 `javascript_tool` 執行 `await fetch('/api/office/presence', { headers: { authorization: 'Bearer ' + localStorage.getItem('qp_access_token') } }).then(r => r.json())`，Expected 陣列中有自己的 membershipId 且 presence 為 `online`。

- [ ] **Step 9: Commit**

```bash
git add apps/web/package.json apps/web/package-lock.json apps/web/src/shared/api/http-client.ts apps/web/src/shared/services/office-service.ts apps/web/src/app/composables/use-presence.ts apps/web/src/app/components/PresenceDot.vue apps/web/src/app/components/AdminShell.vue
git commit -m "feat: 後台建立全域上線連線並新增上線符號元件

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

（若 `apps/web/package-lock.json` 不存在，改加入實際更新的 lock 檔。）

---

### Task 10: 辦公室頁面（Phaser 地圖、成員與聊天）

**Files:**
- Modify: `apps/web/package.json`（新增 `phaser`）
- Create: `apps/web/src/pages/office/_components/office-grid.ts`
- Create: `apps/web/src/pages/office/_components/office-textures.ts`
- Create: `apps/web/src/pages/office/_components/office-scene.ts`
- Create: `apps/web/src/pages/office/_components/OfficeSidePanel.vue`
- Create: `apps/web/src/pages/office/_components/OfficeView.vue`
- Create: `apps/web/src/pages/office/index.astro`
- Modify: `apps/web/src/app/route-manifest.ts`
- Modify: `apps/web/src/app/components/AdminApp.vue`

**Interfaces:**
- Consumes: Task 9 全部前端 API
- Produces: 路由 `/office`（RouteKey `'office'`，requirement `{ code: 'office' }`，scope `company`）；`office-grid.ts`：`Point`、`isWalkable`、`zoneAt`、`neighbors`、`findPath`、`facingTo`、`chatRecipients`、`zoneCenter`；`office-textures.ts`：`TILE`、`AVATAR_COLORS`、`ensureTextures(scene, theme)`；`office-scene.ts`：`OfficeScene`（`loadOffice`、`upsertOccupant`、`removeOccupant`、`moveOccupant`、`setStatus`、`setAvatar`、`showEmote`、`showBubble`、`walkTo`、`wake`）與 `createOfficeGame(parent, handlers)`

- [ ] **Step 1: 安裝 Phaser 3**

Run: `npm --prefix apps/web install --save-exact phaser@3`
Expected: `apps/web/package.json` 出現 `"phaser": "3.x.y"`（不帶 `^`）

- [ ] **Step 2: 路由與頁面骨架**

`route-manifest.ts`：`RouteKey` 加 `| 'office'`；`routeManifest` 在 `dashboard` 之後加：

```ts
  { key: 'office', path: '/office', title: '虛擬辦公室', scope: 'company', requirement: { code: 'office' } },
```

`AdminApp.vue` 的 `views` 加：

```ts
  office: () => import('@/pages/office/_components/OfficeView.vue'),
```

`apps/web/src/pages/office/index.astro`：

```astro
---
import BaseLayout from '@/app/layouts/BaseLayout.astro';
import AdminApp from '@/app/components/AdminApp.vue';
---

<BaseLayout title="虛擬辦公室">
  <AdminApp routeKey="office" client:only="vue" />
</BaseLayout>
```

- [ ] **Step 3: office-grid（純函式）**

`apps/web/src/pages/office/_components/office-grid.ts`：

```ts
/**
 * @file office-grid.ts
 * 地圖格子運算：可否通行、尋路、朝向，以及「訊息會傳給誰」的提示。
 * chatRecipients 與後端 OfficeRules.ChatRecipients 規則相同，只用於提示；實際收訊對象由後端決定。
 */
import type { Facing, OfficeMap, OfficeZone } from '@/shared/services/office-service';

export interface Point {
  x: number;
  y: number;
}

export const CHAT_RANGE = 2;

export function isWalkable(map: OfficeMap, x: number, y: number): boolean {
  return x >= 0 && y >= 0 && x < map.width && y < map.height && map.grid[y][x] === '.';
}

export function zoneAt(map: OfficeMap, x: number, y: number): OfficeZone | undefined {
  return map.zones.find((zone) => x >= zone.x && x < zone.x + zone.w && y >= zone.y && y < zone.y + zone.h);
}

export function neighbors(point: Point): Point[] {
  return [
    { x: point.x, y: point.y - 1 },
    { x: point.x + 1, y: point.y },
    { x: point.x, y: point.y + 1 },
    { x: point.x - 1, y: point.y },
  ];
}

/** 四方向 BFS；目標不可走（家具）時走到它旁邊。回傳不含起點的每一步，走不到時回傳空陣列。 */
export function findPath(map: OfficeMap, from: Point, to: Point): Point[] {
  const targetWalkable = isWalkable(map, to.x, to.y);
  const reached = (point: Point) => (targetWalkable ? point.x === to.x && point.y === to.y : Math.abs(point.x - to.x) + Math.abs(point.y - to.y) === 1);
  if (reached(from)) return [];
  const key = (point: Point) => point.y * map.width + point.x;
  const previous = new Map<number, Point | null>([[key(from), null]]);
  const queue: Point[] = [from];
  while (queue.length > 0) {
    const current = queue.shift()!;
    for (const next of neighbors(current)) {
      if (!isWalkable(map, next.x, next.y) || previous.has(key(next))) continue;
      previous.set(key(next), current);
      if (reached(next)) {
        const steps: Point[] = [];
        for (let point: Point | null = next; point && key(point) !== key(from); point = previous.get(key(point)) ?? null) steps.unshift(point);
        return steps;
      }
      queue.push(next);
    }
  }
  return [];
}

export function facingTo(from: Point, to: Point): Facing {
  if (to.x > from.x) return 'right';
  if (to.x < from.x) return 'left';
  return to.y < from.y ? 'up' : 'down';
}

function roomAt(map: OfficeMap, point: Point): OfficeZone | undefined {
  const zone = zoneAt(map, point.x, point.y);
  return zone && (zone.type === 'meeting' || zone.type === 'pantry') ? zone : undefined;
}

export function chatRecipients(map: OfficeMap, sender: string, positions: Map<string, Point>): string[] {
  const origin = positions.get(sender);
  if (!origin) return [];
  const room = roomAt(map, origin);
  return [...positions.entries()]
    .filter(([id, point]) => {
      if (id === sender) return false;
      if (room) return point.x >= room.x && point.x < room.x + room.w && point.y >= room.y && point.y < room.y + room.h;
      return !roomAt(map, point) && Math.max(Math.abs(point.x - origin.x), Math.abs(point.y - origin.y)) <= CHAT_RANGE;
    })
    .map(([id]) => id);
}

/** 區塊內最靠近中心的可走格子（狀態自動走位用）。 */
export function zoneCenter(map: OfficeMap, zone: OfficeZone): Point {
  const center = { x: zone.x + zone.w / 2, y: zone.y + zone.h / 2 };
  let best: Point = { x: zone.x, y: zone.y };
  let bestDistance = Number.POSITIVE_INFINITY;
  for (let y = zone.y; y < zone.y + zone.h; y++) {
    for (let x = zone.x; x < zone.x + zone.w; x++) {
      const distance = Math.abs(x + 0.5 - center.x) + Math.abs(y + 0.5 - center.y);
      if (isWalkable(map, x, y) && distance < bestDistance) {
        best = { x, y };
        bestDistance = distance;
      }
    }
  }
  return best;
}
```

- [ ] **Step 4: office-textures（程式繪製的備援素材）**

`apps/web/src/pages/office/_components/office-textures.ts`：

```ts
/**
 * @file office-textures.ts
 * 以程式繪製 16×16 圖塊與角色；Kenney 素材（Task 11）載入失敗或尚未加入時使用。
 * 貼圖鍵名：floor-{theme}-{zone}、wall-{theme}、furniture-{kind}、avatar-{key}-{facing}。
 */
import type Phaser from 'phaser';

export const TILE = 16;

const PALETTES = {
  wood: { desk: 0xc8a27a, meeting: 0xd9c7a3, pantry: 0xe8dcc8, entrance: 0xb9a48a, wall: 0x5b4636 },
  carpet: { desk: 0x7f93a8, meeting: 0x96a6b8, pantry: 0xd4d8dc, entrance: 0x6f7f90, wall: 0x37474f },
  dark: { desk: 0x3a4048, meeting: 0x454c55, pantry: 0x525a63, entrance: 0x30353c, wall: 0x1c1f24 },
} as const;

export const AVATAR_COLORS: Record<string, number> = {
  a1: 0xe57373, a2: 0x64b5f6, a3: 0x81c784, a4: 0xffb74d,
  a5: 0xba68c8, a6: 0x4db6ac, a7: 0xf06292, a8: 0x90a4ae,
};

const FURNITURE: Record<string, (g: Phaser.GameObjects.Graphics) => void> = {
  desk: (g) => { g.fillStyle(0x8d6e63); g.fillRect(0, 1, 16, 14); g.fillStyle(0x263238); g.fillRect(4, 3, 8, 6); g.fillStyle(0x4fc3f7); g.fillRect(5, 4, 6, 4); },
  chair: (g) => { g.fillStyle(0x546e7a); g.fillRoundedRect(4, 4, 8, 8, 2); },
  'meeting-table': (g) => { g.fillStyle(0x6d4c41); g.fillRect(0, 1, 16, 14); },
  coffee: (g) => { g.fillStyle(0x424242); g.fillRect(3, 2, 10, 12); g.fillStyle(0xd32f2f); g.fillRect(5, 4, 6, 3); g.fillStyle(0xffffff); g.fillRect(6, 10, 4, 3); },
  fridge: (g) => { g.fillStyle(0xeceff1); g.fillRect(3, 0, 10, 16); g.fillStyle(0xb0bec5); g.fillRect(3, 6, 10, 1); g.fillRect(11, 2, 1, 3); },
  counter: (g) => { g.fillStyle(0x90a4ae); g.fillRect(0, 2, 16, 13); g.fillStyle(0xcfd8dc); g.fillRect(0, 2, 16, 3); },
  table: (g) => { g.fillStyle(0xa1887f); g.fillRect(1, 2, 14, 12); },
  plant: (g) => { g.fillStyle(0x8d6e63); g.fillRect(5, 10, 6, 5); g.fillStyle(0x43a047); g.fillCircle(8, 7, 4); g.fillCircle(5, 6, 3); g.fillCircle(11, 6, 3); },
  whiteboard: (g) => { g.fillStyle(0xffffff); g.fillRect(1, 4, 14, 9); g.lineStyle(1, 0x9e9e9e); g.strokeRect(1.5, 4.5, 13, 8); g.fillStyle(0x1e88e5); g.fillRect(3, 7, 6, 1); },
  rug: (g) => { g.fillStyle(0x8d3b3b, 0.6); g.fillRect(1, 3, 14, 10); },
};

function paint(scene: Phaser.Scene, key: string, draw: (g: Phaser.GameObjects.Graphics) => void): void {
  if (scene.textures.exists(key)) return;
  const graphics = scene.make.graphics({}, false);
  draw(graphics);
  graphics.generateTexture(key, TILE, TILE);
  graphics.destroy();
}

function drawAvatar(g: Phaser.GameObjects.Graphics, color: number, facing: string): void {
  g.fillStyle(0x000000, 0.2);
  g.fillEllipse(8, 14.5, 10, 3);
  g.fillStyle(color);
  g.fillRect(4, 8, 8, 6);
  g.fillStyle(0xf1c27d);
  g.fillCircle(8, 5.5, 4);
  g.fillStyle(0x4e342e);
  if (facing === 'up') {
    g.fillCircle(8, 5, 4);
    return;
  }
  g.fillRect(4, 1.5, 8, 2);
  g.fillStyle(0x212121);
  if (facing === 'down') {
    g.fillRect(6, 5, 1, 2);
    g.fillRect(9, 5, 1, 2);
  } else {
    g.fillRect(facing === 'left' ? 5 : 10, 5, 1, 2);
  }
}

export function ensureTextures(scene: Phaser.Scene, theme: string): void {
  const palette = PALETTES[(theme in PALETTES ? theme : 'wood') as keyof typeof PALETTES];
  for (const zone of ['desk', 'meeting', 'pantry', 'entrance'] as const) {
    paint(scene, `floor-${theme}-${zone}`, (g) => {
      g.fillStyle(palette[zone]);
      g.fillRect(0, 0, TILE, TILE);
      g.lineStyle(1, 0x000000, 0.08);
      g.strokeRect(0.5, 0.5, TILE - 1, TILE - 1);
    });
  }
  paint(scene, `wall-${theme}`, (g) => {
    g.fillStyle(palette.wall);
    g.fillRect(0, 0, TILE, TILE);
    g.fillStyle(0xffffff, 0.08);
    g.fillRect(0, 0, TILE, 3);
  });
  for (const [kind, draw] of Object.entries(FURNITURE)) paint(scene, `furniture-${kind}`, draw);
  for (const [key, color] of Object.entries(AVATAR_COLORS)) {
    for (const facing of ['up', 'down', 'left', 'right']) paint(scene, `avatar-${key}-${facing}`, (g) => drawAvatar(g, color, facing));
  }
}
```

- [ ] **Step 5: office-scene**

`apps/web/src/pages/office/_components/office-scene.ts`：

```ts
/**
 * @file office-scene.ts
 * 虛擬辦公室的 Phaser 場景：只負責畫地圖、角色、補間、泡泡與鍵盤／點擊走動。
 * 不直接連 hub；自己的每一步透過 handlers.step 交給外層送出，其他人的變化由外層呼叫公開方法更新。
 * 效能：上限 30 fps；分頁在背景時 Phaser 自動暫停；一分鐘沒有任何變化就讓迴圈休眠，有事件或操作再喚醒。
 */
import Phaser from 'phaser';
import type { Facing, OfficeMap, OfficeOccupant, OfficeSnapshot } from '@/shared/services/office-service';
import { ensureTextures, TILE } from './office-textures';
import { facingTo, findPath, isWalkable, zoneAt, type Point } from './office-grid';

const STEP_MS = 140;
const TWEEN_MS = 150;
const SLEEP_AFTER_MS = 60_000;
const LABEL_STYLE: Phaser.Types.GameObjects.Text.TextStyle = { fontFamily: 'sans-serif', fontSize: '6px', color: '#ffffff', stroke: '#000000', strokeThickness: 2, resolution: 4 };
const SEAT_STYLE: Phaser.Types.GameObjects.Text.TextStyle = { fontFamily: 'sans-serif', fontSize: '5px', color: '#f5f5f5', resolution: 4 };
const BUBBLE_STYLE: Phaser.Types.GameObjects.Text.TextStyle = { fontFamily: 'sans-serif', fontSize: '6px', color: '#1f1f1f', resolution: 4, wordWrap: { width: 90, useAdvancedWrap: true } };
const ICON_STYLE: Phaser.Types.GameObjects.Text.TextStyle = { fontFamily: 'sans-serif', fontSize: '8px', resolution: 4 };
const STATUS_ICONS: Record<string, string> = { available: '', busy: '⛔', meeting: '📅', break: '☕', away: '🚶' };
const DIRECTIONS: Record<Facing, Point> = { up: { x: 0, y: -1 }, down: { x: 0, y: 1 }, left: { x: -1, y: 0 }, right: { x: 1, y: 0 } };

export interface OfficeSceneHandlers {
  /** 自己的角色走了一步或原地轉向，由外層送給 hub 的 Move。 */
  step(x: number, y: number, facing: Facing): void;
}

interface AvatarView {
  occupant: OfficeOccupant;
  container: Phaser.GameObjects.Container;
  sprite: Phaser.GameObjects.Image;
  status: Phaser.GameObjects.Text;
  bubble?: Phaser.GameObjects.Container;
}

const toPixel = (point: Point) => ({ x: (point.x + 0.5) * TILE, y: (point.y + 1) * TILE });
const textureOf = (occupant: Pick<OfficeOccupant, 'avatarKey' | 'facing'>) => `avatar-${occupant.avatarKey}-${occupant.facing}`;

function isTyping(): boolean {
  const element = document.activeElement;
  return element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || (element instanceof HTMLElement && element.isContentEditable);
}

export class OfficeScene extends Phaser.Scene {
  private office: OfficeMap | null = null;
  private meId = '';
  private readonly avatars = new Map<string, AvatarView>();
  private readonly seatLabels = new Map<string, Phaser.GameObjects.Text>();
  private mapLayer: Phaser.GameObjects.Container | null = null;
  private route: Point[] = [];
  private nextStepAt = 0;
  private lastActiveAt = performance.now();
  private sleeping = false;
  private keys: Record<string, Phaser.Input.Keyboard.Key> = {};

  constructor(private readonly handlers: OfficeSceneHandlers, private readonly onReady: (scene: OfficeScene) => void) {
    super('office');
  }

  create(): void {
    // 不攔截按鍵（enableCapture=false），聊天輸入框才能正常打字；打字時 update 會忽略方向鍵。
    this.keys = this.input.keyboard!.addKeys({ up: 'UP', down: 'DOWN', left: 'LEFT', right: 'RIGHT', w: 'W', s: 'S', a: 'A', d: 'D' }, false) as Record<string, Phaser.Input.Keyboard.Key>;
    this.input.on('pointerdown', (pointer: Phaser.Input.Pointer) => this.walkTo({ x: Math.floor(pointer.worldX / TILE), y: Math.floor(pointer.worldY / TILE) }));
    this.scale.on('resize', () => this.fitCamera());
    this.onReady(this);
  }

  loadOffice(snapshot: OfficeSnapshot, meId: string): void {
    this.wake();
    this.mapLayer?.destroy();
    this.avatars.forEach((view) => view.container.destroy());
    this.avatars.clear();
    this.seatLabels.clear();
    this.route = [];
    this.office = snapshot.map;
    this.meId = meId;
    const { map } = snapshot;
    ensureTextures(this, map.theme);
    const layer = this.add.container(0, 0);
    for (let y = 0; y < map.height; y++) {
      for (let x = 0; x < map.width; x++) {
        const key = map.grid[y][x] === '#' ? `wall-${map.theme}` : `floor-${map.theme}-${zoneAt(map, x, y)?.type ?? 'desk'}`;
        layer.add(this.add.image(x * TILE, y * TILE, key).setOrigin(0));
      }
    }
    for (const item of map.furniture) layer.add(this.add.image(item.x * TILE, item.y * TILE, `furniture-${item.kind}`).setOrigin(0));
    // 座位主人不在場時，空椅子上淡淡顯示名字。
    for (const seat of map.seats) {
      const label = this.add.text((seat.x + 0.5) * TILE, (seat.y + 0.5) * TILE, seat.name, SEAT_STYLE).setOrigin(0.5).setAlpha(0.6);
      layer.add(label);
      this.seatLabels.set(seat.membershipId, label);
    }
    this.mapLayer = layer;
    snapshot.occupants.forEach((occupant) => this.upsertOccupant(occupant));
    this.fitCamera();
    const me = this.avatars.get(meId);
    if (me) this.cameras.main.startFollow(me.container, true, 0.2, 0.2);
  }

  upsertOccupant(occupant: OfficeOccupant): void {
    this.wake();
    const { x, y } = toPixel(occupant);
    const existing = this.avatars.get(occupant.membershipId);
    if (existing) {
      existing.occupant = occupant;
      existing.sprite.setTexture(textureOf(occupant));
      existing.status.setText(STATUS_ICONS[occupant.status] ?? '');
      existing.container.setPosition(x, y).setDepth(occupant.y + 10);
      return;
    }
    const sprite = this.add.image(0, 0, textureOf(occupant)).setOrigin(0.5, 1);
    const name = this.add.text(0, -TILE, occupant.name, LABEL_STYLE).setOrigin(0.5, 1);
    const status = this.add.text(TILE * 0.55, -TILE * 0.9, STATUS_ICONS[occupant.status] ?? '', ICON_STYLE).setOrigin(0.5, 1);
    const container = this.add.container(x, y, [sprite, name, status]).setDepth(occupant.y + 10);
    this.avatars.set(occupant.membershipId, { occupant, container, sprite, status });
    this.seatLabels.get(occupant.membershipId)?.setVisible(false);
  }

  removeOccupant(membershipId: string): void {
    this.avatars.get(membershipId)?.container.destroy();
    this.avatars.delete(membershipId);
    this.seatLabels.get(membershipId)?.setVisible(true);
    this.wake();
  }

  /** 移動角色；animate=false 用於自己被後端拉回或瞬移（回座位、接受敲門）。 */
  moveOccupant(membershipId: string, x: number, y: number, facing: Facing, animate = true): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    this.wake();
    if (membershipId === this.meId && !animate) this.route = [];
    view.occupant = { ...view.occupant, x, y, facing };
    view.sprite.setTexture(textureOf(view.occupant));
    view.container.setDepth(y + 10);
    const target = toPixel({ x, y });
    this.tweens.killTweensOf(view.container);
    if (animate) this.tweens.add({ targets: view.container, x: target.x, y: target.y, duration: TWEEN_MS });
    else view.container.setPosition(target.x, target.y);
  }

  setStatus(membershipId: string, status: OfficeOccupant['status']): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    view.occupant = { ...view.occupant, status };
    view.status.setText(STATUS_ICONS[status] ?? '');
    this.wake();
  }

  setAvatar(membershipId: string, avatarKey: string): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    view.occupant = { ...view.occupant, avatarKey };
    view.sprite.setTexture(textureOf(view.occupant));
    this.wake();
  }

  showEmote(membershipId: string, icon: string): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    this.wake();
    const emote = this.add.text(0, -TILE * 1.8, icon, { ...ICON_STYLE, fontSize: '10px' }).setOrigin(0.5, 1);
    view.container.add(emote);
    this.tweens.add({ targets: emote, y: emote.y - 6, alpha: 0, delay: 2400, duration: 600, onComplete: () => emote.destroy() });
  }

  showBubble(membershipId: string, text: string): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    this.wake();
    view.bubble?.destroy();
    const content = this.add.text(0, 0, text.length > 60 ? `${text.slice(0, 60)}…` : text, BUBBLE_STYLE).setOrigin(0.5, 1);
    const padding = 3;
    const background = this.add.rectangle(0, padding, content.width + padding * 2, content.height + padding * 2, 0xffffff, 0.95).setOrigin(0.5, 1).setStrokeStyle(0.5, 0x8c8c8c);
    const bubble = this.add.container(0, -TILE * 1.9, [background, content]);
    view.container.add(bubble);
    view.bubble = bubble;
    this.time.delayedCall(5000, () => {
      if (view.bubble === bubble) view.bubble = undefined;
      bubble.destroy();
    });
  }

  walkTo(target: Point): void {
    const me = this.avatars.get(this.meId);
    if (!this.office || !me) return;
    this.wake();
    this.route = findPath(this.office, me.occupant, target);
  }

  wake(): void {
    this.lastActiveAt = performance.now();
    if (!this.sleeping) return;
    this.sleeping = false;
    this.game.loop.wake(true);
  }

  update(time: number): void {
    const me = this.avatars.get(this.meId);
    if (!this.office || !me) return;
    const direction = isTyping() ? null : this.pressedDirection();
    if (direction) {
      this.route = [];
      this.lastActiveAt = performance.now();
    }
    if (time >= this.nextStepAt) {
      const from = me.occupant;
      const next = direction ? { x: from.x + DIRECTIONS[direction].x, y: from.y + DIRECTIONS[direction].y } : this.route.shift();
      if (next) {
        const facing = direction ?? facingTo(from, next);
        const target = isWalkable(this.office, next.x, next.y) ? next : { x: from.x, y: from.y };
        if (target.x !== from.x || target.y !== from.y || facing !== from.facing) {
          this.moveOccupant(this.meId, target.x, target.y, facing);
          this.handlers.step(target.x, target.y, facing);
        }
        this.nextStepAt = time + STEP_MS;
        this.lastActiveAt = performance.now();
      }
    }
    if (performance.now() - this.lastActiveAt > SLEEP_AFTER_MS && this.tweens.getTweens().length === 0) {
      this.sleeping = true;
      this.game.loop.sleep();
    }
  }

  private pressedDirection(): Facing | null {
    const keys = this.keys;
    if (keys.up?.isDown || keys.w?.isDown) return 'up';
    if (keys.down?.isDown || keys.s?.isDown) return 'down';
    if (keys.left?.isDown || keys.a?.isDown) return 'left';
    if (keys.right?.isDown || keys.d?.isDown) return 'right';
    return null;
  }

  private fitCamera(): void {
    if (!this.office) return;
    const fit = Math.min(this.scale.width / (this.office.width * TILE), this.scale.height / (this.office.height * TILE));
    this.cameras.main.setZoom(Math.min(4, Math.max(2, Math.floor(fit * 2) / 2)));
    this.cameras.main.setBounds(0, 0, this.office.width * TILE, this.office.height * TILE);
  }
}

/** 建立 Phaser 遊戲並等場景 create 完成；呼叫端以動態 import 載入此模組，Phaser 只在 /office 下載。 */
export function createOfficeGame(parent: HTMLElement, handlers: OfficeSceneHandlers): Promise<{ game: Phaser.Game; scene: OfficeScene }> {
  return new Promise((resolve) => {
    const game: Phaser.Game = new Phaser.Game({
      type: Phaser.AUTO,
      parent,
      pixelArt: true,
      backgroundColor: '#1b1f24',
      banner: false,
      audio: { noAudio: true },
      fps: { target: 30, limit: 30 },
      scale: { mode: Phaser.Scale.RESIZE, width: parent.clientWidth, height: parent.clientHeight },
      scene: new OfficeScene(handlers, (scene) => resolve({ game, scene })),
    });
  });
}
```

- [ ] **Step 6: OfficeSidePanel**

`apps/web/src/pages/office/_components/OfficeSidePanel.vue`：

```vue
<script setup lang="ts">
/**
 * @component OfficeSidePanel
 * 辦公室右側面板：目前在場的人、部門成員（上線狀態與敲門）、靠近聊天。
 * 「會傳給誰」只是提示，實際收訊對象由後端依位置決定。
 */
import { computed, ref } from 'vue';
import dayjs from 'dayjs';
import PresenceDot from '@/app/components/PresenceDot.vue';
import { presenceOf } from '@/app/composables/use-presence';
import { OFFICE_STATUS_LABELS, type OfficeChatMessage, type OfficeOccupant, type OfficeSeat } from '@/shared/services/office-service';

const props = defineProps<{
  departmentId: string | undefined;
  seats: OfficeSeat[];
  occupants: OfficeOccupant[];
  meId: string;
  recipients: string[];
  messages: OfficeChatMessage[];
  /** 送出聊天；回傳 false 時保留輸入內容。 */
  send: (text: string) => Promise<boolean>;
}>();
const emit = defineEmits<{ knock: [membershipId: string] }>();

const draft = ref('');
const sending = ref(false);
const present = computed(() => [...props.occupants].sort((a, b) => a.name.localeCompare(b.name, 'zh-Hant')));
const nameOf = (id: string) => props.occupants.find((value) => value.membershipId === id)?.name ?? props.seats.find((value) => value.membershipId === id)?.name ?? '同事';
const recipientHint = computed(() => (props.recipients.length ? `會傳給：${props.recipients.map(nameOf).join('、')}` : '附近沒有人，走近同事就能聊天'));
const canKnock = (id: string) => id !== props.meId && presenceOf(id) !== undefined;

async function submit() {
  const text = draft.value.trim();
  if (!text || sending.value) return;
  sending.value = true;
  try {
    if (await props.send(text)) draft.value = '';
  } finally {
    sending.value = false;
  }
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
    event.preventDefault();
    void submit();
  }
}
</script>

<template>
  <aside class="side">
    <section>
      <h4>在這裡（{{ present.length }}）</h4>
      <div v-for="occupant in present" :key="occupant.membershipId" class="row">
        <PresenceDot :membership-id="occupant.membershipId" />
        <span class="name">{{ occupant.name }}</span>
        <a-tag v-if="occupant.homeDepartmentId !== departmentId" color="blue">訪客</a-tag>
        <span v-if="occupant.status !== 'available'" class="muted">{{ OFFICE_STATUS_LABELS[occupant.status] }}</span>
      </div>
    </section>
    <section>
      <h4>部門成員（{{ seats.length }}）</h4>
      <div v-for="seat in seats" :key="seat.membershipId" class="row">
        <PresenceDot :membership-id="seat.membershipId" :last-seen-at="seat.lastSeenAt" />
        <span class="name">{{ seat.name }}</span>
        <a-button v-if="canKnock(seat.membershipId)" size="small" type="link" @click="emit('knock', seat.membershipId)">敲門</a-button>
      </div>
    </section>
    <section class="chat">
      <h4>附近聊天</h4>
      <div class="messages">
        <p v-if="!messages.length" class="muted">走到同事旁邊或進入會議室就能聊天，訊息不會保存。</p>
        <div v-for="(message, index) in messages" :key="index" class="message">
          <span class="muted">{{ dayjs(message.sentAt).format('HH:mm') }}</span>
          <strong>{{ message.fromName }}</strong>
          <span>{{ message.text }}</span>
        </div>
      </div>
      <p class="muted hint">{{ recipientHint }}</p>
      <a-textarea v-model:value="draft" :auto-size="{ minRows: 1, maxRows: 4 }" :maxlength="500" placeholder="輸入訊息，Enter 送出" @keydown="onKeydown" />
      <a-button type="primary" block :loading="sending" :disabled="!draft.trim()" @click="submit">送出</a-button>
    </section>
  </aside>
</template>

<style scoped>
.side { display: flex; flex-direction: column; gap: 12px; width: 280px; min-width: 0; overflow-y: auto; }
.side h4 { margin: 0 0 6px; font-size: 13px; }
.row { display: flex; align-items: center; gap: 6px; min-height: 28px; }
.name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.muted { color: rgba(0, 0, 0, 0.45); font-size: 12px; }
.chat { display: flex; flex: 1; flex-direction: column; gap: 6px; min-height: 220px; }
.messages { flex: 1; overflow-y: auto; }
.message { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 4px; word-break: break-word; }
.hint { margin: 0; }
@media (max-width: 960px) { .side { width: 100%; } }
</style>
```

- [ ] **Step 7: OfficeView**

`apps/web/src/pages/office/_components/OfficeView.vue`：

```vue
<script setup lang="ts">
/**
 * @component OfficeView
 * 部門虛擬辦公室：上方工具列（部門切換、狀態、表情、角色、主題），左側 Phaser 地圖，右側成員與聊天。
 * 所有互動都經過全域 hub 連線（use-presence）；後端驗證後才廣播，這裡只送意圖並依事件更新畫面。
 */
import { computed, onBeforeUnmount, onMounted, reactive, ref, shallowRef } from 'vue';
import { message } from 'ant-design-vue';
import { useMe } from '@/app/composables/use-me';
import { answerKnock, connectionState, onOfficeReconnected, setOfficeKnockHandler, waitForOfficeConnection } from '@/app/composables/use-presence';
import {
  hubErrorMessage,
  listOfficeRoomsApi,
  officeConnection,
  updateRoomThemeApi,
  OFFICE_AVATARS,
  OFFICE_EMOTES,
  OFFICE_STATUS_LABELS,
  OFFICE_THEMES,
  type Facing,
  type OfficeChatMessage,
  type OfficeKnock,
  type OfficeOccupant,
  type OfficeRoom,
  type OfficeSnapshot,
  type OfficeStatus,
  type PresenceEntry,
} from '@/shared/services/office-service';
import { AVATAR_COLORS } from './office-textures';
import { chatRecipients, zoneCenter } from './office-grid';
import type { OfficeScene } from './office-scene';
import OfficeSidePanel from './OfficeSidePanel.vue';

const { me, can } = useMe();
const meId = me.value!.scope.membershipId!;
const hub = officeConnection();
const canvasHost = ref<HTMLDivElement>();
const rooms = ref<OfficeRoom[]>([]);
const departmentId = ref<string>();
const snapshot = shallowRef<OfficeSnapshot | null>(null);
const occupants = reactive(new Map<string, OfficeOccupant>());
const messages = ref<OfficeChatMessage[]>([]);
const status = ref<OfficeStatus>('available');
const statusText = ref('');
const savedStatusText = ref('');
const replaced = ref(false);
const joining = ref(false);
const canEditTheme = computed(() => can('company.departments', 'update'));
let scene: OfficeScene | null = null;
let game: { destroy(removeCanvas: boolean): void } | null = null;
let stopReconnected: (() => void) | null = null;

const statusOptions = Object.entries(OFFICE_STATUS_LABELS).map(([value, label]) => ({ value, label }));
const themeOptions = OFFICE_THEMES.map((theme) => ({ value: theme.value, label: theme.label }));
const avatarColor = (key: string) => `#${(AVATAR_COLORS[key] ?? 0x999999).toString(16).padStart(6, '0')}`;

/** 部門樹已完整載入（小型資料），這裡只依層級縮排顯示。 */
const roomOptions = computed(() => {
  const byId = new Map(rooms.value.map((room) => [room.departmentId, room]));
  const depth = (room: OfficeRoom): number => (room.parentId && byId.has(room.parentId) ? depth(byId.get(room.parentId)!) + 1 : 0);
  return rooms.value.map((room) => ({ value: room.departmentId, label: `${'　'.repeat(depth(room))}${room.name}（${room.onlineCount} 人在線）` }));
});

const occupantList = computed(() => [...occupants.values()]);
const recipients = computed(() => {
  const map = snapshot.value?.map;
  if (!map) return [];
  return chatRecipients(map, meId, new Map(occupantList.value.map((occupant) => [occupant.membershipId, { x: occupant.x, y: occupant.y }])));
});

function updateOccupant(membershipId: string, patch: Partial<OfficeOccupant>) {
  const current = occupants.get(membershipId);
  if (current) occupants.set(membershipId, { ...current, ...patch });
}

const subscriptions: [string, (...args: any[]) => void][] = [
  ['OccupantJoined', (occupant: OfficeOccupant) => { occupants.set(occupant.membershipId, occupant); scene?.upsertOccupant(occupant); }],
  ['OccupantLeft', (id: string) => { occupants.delete(id); scene?.removeOccupant(id); }],
  ['Moved', (id: string, x: number, y: number, facing: Facing) => { updateOccupant(id, { x, y, facing }); scene?.moveOccupant(id, x, y, facing, id !== meId); }],
  ['PositionCorrected', (x: number, y: number, facing: Facing) => { updateOccupant(meId, { x, y, facing }); scene?.moveOccupant(meId, x, y, facing, false); }],
  ['Emoted', (id: string, key: string) => scene?.showEmote(id, OFFICE_EMOTES.find((emote) => emote.key === key)?.icon ?? '❔')],
  ['AvatarChanged', (id: string, key: string) => { updateOccupant(id, { avatarKey: key }); scene?.setAvatar(id, key); }],
  ['ChatReceived', (chat: OfficeChatMessage) => { messages.value = [...messages.value.slice(-99), chat]; scene?.showBubble(chat.fromMembershipId, chat.text); }],
  ['KnockAnswered', (_id: string, name: string, accepted: boolean, reply: string | null) => (accepted ? message.success(`${name} 正在過來`) : message.info(`${name}：${reply || '稍後再過去'}`))],
  ['OfficeSessionReplaced', () => { replaced.value = true; }],
  ['PresenceChanged', (entry: PresenceEntry) => { updateOccupant(entry.membershipId, { status: entry.status, statusText: entry.statusText }); scene?.setStatus(entry.membershipId, entry.status); }],
];

const wakeScene = () => scene?.wake();

onMounted(async () => {
  const params = new URLSearchParams(window.location.search);
  const answer = params.get('answer');
  if (params.has('dept') || answer) history.replaceState(null, '', '/office');
  rooms.value = await listOfficeRoomsApi();
  const wanted = params.get('dept') ?? me.value!.scope.departmentId ?? rooms.value[0]?.departmentId;
  const { createOfficeGame } = await import('./office-scene');
  const created = await createOfficeGame(canvasHost.value!, { step: onStep });
  scene = created.scene;
  game = created.game;
  subscriptions.forEach(([name, handler]) => hub.on(name, handler));
  setOfficeKnockHandler(goToKnock);
  stopReconnected = onOfficeReconnected(() => {
    if (departmentId.value) void join(departmentId.value);
  });
  window.addEventListener('keydown', wakeScene);
  window.addEventListener('pointerdown', wakeScene);
  await waitForOfficeConnection();
  if (wanted) await join(wanted);
  if (answer) await respond(answer);
});

onBeforeUnmount(() => {
  void hub.send('LeaveOffice').catch(() => undefined);
  subscriptions.forEach(([name, handler]) => hub.off(name, handler));
  setOfficeKnockHandler(null);
  stopReconnected?.();
  window.removeEventListener('keydown', wakeScene);
  window.removeEventListener('pointerdown', wakeScene);
  game?.destroy(true);
});

async function join(target: string) {
  joining.value = true;
  try {
    const result = await hub.invoke<OfficeSnapshot>('JoinOffice', target);
    if (target !== departmentId.value) messages.value = [];
    departmentId.value = target;
    snapshot.value = result;
    replaced.value = false;
    occupants.clear();
    result.occupants.forEach((occupant) => occupants.set(occupant.membershipId, occupant));
    status.value = result.me.status;
    statusText.value = savedStatusText.value = result.me.statusText ?? '';
    scene?.loadOffice(result, meId);
    rooms.value = await listOfficeRoomsApi();
  } catch (error) {
    message.error(hubErrorMessage(error));
  } finally {
    joining.value = false;
  }
}

function onStep(x: number, y: number, facing: Facing) {
  updateOccupant(meId, { x, y, facing });
  void hub.send('Move', x, y, facing).catch(() => undefined);
}

async function invoke(method: string, ...args: unknown[]): Promise<boolean> {
  try {
    await hub.invoke(method, ...args);
    return true;
  } catch (error) {
    message.warning(hubErrorMessage(error));
    return false;
  }
}

async function changeStatus(next: OfficeStatus) {
  if (!(await invoke('SetStatus', next, statusText.value.trim() || null))) return;
  status.value = next;
  savedStatusText.value = statusText.value.trim();
  const map = snapshot.value?.map;
  const zoneType = next === 'meeting' ? 'meeting' : next === 'break' ? 'pantry' : null;
  const zone = map && zoneType ? map.zones.find((value) => value.type === zoneType) : undefined;
  if (map && zone) scene?.walkTo(zoneCenter(map, zone));
}

function saveStatusText() {
  if (statusText.value.trim() !== savedStatusText.value) void changeStatus(status.value);
}

const sendChat = (text: string) => invoke('SendChat', text);
const emote = (key: string) => void invoke('Emote', key);
const goToSeat = () => void invoke('GoToSeat');
const setAvatar = (key: string) => void invoke('SetAvatar', key);

async function knock(membershipId: string) {
  if (await invoke('Knock', membershipId, null)) message.success('已敲門，等對方回應');
}

async function respond(from: string) {
  try {
    await answerKnock(from, true);
  } catch (error) {
    message.error(hubErrorMessage(error));
  }
}

async function goToKnock(knockInfo: OfficeKnock) {
  if (knockInfo.departmentId !== departmentId.value) await join(knockInfo.departmentId);
  await respond(knockInfo.fromMembershipId);
}

async function changeTheme(theme: string) {
  if (!departmentId.value) return;
  try {
    await updateRoomThemeApi(departmentId.value, theme);
    await join(departmentId.value);
  } catch (error) {
    message.error((error as Error).message);
  }
}
</script>

<template>
  <div class="office">
    <div class="toolbar">
      <a-select :value="departmentId" :options="roomOptions" :loading="joining" style="width: 240px" @change="(value: unknown) => join(String(value))" />
      <a-select :value="status" :options="statusOptions" style="width: 110px" @change="(value: unknown) => changeStatus(value as OfficeStatus)" />
      <a-input v-model:value="statusText" :maxlength="40" placeholder="狀態說明（選填）" style="width: 180px" @press-enter="saveStatusText" @blur="saveStatusText" />
      <a-space :size="4">
        <a-button v-for="item in OFFICE_EMOTES" :key="item.key" size="small" @click="emote(item.key)">{{ item.icon }}</a-button>
      </a-space>
      <a-button @click="goToSeat">回到座位</a-button>
      <a-popover trigger="click" title="選擇角色">
        <template #content>
          <a-space wrap>
            <a-button v-for="key in OFFICE_AVATARS" :key="key" shape="circle" :style="{ background: avatarColor(key) }" @click="setAvatar(key)" />
          </a-space>
        </template>
        <a-button>角色</a-button>
      </a-popover>
      <a-select v-if="canEditTheme && snapshot" :value="snapshot.map.theme" :options="themeOptions" style="width: 100px" @change="(value: unknown) => changeTheme(String(value))" />
    </div>
    <a-alert v-if="connectionState === 'reconnecting' || connectionState === 'disconnected'" type="warning" show-icon banner message="連線中斷，重新連線中…" />
    <div class="body">
      <div ref="canvasHost" class="canvas">
        <div v-if="replaced" class="overlay">
          <p>辦公室已在其他分頁開啟。</p>
          <a-button type="primary" @click="departmentId && join(departmentId)">在這裡繼續</a-button>
        </div>
      </div>
      <OfficeSidePanel
        :department-id="departmentId"
        :seats="snapshot?.map.seats ?? []"
        :occupants="occupantList"
        :me-id="meId"
        :recipients="recipients"
        :messages="messages"
        :send="sendChat"
        @knock="knock"
      />
    </div>
  </div>
</template>

<style scoped>
.office { display: flex; flex-direction: column; gap: 8px; height: calc(100vh - 140px); min-height: 480px; }
.toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.body { display: flex; flex: 1; gap: 12px; min-height: 0; }
.canvas { position: relative; flex: 1; min-width: 0; overflow: hidden; border-radius: 6px; background: #1b1f24; }
.overlay { position: absolute; inset: 0; z-index: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; color: #fff; background: rgba(0, 0, 0, 0.6); }
@media (max-width: 960px) {
  .office { height: auto; }
  .body { flex-direction: column; }
  .canvas { flex: none; height: 60vh; }
}
</style>
```

- [ ] **Step 8: 型別檢查與建置**

Run: `npm run check:web` → Expected: 0 errors（若 Phaser 型別對 `game.loop.sleep`、`wake(true)`、`tweens.getTweens()` 的簽章與上方不同，依安裝版本的 `.d.ts` 調整呼叫方式，不改行為）
Run: `npm run build:web` → Expected: 成功；在輸出中確認 Phaser 被切成獨立 chunk（`dist/_astro/` 有一個約 1 MB 的檔案，且不在其他頁面的入口 chunk 內）

- [ ] **Step 9: 瀏覽器實測（兩個帳號）**

啟動 API 與 `npm run dev:web`。用內建瀏覽器開兩個分頁：分頁 1 以 `demo-manager` 登入、分頁 2 以 `demo-sales` 登入（密碼取自 `.env.development-api.local`，不在對話中顯示；兩者 refresh cookie 會互相覆蓋時，改用 Task 8 smoke 建立、尚未停用的新帳號，或分頁 2 改用 `tabs_create` 搭配不同帳號依序操作）。逐項確認：
1. 左側選單出現「虛擬辦公室」，進入後地圖畫出茶水間、會議室、桌位與自己的角色。
2. 分頁 1 用方向鍵走動，分頁 2 看到角色平滑移動；點擊地圖會自動尋路。
3. 兩人走近後在聊天框送出訊息，對方頭上出現對話泡泡、側欄出現訊息；走遠後送出顯示「附近沒有人」。
4. 狀態改「開會中」→ 角色自動走進會議室，對方看到 📅 圖示與側欄狀態。
5. 從側欄對另一人按「敲門」→ 對方右下角跳出通知，按「前往」後被放到旁邊。
6. 切換部門到「財務部」→ 對方看到你離開；切回後重新出現。
7. 在同一帳號開第二個 `/office` 分頁 → 第一個分頁出現「辦公室已在其他分頁開啟」。
8. `resize_window` 切到 mobile 寬度，確認側欄移到地圖下方、沒有水平捲軸；測完切回 desktop。
有任何一項不符合，修正後再測。

- [ ] **Step 10: Commit**

```bash
git add apps/web/package.json apps/web/package-lock.json apps/web/src/pages/office apps/web/src/app/route-manifest.ts apps/web/src/app/components/AdminApp.vue
git commit -m "feat: 新增部門虛擬辦公室頁面

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Kenney CC0 像素素材

**前置：** 下載外部檔案需要使用者明確同意。開始前先詢問使用者：由使用者自行從 kenney.nl 下載，或同意由代理人下載下列兩個素材包（說明檔名、來源與大小後等待同意）：
- Roguelike Indoors（https://kenney.nl/assets/roguelike-indoors）：16×16 室內圖塊，1px 間隔
- Roguelike Characters（https://kenney.nl/assets/roguelike-characters）：16×16 角色部件，1px 間隔

**Files:**
- Create: `apps/web/public/office/kenney/roguelike-indoors/`（spritesheet 與 `License.txt`）
- Create: `apps/web/public/office/kenney/roguelike-characters/`（spritesheet 與 `License.txt`）
- Create: `apps/web/src/pages/office/_components/office-tiles.ts`
- Modify: `apps/web/src/pages/office/_components/office-scene.ts`

**Interfaces:**
- Consumes: Task 10 的貼圖鍵名規則（`floor-{theme}-{zone}`、`wall-{theme}`、`furniture-{kind}`、`avatar-{key}-{facing}`）
- Produces: `office-tiles.ts`：`KENNEY_SHEETS`、`OFFICE_TILE_FRAMES`、`OFFICE_AVATAR_LAYERS`

- [ ] **Step 1: 放置素材**

把兩個 zip 解壓到 scratchpad 的新目錄，只把 spritesheet PNG（檔名含 `transparent` 的版本）與 `License.txt` 複製到上述 `public/office/kenney/...` 目錄；其他檔案不進 repo。

- [ ] **Step 2: 對照 frame 編號**

用 Read 工具開啟兩張 spritesheet，依「每列 frame 數 = (圖寬 + 1) / 17」計算編號（frame = 列 × 每列數量 + 欄，從 0 起算），為下列每個鍵選一個最接近的圖塊並記下編號：
- 三種主題 × 四種區域的地板（12 個）、三種主題的牆（3 個）
- 家具：`desk`、`chair`、`meeting-table`、`coffee`、`fridge`、`counter`、`table`、`plant`、`whiteboard`、`rug`
- 角色：8 組（a1～a8），每組依序列出要疊的 frame（身體、褲子、上衣、頭髮），顏色盡量對應 `AVATAR_COLORS` 的色系

找不到合適圖塊的鍵不要硬配，留給程式繪製的備援。

- [ ] **Step 3: 建立 office-tiles.ts**

```ts
/**
 * @file office-tiles.ts
 * Kenney CC0 素材（Roguelike Indoors／Roguelike Characters）與語意貼圖鍵的對照。
 * 沒有列在這裡的鍵使用 office-textures.ts 的程式繪製版本；素材載入失敗時全部改用程式繪製。
 */
export const KENNEY_SHEETS = {
  indoor: { key: 'kenney-indoor', url: '/office/kenney/roguelike-indoors/<Step 1 實際檔名>.png' },
  characters: { key: 'kenney-characters', url: '/office/kenney/roguelike-characters/<Step 1 實際檔名>.png' },
} as const;

/** 語意鍵 → indoor sheet 的 frame 編號（Step 2 對照結果）。 */
export const OFFICE_TILE_FRAMES: Partial<Record<string, number>> = {
  // 例：'floor-wood-desk': 123,
};

/** 角色鍵 → characters sheet 由下往上疊的 frame 編號（Step 2 對照結果）。 */
export const OFFICE_AVATAR_LAYERS: Partial<Record<string, number[]>> = {
  // 例：a1: [0, 54, 108, 162],
};
```

把 `<Step 1 實際檔名>` 與兩個物件的內容換成 Step 1、Step 2 的實際結果（不保留範例註解）。

- [ ] **Step 4: 場景改用素材**

`office-scene.ts`：
1. import `import { KENNEY_SHEETS, OFFICE_AVATAR_LAYERS, OFFICE_TILE_FRAMES } from './office-tiles';`
2. 類別欄位加 `private assetsFailed = false;`
3. 新增 `preload()`：
   ```ts
     preload(): void {
       this.load.on('loaderror', () => { this.assetsFailed = true; });
       for (const sheet of Object.values(KENNEY_SHEETS)) this.load.spritesheet(sheet.key, sheet.url, { frameWidth: 16, frameHeight: 16, spacing: 1 });
     }
   ```
4. 新增私有方法並把 `loadOffice` 中三個 `this.add.image(... key ...)` 改為 `this.tile(x * TILE, y * TILE, key)`：
   ```ts
     /** 有對照的 frame 就用 Kenney 素材，否則用程式繪製的貼圖。 */
     private tile(x: number, y: number, key: string): Phaser.GameObjects.Image {
       const frame = this.assetsFailed ? undefined : OFFICE_TILE_FRAMES[key];
       return frame === undefined ? this.add.image(x, y, key).setOrigin(0) : this.add.image(x, y, KENNEY_SHEETS.indoor.key, frame).setOrigin(0);
     }
   ```
5. 角色：在 `upsertOccupant` 建立 sprite 處，若 `!this.assetsFailed && OFFICE_AVATAR_LAYERS[occupant.avatarKey]` 存在，改為建立多張 `this.add.image(0, 0, KENNEY_SHEETS.characters.key, frame).setOrigin(0.5, 1)` 疊成角色（Kenney 角色只有正面：`left` 時 `setFlipX(true)`，其他方向不翻轉），`AvatarView` 改存 `sprites: Phaser.GameObjects.Image[]` 與 `kenney: boolean`；`setTexture` 的三個地方（`upsertOccupant`、`moveOccupant`、`setAvatar`）依 `kenney` 分支處理：程式繪製版本照舊換貼圖，Kenney 版本換朝向時只改 flipX、換角色時重建 sprites。
6. `create()` 最後若 `this.assetsFailed`，呼叫建構式新增的第三個參數 `onAssetsFailed()`；`createOfficeGame` 對應加參數，`OfficeView` 傳入 `() => message.warning('部分素材載入失敗，已改用簡易圖案。')`。

- [ ] **Step 5: 驗證**

Run: `npm run check:web`、`npm run build:web` → Expected: 成功
瀏覽器重跑 Task 10 Step 9 的第 1、2、4 項，並截圖確認圖塊沒有錯位（牆、地板、家具對齊格子）；再把 `KENNEY_SHEETS.indoor.url` 暫改成不存在的路徑重新整理，確認出現「部分素材載入失敗」提示且畫面改用程式繪製，確認後改回。

- [ ] **Step 6: Commit**

```bash
git add apps/web/public/office apps/web/src/pages/office/_components/office-tiles.ts apps/web/src/pages/office/_components/office-scene.ts apps/web/src/pages/office/_components/OfficeView.vue
git commit -m "feat: 虛擬辦公室改用 Kenney CC0 像素素材

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: 整體驗證

**Files:** 無新增；只在發現問題時修正對應檔案。

- [ ] **Step 1: 全部自動檢查**

依序執行並確認全部通過：

```bash
npm run build:api
```

```bash
npm run test:api
```

```bash
npm run check:web
```

```bash
npm run build:web
```

API 以 `Quote__LoginCaptchaEnabled=false Quote__OfficeAuthSweepSeconds=5` 啟動後：

```bash
npm run test:smoke
```

```bash
npm run test:smoke:office
```

- [ ] **Step 2: 檔案長度**

Run: `wc -l apps/api/QuotePlatform.Api/Modules/Office/*.cs apps/web/src/pages/office/_components/* apps/web/src/app/composables/use-presence.ts apps/web/src/shared/services/office-service.ts`
Expected: 每個檔案都不超過 1000 行

- [ ] **Step 3: 文件一致性**

逐一確認以下內容與實作一致：spec（Task 1 Step 1 的修改）、`docs/permissions-and-data-scope.md`、`docs/database-planning.md`、`docs/database-schema.sql`、`docs/system-architecture.md` 6.8 節。

- [ ] **Step 4: 回報**

整理給使用者：完成的功能、各項測試結果（含實際輸出摘要）、尚未驗證的部分（例如正式環境的 Nginx WebSocket 尚未實際部署）、部署注意事項（此版本部署時必須執行一次 Seeder 才會替既有角色開放 `office:read`；API 只能單一實例）。
