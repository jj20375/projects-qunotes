# 專案跟進紀錄設計

- 日期：2026-11-04
- 狀態：設計已確認，待實作（分支 `feat/project-followups`）

## 1. 目標與範圍

業務在專案底下記錄每一次和客戶的互動（電話、拜訪、會議……），並留下「下次聯絡日」與「下次要做的事」，讓自己和主管知道每個案子跟到哪裡、哪些已經該聯絡卻還沒聯絡。

### 這一版包含

- 專案詳細頁的「跟進紀錄」頁籤：下次聯絡卡片、時間軸、新增／編輯／刪除、「不用跟進」。
- 專案列表的「下次聯絡」欄（可排序）與「跟進狀態」篩選（待跟進、已逾期、沒有下次聯絡）。
- 跨專案的「跟進紀錄」總表頁 `/followups`：列出資料範圍內所有專案的紀錄，讓主管一次看完整個部門（見 5.4）。
- 獨立權限 `followups`，既有角色由 Seeder 一次性補上預設值。

### 這一版不包含

- 「今日待辦」（下一個功能，直接使用本設計的 `projects.NextFollowUpOn`）。
- 提醒通知、Email／行事曆整合。
- 只掛客戶、不屬於任何專案的紀錄（已決定一律掛專案）。
- 紀錄附檔。
- 多筆同時存在的待辦（已決定「寫下一筆就自動完成」，見 3.2）。

## 2. 資料

### 2.1 `project_followups`

業務 entity，實作 `ICompanyOwned` 與 `ISoftDelete`（與訂單、保固一致），`CompanyId` 由 `SaveChanges` 攔截器填入。

| 欄位 | 型別 | 說明 |
|---|---|---|
| `Id` | `char(36)` | 主鍵 |
| `CompanyId` | `char(36)` | 所屬公司 |
| `ProjectId` | `char(36)` | 所屬專案，必填 |
| `Type` | `varchar(20)` | 類型：`call` 電話、`visit` 拜訪、`meeting` 會議、`email` Email、`message` 訊息（LINE 等）、`other` 其他 |
| `OccurredAt` | `datetime(6)` | 互動發生時間（UTC 儲存），不可晚於現在（容許 5 分鐘時鐘誤差） |
| `ContactId` | `char(36)` NULL | 對象聯絡人；必須屬於專案的客戶，聯絡人刪除時設為 NULL |
| `ContactName` | `varchar(100)` NULL | 對象姓名快照；選了聯絡人時帶入其姓名，也可只填姓名不選聯絡人 |
| `Summary` | `varchar(200)` | 主旨，必填 |
| `Content` | `text` NULL | 內容，最多 4000 字 |
| `NextFollowUpOn` | `date` NULL | 下次聯絡日；不可早於 `OccurredAt` 的台灣日期 |
| `NextAction` | `varchar(200)` NULL | 下次要做的事；沒有 `NextFollowUpOn` 時存成 NULL |
| `NextDismissedAt` | `datetime(6)` NULL | 按「不用跟進」的時間 |
| `NextDismissedBy` | `char(36)` NULL | 按「不用跟進」的人 |
| `CreatedBy`／`CreatedAt`／`UpdatedBy`／`UpdatedAt` | | 建立與最後修改 |
| `IsDeleted`／`DeletedAt` | | 軟刪除 |

索引：`(CompanyId, ProjectId, IsDeleted, OccurredAt)`，供專案內時間軸與「最新一筆」查詢。

### 2.2 `projects` 新增欄位

| 欄位 | 型別 | 說明 |
|---|---|---|
| `LastFollowUpAt` | `datetime(6)` NULL | 最新一筆跟進紀錄的 `OccurredAt` |
| `NextFollowUpOn` | `date` NULL | 最新一筆的下次聯絡日；最新一筆沒填或已按「不用跟進」時為 NULL |

索引：`(CompanyId, NextFollowUpOn)`，供專案列表排序與逾期篩選。

所有欄位與資料表都要有繁體中文 `COMMENT`。migration 同步更新 `docs/database-planning.md` 與 `docs/database-schema.sql`。

## 3. 規則

### 3.1 權限與可見範圍

- 新權限 `followups`（讀、新增、修改、刪除），分類與 `orders`、`warranties` 相同（company、business），沒有額外選項。
- 內建角色預設：

  | 角色 | 預設 |
  |---|---|
  | 公司管理員 | 全部（依既有 `Grantable("company")` 規則自動取得） |
  | 業務主管 | 讀、新增、修改、刪除 |
  | 業務 | 讀、新增、修改、刪除 |
  | 會計 | 無 |
  | 唯讀 | 讀 |

- 既有公司角色由 Seeder 一次性補上（沿用虛擬辦公室的 `permission.default_granted` 機制，`audit_logs` 記錄、同一交易），部署時必須執行一次 Seeder。之後管理員移除的權限不會被補回。
- 讀取：專案必須在 `ProjectAccessService.VisibleProjects` 內，且具備 `followups:read`。看不到專案一律回 404（不透露是否存在）；看得到專案但沒有 `followups:read` 回 403。
- 因此主管看得到部屬寫的紀錄，範圍由角色（或個人覆寫）的資料範圍決定：

  | 角色 | 資料範圍 | 看得到的紀錄 |
  |---|---|---|
  | 公司管理員 | 全公司 | 全公司所有專案 |
  | 業務主管 | 部門樹 | 自己部門與所有下層部門的專案 |
  | 部門管理者（自訂角色） | 部門 | 自己部門的專案 |
  | 業務 | 本人 | 自己負責或參與的專案 |

  判斷依據是**專案所屬的部門**，不是撰寫人的部門。系統沒有內建「部門管理者」角色，以自訂角色設定資料範圍「部門」或「部門樹」並勾選 `followups:read` 即可。
- 寫入：需要對應的 `followups:create|update|delete`，加上專案的寫入身分（`ProjectAccess.CanWriteChild`，即負責人、成員或資料範圍內的主管），與訂單相同。能寫專案的人可以修改、刪除任何人寫的紀錄。
- 已結案（`closed`）的專案：紀錄只能讀，所有寫入回 400「已結案的專案不可修改。」，與專案本身的規則一致。
- 新增、修改、刪除、不用跟進都寫 `audit_logs`（entity `project_followup`），修改記錄前後內容。

### 3.2 下次聯絡與「最新一筆」

- 「最新一筆」：同專案未刪除的紀錄中 `OccurredAt` 最大者，相同時再比 `CreatedAt`。補登一筆較早的拜訪不會蓋掉後來的下次聯絡日。
- 專案的狀態欄位由唯一的重算函式 `FollowUpService.RecalculateAsync(project)` 寫入：
  - `LastFollowUpAt` = 最新一筆的 `OccurredAt`（沒有紀錄時為 NULL）。
  - `NextFollowUpOn` = 最新一筆的 `NextFollowUpOn`，但該筆 `NextDismissedAt` 不為 NULL 時為 NULL。
- 新增、修改、刪除、不用跟進都在同一個 `SaveChanges` 交易內呼叫重算；其他程式不得直接寫這兩個欄位。
- 寫了新的一筆，前一筆的待辦自然結束（只看最新一筆），不需要另外打勾。
- 刪除最新一筆時，狀態倒回前一筆的值（包含前一筆的下次聯絡日）。
- 「不用跟進」只作用在最新一筆；沒有下次聯絡日時回 400。修改該筆的下次聯絡日會清掉 `NextDismissedAt`（重新提醒）。

### 3.3 提醒範圍

- 「待跟進」：`NextFollowUpOn` 不為 NULL，且專案狀態不是 `lost`、`closed`、`cancelled`。
- 「已逾期」：待跟進，且 `NextFollowUpOn` 早於台灣時間的今天。
- 專案變成未得標、結案或取消時，日期仍保留，只是不算待跟進；專案重新啟動後會再出現。

## 4. API

新模組 `apps/api/QuotePlatform.Api/Modules/FollowUps/`（`FollowUpsController`、`FollowUpService`、`FollowUpContracts`）。路由寫法與訂單、保固一致。

| 方法與路徑 | 權限 | 說明 |
|---|---|---|
| `GET /api/followups` | `followups:read` | 跨專案總表，見 4.2 |
| `GET /api/projects/{projectId}/followups` | `followups:read` | `keyword`（主旨、內容、對象姓名）、`type`、`page`、`pageSize`；固定依 `OccurredAt` 新到舊；回 `{ total, page, pageSize, items }`，使用 `Modules/Common/ListQuery` |
| `GET /api/projects/{projectId}/followups/contacts` | `followups:read` | 專案客戶的聯絡人（`id`、`name`、`title`），供表單下拉選單 |
| `POST /api/projects/{projectId}/followups` | `followups:create` | 新增 |
| `PUT /api/followups/{id}` | `followups:update` | 修改 |
| `DELETE /api/followups/{id}` | `followups:delete` | 軟刪除 |
| `POST /api/projects/{projectId}/followups/dismiss-next` | `followups:update` | 不用跟進 |

- 聯絡人另開端點而不是呼叫 `GET /api/clients/{id}`：客戶的可見範圍是依客戶部門判斷，專案成員可能看得到專案卻看不到客戶。這個端點改以專案可見範圍授權，只回傳表單需要的欄位。
- 每筆 item 回傳：`id`、`type`、`occurredAt`、`contactId`、`contactName`、`summary`、`content`、`nextFollowUpOn`、`nextAction`、`nextDismissedAt`、`createdBy`（id 與姓名）、`createdAt`、`updatedBy`（id 與姓名）、`updatedAt`、`canEdit`、`canDelete`。
- 寫入的回應帶回重算後的專案狀態：`{ item, project: { lastFollowUpAt, nextFollowUpOn, nextAction } }`。
- 錯誤文案一律繁體中文，例如「發生時間不可晚於現在。」「下次聯絡日不可早於發生日期。」「聯絡人不屬於此專案的客戶。」

### 4.1 專案 API 的改動

- `GET /api/projects`：
  - `SortFields` 新增 `nextFollowUpOn`、`lastFollowUpAt`；排序時 NULL 一律排在最後。
  - 新增篩選 `followUp=pending|overdue|none`（3.3 的定義；`none` 為 `NextFollowUpOn` 為 NULL）。
  - item 新增 `lastFollowUpAt`、`nextFollowUpOn`。
- `GET /api/projects/{id}`：新增 `lastFollowUpAt`、`nextFollowUpOn`、`nextAction`（取自最新一筆）。
- 沒有 `followups:read` 時：上述欄位一律回 `null`，`followUp` 篩選與兩個排序欄位視為未指定（排序退回預設）。

### 4.2 跨專案總表 `GET /api/followups`

- 範圍：`VisibleProjects(scope)` 內所有專案的未刪除紀錄，不另訂規則。
- 參數：
  - `keyword`：主旨、內容、對象姓名、專案編號、專案名稱、客戶名稱。
  - `type`：類型。
  - `createdBy`：撰寫人（人員下拉以 API 遠端查詢，沿用既有 `UserSelect`）。
  - `departmentId`：專案所屬部門，含下層部門，且仍受資料範圍限制（與 Dashboard 的部門篩選相同）。
  - `projectId`：指定專案。
  - `from`、`to`：發生日期區間（台灣日期，含頭尾）。
  - `sort`：`occurredAt`（預設，新到舊）、`createdAt`；`desc`。
  - `page`、`pageSize`。
- 回 `{ total, page, pageSize, items }`；item 為 4 節的欄位，另加 `project`（`id`、`projectNo`、`name`、`status`、`nextFollowUpOn`）與 `clientName`。
- 只讀；寫入一律到專案頁籤進行（總表的「編輯」連到該專案頁籤）。

## 5. 畫面

### 5.1 專案詳細頁「跟進紀錄」頁籤

- 新元件 `apps/web/src/pages/projects/detail/_components/ProjectFollowUpsTab.vue`，在 `ProjectDetailView.vue` 以 `v-if="can('followups', 'read')"` 加入頁籤，位置在「狀態紀錄」之後。
- 最上方「下次聯絡」卡片：日期、下次要做的事，逾期顯示紅色「逾期 N 天」，今天顯示「今天」；有寫入權時顯示「不用跟進」按鈕（確認後呼叫 API）。沒有下次聯絡時顯示「目前沒有排定下次聯絡」。
- 工具列：關鍵字搜尋、類型篩選、「新增紀錄」按鈕（有 `followups:create` 且專案可寫、未結案時顯示）。
- 時間軸（`a-timeline`，與「狀態紀錄」一致，不是表格，所以不用 `DataTable`）：每筆顯示類型圖示與名稱、發生時間、對象、主旨、內容（保留換行）、作者；有修改過時顯示「由 某某 修改於 …」；依 `canEdit`、`canDelete` 顯示編輯、刪除。
- 分頁由後端處理：每頁 20 筆，底部「載入更多」取下一頁；改變關鍵字或類型時從第 1 頁重新查詢。
- 寫入成功後以回應的 `project` 更新卡片與詳細頁頂部資訊，不重抓整個專案。

### 5.2 新增／編輯視窗

- 新元件 `ProjectFollowUpFormModal.vue`。
- 欄位：類型（一排按鈕）、發生時間（預設現在）、對象（聯絡人下拉，可直接輸入姓名）、主旨、內容、下次聯絡日（附「明天」「3 天後」「下週」「2 週後」快速按鈕，可清除）、下次要做的事（沒有下次聯絡日時停用）。
- 前端驗證只作提示，最終以 API 為準。

### 5.3 專案列表

- `ProjectsView.vue` 新增「下次聯絡」欄（`enableSorting: true`，送 `sort=nextFollowUpOn`）：逾期紅字，今天顯示「今天」標籤，非待跟進狀態的專案以灰字顯示日期。
- 篩選列新增「跟進狀態」下拉：全部、待跟進、已逾期、沒有下次聯絡，送 `followUp` 給 API。
- 兩者都只在具備 `followups:read` 時顯示。

### 5.4 跟進紀錄總表頁 `/followups`

- 新頁面 `apps/web/src/pages/followups/index.astro` 與 `_components/FollowUpsView.vue`，側邊選單「業務」群組新增「跟進紀錄」，需 `followups:read`。
- 篩選列：關鍵字、類型、撰寫人、部門、發生日期區間；全部送給 API，不在前端過濾。
- 表格使用共用 `DataTable`：發生時間（可排序）、類型、專案（編號＋名稱，連到專案「跟進紀錄」頁籤）、客戶、對象、主旨與內容摘要（可展開看全文，用 `#expanded`）、下次聯絡日（逾期紅字）、撰寫人。後端分頁，預設每頁 20 筆。

### 5.5 前端型別與服務

- `src/shared/types/permissions.ts` 的 `PermissionCode` 新增 `followups`；`route-manifest.ts` 新增 `/followups`（需 `followups:read`）；`PermissionCatalog.cs` 中 `followups` 的路由設為 `/followups`。
- 專案詳細頁支援以網址參數 `tab=followups` 直接開啟該頁籤，供總表連結使用。
- 新服務函式（JSDoc 完整）：`listFollowUpsApi`、`listProjectFollowUpsApi`、`listFollowUpContactsApi`、`createFollowUpApi`、`updateFollowUpApi`、`deleteFollowUpApi`、`dismissNextFollowUpApi`。
- 「今天」與逾期天數以台灣時間計算，和後端一致。

## 6. 驗證

- `npm run build:api`（0 警告）、`npm run test:api`。
- 新增 `tests/followups-smoke.mjs` 與 `npm run test:smoke:followups`，涵蓋：
  - 看不到專案的業務讀寫皆 404；會計（沒有 `followups`）讀取 403，專案列表的跟進欄位為 `null`。
  - 新增、修改、刪除與 `audit_logs`。
  - 補登較早的紀錄不改變 `NextFollowUpOn`；刪除最新一筆後倒回前一筆的值；不用跟進後為 NULL，修改日期後重新出現。
  - 已結案專案寫入 400；聯絡人不屬於該客戶 400；發生時間晚於現在 400。
  - 紀錄列表的關鍵字、類型、分頁與 `total`。
  - 專案列表 `followUp=overdue|pending|none` 與 `sort=nextFollowUpOn`。
  - 總表 `GET /api/followups`：業務主管看得到部屬（部門樹內）專案的紀錄、看不到其他部門的；業務只看到自己專案的；公司管理員看到全部；`createdBy`、`departmentId`、日期區間、關鍵字、分頁與 `total`。
- `npm run check:web`、`npm run build:web`。
- migration 以空資料庫與既有資料庫各驗證一次；確認沒有待產生的 model 變更。
- 權限目錄異動同步：`PermissionCatalog.cs`、`permissions.ts`、`docs/permissions-and-data-scope.md`；架構文件 `docs/system-architecture.md` 新增一節。
