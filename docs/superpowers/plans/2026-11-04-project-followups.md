# 專案跟進紀錄 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 業務在專案底下記錄每次客戶互動與下次聯絡日；專案列表可依下次聯絡排序與篩選逾期，主管有一頁跨專案總表看得到部屬的紀錄。

**Architecture:** 新增 `project_followups` 表與 `Modules/FollowUps/`（Rules、Service、Controller）。可見範圍一律經 `ProjectAccessService`；專案上的 `LastFollowUpAt`、`NextFollowUpOn` 是依「最新一筆」重算的快取，只由 `FollowUpService` 以單一 `ExecuteUpdateAsync` 寫入。前端新增專案頁籤、表單視窗、`/followups` 總表頁，並在專案列表加欄位與篩選。

**Tech Stack:** ASP.NET Core 10、EF Core 9（Pomelo MySQL 9.0.0）、MariaDB、Astro + Vue 3、Ant Design Vue 4.2.6、@tanstack/vue-query 5、@ant-design/icons-vue 7、Node 24 smoke 測試。

**規格：** `docs/superpowers/specs/2026-11-04-project-followups-design.md`（本計畫與規格衝突時以規格為準，並回報）。

## Global Constraints

- 工作目錄一律是 worktree：`/Users/emmt-1246/Documents/projects/專案報價管理系統/.worktrees/project-followups`（分支 `feat/project-followups`）。不要切換分支、不要 push、不要碰主目錄 `main` 上使用者未提交的儀表板改動。
- 所有程式碼註解、錯誤文案、文件使用繁體中文；技術名稱保留原文。
- 介面契約層完整文件化：公開 Controller 動作、核心 Service 公開方法、Contracts record、Entity 與常數類別寫 XML Doc（`<summary>`、`<param>`、`<returns>`）；前端 `…Api` 函式、共用工具函式與重要型別寫 JSDoc（含 `@param`、`@returns`），重要檔案頂部寫 `@file`／`@component`。
- 內部實作只寫「為什麼」的註解，禁止逐行機械式註解。
- JS/TS/Vue 的 `/* … */` 區塊註解內不得出現 `*/` 字樣；一般邏輯註解用 `//`。
- migration 中所有新資料表與欄位都要有繁體中文 COMMENT（`HasComment`）。
- 業務查詢用 EF Core/LINQ，不寫 SQL 字串。EF Core 9 的 LINQ 中，`Contains` 一律對 `List<T>` 呼叫（C# 14 會把陣列的 `Contains` 綁到 `ReadOnlySpan` 多載，無法轉成 SQL）。
- 列表的搜尋、篩選、排序、分頁一律由後端處理；表格一律用 `DataTable`。時間軸不是表格，可用 `a-timeline`。
- 單一程式碼檔案不超過 1000 行。
- Commit：Conventional Commits、繁體中文 subject、單一目的，結尾加：
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  ```
- 驗證：`npm run build:api` 必須 0 警告；`npm run test:api`；前端 `npm run check:web` 與 `npm run build:web`（Node 24）。
- **行程規則**：使用者自己的 API 在 5280、Astro dev server 在 4341，不要停止、重啟或修改它們。需要執行 API 時另外啟動測試實例在 5281、前端測試 dev server 在 4342；啟動後記下 PID，只用 `kill <PID>` 停止，禁止 `pkill`、`killall` 或依名稱比對。
- 不要在輸出或報告中顯示任何密碼、金鑰或 token。`.env.development-api.local` 不在版控中，不要 commit。
- 共用資料庫注意：測試實例會對 shared-mariadb（127.0.0.1:9529）的 `quote_platform` 套用本分支的 migration 與 Seeder。新欄位都可為 NULL，使用者 5280 上的舊程式照常運作；但使用者若在合併前以舊程式重跑 Seeder，完整存取角色上的 `followups` 會被同步掉，合併後再跑一次 Seeder 即可恢復。完成時要在報告中提醒。

---

## 檔案結構

**新增**
| 檔案 | 職責 |
|---|---|
| `apps/api/QuotePlatform.Api/Infrastructure/FollowUpEntities.cs` | `ProjectFollowUp` entity 與 `FollowUpTypes` 常數 |
| `apps/api/QuotePlatform.Api/Migrations/*_AddProjectFollowUps.cs` | 由 EF 產生 |
| `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpContracts.cs` | 輸入、查詢與回傳 record |
| `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpRules.cs` | 純規則（輸入檢查、台灣日期、提醒狀態常數）、`LatestFirst` 排序、self-test |
| `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpService.cs` | 列表、總表、聯絡人、寫入、不用跟進、狀態重算 |
| `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpsController.cs` | HTTP 端點 |
| `tests/followups-smoke.mjs` | 整合驗證 |
| `apps/web/src/shared/services/followup-service.ts` | 前端 API 與顯示工具 |
| `apps/web/src/pages/projects/detail/_components/ProjectFollowUpsTab.vue` | 專案頁籤 |
| `apps/web/src/pages/projects/detail/_components/ProjectFollowUpFormModal.vue` | 新增／編輯視窗 |
| `apps/web/src/pages/followups/index.astro`、`_components/FollowUpsView.vue` | 總表頁 |

**修改**
`BusinessEntities.cs`（Project 兩欄位）、`QuoteDbContext.cs`、`PermissionCatalog.cs`、`QuoteSeeder.cs`、`Program.cs`、`Modules/Projects/ProjectService.cs`、`Modules/Projects/ProjectsController.cs`、`package.json`、`apps/web/src/shared/types/permissions.ts`、`app/route-manifest.ts`、`app/components/AdminApp.vue`、`shared/services/business-service.ts`、`pages/projects/_components/ProjectsView.vue`、`pages/projects/detail/_components/ProjectDetailView.vue`、`docs/database-planning.md`、`docs/database-schema.sql`、`docs/permissions-and-data-scope.md`、`docs/system-architecture.md`。

---

### Task 0: 準備 worktree 的執行環境

**Files:** 無版控檔案變更。

- [ ] **Step 1: 連結本機設定檔**（不複製，避免多一份機密）

```bash
cd /Users/emmt-1246/Documents/projects/專案報價管理系統/.worktrees/project-followups
ln -s ../../.env.development-api.local .env.development-api.local
git status --short
```
Expected: `git status` 沒有輸出（`.env*.local` 已被 `.gitignore` 忽略）。若出現 `.env.development-api.local`，停下來回報，不要 commit。

- [ ] **Step 2: 安裝前端套件**

```bash
export PATH="$HOME/.nvm/versions/node/v24.15.0/bin:$PATH"
npm ci --prefix apps/web
```
Expected: 成功結束。

- [ ] **Step 3: 建立測試實例的環境檔**（主目錄的 `.superpowers/` 已被忽略）

```bash
mkdir -p /Users/emmt-1246/Documents/projects/專案報價管理系統/.superpowers/sdd/2026-11-04-project-followups
printf 'Quote__LoginCaptchaEnabled=false\nASPNETCORE_URLS=http://127.0.0.1:5281\n' > /Users/emmt-1246/Documents/projects/專案報價管理系統/.superpowers/sdd/2026-11-04-project-followups/test-api.env
```

之後所有任務啟動測試 API 的指令（在 worktree 根目錄、背景執行，記下 `lsof -tiTCP:5281 -sTCP:LISTEN` 的 PID）：
```bash
node scripts/run-with-env.mjs .env.development-api.local /Users/emmt-1246/Documents/projects/專案報價管理系統/.superpowers/sdd/2026-11-04-project-followups/test-api.env -- dotnet run --project apps/api/QuotePlatform.Api --no-launch-profile
```

- [ ] **Step 4: 確認基準狀態**

Run: `npm run build:api && npm run test:api`
Expected: 0 警告、`self-test passed`。

---

### Task 1: 資料表、Project 欄位與 migration

**Files:**
- Create: `apps/api/QuotePlatform.Api/Infrastructure/FollowUpEntities.cs`
- Modify: `apps/api/QuotePlatform.Api/Infrastructure/BusinessEntities.cs`（`Project` 類別，`ClosedAt` 屬性之後）
- Modify: `apps/api/QuotePlatform.Api/Infrastructure/QuoteDbContext.cs`（DbSet、`OnModelCreating`、`Entity<Project>` 設定、新方法 `ConfigureFollowUps`）
- Create: migration `AddProjectFollowUps`
- Modify: `docs/database-planning.md`、`docs/database-schema.sql`

**Interfaces:**
- Produces: `ProjectFollowUp`（屬性見下）、`FollowUpTypes.All`（`IReadOnlyList<string>`）、`Project.LastFollowUpAt`（`DateTimeOffset?`）、`Project.NextFollowUpOn`（`DateOnly?`）、`QuoteDbContext.ProjectFollowUps`。

- [ ] **Step 1: 建立 entity**

`apps/api/QuotePlatform.Api/Infrastructure/FollowUpEntities.cs`：

```csharp
namespace QuotePlatform.Api.Infrastructure;

/// <summary>專案跟進紀錄：業務與客戶的一次互動（電話、拜訪、會議等），可附下次聯絡日。</summary>
/// <remarks>
/// 可見範圍完全繼承所屬專案。專案的 <see cref="Project.LastFollowUpAt"/>、<see cref="Project.NextFollowUpOn"/>
/// 由 FollowUpService 依「最新一筆」重算，其他程式不得直接寫入。
/// </remarks>
public sealed class ProjectFollowUp : ICompanyOwned, ISoftDelete
{
    /// <summary>跟進紀錄唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬專案識別碼。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>類型（參考 <see cref="FollowUpTypes"/>）。</summary>
    public string Type { get; set; } = FollowUpTypes.Call;

    /// <summary>互動發生時間（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>對象聯絡人；必須屬於專案的客戶，聯絡人刪除時設為 null。</summary>
    public Guid? ContactId { get; set; }

    /// <summary>對象姓名快照；選了聯絡人時為聯絡人姓名。</summary>
    public string? ContactName { get; set; }

    /// <summary>主旨。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>內容。</summary>
    public string? Content { get; set; }

    /// <summary>下次聯絡日（台灣日期）。</summary>
    public DateOnly? NextFollowUpOn { get; set; }

    /// <summary>下次要做的事；沒有下次聯絡日時為 null。</summary>
    public string? NextAction { get; set; }

    /// <summary>按「不用跟進」的時間（UTC）；null 表示仍需提醒。</summary>
    public DateTimeOffset? NextDismissedAt { get; set; }

    /// <summary>按「不用跟進」的使用者識別碼。</summary>
    public Guid? NextDismissedBy { get; set; }

    /// <summary>撰寫者使用者識別碼。</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後修改者使用者識別碼；沒有修改過為 null。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>是否已軟刪除。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>軟刪除時間戳記（UTC）。</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>跟進紀錄類型代碼。</summary>
public static class FollowUpTypes
{
    /// <summary>電話。</summary>
    public const string Call = "call";

    /// <summary>拜訪客戶。</summary>
    public const string Visit = "visit";

    /// <summary>會議（含視訊會議）。</summary>
    public const string Meeting = "meeting";

    /// <summary>Email。</summary>
    public const string Email = "email";

    /// <summary>即時訊息（LINE 等）。</summary>
    public const string Message = "message";

    /// <summary>其他。</summary>
    public const string Other = "other";

    /// <summary>全部類型，順序即畫面上的按鈕順序。</summary>
    public static readonly IReadOnlyList<string> All = [Call, Visit, Meeting, Email, Message, Other];
}
```

- [ ] **Step 2: Project 新增兩個欄位**

在 `BusinessEntities.cs` 的 `Project` 類別，`ClosedAt` 屬性之後加入：

```csharp
    /// <summary>最新一筆跟進紀錄的發生時間（UTC）；只由 FollowUpService 重算寫入。</summary>
    public DateTimeOffset? LastFollowUpAt { get; set; }

    /// <summary>下次聯絡日（台灣日期）；最新一筆沒填或已按「不用跟進」時為 null。只由 FollowUpService 重算寫入。</summary>
    public DateOnly? NextFollowUpOn { get; set; }
```

- [ ] **Step 3: DbContext 設定**

1. DbSet 區塊（`ProjectInvoices` 之後）加：
```csharp
    public DbSet<ProjectFollowUp> ProjectFollowUps => Set<ProjectFollowUp>();
```
2. `OnModelCreating` 中 `ConfigureOffice(modelBuilder);` 之後加 `ConfigureFollowUps(modelBuilder);`（必須在套用 Global Query Filter 的 foreach 之前）。
3. `modelBuilder.Entity<Project>(entity => { … })` 內，`entity.HasIndex(value => new { value.CompanyId, value.OwnerUserId });` 之後加：
```csharp
            entity.Property(value => value.LastFollowUpAt).HasComment("最新一筆跟進紀錄的發生時間（UTC）；由跟進紀錄重算");
            entity.Property(value => value.NextFollowUpOn).HasComment("下次聯絡日（台灣日期）；最新一筆未填或已按不用跟進時為 NULL");
            entity.HasIndex(value => new { value.CompanyId, value.NextFollowUpOn });
```
4. `ConfigureOffice` 方法之後新增：
```csharp
    /// <summary>專案跟進紀錄。</summary>
    private static void ConfigureFollowUps(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectFollowUp>(entity =>
        {
            entity.ToTable("project_followups", table =>
            {
                table.HasComment("專案跟進紀錄（電話、拜訪、會議等客戶互動與下次聯絡日）");
                table.HasCheckConstraint("CK_project_followups_Type", "Type IN ('call', 'visit', 'meeting', 'email', 'message', 'other')");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Id).HasComment("跟進紀錄唯一識別碼");
            entity.Property(value => value.CompanyId).HasComment("所屬公司識別碼");
            entity.Property(value => value.ProjectId).HasComment("所屬專案識別碼");
            entity.Property(value => value.Type).HasMaxLength(20).IsRequired().HasComment("類型（call / visit / meeting / email / message / other）");
            entity.Property(value => value.OccurredAt).HasComment("互動發生時間（UTC）");
            entity.Property(value => value.ContactId).HasComment("對象聯絡人識別碼；聯絡人刪除時設為 NULL");
            entity.Property(value => value.ContactName).HasMaxLength(100).HasComment("對象姓名快照");
            entity.Property(value => value.Summary).HasMaxLength(200).IsRequired().HasComment("主旨");
            entity.Property(value => value.Content).HasColumnType("text").HasComment("內容");
            entity.Property(value => value.NextFollowUpOn).HasComment("下次聯絡日（台灣日期）");
            entity.Property(value => value.NextAction).HasMaxLength(200).HasComment("下次要做的事");
            entity.Property(value => value.NextDismissedAt).HasComment("按「不用跟進」的時間（UTC）；NULL 表示仍需提醒");
            entity.Property(value => value.NextDismissedBy).HasComment("按「不用跟進」的使用者識別碼");
            entity.Property(value => value.CreatedBy).HasComment("撰寫者使用者識別碼");
            entity.Property(value => value.CreatedAt).HasComment("建立時間戳記（UTC）");
            entity.Property(value => value.UpdatedBy).HasComment("最後修改者使用者識別碼；未修改過為 NULL");
            entity.Property(value => value.UpdatedAt).HasComment("最後更新時間戳記（UTC）");
            entity.Property(value => value.IsDeleted).HasComment("是否已軟刪除");
            entity.Property(value => value.DeletedAt).HasComment("軟刪除時間戳記（UTC）");
            entity.HasIndex(value => new { value.CompanyId, value.ProjectId, value.IsDeleted, value.OccurredAt });
            entity.HasIndex(value => new { value.CompanyId, value.OccurredAt });
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(value => value.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ClientContact>().WithMany().HasForeignKey(value => value.ContactId).OnDelete(DeleteBehavior.SetNull);
        });
    }
```

- [ ] **Step 4: 建置**

Run: `npm run build:api`
Expected: 0 警告、0 錯誤。

- [ ] **Step 5: 產生 migration**

Run: `npm run migrations:add -- AddProjectFollowUps`
Expected: 產生 `Migrations/<時間戳>_AddProjectFollowUps.cs`。打開檢查：`CreateTable("project_followups")` 每個欄位有 `comment:`、表有 `.Annotation("MySql:Comment"…)` 或 `comment:`、有 CHECK 約束、`projects` 的兩個 `AddColumn` 有 comment、`ContactId` 外鍵為 `ReferentialAction.SetNull`。沒有任何與本功能無關的變更（若出現其他表的變更，停下來回報）。

- [ ] **Step 6: 驗證 migration**

Run:
```bash
dotnet ef migrations has-pending-model-changes --project apps/api/QuotePlatform.Api
dotnet ef migrations script 0 --project apps/api/QuotePlatform.Api --idempotent --output "$TMPDIR/followups-full-schema.sql"
grep -c "CREATE TABLE \`project_followups\`" "$TMPDIR/followups-full-schema.sql"
npm run dev:api:init
```
Expected: 第一行回報沒有待產生的變更；`grep` 輸出 `1`（空資料庫從頭建立的腳本包含新表）；`dev:api:init` 對既有的 `quote_platform` 套用成功並結束。`quote_app` 沒有建庫權限，不要嘗試用 root 另建空資料庫。

- [ ] **Step 7: 同步資料庫文件**

1. `docs/database-planning.md` 第 5 行「**目前狀態**」段落句尾（「草稿中的 … 尚未建立。」之前）加一句：
   「專案跟進紀錄 `project_followups` 與 `projects.LastFollowUpAt`、`projects.NextFollowUpOn` 由 migration `AddProjectFollowUps` 建立，entity 在 `Infrastructure/FollowUpEntities.cs`。」
2. 同檔「### 4.3 專案」表格最後一列之後加：
```markdown
| `project_followups` | 跟進紀錄（電話、拜訪、會議、Email、訊息、其他）；`Type` CHECK 限六種值；軟刪除。索引(`CompanyId`,`ProjectId`,`IsDeleted`,`OccurredAt`) 供專案內時間軸與「最新一筆」，(`CompanyId`,`OccurredAt`) 供跨專案總表。`ContactId` 刪除聯絡人時設為 NULL，`ContactName` 保留姓名快照 |
```
   並在 `projects` 那一列句尾加：「`LastFollowUpAt`、`NextFollowUpOn` 是依最新一筆跟進紀錄重算的快取（只由 FollowUpService 寫入），索引(`CompanyId`,`NextFollowUpOn`) 供下次聯絡排序與逾期篩選。」
3. `docs/database-schema.sql`：
   - `CREATE TABLE projects` 中 `ClosedAt` 那一行之後加：
```sql
  LastFollowUpAt  DATETIME(6)   NULL COMMENT '最新一筆跟進紀錄的發生時間（UTC）；由跟進紀錄重算',
  NextFollowUpOn  DATE          NULL COMMENT '下次聯絡日（台灣日期）；最新一筆未填或已按不用跟進時為 NULL',
```
   - 同表 `KEY IX_projects_CompanyId_OwnerUserId …` 之後加 `  KEY IX_projects_CompanyId_NextFollowUpOn (CompanyId, NextFollowUpOn),`
   - 「循環關聯」那一節的標題區塊之前新增一節（並把「循環關聯」的節號往後加一）：
```sql
-- =========================================================
-- 8. 專案跟進紀錄
-- =========================================================

CREATE TABLE project_followups (
  Id              CHAR(36)      NOT NULL COMMENT '跟進紀錄唯一識別碼',
  CompanyId       CHAR(36)      NOT NULL COMMENT '所屬公司識別碼',
  ProjectId       CHAR(36)      NOT NULL COMMENT '所屬專案識別碼',
  Type            VARCHAR(20)   NOT NULL COMMENT '類型（call / visit / meeting / email / message / other）',
  OccurredAt      DATETIME(6)   NOT NULL COMMENT '互動發生時間（UTC）',
  ContactId       CHAR(36)      NULL COMMENT '對象聯絡人識別碼；聯絡人刪除時設為 NULL',
  ContactName     VARCHAR(100)  NULL COMMENT '對象姓名快照',
  Summary         VARCHAR(200)  NOT NULL COMMENT '主旨',
  Content         TEXT          NULL COMMENT '內容',
  NextFollowUpOn  DATE          NULL COMMENT '下次聯絡日（台灣日期）',
  NextAction      VARCHAR(200)  NULL COMMENT '下次要做的事',
  NextDismissedAt DATETIME(6)   NULL COMMENT '按「不用跟進」的時間（UTC）；NULL 表示仍需提醒',
  NextDismissedBy CHAR(36)      NULL COMMENT '按「不用跟進」的使用者識別碼',
  CreatedBy       CHAR(36)      NOT NULL COMMENT '撰寫者使用者識別碼',
  CreatedAt       DATETIME(6)   NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedBy       CHAR(36)      NULL COMMENT '最後修改者使用者識別碼；未修改過為 NULL',
  UpdatedAt       DATETIME(6)   NOT NULL COMMENT '最後更新時間戳記（UTC）',
  IsDeleted       TINYINT(1)    NOT NULL DEFAULT 0 COMMENT '是否已軟刪除',
  DeletedAt       DATETIME(6)   NULL COMMENT '軟刪除時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_project_followups_CompanyId_ProjectId_IsDeleted_OccurredAt (CompanyId, ProjectId, IsDeleted, OccurredAt),
  KEY IX_project_followups_CompanyId_OccurredAt (CompanyId, OccurredAt),
  CONSTRAINT FK_project_followups_companies_CompanyId FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_project_followups_projects_ProjectId FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_project_followups_client_contacts_ContactId FOREIGN KEY (ContactId) REFERENCES client_contacts (Id) ON DELETE SET NULL,
  CONSTRAINT CK_project_followups_Type CHECK (Type IN ('call', 'visit', 'meeting', 'email', 'message', 'other'))
) ENGINE=InnoDB COMMENT='專案跟進紀錄（電話、拜訪、會議等客戶互動與下次聯絡日）';
```
   （索引與外鍵名稱以 Step 5 產生的 migration 為準，不一致時改成 migration 的名稱。）

- [ ] **Step 8: Commit**

```bash
git add apps/api/QuotePlatform.Api/Infrastructure apps/api/QuotePlatform.Api/Migrations docs/database-planning.md docs/database-schema.sql
git commit -m "feat: 新增專案跟進紀錄資料表與專案下次聯絡欄位

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `followups` 權限與既有角色的預設授權

**Files:**
- Modify: `apps/api/QuotePlatform.Api/Infrastructure/PermissionCatalog.cs`
- Modify: `apps/api/QuotePlatform.Api/Infrastructure/QuoteSeeder.cs:91-132`
- Modify: `apps/api/QuotePlatform.Api/Program.cs`（self-test 區塊）
- Modify: `apps/web/src/shared/types/permissions.ts`
- Modify: `docs/permissions-and-data-scope.md`

**Interfaces:**
- Produces: 權限代碼 `followups`（CRUD，無子功能，路由 `/followups`）；`QuoteSeeder.DefaultForExistingRole(string code, string roleCode, bool isSystem, RolePermission? projects)` → `PermissionAllowance?`；`QuoteSeeder.AssertExistingRoleDefaults()`。

- [ ] **Step 1: 先寫 self-test（會失敗）**

在 `QuoteSeeder.cs` 類別內（`GrantNewPermissionDefaultsAsync` 之後）加入：

```csharp
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
```

在 `Program.cs` 的 self-test 區塊 `PresenceTracker.AssertExamples();` 之後加 `QuoteSeeder.AssertExistingRoleDefaults();`。

- [ ] **Step 2: 確認失敗**

Run: `npm run build:api`
Expected: 編譯錯誤 `DefaultForExistingRole` 不存在。

- [ ] **Step 3: 權限目錄**

`PermissionCatalog.cs`：
1. `Definitions` 中 `projects` 那一行之後加：
```csharp
        new("followups", "跟進紀錄", "company", "business", "/followups", []),
```
2. `sales-manager` 與 `sales` 的權限清單中，`Allow("projects", …)` 之後各加 `Allow("followups", "CRUD"),`。
3. `viewer` 的陣列改為：
```csharp
            new[] { "dashboard", "office", "clients", "products", "projects", "followups", "quotes", "orders", "warranties", "invoices" }.Select(code => Allow(code, "R")).ToArray()),
```
4. `accountant` 不加。`company-admin` 由 `Grantable("company")` 自動取得。

- [ ] **Step 4: Seeder 一般化**

把 `QuoteSeeder.cs` 的 `ReadByDefaultForExistingRoles` 常數與 `GrantNewPermissionDefaultsAsync` 整段換成：

```csharp
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
```

注意：office 的標記在既有資料庫已存在，所以 office 不會重跑；新資料庫上 office 的行為與舊版相同（所有非完整存取的公司角色補 read）。

- [ ] **Step 5: 前端權限型別**

`apps/web/src/shared/types/permissions.ts` 的 `PermissionCode` 在 `| 'projects'` 之後加 `| 'followups'`。

- [ ] **Step 6: 建置與 self-test**

Run: `npm run build:api && npm run test:api && export PATH="$HOME/.nvm/versions/node/v24.15.0/bin:$PATH" && npm run check:web`
Expected: 0 警告、`self-test passed`、check:web 0 錯誤。

- [ ] **Step 7: 對共用資料庫跑 Seeder 並確認**

Run: `npm run dev:api:init`
Expected: 記錄檔出現「已記錄 followups 的預設授權，替 N 個既有角色開放。」（N ≥ 0）。重跑一次 `npm run dev:api:init`，這次不應再出現 followups 那一行（標記已存在）。

- [ ] **Step 8: 權限文件**

`docs/permissions-and-data-scope.md`：
1. 資源表格 `projects` 那一列之後加：
```markdown
| company | `followups` | `/followups` | — |
```
2. `/office` 說明段落（「既有公司角色（系統與自訂）由 Seeder 一次性補上 `office:read` …」）之後加一段：
```markdown
`followups` 跟進紀錄掛在專案下，可見範圍完全繼承專案（`ProjectAccessService.VisibleProjects`），沒有另外的規則：業務主管（部門樹）看得到自己部門與下層部門專案的紀錄，公司管理員看全部，業務看自己負責或參與的專案。判斷依據是專案所屬的部門，不是撰寫人的部門。寫入需要對應的 `followups` CRUD 加上專案寫入身分（負責人、成員或資料範圍內的主管），能寫專案的人可以修改、刪除任何人寫的紀錄；已結案專案只能讀。

既有角色的 `followups` 同樣由 Seeder 一次性補上（同一個 `permission.default_granted` 機制）：系統角色依下表（業務、業務主管 CRUD，唯讀 R，會計沒有）；自訂角色比照它對 `projects` 的 CRUD，沒有 `projects:read` 的不補。部署此版本時必須執行一次 Seeder。
```
3. 「### 3.3 內建角色」表格：`sales-manager`、`sales` 的權限摘要在「虛擬辦公室」之前加「跟進紀錄讀寫、」；`viewer` 維持「全部唯讀（含虛擬辦公室）」；`accountant` 不變。

- [ ] **Step 9: Commit**

```bash
git add apps/api/QuotePlatform.Api/Infrastructure/PermissionCatalog.cs apps/api/QuotePlatform.Api/Infrastructure/QuoteSeeder.cs apps/api/QuotePlatform.Api/Program.cs apps/web/src/shared/types/permissions.ts docs/permissions-and-data-scope.md
git commit -m "feat: 新增跟進紀錄權限並依角色補上預設授權

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 跟進紀錄的純規則與契約

**Files:**
- Create: `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpContracts.cs`
- Create: `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpRules.cs`
- Modify: `apps/api/QuotePlatform.Api/Program.cs`（self-test、`using`）

**Interfaces:**
- Produces（namespace `QuotePlatform.Api.Modules.FollowUps`）：
  - `FollowUpInput(string? Type, DateTimeOffset? OccurredAt, Guid? ContactId, string? ContactName, string? Summary, string? Content, DateOnly? NextFollowUpOn, string? NextAction)`
  - `FollowUpSearchQuery(string? Keyword, string? Type, Guid? CreatedBy, Guid? DepartmentId, Guid? ProjectId, DateOnly? From, DateOnly? To, int? Page, int? PageSize, string? Sort, bool? Desc)`
  - `FollowUpProjectState(DateTimeOffset? LastFollowUpAt, DateOnly? NextFollowUpOn, string? NextAction)`
  - `FollowUpUser(Guid Id, string? DisplayName)`
  - `FollowUpItem(Guid Id, Guid ProjectId, string Type, DateTimeOffset OccurredAt, Guid? ContactId, string? ContactName, string Summary, string? Content, DateOnly? NextFollowUpOn, string? NextAction, DateTimeOffset? NextDismissedAt, FollowUpUser CreatedBy, DateTimeOffset CreatedAt, FollowUpUser? UpdatedBy, DateTimeOffset UpdatedAt)`
  - `FollowUpRules.Validate(FollowUpInput, DateTimeOffset now)` → `string?`、`FollowUpRules.Clean(string?)` → `string?`、`FollowUpRules.TaiwanDate(DateTimeOffset)` → `DateOnly`、`FollowUpRules.FilterValues`、`FollowUpRules.InactiveProjectStatuses`（皆 `IReadOnlyList<string>`）、`FollowUpRules.AssertExamples()`
  - 擴充方法 `IQueryable<ProjectFollowUp>.LatestFirst()` → `IOrderedQueryable<ProjectFollowUp>`

- [ ] **Step 1: 契約**

`FollowUpContracts.cs`：

```csharp
namespace QuotePlatform.Api.Modules.FollowUps;

/// <summary>新增或修改跟進紀錄的輸入。</summary>
/// <param name="Type">類型（call、visit、meeting、email、message、other）。</param>
/// <param name="OccurredAt">互動發生時間，不可晚於現在。</param>
/// <param name="ContactId">對象聯絡人；必須屬於專案的客戶，選了就以聯絡人姓名為準。</param>
/// <param name="ContactName">對象姓名；沒有選聯絡人時使用（100 字內）。</param>
/// <param name="Summary">主旨（必填，200 字內）。</param>
/// <param name="Content">內容（4000 字內）。</param>
/// <param name="NextFollowUpOn">下次聯絡日（台灣日期），不可早於發生日期。</param>
/// <param name="NextAction">下次要做的事（200 字內）；沒有下次聯絡日時不儲存。</param>
public sealed record FollowUpInput(string? Type, DateTimeOffset? OccurredAt, Guid? ContactId, string? ContactName, string? Summary, string? Content, DateOnly? NextFollowUpOn, string? NextAction);

/// <summary>跨專案跟進紀錄總表的查詢條件；範圍一律限於目前使用者看得到的專案。</summary>
/// <param name="Keyword">關鍵字（主旨、內容、對象姓名、專案編號、專案名稱、客戶名稱）。</param>
/// <param name="Type">類型。</param>
/// <param name="CreatedBy">撰寫人使用者識別碼。</param>
/// <param name="DepartmentId">專案所屬部門（含下層部門）。</param>
/// <param name="ProjectId">指定專案。</param>
/// <param name="From">發生日期起（台灣日期，含當天）。</param>
/// <param name="To">發生日期迄（台灣日期，含當天）。</param>
/// <param name="Page">頁碼（預設 1）。</param>
/// <param name="PageSize">每頁筆數（預設 20，最多 200）。</param>
/// <param name="Sort">排序欄位：occurredAt（預設）或 createdAt。</param>
/// <param name="Desc">是否降冪（預設 true）。</param>
public sealed record FollowUpSearchQuery(string? Keyword, string? Type, Guid? CreatedBy, Guid? DepartmentId, Guid? ProjectId, DateOnly? From, DateOnly? To, int? Page, int? PageSize, string? Sort, bool? Desc);

/// <summary>專案目前的跟進狀態；每次寫入後回傳，畫面據此更新。</summary>
/// <param name="LastFollowUpAt">最新一筆的發生時間；沒有紀錄時為 null。</param>
/// <param name="NextFollowUpOn">下次聯絡日；沒有排定時為 null。</param>
/// <param name="NextAction">下次要做的事。</param>
public sealed record FollowUpProjectState(DateTimeOffset? LastFollowUpAt, DateOnly? NextFollowUpOn, string? NextAction);

/// <summary>人員摘要（撰寫人、修改人）。</summary>
/// <param name="Id">使用者識別碼。</param>
/// <param name="DisplayName">顯示名稱。</param>
public sealed record FollowUpUser(Guid Id, string? DisplayName);

/// <summary>一筆跟進紀錄的回傳內容。</summary>
/// <param name="Id">跟進紀錄識別碼。</param>
/// <param name="ProjectId">所屬專案識別碼。</param>
/// <param name="Type">類型。</param>
/// <param name="OccurredAt">發生時間。</param>
/// <param name="ContactId">對象聯絡人識別碼。</param>
/// <param name="ContactName">對象姓名。</param>
/// <param name="Summary">主旨。</param>
/// <param name="Content">內容。</param>
/// <param name="NextFollowUpOn">下次聯絡日。</param>
/// <param name="NextAction">下次要做的事。</param>
/// <param name="NextDismissedAt">按「不用跟進」的時間。</param>
/// <param name="CreatedBy">撰寫人。</param>
/// <param name="CreatedAt">建立時間。</param>
/// <param name="UpdatedBy">最後修改人；沒有修改過為 null。</param>
/// <param name="UpdatedAt">最後更新時間。</param>
public sealed record FollowUpItem(
    Guid Id,
    Guid ProjectId,
    string Type,
    DateTimeOffset OccurredAt,
    Guid? ContactId,
    string? ContactName,
    string Summary,
    string? Content,
    DateOnly? NextFollowUpOn,
    string? NextAction,
    DateTimeOffset? NextDismissedAt,
    FollowUpUser CreatedBy,
    DateTimeOffset CreatedAt,
    FollowUpUser? UpdatedBy,
    DateTimeOffset UpdatedAt);
```

- [ ] **Step 2: 先寫 self-test 與空殼（會失敗）**

`FollowUpRules.cs` 先放測試與會讓測試失敗的空實作：

```csharp
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.FollowUps;

/// <summary>跟進紀錄的純規則：輸入檢查、台灣日期換算與提醒範圍；不碰資料庫，供 <c>--self-test</c> 驗證。</summary>
public static class FollowUpRules
{
    /// <summary>主旨長度上限。</summary>
    public const int SummaryMaxLength = 200;

    /// <summary>內容長度上限。</summary>
    public const int ContentMaxLength = 4000;

    /// <summary>對象姓名長度上限。</summary>
    public const int ContactNameMaxLength = 100;

    /// <summary>下次要做的事長度上限。</summary>
    public const int NextActionMaxLength = 200;

    /// <summary>專案列表 <c>followUp</c> 篩選可用的值：pending 待跟進、overdue 已逾期、none 沒有下次聯絡。</summary>
    public static readonly IReadOnlyList<string> FilterValues = ["pending", "overdue", "none"];

    /// <summary>不算「待跟進」的專案狀態：未得標、已結案、已取消（日期保留，專案重新啟動後會再出現）。</summary>
    public static readonly IReadOnlyList<string> InactiveProjectStatuses = [ProjectStatuses.Lost, ProjectStatuses.Closed, ProjectStatuses.Cancelled];

    /// <summary>
    /// 檢查跟進紀錄輸入。
    /// </summary>
    /// <param name="input">輸入內容。</param>
    /// <param name="now">目前時間。</param>
    /// <returns>錯誤訊息；通過時為 null。</returns>
    public static string? Validate(FollowUpInput input, DateTimeOffset now) => null;

    /// <summary>
    /// 去除前後空白，空字串轉成 null。
    /// </summary>
    /// <param name="value">原始字串。</param>
    /// <returns>整理後的字串或 null。</returns>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 換算台灣日期。
    /// </summary>
    /// <param name="value">時間。</param>
    /// <returns>該時間在台灣的日期。</returns>
    public static DateOnly TaiwanDate(DateTimeOffset value) => DateOnly.FromDateTime(value.UtcDateTime);

    /// <summary>供 <c>--self-test</c> 驗證輸入檢查與台灣日期換算。</summary>
    public static void AssertExamples()
    {
        var now = new DateTimeOffset(2026, 11, 4, 2, 0, 0, TimeSpan.Zero);
        FollowUpInput Valid() => new("call", now.AddHours(-1), null, null, "確認規格", null, null, null);

        Check(Validate(Valid(), now) is null, "合法輸入應通過");
        Check(Validate(Valid() with { Type = "fax" }, now) == "請選擇跟進類型。", "不支援的類型");
        Check(Validate(Valid() with { Type = null }, now) == "請選擇跟進類型。", "沒有類型");
        Check(Validate(Valid() with { OccurredAt = null }, now) == "請輸入發生時間。", "沒有發生時間");
        Check(Validate(Valid() with { OccurredAt = now.AddMinutes(4) }, now) is null, "容許 5 分鐘時鐘誤差");
        Check(Validate(Valid() with { OccurredAt = now.AddMinutes(6) }, now) == "發生時間不可晚於現在。", "未來時間");
        Check(Validate(Valid() with { Summary = "  " }, now) == "請輸入主旨。", "空白主旨");
        Check(Validate(Valid() with { Summary = new string('字', 201) }, now) == "主旨不可超過 200 字。", "主旨過長");
        Check(Validate(Valid() with { Content = new string('字', 4001) }, now) == "內容不可超過 4000 字。", "內容過長");
        Check(Validate(Valid() with { ContactName = new string('字', 101) }, now) == "對象姓名不可超過 100 字。", "姓名過長");
        Check(Validate(Valid() with { NextAction = new string('字', 201) }, now) == "下次要做的事不可超過 200 字。", "下次要做的事過長");

        // UTC 11/3 17:00 是台灣 11/4 01:00：下次聯絡日以台灣日期比較，11/3 要擋下、11/4 可以。
        var lateNight = new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.Zero);
        Check(TaiwanDate(lateNight) == new DateOnly(2026, 11, 4), "台灣日期換算");
        Check(Validate(Valid() with { OccurredAt = lateNight, NextFollowUpOn = new DateOnly(2026, 11, 3) }, now) == "下次聯絡日不可早於發生日期。", "下次聯絡早於發生日");
        Check(Validate(Valid() with { OccurredAt = lateNight, NextFollowUpOn = new DateOnly(2026, 11, 4) }, now) is null, "下次聯絡同一天可以");
        Check(Clean("  ") is null && Clean(" 甲 ") == "甲", "Clean");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"FollowUpRules：{message}");
    }
}
```

`Program.cs`：`using QuotePlatform.Api.Modules.FollowUps;`（依字母順序放在 `Files` 之後），self-test 區塊 `QuoteSeeder.AssertExistingRoleDefaults();` 之後加 `FollowUpRules.AssertExamples();`。

- [ ] **Step 3: 確認失敗**

Run: `npm run build:api && npm run test:api`
Expected: 建置成功；self-test 拋出 `FollowUpRules：不支援的類型`（或第一個失敗的檢查）。

- [ ] **Step 4: 實作**

把 `Validate`、`TaiwanDate` 換成真正的實作，並在 `FollowUpRules.cs` 檔尾加入排序擴充：

```csharp
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public static string? Validate(FollowUpInput input, DateTimeOffset now)
    {
        if (input.Type is null || !FollowUpTypes.All.Contains(input.Type)) return "請選擇跟進類型。";
        if (input.OccurredAt is not DateTimeOffset occurredAt) return "請輸入發生時間。";
        // 容許使用者電腦與伺服器時鐘的小誤差，避免剛按下「現在」就被判定為未來時間。
        if (occurredAt > now + ClockSkew) return "發生時間不可晚於現在。";
        if (string.IsNullOrWhiteSpace(input.Summary)) return "請輸入主旨。";
        if (input.Summary.Trim().Length > SummaryMaxLength) return $"主旨不可超過 {SummaryMaxLength} 字。";
        if (input.Content?.Trim().Length > ContentMaxLength) return $"內容不可超過 {ContentMaxLength} 字。";
        if (input.ContactName?.Trim().Length > ContactNameMaxLength) return $"對象姓名不可超過 {ContactNameMaxLength} 字。";
        if (input.NextAction?.Trim().Length > NextActionMaxLength) return $"下次要做的事不可超過 {NextActionMaxLength} 字。";
        if (input.NextFollowUpOn is DateOnly next && next < TaiwanDate(occurredAt)) return "下次聯絡日不可早於發生日期。";
        return null;
    }

    public static DateOnly TaiwanDate(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(BusinessTime.Offset).DateTime);
```
（保留 Step 2 寫好的 XML Doc，只替換方法本體；`ClockSkew` 放在常數區。）

```csharp
/// <summary>跟進紀錄查詢的共用排序。</summary>
public static class FollowUpQueryExtensions
{
    /// <summary>
    /// 「最新一筆」的排序：發生時間新到舊，同時間再比建立時間。
    /// </summary>
    /// <param name="rows">跟進紀錄查詢。</param>
    /// <returns>排序後的查詢。</returns>
    /// <remarks>補登一筆較早的拜訪不會變成最新一筆。重算專案狀態、取下次要做的事與時間軸都必須用這個排序，三者才會一致。</remarks>
    public static IOrderedQueryable<ProjectFollowUp> LatestFirst(this IQueryable<ProjectFollowUp> rows) =>
        rows.OrderByDescending(value => value.OccurredAt).ThenByDescending(value => value.CreatedAt);
}
```

- [ ] **Step 5: 確認通過**

Run: `npm run build:api && npm run test:api`
Expected: 0 警告、`self-test passed`。

- [ ] **Step 6: Commit**

```bash
git add apps/api/QuotePlatform.Api/Modules/FollowUps apps/api/QuotePlatform.Api/Program.cs
git commit -m "feat: 新增跟進紀錄輸入規則與契約

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 專案內的跟進紀錄 API（讀、寫、不用跟進、聯絡人）

**Files:**
- Create: `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpService.cs`
- Create: `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpsController.cs`
- Modify: `apps/api/QuotePlatform.Api/Program.cs`（`builder.Services.AddScoped<FollowUpService>();`，放在 `OfficeService` 之後）
- Create: `tests/followups-smoke.mjs`
- Modify: `package.json`（`"test:smoke:followups": "node tests/followups-smoke.mjs"`，放在 `test:smoke:office` 之後）

**Interfaces:**
- Consumes: Task 1 的 entity、Task 3 的契約與 `FollowUpRules`、`LatestFirst()`；既有 `ProjectAccessService.GetAsync(scope, id, track, ct)` → `ProjectAccess?`、`ProjectAccess.CanWriteChild(scope, code, action = "update")`、`AuditService.Add(companyId, actorUserId, entityType, entityId, action, before, after)`。
- Produces（`FollowUpService`）：
  - `ListByProjectAsync(RequestScope, Guid projectId, string? type, ListQuery, CancellationToken)` → `ServiceResult<object>`
  - `ContactsAsync(RequestScope, Guid projectId, string? keyword, CancellationToken)` → `ServiceResult<object>`
  - `CreateAsync(RequestScope, Guid projectId, FollowUpInput, CancellationToken)`、`UpdateAsync(RequestScope, Guid id, FollowUpInput, CancellationToken)`、`DeleteAsync(RequestScope, Guid id, CancellationToken)`、`DismissNextAsync(RequestScope, Guid projectId, CancellationToken)` → `ServiceResult<object>`
  - `NextActionAsync(Guid projectId, CancellationToken)` → `Task<string?>`（Task 6 的專案明細會用）
  - 回應：寫入回 `{ item: FollowUpItem, project: FollowUpProjectState }`；刪除與不用跟進回 `{ project }`；列表回 `{ canCreate, canUpdate, canDelete, total, page, pageSize, items, project }`。

- [ ] **Step 1: 先寫 smoke 測試（前半）**

建立 `tests/followups-smoke.mjs`：

```js
/**
 * 專案跟進紀錄驗證：資料範圍與權限、最新一筆規則（補登、刪除倒回、不用跟進）、輸入檢查、已結案唯讀、
 * 專案內列表與跨專案總表的篩選分頁，以及專案列表的跟進狀態篩選與排序。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false 啟動，並已執行 Seeder（followups 權限已建立）。
 * 用法：node tests/followups-smoke.mjs [baseUrl]
 */
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5280';
const envText = await readFile(new URL('../.env.development-api.local', import.meta.url), 'utf8');
const seedPassword = envText.match(/^Quote__BootstrapAdminPassword=(.+)$/m)?.[1]?.trim();
assert.ok(seedPassword, '找不到 Quote__BootstrapAdminPassword');

const suffix = Date.now().toString(36);
let passed = 0;

async function call(method, path, { token, body } = {}) {
  const headers = {};
  if (token) headers.authorization = `Bearer ${token}`;
  if (body !== undefined) headers['content-type'] = 'application/json';
  const response = await fetch(`${baseUrl}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  return { status: response.status, data: text ? JSON.parse(text) : null };
}

async function login(account) {
  const result = await call('POST', '/api/auth/login', { body: { account, password: seedPassword } });
  assert.equal(result.status, 200, `${account} 登入失敗：${JSON.stringify(result.data)}`);
  return result.data.accessToken;
}

async function ok(promise, expected = [200, 201]) {
  const result = await promise;
  assert.ok(expected.includes(result.status), `${result.status} ${JSON.stringify(result.data)}`);
  return result.data;
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

// 台灣日期（offsetDays 天後）與 N 小時前的 ISO 時間
const taipeiDate = (offsetDays = 0) => new Date(Date.now() + 8 * 3600_000 + offsetDays * 86400_000).toISOString().slice(0, 10);
const hoursAgo = (hours) => new Date(Date.now() - hours * 3600_000).toISOString();
const sameInstant = (a, b) => Math.abs(Date.parse(a) - Date.parse(b)) < 1000;

const write = (token, project, body) => call('POST', `/api/projects/${project}/followups`, { token, body });
const base = (extra = {}) => ({ type: 'call', occurredAt: hoursAgo(2), summary: '確認規格', ...extra });

const platform = await login('admin');
let admin;
let manager;
let salesA;
let salesB;
let accountant;
let salesDept;
let teamDept;
let financeDept;
let otherClientContact;
let contactId;
let pA;
let pB;
let pF;
let r0;
let r1;
let r1At;
let rB;
let rBAuthor;
let rF;

await step('準備：新公司、業務部與下層一課、財務部、主管、兩位業務、會計', async () => {
  await ok(call('POST', '/api/platform/companies', { token: platform, body: { name: `Followups ${suffix}`, slug: `fu-${suffix}`, adminAccount: `fu-${suffix}`, adminDisplayName: '跟進管理員', adminPassword: seedPassword } }));
  admin = await login(`fu-${suffix}`);
  const root = (await ok(call('GET', '/api/admin/departments', { token: admin })))[0];
  salesDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: root.id, name: '業務部' } }))).id;
  teamDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: salesDept, name: '業務一課' } }))).id;
  financeDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: root.id, name: '財務部' } }))).id;
  const roles = await ok(call('GET', '/api/admin/roles', { token: admin }));
  const role = (code) => roles.find((value) => value.code === code).id;
  for (const [account, roleCode, departmentId, displayName] of [
    ['m', 'sales-manager', salesDept, '主管'],
    ['a', 'sales', teamDept, '業務A'],
    ['b', 'sales', teamDept, '業務B'],
    ['c', 'accountant', financeDept, '會計'],
  ]) {
    await ok(call('POST', '/api/admin/users', { token: admin, body: { account: `fu-${account}-${suffix}`, displayName, password: seedPassword, departmentId, roleId: role(roleCode) } }));
  }
  manager = await login(`fu-m-${suffix}`);
  salesA = await login(`fu-a-${suffix}`);
  salesB = await login(`fu-b-${suffix}`);
  accountant = await login(`fu-c-${suffix}`);
});

await step('準備：客戶（兩位聯絡人）、另一個客戶、三個專案（業務 A、業務 B、財務部）', async () => {
  // 客戶歸在業務一課：本人範圍的業務才選得到（客戶依所屬部門隔離）。
  const client = (await ok(call('POST', '/api/clients', { token: admin, body: { name: '跟進客戶', departmentId: teamDept, contacts: [{ name: '陳經理', title: '採購', isPrimary: true }, { name: '李工程師', isPrimary: false }] } }))).id;
  const other = (await ok(call('POST', '/api/clients', { token: admin, body: { name: '其他客戶', departmentId: teamDept, contacts: [{ name: '王小姐', isPrimary: true }] } }))).id;
  otherClientContact = (await ok(call('GET', `/api/clients/${other}`, { token: admin }))).contacts[0].id;
  pA = (await ok(call('POST', '/api/projects', { token: salesA, body: { name: `FU-A ${suffix}`, clientId: client } }))).id;
  pB = (await ok(call('POST', '/api/projects', { token: salesB, body: { name: `FU-B ${suffix}`, clientId: client } }))).id;
  pF = (await ok(call('POST', '/api/projects', { token: admin, body: { name: `FU-F ${suffix}`, clientId: client, departmentId: financeDept } }))).id;
});

await step('新增：選了聯絡人就以聯絡人姓名為準，回傳重算後的下次聯絡日', async () => {
  const contacts = await ok(call('GET', `/api/projects/${pA}/followups/contacts?keyword=${encodeURIComponent('陳')}`, { token: salesA }));
  assert.deepEqual(contacts.map((value) => value.name), ['陳經理']);
  contactId = contacts[0].id;
  const created = await ok(write(salesA, pA, base({ contactId, contactName: '亂打的名字', nextFollowUpOn: taipeiDate(3), nextAction: '寄修正版報價' })), [201]);
  r1 = created.item.id;
  r1At = created.item.occurredAt;
  assert.equal(created.item.contactName, '陳經理');
  assert.equal(created.item.createdBy.displayName, '業務A');
  assert.equal(created.item.updatedBy, null);
  assert.equal(created.project.nextFollowUpOn, taipeiDate(3));
  assert.equal(created.project.nextAction, '寄修正版報價');
  assert.ok(sameInstant(created.project.lastFollowUpAt, r1At));
});

await step('權限：會計沒有 followups（403）；看不到專案的業務 B 讀寫皆 404', async () => {
  assert.equal((await call('GET', `/api/projects/${pA}/followups`, { token: accountant })).status, 403);
  for (const result of [
    await call('GET', `/api/projects/${pA}/followups`, { token: salesB }),
    await call('GET', `/api/projects/${pA}/followups/contacts`, { token: salesB }),
    await write(salesB, pA, base()),
    await call('PUT', `/api/followups/${r1}`, { token: salesB, body: base() }),
    await call('DELETE', `/api/followups/${r1}`, { token: salesB }),
    await call('POST', `/api/projects/${pA}/followups/dismiss-next`, { token: salesB }),
  ]) {
    assert.equal(result.status, 404, JSON.stringify(result.data));
  }
});

await step('最新一筆：補登較早的紀錄不改變下次聯絡；新的一筆沒排定就清空，刪掉後倒回', async () => {
  const backfill = await ok(write(salesA, pA, base({ type: 'visit', occurredAt: hoursAgo(72), summary: `補登拜訪 ${suffix}`, nextFollowUpOn: taipeiDate(10) })), [201]);
  r0 = backfill.item.id;
  assert.equal(backfill.project.nextFollowUpOn, taipeiDate(3));
  const newer = await ok(write(salesA, pA, base({ type: 'meeting', occurredAt: hoursAgo(1), summary: '開會' })), [201]);
  assert.equal(newer.project.nextFollowUpOn, null);
  assert.equal(newer.project.nextAction, null);
  const removed = await ok(call('DELETE', `/api/followups/${newer.item.id}`, { token: salesA }));
  assert.equal(removed.project.nextFollowUpOn, taipeiDate(3));
  assert.equal(removed.project.nextAction, '寄修正版報價');
  assert.ok(sameInstant(removed.project.lastFollowUpAt, r1At));
});

await step('不用跟進：清掉下次聯絡、再按一次 400；主管修改日期後重新提醒', async () => {
  const dismissed = await ok(call('POST', `/api/projects/${pA}/followups/dismiss-next`, { token: salesA }));
  assert.equal(dismissed.project.nextFollowUpOn, null);
  const again = await call('POST', `/api/projects/${pA}/followups/dismiss-next`, { token: salesA });
  assert.equal(again.status, 400);
  assert.equal(again.data.message, '目前沒有排定下次聯絡。');
  // 主管（部門樹）不是撰寫人也能改：能寫專案的人都能改紀錄。
  const updated = await ok(call('PUT', `/api/followups/${r1}`, { token: manager, body: base({ occurredAt: r1At, contactId, summary: '確認規格（主管補充）', nextFollowUpOn: taipeiDate(5), nextAction: '寄修正版報價' }) }));
  assert.equal(updated.project.nextFollowUpOn, taipeiDate(5));
  assert.equal(updated.item.nextDismissedAt, null);
  assert.equal(updated.item.updatedBy.displayName, '主管');
  assert.equal(updated.item.createdBy.displayName, '業務A');
});

await step('輸入檢查：未來時間、下次聯絡早於發生日、別的客戶的聯絡人、空主旨、錯誤類型皆 400', async () => {
  const bad = async (body, text) => {
    const result = await write(salesA, pA, body);
    assert.equal(result.status, 400, JSON.stringify(result.data));
    assert.equal(result.data.message, text);
  };
  await bad(base({ occurredAt: new Date(Date.now() + 3600_000).toISOString() }), '發生時間不可晚於現在。');
  await bad(base({ occurredAt: hoursAgo(72), nextFollowUpOn: taipeiDate(-5) }), '下次聯絡日不可早於發生日期。');
  await bad(base({ contactId: otherClientContact }), '聯絡人不屬於此專案的客戶。');
  await bad(base({ summary: '  ' }), '請輸入主旨。');
  await bad(base({ type: 'fax' }), '請選擇跟進類型。');
});

await step('專案內列表：依最新一筆排序、關鍵字、類型、分頁與 total', async () => {
  const all = await ok(call('GET', `/api/projects/${pA}/followups`, { token: salesA }));
  assert.equal(all.total, 2);
  assert.deepEqual(all.items.map((value) => value.id), [r1, r0]);
  assert.equal(all.canCreate, true);
  assert.equal(all.project.nextFollowUpOn, taipeiDate(5));
  const byKeyword = await ok(call('GET', `/api/projects/${pA}/followups?keyword=${encodeURIComponent(`補登拜訪 ${suffix}`)}`, { token: salesA }));
  assert.deepEqual(byKeyword.items.map((value) => value.id), [r0]);
  const byType = await ok(call('GET', `/api/projects/${pA}/followups?type=visit`, { token: salesA }));
  assert.equal(byType.total, 1);
  const paged = await ok(call('GET', `/api/projects/${pA}/followups?page=2&pageSize=1`, { token: salesA }));
  assert.equal(paged.total, 2);
  assert.deepEqual(paged.items.map((value) => value.id), [r0]);
  assert.equal((await call('GET', `/api/projects/${pA}/followups?type=fax`, { token: salesA })).status, 400);
});

// TASK5_STEPS

console.log(`\n${passed} 項跟進紀錄驗證通過`);
```

`package.json` 加 `"test:smoke:followups": "node tests/followups-smoke.mjs",`。

- [ ] **Step 2: 確認失敗**

啟動測試 API（Task 0 的指令），等 5281 可連線後：
Run: `node tests/followups-smoke.mjs http://127.0.0.1:5281`
Expected: 「新增」那一步失敗（404，端點尚不存在）。停掉測試 API（`kill <PID>`）。

- [ ] **Step 3: 實作 Service**

`FollowUpService.cs`：

```csharp
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.FollowUps;

/// <summary>專案跟進紀錄：專案內列表、聯絡人、新增修改刪除、不用跟進，以及專案跟進狀態的重算。</summary>
/// <remarks>
/// 可見範圍一律經 <see cref="ProjectAccessService"/>，看不到專案回 404。
/// 專案的 LastFollowUpAt／NextFollowUpOn 只由 <see cref="SaveAndRecalculateAsync"/> 寫入。
/// </remarks>
public sealed class FollowUpService(QuoteDbContext db, ProjectAccessService access, AuditService audit)
{
    /// <summary>
    /// 查詢專案內的跟進紀錄（最新一筆在前）。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="type">類型篩選；null 表示全部。</param>
    /// <param name="query">關鍵字（主旨、內容、對象）與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>分頁結果、寫入旗標與專案目前的跟進狀態。</returns>
    public async Task<ServiceResult<object>> ListByProjectAsync(RequestScope scope, Guid projectId, string? type, ListQuery query, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        type = FollowUpRules.Clean(type);
        if (type is not null && !FollowUpTypes.All.Contains(type)) return ServiceResult<object>.BadRequest("跟進類型不正確。");

        var rows = db.ProjectFollowUps.AsNoTracking().Where(value => value.ProjectId == projectId);
        if (type is not null) rows = rows.Where(value => value.Type == type);
        if (query.KeywordText is string keyword)
        {
            rows = rows.Where(value => value.Summary.Contains(keyword)
                || (value.Content != null && value.Content.Contains(keyword))
                || (value.ContactName != null && value.ContactName.Contains(keyword)));
        }
        var total = await rows.CountAsync(cancellationToken);
        var items = await ToItems(rows.LatestFirst().Skip(query.Skip).Take(query.PageSizeValue)).ToListAsync(cancellationToken);
        var writable = project.Project.Status != ProjectStatuses.Closed;
        return ServiceResult<object>.Ok(new
        {
            canCreate = writable && project.CanWriteChild(scope, "followups", "create"),
            canUpdate = writable && project.CanWriteChild(scope, "followups"),
            canDelete = writable && project.CanWriteChild(scope, "followups", "delete"),
            total,
            page = query.PageValue,
            pageSize = query.PageSizeValue,
            items,
            project = await StateAsync(projectId, cancellationToken),
        });
    }

    /// <summary>
    /// 專案客戶的聯絡人，供表單下拉選單遠端查詢。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="keyword">比對姓名與職稱；null 表示不篩選。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>最多 20 筆聯絡人（id、name、title）。</returns>
    /// <remarks>不呼叫客戶 API：客戶依客戶部門判斷可見範圍，專案成員可能看得到專案卻看不到客戶。</remarks>
    public async Task<ServiceResult<object>> ContactsAsync(RequestScope scope, Guid projectId, string? keyword, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        var contacts = db.ClientContacts.AsNoTracking().Where(value => value.ClientId == project.Project.ClientId);
        if (FollowUpRules.Clean(keyword) is string text) contacts = contacts.Where(value => value.Name.Contains(text) || (value.Title != null && value.Title.Contains(text)));
        return ServiceResult<object>.Ok(await contacts
            .OrderByDescending(value => value.IsPrimary).ThenBy(value => value.SortOrder).ThenBy(value => value.Name)
            .Take(20)
            .Select(value => new { value.Id, value.Name, value.Title })
            .ToListAsync(cancellationToken));
    }

    /// <summary>
    /// 新增跟進紀錄並重算專案的跟進狀態。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="input">紀錄內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>201 與 <c>{ item, project }</c>。</returns>
    public async Task<ServiceResult<object>> CreateAsync(RequestScope scope, Guid projectId, FollowUpInput input, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (Denied(scope, project, "create") is { } denied) return denied;
        if (await ValidateAsync(project.Project, input, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);

        var row = new ProjectFollowUp { ProjectId = projectId, CreatedBy = scope.UserId };
        await ApplyAsync(row, input, cancellationToken);
        db.ProjectFollowUps.Add(row);
        audit.Add(scope.CompanyId, scope.UserId, "project_followup", row.Id, "create", after: Snapshot(row));
        var state = await SaveAndRecalculateAsync(projectId, cancellationToken);
        return ServiceResult<object>.Created(new { item = await ItemAsync(row.Id, cancellationToken), project = state });
    }

    /// <summary>
    /// 修改跟進紀錄並重算專案的跟進狀態；改了下次聯絡日會清掉「不用跟進」。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="id">跟進紀錄識別碼。</param>
    /// <param name="input">紀錄內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ item, project }</c>。</returns>
    public async Task<ServiceResult<object>> UpdateAsync(RequestScope scope, Guid id, FollowUpInput input, CancellationToken cancellationToken)
    {
        var row = await db.ProjectFollowUps.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null) return ServiceResult<object>.NotFound("找不到跟進紀錄。");
        var project = await access.GetAsync(scope, row.ProjectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到跟進紀錄。");
        if (Denied(scope, project, "update") is { } denied) return denied;
        if (await ValidateAsync(project.Project, input, cancellationToken) is string error) return ServiceResult<object>.BadRequest(error);

        var before = Snapshot(row);
        await ApplyAsync(row, input, cancellationToken);
        row.UpdatedBy = scope.UserId;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "project_followup", row.Id, "update", before, Snapshot(row));
        var state = await SaveAndRecalculateAsync(row.ProjectId, cancellationToken);
        return ServiceResult<object>.Ok(new { item = await ItemAsync(row.Id, cancellationToken), project = state });
    }

    /// <summary>
    /// 刪除（軟刪除）跟進紀錄；刪掉最新一筆時，狀態倒回前一筆。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="id">跟進紀錄識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ project }</c>。</returns>
    public async Task<ServiceResult<object>> DeleteAsync(RequestScope scope, Guid id, CancellationToken cancellationToken)
    {
        var row = await db.ProjectFollowUps.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null) return ServiceResult<object>.NotFound("找不到跟進紀錄。");
        var project = await access.GetAsync(scope, row.ProjectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到跟進紀錄。");
        if (Denied(scope, project, "delete") is { } denied) return denied;

        row.IsDeleted = true;
        row.DeletedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "project_followup", row.Id, "delete", Snapshot(row));
        return ServiceResult<object>.Ok(new { project = await SaveAndRecalculateAsync(row.ProjectId, cancellationToken) });
    }

    /// <summary>
    /// 「不用跟進」：清掉最新一筆的下次聯絡日。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ project }</c>；目前沒有排定時回 400。</returns>
    public async Task<ServiceResult<object>> DismissNextAsync(RequestScope scope, Guid projectId, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (Denied(scope, project, "update") is { } denied) return denied;

        var latest = await db.ProjectFollowUps.Where(value => value.ProjectId == projectId).LatestFirst().FirstOrDefaultAsync(cancellationToken);
        if (latest is not { NextFollowUpOn: not null, NextDismissedAt: null }) return ServiceResult<object>.BadRequest("目前沒有排定下次聯絡。");
        latest.NextDismissedAt = DateTimeOffset.UtcNow;
        latest.NextDismissedBy = scope.UserId;
        audit.Add(scope.CompanyId, scope.UserId, "project_followup", latest.Id, "dismiss_next", after: new { latest.NextFollowUpOn });
        return ServiceResult<object>.Ok(new { project = await SaveAndRecalculateAsync(projectId, cancellationToken) });
    }

    /// <summary>
    /// 取得專案目前的「下次要做的事」。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>最新一筆的下次要做的事；呼叫端應只在專案的 NextFollowUpOn 有值時使用。</returns>
    public Task<string?> NextActionAsync(Guid projectId, CancellationToken cancellationToken) =>
        db.ProjectFollowUps.AsNoTracking().Where(value => value.ProjectId == projectId).LatestFirst().Select(value => value.NextAction).FirstOrDefaultAsync(cancellationToken);

    private static ServiceResult<object>? Denied(RequestScope scope, ProjectAccess project, string action)
    {
        if (!project.CanWriteChild(scope, "followups", action)) return ServiceResult<object>.Forbidden("您對此專案的跟進紀錄只有檢視權限。");
        return project.Project.Status == ProjectStatuses.Closed ? ServiceResult<object>.BadRequest("已結案的專案不可修改。") : null;
    }

    private async Task<string?> ValidateAsync(Project project, FollowUpInput input, CancellationToken cancellationToken)
    {
        if (FollowUpRules.Validate(input, DateTimeOffset.UtcNow) is string error) return error;
        if (input.ContactId is Guid contactId && !await db.ClientContacts.AnyAsync(value => value.Id == contactId && value.ClientId == project.ClientId, cancellationToken))
        {
            return "聯絡人不屬於此專案的客戶。";
        }
        return null;
    }

    private async Task ApplyAsync(ProjectFollowUp row, FollowUpInput input, CancellationToken cancellationToken)
    {
        row.Type = input.Type!;
        row.OccurredAt = input.OccurredAt!.Value.ToUniversalTime();
        row.ContactId = input.ContactId;
        // 選了聯絡人就存它當下的姓名：聯絡人日後改名或刪除，紀錄仍保留當時的對象。
        row.ContactName = input.ContactId is Guid contactId
            ? await db.ClientContacts.Where(value => value.Id == contactId).Select(value => value.Name).SingleAsync(cancellationToken)
            : FollowUpRules.Clean(input.ContactName);
        row.Summary = input.Summary!.Trim();
        row.Content = FollowUpRules.Clean(input.Content);
        // 下次聯絡日改了代表重新排定，先前按的「不用跟進」不再適用。
        if (row.NextFollowUpOn != input.NextFollowUpOn)
        {
            row.NextDismissedAt = null;
            row.NextDismissedBy = null;
        }
        row.NextFollowUpOn = input.NextFollowUpOn;
        row.NextAction = input.NextFollowUpOn is null ? null : FollowUpRules.Clean(input.NextAction);
    }

    /// <summary>寫入紀錄後在同一個交易內重算專案的跟進狀態。</summary>
    /// <remarks>
    /// 重算是單一 UPDATE 加子查詢：InnoDB 對 UPDATE 內的子查詢做加鎖讀取，同一專案兩人同時寫入時，
    /// 後到的交易會等前一筆提交並讀到它，不會算出過期的狀態。子查詢的排序必須與 LatestFirst 相同。
    /// </remarks>
    private async Task<FollowUpProjectState> SaveAndRecalculateAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var latest = db.ProjectFollowUps.Where(value => value.ProjectId == projectId).LatestFirst();
        await db.Projects.Where(value => value.Id == projectId).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.LastFollowUpAt, value => latest.Select(row => (DateTimeOffset?)row.OccurredAt).FirstOrDefault())
            .SetProperty(value => value.NextFollowUpOn, value => latest.Select(row => row.NextDismissedAt == null ? row.NextFollowUpOn : null).FirstOrDefault()),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await StateAsync(projectId, cancellationToken);
    }

    private async Task<FollowUpProjectState> StateAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking().Where(value => value.Id == projectId).Select(value => new { value.LastFollowUpAt, value.NextFollowUpOn }).SingleAsync(cancellationToken);
        // NextFollowUpOn 有值代表最新一筆的下次聯絡仍有效，下次要做的事就取那一筆。
        var nextAction = project.NextFollowUpOn is null ? null : await NextActionAsync(projectId, cancellationToken);
        return new FollowUpProjectState(project.LastFollowUpAt, project.NextFollowUpOn, nextAction);
    }

    private Task<FollowUpItem> ItemAsync(Guid id, CancellationToken cancellationToken) =>
        ToItems(db.ProjectFollowUps.AsNoTracking().Where(value => value.Id == id)).SingleAsync(cancellationToken);

    /// <summary>轉成回傳格式；撰寫人與修改人的姓名以子查詢取得（users 不受公司過濾）。</summary>
    internal IQueryable<FollowUpItem> ToItems(IQueryable<ProjectFollowUp> rows) => rows.Select(value => new FollowUpItem(
        value.Id,
        value.ProjectId,
        value.Type,
        value.OccurredAt,
        value.ContactId,
        value.ContactName,
        value.Summary,
        value.Content,
        value.NextFollowUpOn,
        value.NextAction,
        value.NextDismissedAt,
        new FollowUpUser(value.CreatedBy, db.Users.Where(user => user.Id == value.CreatedBy).Select(user => user.DisplayName).FirstOrDefault()),
        value.CreatedAt,
        value.UpdatedBy == null ? null : new FollowUpUser(value.UpdatedBy.Value, db.Users.Where(user => user.Id == value.UpdatedBy).Select(user => user.DisplayName).FirstOrDefault()),
        value.UpdatedAt));

    private static object Snapshot(ProjectFollowUp row) => new
    {
        row.Type,
        row.OccurredAt,
        row.ContactId,
        row.ContactName,
        row.Summary,
        row.Content,
        row.NextFollowUpOn,
        row.NextAction,
        row.NextDismissedAt,
    };
}
```

若 Pomelo 無法翻譯 `ExecuteUpdateAsync` 中的子查詢（執行時拋出 translation 例外），**停下來回報**，不要改用 SQL 字串，也不要改成先查再寫的兩段式。

- [ ] **Step 4: 實作 Controller**

`FollowUpsController.cs`：

```csharp
using Microsoft.AspNetCore.Mvc;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.FollowUps;

/// <summary>
/// 專案跟進紀錄控制器（公司範圍）。紀錄的可見範圍完全繼承所屬專案，看不到專案一律回 404。
/// </summary>
/// <param name="service">跟進紀錄業務服務。</param>
[Route("api")]
public sealed class FollowUpsController(FollowUpService service) : ScopedControllerBase
{
    /// <summary>
    /// 查詢專案內的跟進紀錄（最新一筆在前）。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="type">類型篩選（call、visit、meeting、email、message、other）。</param>
    /// <param name="query">關鍵字與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>分頁結果、寫入旗標與專案目前的跟進狀態。</returns>
    [HttpGet("projects/{projectId:guid}/followups")]
    public async Task<ActionResult> ListByProject(Guid projectId, [FromQuery] string? type, [FromQuery] ListQuery query, CancellationToken cancellationToken) =>
        TryCompany("followups", "read", out var scope) ? ToResult(await service.ListByProjectAsync(scope, projectId, type, query, cancellationToken)) : Forbidden();

    /// <summary>
    /// 專案客戶的聯絡人（表單下拉選單用）。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="keyword">比對姓名與職稱。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>最多 20 筆聯絡人。</returns>
    [HttpGet("projects/{projectId:guid}/followups/contacts")]
    public async Task<ActionResult> Contacts(Guid projectId, [FromQuery] string? keyword, CancellationToken cancellationToken) =>
        TryCompany("followups", "read", out var scope) ? ToResult(await service.ContactsAsync(scope, projectId, keyword, cancellationToken)) : Forbidden();

    /// <summary>
    /// 新增跟進紀錄。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="input">紀錄內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>201 與新紀錄、專案重算後的跟進狀態。</returns>
    [HttpPost("projects/{projectId:guid}/followups")]
    public async Task<ActionResult> Create(Guid projectId, FollowUpInput input, CancellationToken cancellationToken) =>
        TryCompany("followups", "create", out var scope) ? ToResult(await service.CreateAsync(scope, projectId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 修改跟進紀錄。
    /// </summary>
    /// <param name="id">跟進紀錄識別碼。</param>
    /// <param name="input">紀錄內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>修改後的紀錄與專案重算後的跟進狀態。</returns>
    [HttpPut("followups/{id:guid}")]
    public async Task<ActionResult> Update(Guid id, FollowUpInput input, CancellationToken cancellationToken) =>
        TryCompany("followups", "update", out var scope) ? ToResult(await service.UpdateAsync(scope, id, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除跟進紀錄。
    /// </summary>
    /// <param name="id">跟進紀錄識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>專案重算後的跟進狀態。</returns>
    [HttpDelete("followups/{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        TryCompany("followups", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, id, cancellationToken)) : Forbidden();

    /// <summary>
    /// 不用跟進：清掉專案目前的下次聯絡日。
    /// </summary>
    /// <param name="projectId">專案識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>專案重算後的跟進狀態。</returns>
    [HttpPost("projects/{projectId:guid}/followups/dismiss-next")]
    public async Task<ActionResult> DismissNext(Guid projectId, CancellationToken cancellationToken) =>
        TryCompany("followups", "update", out var scope) ? ToResult(await service.DismissNextAsync(scope, projectId, cancellationToken)) : Forbidden();
}
```

`Program.cs` 註冊：`builder.Services.AddScoped<FollowUpService>();`。

- [ ] **Step 5: 建置並跑 smoke**

Run: `npm run build:api && npm run test:api`，啟動測試 API，再 `node tests/followups-smoke.mjs http://127.0.0.1:5281`
Expected: 0 警告；smoke 前 8 步全部 ✓。完成後 `kill <PID>`。

- [ ] **Step 6: 回歸**

測試 API 啟動中執行：`node tests/projects-smoke.mjs http://127.0.0.1:5281 && node tests/documents-smoke.mjs http://127.0.0.1:5281`
Expected: 全部通過。結束後 `kill <PID>`。

- [ ] **Step 7: Commit**

```bash
git add apps/api/QuotePlatform.Api/Modules/FollowUps apps/api/QuotePlatform.Api/Program.cs tests/followups-smoke.mjs package.json
git commit -m "feat: 新增專案跟進紀錄 API 與下次聯絡重算

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 跨專案總表 API

**Files:**
- Modify: `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpService.cs`（新增 `SearchAsync`）
- Modify: `apps/api/QuotePlatform.Api/Modules/FollowUps/FollowUpsController.cs`（新增 `GET api/followups`）
- Modify: `tests/followups-smoke.mjs`（把 `// TASK5_STEPS` 換成本任務的步驟；Task 6 的步驟接在後面）

**Interfaces:**
- Consumes: Task 4 的 `ToItems`、`LatestFirst()`；`ProjectAccessService.VisibleProjects(scope)`。
- Produces: `FollowUpService.SearchAsync(RequestScope, FollowUpSearchQuery, CancellationToken)` → `ServiceResult<object>`，回 `{ total, page, pageSize, items }`；每筆含 `FollowUpItem` 的所有欄位，另加 `project: { id, projectNo, name, status, nextFollowUpOn }` 與 `clientName`。

- [ ] **Step 1: 先寫 smoke 步驟**

把 `tests/followups-smoke.mjs` 的 `// TASK5_STEPS` 換成：

```js
await step('準備：業務 B 的逾期紀錄、財務部專案的紀錄', async () => {
  const created = await ok(write(salesB, pB, base({ occurredAt: hoursAgo(120), summary: '催回覆', nextFollowUpOn: taipeiDate(-2) })), [201]);
  rB = created.item.id;
  rBAuthor = created.item.createdBy.id;
  rF = (await ok(write(admin, pF, base({ summary: `財務部的紀錄 ${suffix}` })), [201])).item.id;
});

await step('總表：主管看得到部屬（部門樹）的紀錄、看不到財務部；業務只看到自己的；管理員看全部', async () => {
  const managerAll = await ok(call('GET', '/api/followups', { token: manager }));
  assert.equal(managerAll.total, 3);
  assert.ok(managerAll.items.every((value) => value.project.id !== pF));
  assert.equal((await ok(call('GET', '/api/followups', { token: salesA }))).total, 2);
  const adminAll = await ok(call('GET', '/api/followups', { token: admin }));
  assert.equal(adminAll.total, 4);
  assert.equal(adminAll.items[0].clientName, '跟進客戶');
  assert.equal(adminAll.items.find((value) => value.id === r1).project.nextFollowUpOn, taipeiDate(5));
  assert.equal((await call('GET', '/api/followups', { token: accountant })).status, 403);
});

await step('總表：部門（含下層）、撰寫人、關鍵字、日期區間、分頁與排序', async () => {
  const ids = async (query) => (await ok(call('GET', `/api/followups?${query}`, { token: admin }))).items.map((value) => value.id);
  assert.deepEqual(await ids(`departmentId=${financeDept}`), [rF]);
  assert.equal((await ok(call('GET', `/api/followups?departmentId=${salesDept}`, { token: admin }))).total, 3, '部門篩選含下層部門');
  assert.deepEqual(await ids(`createdBy=${rBAuthor}`), [rB]);
  assert.deepEqual(await ids(`keyword=${encodeURIComponent(`FU-B ${suffix}`)}`), [rB], '關鍵字比對專案名稱');
  assert.deepEqual(await ids(`projectId=${pA}`), [r1, r0]);
  assert.deepEqual(await ids(`from=${taipeiDate(-4)}&to=${taipeiDate(-2)}`), [r0]);
  const paged = await ok(call('GET', '/api/followups?page=2&pageSize=3', { token: admin }));
  assert.equal(paged.total, 4);
  assert.deepEqual(paged.items.map((value) => value.id), [rB], '依發生時間新到舊，最舊的在最後一頁');
  assert.equal((await ids('sort=occurredAt&desc=false'))[0], rB);
  assert.equal((await call('GET', `/api/followups?from=${taipeiDate()}&to=${taipeiDate(-1)}`, { token: admin })).status, 400);
});

// TASK6_STEPS
```

- [ ] **Step 2: 確認失敗**

啟動測試 API，`node tests/followups-smoke.mjs http://127.0.0.1:5281`
Expected: 「總表」那一步失敗（`GET /api/followups` 404 或 405）。`kill <PID>`。

- [ ] **Step 3: 實作 `SearchAsync`**

加到 `FollowUpService`（放在 `ListByProjectAsync` 之後）：

```csharp
    /// <summary>
    /// 跨專案跟進紀錄總表：目前使用者看得到的專案內的所有紀錄。
    /// </summary>
    /// <param name="scope">目前使用者範圍。</param>
    /// <param name="query">篩選、排序與分頁條件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns><c>{ total, page, pageSize, items }</c>；每筆附專案摘要與客戶名稱。</returns>
    /// <remarks>部門篩選含下層部門，且仍在資料範圍內（先套 VisibleProjects 再篩部門），與 Dashboard 相同。</remarks>
    public async Task<ServiceResult<object>> SearchAsync(RequestScope scope, FollowUpSearchQuery query, CancellationToken cancellationToken)
    {
        if (query.From is DateOnly from && query.To is DateOnly to && from > to) return ServiceResult<object>.BadRequest("起始日期不可晚於結束日期。");
        var type = FollowUpRules.Clean(query.Type);
        if (type is not null && !FollowUpTypes.All.Contains(type)) return ServiceResult<object>.BadRequest("跟進類型不正確。");

        var projects = access.VisibleProjects(scope);
        if (query.DepartmentId is Guid departmentId)
        {
            projects = projects.Where(project => db.DepartmentClosures.Any(path => path.AncestorId == departmentId && path.DescendantId == project.DepartmentId));
        }
        if (query.ProjectId is Guid projectId) projects = projects.Where(project => project.Id == projectId);

        var rows = db.ProjectFollowUps.AsNoTracking().Where(value => projects.Any(project => project.Id == value.ProjectId));
        if (type is not null) rows = rows.Where(value => value.Type == type);
        if (query.CreatedBy is Guid createdBy) rows = rows.Where(value => value.CreatedBy == createdBy);
        if (query.From is DateOnly fromDate)
        {
            var fromAt = BusinessTime.StartOfDay(fromDate);
            rows = rows.Where(value => value.OccurredAt >= fromAt);
        }
        if (query.To is DateOnly toDate)
        {
            var toAt = BusinessTime.StartOfDay(toDate.AddDays(1));
            rows = rows.Where(value => value.OccurredAt < toAt);
        }
        if (FollowUpRules.Clean(query.Keyword) is string keyword)
        {
            rows = rows.Where(value => value.Summary.Contains(keyword)
                || (value.Content != null && value.Content.Contains(keyword))
                || (value.ContactName != null && value.ContactName.Contains(keyword))
                || db.Projects.Any(project => project.Id == value.ProjectId
                    && (project.ProjectNo.Contains(keyword) || project.Name.Contains(keyword) || project.Client.Name.Contains(keyword))));
        }

        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 200);
        var page = Math.Max(query.Page ?? 1, 1);
        var total = await rows.CountAsync(cancellationToken);
        var sorted = (query.Sort == "createdAt", query.Desc ?? true) switch
        {
            (true, true) => rows.OrderByDescending(value => value.CreatedAt).ThenByDescending(value => value.Id),
            (true, false) => rows.OrderBy(value => value.CreatedAt).ThenBy(value => value.Id),
            (false, false) => rows.OrderBy(value => value.OccurredAt).ThenBy(value => value.CreatedAt),
            _ => rows.LatestFirst(),
        };
        var items = await ToItems(sorted.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);

        var projectIds = items.Select(value => value.ProjectId).Distinct().ToList();
        var info = await db.Projects.AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .Select(project => new { project.Id, project.ProjectNo, project.Name, project.Status, project.NextFollowUpOn, ClientName = project.Client.Name })
            .ToDictionaryAsync(value => value.Id, cancellationToken);

        return ServiceResult<object>.Ok(new
        {
            total,
            page,
            pageSize,
            items = items.Select(item =>
            {
                var project = info.GetValueOrDefault(item.ProjectId);
                return new
                {
                    item.Id,
                    item.ProjectId,
                    item.Type,
                    item.OccurredAt,
                    item.ContactId,
                    item.ContactName,
                    item.Summary,
                    item.Content,
                    item.NextFollowUpOn,
                    item.NextAction,
                    item.NextDismissedAt,
                    item.CreatedBy,
                    item.CreatedAt,
                    item.UpdatedBy,
                    item.UpdatedAt,
                    project = project is null ? null : new { project.Id, project.ProjectNo, project.Name, project.Status, project.NextFollowUpOn },
                    clientName = project?.ClientName,
                };
            }),
        });
    }
```

- [ ] **Step 4: Controller 端點**（放在類別最前面）

```csharp
    /// <summary>
    /// 跨專案跟進紀錄總表（目前資料範圍內的所有專案）。
    /// </summary>
    /// <param name="query">關鍵字、類型、撰寫人、部門、專案、日期區間、排序與分頁。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回 { total, page, pageSize, items }。</returns>
    [HttpGet("followups")]
    public async Task<ActionResult> Search([FromQuery] FollowUpSearchQuery query, CancellationToken cancellationToken) =>
        TryCompany("followups", "read", out var scope) ? ToResult(await service.SearchAsync(scope, query, cancellationToken)) : Forbidden();
```

- [ ] **Step 5: 建置並跑 smoke**

Run: `npm run build:api`，啟動測試 API，`node tests/followups-smoke.mjs http://127.0.0.1:5281`
Expected: 0 警告；前 11 步全部 ✓。`kill <PID>`。

- [ ] **Step 6: Commit**

```bash
git add apps/api/QuotePlatform.Api/Modules/FollowUps tests/followups-smoke.mjs
git commit -m "feat: 新增跨專案跟進紀錄總表 API

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 專案 API 的跟進欄位、篩選與排序

**Files:**
- Modify: `apps/api/QuotePlatform.Api/Modules/Projects/ProjectsController.cs`（`ProjectSearchQuery` 加 `FollowUp`）
- Modify: `apps/api/QuotePlatform.Api/Modules/Projects/ProjectService.cs`
- Modify: `tests/followups-smoke.mjs`（把 `// TASK6_STEPS` 換成本任務的步驟）

**Interfaces:**
- Consumes: `FollowUpRules.FilterValues`、`FollowUpRules.InactiveProjectStatuses`、`FollowUpService.NextActionAsync`。
- Produces: `GET /api/projects` 支援 `followUp=pending|overdue|none`、`sort=nextFollowUpOn|lastFollowUpAt`，每列多 `lastFollowUpAt`、`nextFollowUpOn`；`GET /api/projects/{id}` 多 `lastFollowUpAt`、`nextFollowUpOn`、`nextAction`。沒有 `followups:read` 時欄位為 null、篩選與這兩個排序被忽略。

- [ ] **Step 1: 先寫 smoke 步驟**

把 `// TASK6_STEPS` 換成：

```js
await step('專案明細與列表：沒有 followups:read 的會計看到的跟進欄位為 null', async () => {
  const detail = await ok(call('GET', `/api/projects/${pA}`, { token: accountant }));
  assert.equal(detail.nextFollowUpOn, null);
  assert.equal(detail.nextAction, null);
  const list = await ok(call('GET', `/api/projects?keyword=${encodeURIComponent(`FU-A ${suffix}`)}`, { token: accountant }));
  assert.equal(list.items[0].nextFollowUpOn, null);
  assert.equal(list.items[0].lastFollowUpAt, null);
  const mine = await ok(call('GET', `/api/projects/${pA}`, { token: salesA }));
  assert.equal(mine.nextFollowUpOn, taipeiDate(5));
  assert.equal(mine.nextAction, '寄修正版報價');
});

const projectIds = async (query, token = admin) => (await ok(call('GET', `/api/projects?keyword=${encodeURIComponent(suffix)}&${query}`, { token }))).items.map((value) => value.id);

await step('專案列表：已逾期、待跟進、沒有下次聯絡的篩選，與下次聯絡排序（未排定排最後）', async () => {
  assert.deepEqual(await projectIds('followUp=overdue'), [pB]);
  assert.deepEqual((await projectIds('followUp=pending')).sort(), [pA, pB].sort());
  assert.deepEqual(await projectIds('followUp=none'), [pF]);
  assert.deepEqual(await projectIds('sort=nextFollowUpOn&desc=false'), [pB, pA, pF]);
  assert.deepEqual(await projectIds('sort=nextFollowUpOn&desc=true'), [pA, pB, pF]);
  assert.equal((await call('GET', '/api/projects?followUp=bogus', { token: admin })).status, 400);
  assert.equal((await projectIds('followUp=overdue', accountant)).length, 3, '沒有 followups:read 時忽略跟進篩選');
});

await step('提醒範圍：未得標後不算待跟進但日期保留；已結案的專案只能讀', async () => {
  await ok(call('POST', `/api/projects/${pB}/status`, { token: salesB, body: { status: 'quoting' } }));
  await ok(call('POST', `/api/projects/${pB}/status`, { token: salesB, body: { status: 'lost', reason: '價格' } }));
  assert.deepEqual(await projectIds('followUp=overdue'), []);
  assert.deepEqual(await projectIds('followUp=pending'), [pA]);
  assert.equal((await ok(call('GET', `/api/projects/${pB}`, { token: salesB }))).nextFollowUpOn, taipeiDate(-2));
  await ok(call('POST', `/api/projects/${pB}/status`, { token: admin, body: { status: 'closed' } }));
  const closed = await write(admin, pB, base());
  assert.equal(closed.status, 400);
  assert.equal(closed.data.message, '已結案的專案不可修改。');
  assert.equal((await call('PUT', `/api/followups/${rB}`, { token: admin, body: base() })).status, 400);
  const list = await ok(call('GET', `/api/projects/${pB}/followups`, { token: admin }));
  assert.equal(list.canCreate, false);
  assert.equal(list.canUpdate, false);
  assert.equal(list.total, 1);
});
```

- [ ] **Step 2: 確認失敗**

啟動測試 API，`node tests/followups-smoke.mjs http://127.0.0.1:5281`
Expected: 「專案明細與列表」那一步失敗（`detail.nextFollowUpOn` 為 undefined，不等於 null）。`kill <PID>`。

- [ ] **Step 3: 查詢參數**

`ProjectsController.cs`：`ProjectSearchQuery` 的參數列尾端（`bool? Desc` 之後）加 `string? FollowUp`，XML Doc 加：
```csharp
/// <param name="FollowUp">跟進狀態篩選（pending 待跟進、overdue 已逾期、none 沒有下次聯絡）；沒有 followups:read 時忽略。</param>
```
（撰寫計畫時已用 `grep -rn "new ProjectSearchQuery" apps/api` 確認沒有其他程式以建構子建立它；若建置出現建構子參數錯誤，補上 `null`。）

- [ ] **Step 4: `ProjectService` 改動**

1. 建構子加入 `FollowUpService followUps`：
```csharp
public sealed class ProjectService(QuoteDbContext db, ProjectAccessService access, ClientAccessService clientAccess, NumberSequenceService numbers, AuditService audit, FollowUpService followUps)
```
並加 `using QuotePlatform.Api.Modules.FollowUps;`。
2. `SortFields` 改為：
```csharp
    private static readonly string[] SortFields = ["createdAt", "projectNo", "name", "startDate", "endDate", "expectedAmount", "contractAmount", "status", "nextFollowUpOn", "lastFollowUpAt"];
    private static readonly string[] FollowUpSortFields = ["nextFollowUpOn", "lastFollowUpAt"];
```
3. `SearchAsync` 開頭（狀態檢查之後）加：
```csharp
        // 沒有 followups:read 的人不能藉由篩選或排序推敲出跟進紀錄，所以直接忽略這些參數。
        var canReadFollowUps = scope.Has("followups", "read");
        var followUp = canReadFollowUps ? FollowUpRules.Clean(query.FollowUp) : null;
        if (followUp is not null && !FollowUpRules.FilterValues.Contains(followUp)) return ServiceResult<object>.BadRequest("跟進狀態不正確。");
        var sort = canReadFollowUps || !FollowUpSortFields.Contains(query.Sort) ? query.Sort : null;
```
4. `var filtered = ApplyFilters(access.VisibleProjects(scope).AsNoTracking(), scope, query);` 改為：
```csharp
        var filtered = ApplyFollowUpFilter(ApplyFilters(access.VisibleProjects(scope).AsNoTracking(), scope, query), followUp);
```
5. `Sort(filtered, query.Sort, query.Desc ?? true)` 改為 `Sort(filtered, sort, query.Desc ?? true)`。
6. 列表投影 `memberCount = value.Members.Count,` 之後加：
```csharp
                lastFollowUpAt = canReadFollowUps ? value.LastFollowUpAt : null,
                nextFollowUpOn = canReadFollowUps ? value.NextFollowUpOn : null,
```
7. `Sort` 方法的 switch 在 `("status", true)` 之後加：
```csharp
            // 沒有排定的專案不論升降冪都排在最後。
            ("nextFollowUpOn", false) => projects.OrderBy(value => value.NextFollowUpOn == null).ThenBy(value => value.NextFollowUpOn),
            ("nextFollowUpOn", true) => projects.OrderBy(value => value.NextFollowUpOn == null).ThenByDescending(value => value.NextFollowUpOn),
            ("lastFollowUpAt", false) => projects.OrderBy(value => value.LastFollowUpAt == null).ThenBy(value => value.LastFollowUpAt),
            ("lastFollowUpAt", true) => projects.OrderBy(value => value.LastFollowUpAt == null).ThenByDescending(value => value.LastFollowUpAt),
```
8. `Sort` 方法之後新增：
```csharp
    /// <summary>跟進狀態篩選；待跟進不含未得標、結案、取消的專案，逾期以台灣時間的今天為準。</summary>
    private static IQueryable<Project> ApplyFollowUpFilter(IQueryable<Project> projects, string? followUp)
    {
        var inactive = FollowUpRules.InactiveProjectStatuses.ToList();
        var today = BusinessTime.Today;
        return followUp switch
        {
            "pending" => projects.Where(value => value.NextFollowUpOn != null && !inactive.Contains(value.Status)),
            "overdue" => projects.Where(value => value.NextFollowUpOn < today && !inactive.Contains(value.Status)),
            "none" => projects.Where(value => value.NextFollowUpOn == null),
            _ => projects,
        };
    }
```
9. `GetAsync`：在 `var nextStatuses = …` 之前加：
```csharp
        var canReadFollowUps = scope.Has("followups", "read");
        var nextAction = canReadFollowUps && project.NextFollowUpOn is not null ? await followUps.NextActionAsync(projectId, cancellationToken) : null;
```
   回傳物件 `project.ClosedAt,` 之後加：
```csharp
            lastFollowUpAt = canReadFollowUps ? project.LastFollowUpAt : null,
            nextFollowUpOn = canReadFollowUps ? project.NextFollowUpOn : null,
            nextAction,
```

- [ ] **Step 5: 建置並跑 smoke 與回歸**

Run: `npm run build:api && npm run test:api`，啟動測試 API：
```bash
node tests/followups-smoke.mjs http://127.0.0.1:5281
node tests/projects-smoke.mjs http://127.0.0.1:5281
node tests/api-smoke.mjs http://127.0.0.1:5281
```
Expected: 0 警告；跟進 smoke 14 步全部 ✓、其他兩個通過。`kill <PID>`。

- [ ] **Step 6: Commit**

```bash
git add apps/api/QuotePlatform.Api/Modules/Projects tests/followups-smoke.mjs
git commit -m "feat: 專案列表新增下次聯絡欄位、跟進狀態篩選與排序

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: 前端服務與型別

**Files:**
- Create: `apps/web/src/shared/services/followup-service.ts`
- Modify: `apps/web/src/shared/services/business-service.ts`（`ProjectSearchQuery`、`ProjectRow`、`ProjectDetail`）

**Interfaces:**
- Produces（`followup-service.ts`）：型別 `FollowUpType`、`FollowUp`、`FollowUpUser`、`FollowUpProjectState`、`FollowUpInput`、`ProjectFollowUpList`、`FollowUpRow`、`FollowUpSearchQuery`、`FollowUpFilter`、`FollowUpContact`、`FollowUpDue`；常數 `FOLLOW_UP_TYPES`、`FOLLOW_UP_FILTERS`；函式 `followUpTypeLabel`、`taipeiToday`、`followUpDue`、`listFollowUpsApi`、`listProjectFollowUpsApi`、`listFollowUpContactsApi`、`createFollowUpApi`、`updateFollowUpApi`、`deleteFollowUpApi`、`dismissNextFollowUpApi`。

- [ ] **Step 1: 建立服務檔**

```ts
/**
 * @file followup-service.ts
 * 專案跟進紀錄 API 與顯示用工具。
 *
 * 跟進紀錄一律掛在專案下，可見範圍與專案相同。專案的「下次聯絡」只看最新一筆，
 * 每次寫入後由後端重算並回傳 `project`，畫面用它更新，不自行推算。
 */
import { apiRequest } from '@/shared/api/http-client';

/** 跟進類型代碼 */
export type FollowUpType = 'call' | 'visit' | 'meeting' | 'email' | 'message' | 'other';

/** 跟進類型選項；順序即畫面上的按鈕順序 */
export const FOLLOW_UP_TYPES: { value: FollowUpType; label: string }[] = [
  { value: 'call', label: '電話' },
  { value: 'visit', label: '拜訪' },
  { value: 'meeting', label: '會議' },
  { value: 'email', label: 'Email' },
  { value: 'message', label: '訊息' },
  { value: 'other', label: '其他' },
];

/** 專案列表的跟進狀態篩選值 */
export type FollowUpFilter = 'pending' | 'overdue' | 'none';

/** 專案列表的跟進狀態篩選選項 */
export const FOLLOW_UP_FILTERS: { value: FollowUpFilter; label: string }[] = [
  { value: 'pending', label: '待跟進' },
  { value: 'overdue', label: '已逾期' },
  { value: 'none', label: '沒有下次聯絡' },
];

/** 不算「待跟進」的專案狀態，與後端 FollowUpRules.InactiveProjectStatuses 相同 */
const INACTIVE_PROJECT_STATUSES = ['lost', 'closed', 'cancelled'];

/** 人員摘要 */
export interface FollowUpUser {
  /** 使用者識別碼 */
  id: string;
  /** 顯示名稱 */
  displayName: string | null;
}

/** 一筆跟進紀錄 */
export interface FollowUp {
  /** 跟進紀錄識別碼 */
  id: string;
  /** 所屬專案識別碼 */
  projectId: string;
  /** 類型 */
  type: FollowUpType;
  /** 發生時間（ISO 8601） */
  occurredAt: string;
  /** 對象聯絡人識別碼 */
  contactId: string | null;
  /** 對象姓名 */
  contactName: string | null;
  /** 主旨 */
  summary: string;
  /** 內容 */
  content: string | null;
  /** 下次聯絡日（YYYY-MM-DD） */
  nextFollowUpOn: string | null;
  /** 下次要做的事 */
  nextAction: string | null;
  /** 按「不用跟進」的時間 */
  nextDismissedAt: string | null;
  /** 撰寫人 */
  createdBy: FollowUpUser;
  /** 建立時間 */
  createdAt: string;
  /** 最後修改人；沒有修改過為 null */
  updatedBy: FollowUpUser | null;
  /** 最後更新時間 */
  updatedAt: string;
}

/** 專案目前的跟進狀態（後端依最新一筆重算） */
export interface FollowUpProjectState {
  /** 最新一筆的發生時間 */
  lastFollowUpAt: string | null;
  /** 下次聯絡日（YYYY-MM-DD）；沒有排定為 null */
  nextFollowUpOn: string | null;
  /** 下次要做的事 */
  nextAction: string | null;
}

/** 新增或修改跟進紀錄的輸入 */
export type FollowUpInput = {
  type: FollowUpType;
  occurredAt: string;
  contactId: string | null;
  contactName: string | null;
  summary: string;
  content: string | null;
  nextFollowUpOn: string | null;
  nextAction: string | null;
};

/** 專案內列表結果 */
export interface ProjectFollowUpList {
  /** 是否可新增 */
  canCreate: boolean;
  /** 是否可修改（含「不用跟進」） */
  canUpdate: boolean;
  /** 是否可刪除 */
  canDelete: boolean;
  /** 總筆數 */
  total: number;
  /** 目前頁碼 */
  page: number;
  /** 每頁筆數 */
  pageSize: number;
  /** 本頁紀錄（最新一筆在前） */
  items: FollowUp[];
  /** 專案目前的跟進狀態 */
  project: FollowUpProjectState;
}

/** 跨專案總表的一列 */
export interface FollowUpRow extends FollowUp {
  /** 所屬專案摘要 */
  project: { id: string; projectNo: string; name: string; status: string; nextFollowUpOn: string | null } | null;
  /** 客戶名稱 */
  clientName: string | null;
}

/** 跨專案總表的查詢條件；全部由後端處理 */
export type FollowUpSearchQuery = {
  keyword?: string;
  type?: string;
  createdBy?: string;
  departmentId?: string;
  projectId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
  sort?: 'occurredAt' | 'createdAt';
  desc?: boolean;
};

/** 表單下拉選單用的聯絡人 */
export interface FollowUpContact {
  /** 聯絡人識別碼 */
  id: string;
  /** 姓名 */
  name: string;
  /** 職稱 */
  title: string | null;
}

/** 下次聯絡的提醒狀態 */
export interface FollowUpDue {
  /** none 沒有排定、inactive 專案已停止、overdue 逾期、today 今天、upcoming 之後 */
  state: 'none' | 'inactive' | 'overdue' | 'today' | 'upcoming';
  /** 與今天相差的天數（逾期為負數） */
  days: number;
}

/**
 * 取得跟進類型的中文名稱
 *
 * @param type 類型代碼
 * @returns 中文名稱；未知代碼原樣回傳
 */
export function followUpTypeLabel(type: string): string {
  return FOLLOW_UP_TYPES.find((option) => option.value === type)?.label ?? type;
}

/**
 * 台灣時間的今天（與後端 BusinessTime.Today 相同的判斷基準）
 *
 * @returns YYYY-MM-DD
 */
export function taipeiToday(): string {
  return new Date(Date.now() + 8 * 3600_000).toISOString().slice(0, 10);
}

/**
 * 計算下次聯絡的提醒狀態（與後端專案列表的待跟進、逾期規則一致）
 *
 * @param nextFollowUpOn 下次聯絡日（YYYY-MM-DD）
 * @param projectStatus 專案狀態
 * @returns 提醒狀態與相差天數
 */
export function followUpDue(nextFollowUpOn: string | null | undefined, projectStatus: string | null | undefined): FollowUpDue {
  if (!nextFollowUpOn) return { state: 'none', days: 0 };
  const days = Math.round((Date.parse(nextFollowUpOn) - Date.parse(taipeiToday())) / 86400_000);
  if (INACTIVE_PROJECT_STATUSES.includes(projectStatus ?? '')) return { state: 'inactive', days };
  return { state: days < 0 ? 'overdue' : days === 0 ? 'today' : 'upcoming', days };
}

/**
 * 查詢跨專案跟進紀錄總表
 *
 * @param query 篩選、排序與分頁條件
 * @returns 分頁結果
 */
export function listFollowUpsApi(query: FollowUpSearchQuery): Promise<{ total: number; page: number; pageSize: number; items: FollowUpRow[] }> {
  return apiRequest('/api/followups', { query: { ...query } });
}

/**
 * 查詢專案內的跟進紀錄
 *
 * @param projectId 專案識別碼
 * @param query 關鍵字、類型與分頁
 * @returns 分頁結果、寫入旗標與專案目前的跟進狀態
 */
export function listProjectFollowUpsApi(
  projectId: string,
  query: { keyword?: string; type?: string; page?: number; pageSize?: number } = {}
): Promise<ProjectFollowUpList> {
  return apiRequest(`/api/projects/${projectId}/followups`, { query: { ...query } });
}

/**
 * 查詢專案客戶的聯絡人（由後端依關鍵字搜尋，最多 20 筆）
 *
 * @param projectId 專案識別碼
 * @param keyword 姓名或職稱關鍵字
 * @returns 聯絡人清單
 */
export function listFollowUpContactsApi(projectId: string, keyword = ''): Promise<FollowUpContact[]> {
  return apiRequest(`/api/projects/${projectId}/followups/contacts`, { query: { keyword: keyword || undefined } });
}

/**
 * 新增跟進紀錄
 *
 * @param projectId 專案識別碼
 * @param input 紀錄內容
 * @returns 新紀錄與專案重算後的跟進狀態
 */
export function createFollowUpApi(projectId: string, input: FollowUpInput): Promise<{ item: FollowUp; project: FollowUpProjectState }> {
  return apiRequest(`/api/projects/${projectId}/followups`, { method: 'POST', body: input });
}

/**
 * 修改跟進紀錄
 *
 * @param id 跟進紀錄識別碼
 * @param input 紀錄內容
 * @returns 修改後的紀錄與專案重算後的跟進狀態
 */
export function updateFollowUpApi(id: string, input: FollowUpInput): Promise<{ item: FollowUp; project: FollowUpProjectState }> {
  return apiRequest(`/api/followups/${id}`, { method: 'PUT', body: input });
}

/**
 * 刪除跟進紀錄
 *
 * @param id 跟進紀錄識別碼
 * @returns 專案重算後的跟進狀態
 */
export function deleteFollowUpApi(id: string): Promise<{ project: FollowUpProjectState }> {
  return apiRequest(`/api/followups/${id}`, { method: 'DELETE' });
}

/**
 * 不用跟進：清掉專案目前的下次聯絡日
 *
 * @param projectId 專案識別碼
 * @returns 專案重算後的跟進狀態
 */
export function dismissNextFollowUpApi(projectId: string): Promise<{ project: FollowUpProjectState }> {
  return apiRequest(`/api/projects/${projectId}/followups/dismiss-next`, { method: 'POST' });
}
```

- [ ] **Step 2: 專案型別**

`business-service.ts`：
1. 檔案頂端 import 加 `import type { FollowUpFilter } from '@/shared/services/followup-service';`
2. `ProjectSearchQuery` 在 `desc?: boolean;` 之後加 `followUp?: FollowUpFilter;`
3. `ProjectRow` 在 `memberCount: number;` 之後加：
```ts
  /** 最新一筆跟進紀錄的發生時間；沒有 followups:read 時為 null */
  lastFollowUpAt: string | null;
  /** 下次聯絡日（YYYY-MM-DD）；沒有排定或沒有 followups:read 時為 null */
  nextFollowUpOn: string | null;
```
4. `ProjectDetail` 在 `closedAt: string | null;` 之後加：
```ts
  /** 最新一筆跟進紀錄的發生時間；沒有 followups:read 時為 null */
  lastFollowUpAt: string | null;
  /** 下次聯絡日（YYYY-MM-DD）；沒有排定或沒有 followups:read 時為 null */
  nextFollowUpOn: string | null;
  /** 下次要做的事 */
  nextAction: string | null;
```

- [ ] **Step 3: 型別檢查**

Run: `export PATH="$HOME/.nvm/versions/node/v24.15.0/bin:$PATH" && npm run check:web`
Expected: 0 錯誤。若 `apiRequest` 的 `query` 型別不接受，確認傳入的是 `{ ...query }` 物件字面值（`type` 別名，不是 `interface`）。

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/shared/services
git commit -m "feat: 新增前端跟進紀錄服務與專案跟進欄位型別

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 專案詳細頁的「跟進紀錄」頁籤與表單

**Files:**
- Create: `apps/web/src/pages/projects/detail/_components/ProjectFollowUpFormModal.vue`
- Create: `apps/web/src/pages/projects/detail/_components/ProjectFollowUpsTab.vue`
- Modify: `apps/web/src/pages/projects/detail/_components/ProjectDetailView.vue`

**Interfaces:**
- Consumes: Task 7 的服務與型別；`ProjectDetail`（含 `nextFollowUpOn`、`nextAction`、`lastFollowUpAt`）；vue-query 快取鍵 `['project', projectId]`（`ProjectDetailView` 既有）。
- Produces: `ProjectFollowUpFormModal` props `{ open: boolean; projectId: string; record: FollowUp | null }`、emits `close`、`saved(state: FollowUpProjectState)`；`ProjectFollowUpsTab` props `{ project: ProjectDetail }`；專案詳細頁支援網址參數 `tab=followups`。

- [ ] **Step 1: 表單視窗**

`ProjectFollowUpFormModal.vue`：

```vue
<script setup lang="ts">
/**
 * @component ProjectFollowUpFormModal
 * 新增／編輯一筆跟進紀錄。對象可從專案客戶的聯絡人選（由後端依關鍵字搜尋），也可直接輸入姓名。
 * 前端檢查只是提示，最終以 API 的驗證為準。
 */
import { reactive, ref, watch } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { message } from 'ant-design-vue';
import {
  createFollowUpApi,
  FOLLOW_UP_TYPES,
  listFollowUpContactsApi,
  updateFollowUpApi,
  type FollowUp,
  type FollowUpInput,
  type FollowUpProjectState,
  type FollowUpType,
} from '@/shared/services/followup-service';

const props = defineProps<{ open: boolean; projectId: string; record: FollowUp | null }>();
const emit = defineEmits<{ close: []; saved: [state: FollowUpProjectState] }>();

const form = reactive({
  type: 'call' as FollowUpType,
  occurredAt: dayjs() as Dayjs,
  contactId: null as string | null,
  contactName: '' as string | undefined,
  summary: '',
  content: '',
  nextFollowUpOn: null as Dayjs | null,
  nextAction: '',
});
const saving = ref(false);
const contactOptions = ref<{ value: string; label: string; contactId: string }[]>([]);
const quickNext = [
  { label: '明天', days: 1 },
  { label: '3 天後', days: 3 },
  { label: '下週', days: 7 },
  { label: '2 週後', days: 14 },
];
let contactTimer: ReturnType<typeof setTimeout> | undefined;

watch(
  () => props.open,
  (open) => {
    if (!open) return;
    const row = props.record;
    Object.assign(form, {
      type: row?.type ?? 'call',
      occurredAt: row ? dayjs(row.occurredAt) : dayjs(),
      contactId: row?.contactId ?? null,
      contactName: row?.contactName ?? '',
      summary: row?.summary ?? '',
      content: row?.content ?? '',
      nextFollowUpOn: row?.nextFollowUpOn ? dayjs(row.nextFollowUpOn) : null,
      nextAction: row?.nextAction ?? '',
    });
    searchContacts('');
  }
);

// 聯絡人由後端依關鍵字查詢，不在前端過濾整份名單。
function searchContacts(keyword: string) {
  clearTimeout(contactTimer);
  contactTimer = setTimeout(async () => {
    try {
      const contacts = await listFollowUpContactsApi(props.projectId, keyword);
      contactOptions.value = contacts.map((contact) => ({
        value: contact.name,
        label: contact.title ? `${contact.name}（${contact.title}）` : contact.name,
        contactId: contact.id,
      }));
    } catch {
      contactOptions.value = [];
    }
  }, 250);
}

// 自己輸入的姓名就不是聯絡人；只有從選單選到的才帶 contactId。
function onContactSearch(keyword: string) {
  form.contactId = null;
  searchContacts(keyword);
}

function onContactSelect(_value: unknown, option: unknown) {
  form.contactId = (option as { contactId: string }).contactId;
}

function onContactChange(value: unknown) {
  if (!value) form.contactId = null;
}

function setNext(days: number) {
  form.nextFollowUpOn = dayjs().add(days, 'day');
}

async function save() {
  if (!form.summary.trim()) {
    message.warning('請輸入主旨');
    return;
  }
  saving.value = true;
  try {
    const input: FollowUpInput = {
      type: form.type,
      occurredAt: form.occurredAt.toISOString(),
      contactId: form.contactId,
      contactName: (form.contactName ?? '').trim() || null,
      summary: form.summary.trim(),
      content: form.content.trim() || null,
      nextFollowUpOn: form.nextFollowUpOn?.format('YYYY-MM-DD') ?? null,
      nextAction: form.nextFollowUpOn ? form.nextAction.trim() || null : null,
    };
    const result = props.record ? await updateFollowUpApi(props.record.id, input) : await createFollowUpApi(props.projectId, input);
    message.success('跟進紀錄已儲存');
    emit('saved', result.project);
    emit('close');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <a-modal :open="open" :title="record ? '編輯跟進紀錄' : '新增跟進紀錄'" :confirm-loading="saving" ok-text="儲存" width="600px" @ok="save" @cancel="emit('close')">
    <a-form layout="vertical">
      <a-form-item label="類型" required>
        <a-radio-group v-model:value="form.type" button-style="solid">
          <a-radio-button v-for="option in FOLLOW_UP_TYPES" :key="option.value" :value="option.value">{{ option.label }}</a-radio-button>
        </a-radio-group>
      </a-form-item>
      <a-row :gutter="12">
        <a-col :xs="24" :sm="12">
          <a-form-item label="發生時間" required>
            <a-date-picker
              v-model:value="form.occurredAt"
              show-time
              format="YYYY-MM-DD HH:mm"
              :allow-clear="false"
              :disabled-date="(day: Dayjs) => day.isAfter(dayjs(), 'day')"
              style="width: 100%"
            />
          </a-form-item>
        </a-col>
        <a-col :xs="24" :sm="12">
          <a-form-item label="對象">
            <a-auto-complete
              v-model:value="form.contactName"
              :options="contactOptions"
              :filter-option="false"
              allow-clear
              placeholder="選聯絡人或輸入姓名"
              @search="onContactSearch"
              @select="onContactSelect"
              @change="onContactChange"
            />
          </a-form-item>
        </a-col>
      </a-row>
      <a-form-item label="主旨" required>
        <a-input v-model:value="form.summary" :maxlength="200" show-count placeholder="例如：確認規格、報價後追蹤" />
      </a-form-item>
      <a-form-item label="內容">
        <a-textarea v-model:value="form.content" :maxlength="4000" show-count :auto-size="{ minRows: 3, maxRows: 10 }" />
      </a-form-item>
      <a-form-item label="下次聯絡日">
        <a-space wrap>
          <a-date-picker v-model:value="form.nextFollowUpOn" :disabled-date="(day: Dayjs) => day.isBefore(form.occurredAt, 'day')" />
          <a-button v-for="item in quickNext" :key="item.days" size="small" @click="setNext(item.days)">{{ item.label }}</a-button>
        </a-space>
      </a-form-item>
      <a-form-item label="下次要做的事">
        <a-input v-model:value="form.nextAction" :maxlength="200" :disabled="!form.nextFollowUpOn" placeholder="例如：寄修正版報價、確認交期" />
      </a-form-item>
    </a-form>
  </a-modal>
</template>
```

- [ ] **Step 2: 頁籤**

`ProjectFollowUpsTab.vue`：

```vue
<script setup lang="ts">
/**
 * @component ProjectFollowUpsTab
 * 專案的跟進紀錄：上方「下次聯絡」卡片（只看最新一筆），下方時間軸。
 * 搜尋、類型篩選與分頁都由後端處理；「載入更多」向 API 取下一頁。
 * 寫入後以後端回傳的專案跟進狀態更新專案明細快取，頂部資訊與卡片一起更新。
 */
import { computed, reactive, ref, type Component } from 'vue';
import dayjs from 'dayjs';
import { message, Modal } from 'ant-design-vue';
import { useInfiniteQuery, useQueryClient } from '@tanstack/vue-query';
import { CarOutlined, FileTextOutlined, MailOutlined, MessageOutlined, PhoneOutlined, TeamOutlined } from '@ant-design/icons-vue';
import type { ProjectDetail } from '@/shared/services/business-service';
import {
  deleteFollowUpApi,
  dismissNextFollowUpApi,
  FOLLOW_UP_TYPES,
  followUpDue,
  followUpTypeLabel,
  listProjectFollowUpsApi,
  type FollowUp,
  type FollowUpProjectState,
} from '@/shared/services/followup-service';
import ProjectFollowUpFormModal from './ProjectFollowUpFormModal.vue';

const props = defineProps<{ project: ProjectDetail }>();
const queryClient = useQueryClient();
const filters = reactive({ keyword: '', type: undefined as string | undefined });
const keywordInput = ref('');

const list = useInfiniteQuery({
  queryKey: ['followups', props.project.id, filters],
  queryFn: ({ pageParam }) =>
    listProjectFollowUpsApi(props.project.id, { keyword: filters.keyword || undefined, type: filters.type, page: pageParam, pageSize: 20 }),
  initialPageParam: 1,
  getNextPageParam: (last) => (last.page * last.pageSize < last.total ? last.page + 1 : undefined),
});
const pages = computed(() => list.data.value?.pages ?? []);
const items = computed(() => pages.value.flatMap((page) => page.items));
const first = computed(() => pages.value[0]);
const due = computed(() => followUpDue(props.project.nextFollowUpOn, props.project.status));
const typeIcons: Record<string, Component> = {
  call: PhoneOutlined,
  visit: CarOutlined,
  meeting: TeamOutlined,
  email: MailOutlined,
  message: MessageOutlined,
  other: FileTextOutlined,
};

async function applyState(state: FollowUpProjectState) {
  queryClient.setQueryData<ProjectDetail>(['project', props.project.id], (old) => (old ? { ...old, ...state } : old));
  await queryClient.invalidateQueries({ queryKey: ['followups', props.project.id] });
}

const formOpen = ref(false);
const editing = ref<FollowUp | null>(null);
function open(row: FollowUp | null) {
  editing.value = row;
  formOpen.value = true;
}

function dismiss() {
  Modal.confirm({
    title: '不用再跟進這個專案？',
    content: '會清掉目前的下次聯絡日；之後寫新的紀錄可以再排定。',
    onOk: async () => {
      try {
        await applyState((await dismissNextFollowUpApi(props.project.id)).project);
        message.success('已清除下次聯絡');
      } catch (error) {
        message.error((error as Error).message);
      }
    },
  });
}

function remove(row: FollowUp) {
  Modal.confirm({
    title: `刪除「${row.summary}」？`,
    okType: 'danger',
    onOk: async () => {
      try {
        await applyState((await deleteFollowUpApi(row.id)).project);
        message.success('已刪除');
      } catch (error) {
        message.error((error as Error).message);
      }
    },
  });
}

function search() {
  filters.keyword = keywordInput.value.trim();
}
</script>

<template>
  <a-space direction="vertical" :size="16" style="width: 100%">
    <a-card size="small" :class="['next', due.state]">
      <template v-if="project.nextFollowUpOn">
        <div class="next-head">
          <span class="muted">下次聯絡</span>
          <strong>{{ project.nextFollowUpOn }}</strong>
          <a-tag v-if="due.state === 'overdue'" color="red">逾期 {{ -due.days }} 天</a-tag>
          <a-tag v-else-if="due.state === 'today'" color="orange">今天</a-tag>
          <a-tag v-else-if="due.state === 'inactive'">專案已停止，不列入待跟進</a-tag>
          <a-button v-if="first?.canUpdate" size="small" class="push" @click="dismiss">不用跟進</a-button>
        </div>
        <div v-if="project.nextAction" class="next-action">{{ project.nextAction }}</div>
      </template>
      <span v-else class="muted">目前沒有排定下次聯絡</span>
    </a-card>

    <div class="toolbar">
      <a-space wrap>
        <a-input-search v-model:value="keywordInput" placeholder="主旨、內容、對象" allow-clear style="width: 240px" @search="search" />
        <a-select v-model:value="filters.type" :options="FOLLOW_UP_TYPES" allow-clear placeholder="全部類型" style="width: 120px" />
      </a-space>
      <a-button v-if="first?.canCreate" type="primary" @click="open(null)">新增紀錄</a-button>
    </div>

    <a-skeleton v-if="list.isLoading.value" active />
    <a-empty v-else-if="items.length === 0" description="還沒有跟進紀錄" />
    <a-timeline v-else>
      <a-timeline-item v-for="row in items" :key="row.id">
        <template #dot><component :is="typeIcons[row.type] ?? FileTextOutlined" /></template>
        <div class="row-head">
          <a-tag>{{ followUpTypeLabel(row.type) }}</a-tag>
          <span>{{ dayjs(row.occurredAt).format('YYYY-MM-DD HH:mm') }}</span>
          <span v-if="row.contactName" class="muted">· {{ row.contactName }}</span>
          <span class="muted">· {{ row.createdBy.displayName ?? '—' }}</span>
          <a-space v-if="first?.canUpdate || first?.canDelete" class="push" :size="0">
            <a-button v-if="first?.canUpdate" type="link" size="small" @click="open(row)">編輯</a-button>
            <a-button v-if="first?.canDelete" type="link" size="small" danger @click="remove(row)">刪除</a-button>
          </a-space>
        </div>
        <div class="summary">{{ row.summary }}</div>
        <div v-if="row.content" class="content">{{ row.content }}</div>
        <div v-if="row.nextFollowUpOn" class="muted">
          下次聯絡：{{ row.nextFollowUpOn }}<template v-if="row.nextAction">　{{ row.nextAction }}</template>
          <template v-if="row.nextDismissedAt">（已設為不用跟進）</template>
        </div>
        <div v-if="row.updatedBy" class="muted">由 {{ row.updatedBy.displayName ?? '—' }} 修改於 {{ dayjs(row.updatedAt).format('YYYY-MM-DD HH:mm') }}</div>
      </a-timeline-item>
    </a-timeline>
    <div v-if="list.hasNextPage.value" class="more">
      <a-button :loading="list.isFetchingNextPage.value" @click="list.fetchNextPage()">載入更多（共 {{ first?.total }} 筆）</a-button>
    </div>
  </a-space>

  <ProjectFollowUpFormModal :open="formOpen" :project-id="project.id" :record="editing" @close="formOpen = false" @saved="applyState" />
</template>

<style scoped>
.next.overdue {
  border-color: #ffa39e;
  background: #fff1f0;
}
.next.today {
  border-color: #ffd591;
  background: #fff7e6;
}
.next-head,
.row-head {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.next-action {
  margin-top: 4px;
}
.push {
  margin-left: auto;
}
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.summary {
  font-weight: 500;
  margin-top: 4px;
}
.content {
  white-space: pre-wrap;
  margin-top: 2px;
}
.muted {
  color: #888;
}
.more {
  text-align: center;
}
</style>
```

- [ ] **Step 3: 掛到專案詳細頁**

`ProjectDetailView.vue`：
1. import 加：
```ts
import ProjectFollowUpsTab from './ProjectFollowUpsTab.vue';
import { followUpDue } from '@/shared/services/followup-service';
```
2. `const projectId = …` 之後加：
```ts
// 總表的連結會帶 tab=followups，直接開到跟進紀錄頁籤。
const activeTab = ref(new URLSearchParams(window.location.search).get('tab') ?? 'members');
const due = computed(() => followUpDue(p.value?.nextFollowUpOn, p.value?.status));
```
3. `<a-tabs>` 改成 `<a-tabs v-model:active-key="activeTab">`。
4. `key="history"` 的 `</a-tab-pane>` 之後加：
```vue
        <a-tab-pane v-if="can('followups', 'read')" key="followups" tab="跟進紀錄">
          <ProjectFollowUpsTab :project="p" />
        </a-tab-pane>
```
5. `a-descriptions` 中「最後更新」那一項之後加：
```vue
        <a-descriptions-item v-if="can('followups', 'read')" label="下次聯絡">
          <template v-if="p.nextFollowUpOn">
            {{ p.nextFollowUpOn }}
            <a-tag v-if="due.state === 'overdue'" color="red">逾期 {{ -due.days }} 天</a-tag>
            <a-tag v-else-if="due.state === 'today'" color="orange">今天</a-tag>
          </template>
          <span v-else>—</span>
        </a-descriptions-item>
```
6. 檔頭 `@component` 說明的分頁清單加入「跟進紀錄」。

- [ ] **Step 4: 型別檢查**

Run: `export PATH="$HOME/.nvm/versions/node/v24.15.0/bin:$PATH" && npm run check:web`
Expected: 0 錯誤、0 警告。

- [ ] **Step 5: 瀏覽器驗證**

啟動測試 API（5281）與測試 dev server：`QUOTE_API_URL=http://127.0.0.1:5281 npm --prefix apps/web run dev -- --port 4342`（背景、記 PID）。以示範公司的業務帳號（`.env.development-api.local` 的密碼，只填進本機頁面）登入 `http://127.0.0.1:4342`，開一個專案：
- 「跟進紀錄」頁籤出現；新增一筆（選聯絡人、下次聯絡按「3 天後」），卡片與頂部「下次聯絡」立即更新。
- 編輯、刪除、不用跟進都有效；關鍵字與類型篩選會重新查詢（Network 看得到帶參數的請求）。
- 網址加 `&tab=followups` 會直接開到該頁籤。
- 手機寬度（375px）下表單與時間軸沒有水平捲動。
結束後 `kill` 兩個 PID。

- [ ] **Step 6: Commit**

```bash
git add apps/web/src/pages/projects/detail
git commit -m "feat: 專案詳細頁新增跟進紀錄頁籤與表單

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: 專案列表的「下次聯絡」欄與跟進狀態篩選

**Files:**
- Modify: `apps/web/src/pages/projects/_components/ProjectsView.vue`

**Interfaces:**
- Consumes: `FOLLOW_UP_FILTERS`、`followUpDue`、`FollowUpFilter`（Task 7）；`ProjectSearchQuery.followUp`、`ProjectRow.nextFollowUpOn`。

- [ ] **Step 1: 腳本**

1. import 加：`import { FOLLOW_UP_FILTERS, followUpDue, type FollowUpFilter } from '@/shared/services/followup-service';`
2. `filters` 加 `followUp: (initial.get('followUp') ?? undefined) as FollowUpFilter | undefined,`
3. `query` computed 加 `followUp: filters.followUp,`（放在 `mine` 之後）。
4. `reset()` 的 `Object.assign` 加 `followUp: undefined`。
5. `const { can } = useMe();` 之後加 `const canFollowUps = can('followups');`
6. `columns` 中狀態欄之後插入：
```ts
  ...(canFollowUps ? [col.accessor('nextFollowUpOn', { header: '下次聯絡', enableSorting: true, meta: { width: 120 } })] : []),
```
7. 檔頭 `@component` 說明加上「跟進狀態（待跟進／已逾期／沒有下次聯絡）」。

- [ ] **Step 2: 模板**

1. 篩選列第二排：日期區間 `:md="10"` 改 `:md="8"`、金額 `:md="11"` 改 `:md="7"`，兩者之間加：
```vue
        <a-col v-if="canFollowUps" :xs="24" :md="6">
          <a-select v-model:value="filters.followUp" :options="FOLLOW_UP_FILTERS" allow-clear placeholder="跟進狀態" style="width: 100%" @change="onFilterChange" />
        </a-col>
```
2. `DataTable` 的 `:scroll-x="1300"` 改 `:scroll-x="1420"`。
3. `#cell` 中 `status` 分支之後加：
```vue
          <template v-else-if="columnId === 'nextFollowUpOn'">
            <template v-if="record.nextFollowUpOn">
              <span :class="['due', followUpDue(record.nextFollowUpOn, record.status).state]">{{ record.nextFollowUpOn }}</span>
              <a-tag v-if="followUpDue(record.nextFollowUpOn, record.status).state === 'today'" color="orange" class="today">今天</a-tag>
            </template>
            <span v-else class="muted">—</span>
          </template>
```
4. `<style scoped>` 加：
```css
.due.overdue {
  color: #cf1322;
  font-weight: 500;
}
.due.inactive {
  color: #bbb;
}
.today {
  margin-left: 4px;
}
```

- [ ] **Step 3: 型別檢查**

Run: `npm run check:web`
Expected: 0 錯誤。

- [ ] **Step 4: 瀏覽器驗證**

（測試 API 與 4342 dev server，同 Task 8 Step 5）專案列表：
- 「下次聯絡」欄可點表頭排序，Network 請求帶 `sort=nextFollowUpOn`。
- 「跟進狀態」選「已逾期」後請求帶 `followUp=overdue`、網址同步 `?followUp=overdue`，重新整理後條件還在。
- 會計帳號看不到這一欄與篩選。
結束後 `kill` PID。

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/pages/projects/_components/ProjectsView.vue
git commit -m "feat: 專案列表顯示下次聯絡並可依跟進狀態篩選

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: 跟進紀錄總表頁 `/followups`

**Files:**
- Create: `apps/web/src/pages/followups/index.astro`
- Create: `apps/web/src/pages/followups/_components/FollowUpsView.vue`
- Modify: `apps/web/src/app/route-manifest.ts`、`apps/web/src/app/components/AdminApp.vue`

**Interfaces:**
- Consumes: `listFollowUpsApi`、`FOLLOW_UP_TYPES`、`followUpDue`、`followUpTypeLabel`、`FollowUpRow`、`FollowUpSearchQuery`（Task 7）；既有 `UserSelect`、`DataTable`、`dataColumns`、`listDirectoryDepartmentsApi`、`useMe`。
- Produces: 路由 key `followups`、路徑 `/followups`（選單由 `GET /api/auth/menu` 依 Task 2 的 `RoutePath` 產生）。

- [ ] **Step 1: 路由與頁面**

1. `route-manifest.ts`：`RouteKey` 在 `| 'project-detail'` 之後加 `| 'followups'`；`routeManifest` 在 `project-detail` 那一列之後加：
```ts
  { key: 'followups', path: '/followups', title: '跟進紀錄', scope: 'company', requirement: { code: 'followups' } },
```
2. `AdminApp.vue` 的 `views` 在 `'project-detail'` 之後加：
```ts
  followups: () => import('@/pages/followups/_components/FollowUpsView.vue'),
```
3. `apps/web/src/pages/followups/index.astro`：
```astro
---
import BaseLayout from '@/app/layouts/BaseLayout.astro';
import AdminApp from '@/app/components/AdminApp.vue';
---

<BaseLayout title="跟進紀錄">
  <AdminApp routeKey="followups" client:only="vue" />
</BaseLayout>
```

- [ ] **Step 2: 總表元件**

`FollowUpsView.vue`：

```vue
<script setup lang="ts">
/**
 * @component FollowUpsView
 * 跨專案跟進紀錄總表：列出目前資料範圍內所有專案的紀錄，主管可一次看完部屬的跟進。
 * 篩選、排序、分頁全部送給 API；這頁只讀，修改請點專案連到該專案的「跟進紀錄」頁籤。
 */
import { computed, reactive, ref } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import UserSelect from '@/app/components/UserSelect.vue';
import { useMe } from '@/app/composables/use-me';
import { listDirectoryDepartmentsApi } from '@/shared/services/business-service';
import {
  FOLLOW_UP_TYPES,
  followUpDue,
  followUpTypeLabel,
  listFollowUpsApi,
  type FollowUpRow,
  type FollowUpSearchQuery,
} from '@/shared/services/followup-service';
import { dataColumns, type SortingState } from '@/shared/table/data-table';
import { DATA_SCOPE_LABELS } from '@/shared/types/permissions';

const { me } = useMe();
const filters = reactive({
  keyword: '',
  type: undefined as string | undefined,
  createdBy: undefined as string | undefined,
  departmentId: undefined as string | undefined,
  range: [] as Dayjs[],
});
const keywordInput = ref('');
const paging = reactive({ page: 1, pageSize: 20, sort: 'occurredAt' as 'occurredAt' | 'createdAt', desc: true });

const query = computed<FollowUpSearchQuery>(() => ({
  keyword: filters.keyword || undefined,
  type: filters.type,
  createdBy: filters.createdBy,
  departmentId: filters.departmentId,
  from: filters.range[0]?.format('YYYY-MM-DD'),
  to: filters.range[1]?.format('YYYY-MM-DD'),
  ...paging,
}));
const list = useQuery({ queryKey: ['followups-all', query], queryFn: () => listFollowUpsApi(query.value), placeholderData: keepPreviousData });

// 只有部門樹與全公司範圍才有其他部門可選；部門樹是已完整載入的小型樹狀結構，前端只組裝顯示。
const canFilterDepartment = computed(() => ['company', 'department_tree'].includes(me.value?.scope.dataScope ?? ''));
const departments = useQuery({ queryKey: ['directory-departments'], queryFn: listDirectoryDepartmentsApi, enabled: canFilterDepartment });
const departmentTree = computed(() => {
  const rows = departments.data.value ?? [];
  const build = (parentId: string | null): unknown[] => rows.filter((row) => row.parentId === parentId).map((row) => ({ value: row.id, title: row.name, children: build(row.id) }));
  return build(null);
});

function onFilterChange() {
  paging.page = 1;
}

function search() {
  filters.keyword = keywordInput.value.trim();
  onFilterChange();
}

function reset() {
  keywordInput.value = '';
  Object.assign(filters, { keyword: '', type: undefined, createdBy: undefined, departmentId: undefined, range: [] });
  onFilterChange();
}

function onTableChange(change: TableChange) {
  paging.page = change.page;
  paging.pageSize = change.pageSize;
  paging.sort = change.sort === 'createdAt' ? 'createdAt' : 'occurredAt';
  paging.desc = change.sort ? change.desc : true;
}

/** 這筆的下次聯絡是不是專案目前的那一筆（只有它才需要標示逾期）。 */
function isCurrent(row: FollowUpRow) {
  return !!row.nextFollowUpOn && !row.nextDismissedAt && row.project?.nextFollowUpOn === row.nextFollowUpOn;
}

const sorting = computed<SortingState>(() => [{ id: paging.sort, desc: paging.desc }]);
const col = dataColumns<FollowUpRow>();
const columns = [
  col.accessor('occurredAt', { header: '發生時間', enableSorting: true, meta: { width: 140 } }),
  col.accessor('type', { header: '類型', meta: { width: 80 } }),
  col.accessor((row) => row.project?.name ?? '', { id: 'project', header: '專案', meta: { width: 220 } }),
  col.accessor('clientName', { header: '客戶', meta: { width: 160 } }),
  col.accessor('contactName', { header: '對象', meta: { width: 100 } }),
  col.accessor('summary', { header: '主旨' }),
  col.accessor('nextFollowUpOn', { header: '下次聯絡', meta: { width: 110 } }),
  col.accessor((row) => row.createdBy.displayName ?? '', { id: 'createdBy', header: '撰寫人', meta: { width: 100 } }),
  col.accessor('createdAt', { header: '建立時間', enableSorting: true, meta: { width: 140 } }),
];
</script>

<template>
  <a-space direction="vertical" :size="16" style="width: 100%">
    <a-card :bordered="false" :body-style="{ paddingBottom: '8px' }">
      <a-row :gutter="[12, 12]">
        <a-col :xs="24" :md="8">
          <a-input-search v-model:value="keywordInput" placeholder="主旨、內容、對象、專案、客戶" allow-clear @search="search" />
        </a-col>
        <a-col :xs="24" :md="4">
          <a-select v-model:value="filters.type" :options="FOLLOW_UP_TYPES" allow-clear placeholder="全部類型" style="width: 100%" @change="onFilterChange" />
        </a-col>
        <a-col :xs="24" :md="4">
          <UserSelect v-model:value="filters.createdBy" allow-clear placeholder="撰寫人" @change="onFilterChange" />
        </a-col>
        <a-col v-if="canFilterDepartment" :xs="24" :md="4">
          <a-tree-select
            v-model:value="filters.departmentId"
            :tree-data="departmentTree"
            allow-clear
            tree-default-expand-all
            placeholder="全部部門"
            style="width: 100%"
            @change="onFilterChange"
          />
        </a-col>
        <a-col :xs="24" :md="4">
          <a-range-picker v-model:value="filters.range" style="width: 100%" @change="onFilterChange" />
        </a-col>
        <a-col :span="24" class="bar">
          <span class="muted">資料範圍：{{ DATA_SCOPE_LABELS[me?.scope.dataScope ?? 'own'] }}（依專案所屬部門）</span>
          <a-button @click="reset">清除條件</a-button>
        </a-col>
      </a-row>
    </a-card>

    <a-card :bordered="false">
      <DataTable
        :data="list.data.value?.items ?? []"
        :columns="columns"
        row-key="id"
        :loading="list.isFetching.value"
        :pagination="{ page: paging.page, pageSize: paging.pageSize, total: list.data.value?.total ?? 0, showSizeChanger: true }"
        :sorting="sorting"
        :scroll-x="1300"
        @change="onTableChange"
      >
        <template #cell="{ columnId, record }">
          <template v-if="columnId === 'occurredAt'">{{ dayjs(record.occurredAt).format('YYYY-MM-DD HH:mm') }}</template>
          <template v-else-if="columnId === 'createdAt'">{{ dayjs(record.createdAt).format('YYYY-MM-DD HH:mm') }}</template>
          <template v-else-if="columnId === 'type'">{{ followUpTypeLabel(record.type) }}</template>
          <template v-else-if="columnId === 'project'">
            <a v-if="record.project" :href="`/projects/detail?id=${record.project.id}&tab=followups`">{{ record.project.projectNo }} {{ record.project.name }}</a>
            <span v-else class="muted">—</span>
          </template>
          <template v-else-if="columnId === 'nextFollowUpOn'">
            <span v-if="record.nextFollowUpOn" :class="['due', isCurrent(record) ? followUpDue(record.nextFollowUpOn, record.project?.status).state : 'inactive']">
              {{ record.nextFollowUpOn }}
            </span>
            <span v-else class="muted">—</span>
          </template>
        </template>
        <template #expanded="{ record }">
          <div class="content">{{ record.content ?? '（沒有內容）' }}</div>
          <div v-if="record.nextAction" class="muted">下次要做的事：{{ record.nextAction }}</div>
        </template>
      </DataTable>
    </a-card>
  </a-space>
</template>

<style scoped>
.bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.muted {
  color: #888;
}
.due.overdue {
  color: #cf1322;
  font-weight: 500;
}
.due.inactive {
  color: #bbb;
}
.content {
  white-space: pre-wrap;
}
</style>
```

- [ ] **Step 3: 型別檢查與建置**

Run: `npm run check:web && npm run build:web`
Expected: 0 錯誤；建置成功，頁數比基準多 1（含 `/followups`）。

- [ ] **Step 4: 瀏覽器驗證**

（測試 API 與 4342）以示範公司的業務主管登入：
- 左側選單「業務管理」下出現「跟進紀錄」，進入後看得到部屬寫的紀錄；換成業務帳號只看到自己專案的。
- 撰寫人、部門、類型、日期、關鍵字篩選與「發生時間」「建立時間」排序都會重新呼叫 API。
- 展開列顯示完整內容；點專案連到詳細頁並停在「跟進紀錄」頁籤。
- 會計帳號選單沒有「跟進紀錄」，直接輸入 `/followups` 顯示 403。
結束後 `kill` PID。

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/pages/followups apps/web/src/app/route-manifest.ts apps/web/src/app/components/AdminApp.vue
git commit -m "feat: 新增跨專案跟進紀錄總表頁

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: 架構文件與完整驗證

**Files:**
- Modify: `docs/system-architecture.md`（6.8 之後新增 6.9）
- Modify: `docs/superpowers/specs/2026-11-04-project-followups-design.md`（狀態改為已實作）

- [ ] **Step 1: 架構文件**

在 `docs/system-architecture.md` 的「### 6.8 部門虛擬辦公室」整節之後、「## 7. 主要 API」之前加：

```markdown
### 6.9 專案跟進紀錄

- 規格：`docs/superpowers/specs/2026-11-04-project-followups-design.md`。
- `project_followups` 掛在專案下（電話、拜訪、會議、Email、訊息、其他），可見範圍完全繼承專案：主管依資料範圍看得到部屬專案的紀錄，判斷依據是專案所屬部門。寫入需要 `followups` CRUD 加專案寫入身分；已結案專案只能讀。
- 「最新一筆」依 `OccurredAt`（同時間比 `CreatedAt`）判斷，補登較早的紀錄不會蓋掉後來的下次聯絡。`projects.LastFollowUpAt`、`projects.NextFollowUpOn` 是依最新一筆重算的快取，只由 `FollowUpService` 在同一交易內以單一 `ExecuteUpdateAsync`（UPDATE + 子查詢）寫入；InnoDB 對子查詢加鎖讀取，同專案同時寫入不會算出過期狀態。寫了新的一筆，前一筆的待辦自然結束；「不用跟進」只作用在最新一筆。
- 待跟進不含未得標、已結案、已取消的專案（日期保留）；逾期以台灣時間的今天為準。
- API：`GET /api/followups`（跨專案總表）、`GET|POST /api/projects/{id}/followups`、`GET /api/projects/{id}/followups/contacts`（以專案範圍授權，因客戶依客戶部門判斷可見範圍）、`PUT|DELETE /api/followups/{id}`、`POST /api/projects/{id}/followups/dismiss-next`；專案列表支援 `followUp=pending|overdue|none` 與 `sort=nextFollowUpOn|lastFollowUpAt`。沒有 `followups:read` 時專案的跟進欄位為 null，篩選與排序被忽略。
- 前端：專案詳細頁「跟進紀錄」頁籤（`tab=followups` 可直接開啟）、專案列表「下次聯絡」欄與跟進狀態篩選、`/followups` 總表頁。
- 既有角色的 `followups` 由 Seeder 一次性補上，部署此版本時必須執行一次 Seeder（見 `docs/permissions-and-data-scope.md`）。
```

- [ ] **Step 2: 規格狀態**

規格第 4 行改為：`- 狀態：已實作（分支 \`feat/project-followups\`）`

- [ ] **Step 3: 完整驗證**

```bash
export PATH="$HOME/.nvm/versions/node/v24.15.0/bin:$PATH"
npm run build:api
npm run test:api
dotnet ef migrations has-pending-model-changes --project apps/api/QuotePlatform.Api
npm run check:web
npm run build:web
```
Expected: 0 警告、`self-test passed`、沒有待產生的 model 變更、check:web 0 錯誤、build:web 成功。

啟動測試 API（5281）後：
```bash
node tests/followups-smoke.mjs http://127.0.0.1:5281
node tests/api-smoke.mjs http://127.0.0.1:5281
node tests/projects-smoke.mjs http://127.0.0.1:5281
node tests/roles-scope-smoke.mjs http://127.0.0.1:5281
node tests/documents-smoke.mjs http://127.0.0.1:5281
node tests/dashboard-smoke.mjs http://127.0.0.1:5281
```
Expected: 全部通過。結束後 `kill <PID>`，並確認 5281、4342 都沒有自己啟動的行程殘留；5280、4341 仍在執行。

- [ ] **Step 4: Commit**

```bash
git add docs/system-architecture.md docs/superpowers/specs/2026-11-04-project-followups-design.md
git commit -m "docs: 同步專案跟進紀錄的架構說明

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: 報告**

報告中列出：所有 commit、驗證結果、未做的事（沒有 push、沒有合併），並提醒：
- 部署時必須執行一次 Seeder。
- 使用者 5280 的 API 仍是舊程式；共用資料庫已套用新 migration 與 `followups` 授權。合併前若以舊程式重跑 Seeder，完整存取角色的 `followups` 會被同步掉，合併後再跑一次即可。
