# 身分、權限與資料範圍

## 1. 名詞

| 名詞 | 說明 |
|---|---|
| 使用者（user） | 登入主體，同一帳號可屬於多間公司 |
| 公司（company） | 登入租戶，資料隔離單位（美達特的商家） |
| 部門（department） | 公司內的樹狀組織，決定資料範圍（美達特的商家節點） |
| 客戶（client） | 報價對象，只是資料，不登入 |
| 權限（permission） | 資源 × CRUD action × 子功能 option |
| 資料範圍（data scope） | 能對「哪些專案」操作 |

## 2. Scope 與登入

- `platform` scope：系統帳號（`users.SystemRoleId` 不為空），管理公司、系統帳號、系統角色與權限目錄。要看某公司的業務資料，必須先切換到該公司。
- `company` scope：一般帳號透過 `company_memberships` 進入公司。

### JWT

| Claim | 說明 |
|---|---|
| `sub` | user id |
| `scope_kind` | `platform` 或 `company` |
| `company_id` | company scope 必填 |
| `department_id` | 使用者所屬部門 |
| `data_scope` | 有效資料範圍 |
| `auth_version` | 與 `users.AuthVersion` 比對，不一致即拒絕 |
| `grants` | 有效權限（亦可只放版本，由 `/api/auth/me` 取得） |

### 授權流程

1. `POST /api/auth/login`：驗證帳密（沿用美達特 `LoginAttemptGuard` 鎖定與驗證碼）；系統帳號預設 platform scope，一般帳號預設第一個有效 membership。
2. 回傳 access token（30 分鐘）與 refresh token（14 天，HttpOnly cookie，資料庫只存 hash）。
3. `POST /api/auth/refresh`：輪替 refresh token（舊 token 標記 `RevokedAt`、`ReplacedById`），以目前 scope 重新計算 grants。若偵測已撤銷的 refresh token 被重用，撤銷該使用者全部 refresh token。
4. `GET /api/auth/scopes`、`POST /api/auth/scopes/select`：列出／切換公司，重新簽發 token。
5. `POST /api/auth/logout`：撤銷 refresh token。
6. 帳號停用、membership、角色權限、個人權限、密碼異動 → `users.AuthVersion + 1`，舊 access token 立即失效，並寫入 `audit_logs`。
7. 登入失敗統一回覆「帳號或密碼不正確，或帳號已停用」，密碼最短長度依公司政策（預設 8 碼）。

## 3. 權限模型

沿用美達特三層：

```json
{
  "code": "quotes",
  "actions": ["read", "create", "update"],
  "options": ["export_pdf", "approve"]
}
```

### 3.1 有效權限計算

```
角色 grant   = role_permissions（使用者在目前公司的角色）
個人加權     = user_permissions where Effect = grant 且未過期
個人禁止     = user_permissions where Effect = deny 且未過期

有效 grant = ( 角色 grant ∪ 個人加權 ) − 個人禁止
           ∩ company-admin 上限
```

- 以 action 與 option 為單位計算，例如角色有 `quotes:read,update`，個人禁止 `quotes:update`，結果只剩 `read`。
- 禁止優先於加權。
- 個人權限可設 `ExpiresAt`，做臨時授權（例如代理職務）。
- 任何 company scope 的有效 grant 都不得超過系統角色 `company-admin`（沿用美達特 `CustomerRolePermissionCeilingRules`）。
- 授予個人權限需要 `company.users:update` + `assign_permission`，且授予者不得把自己沒有的權限給別人。

### 3.2 權限目錄（seed）

| Scope | 代碼 | 路由 | 子功能 options |
|---|---|---|---|
| platform | `platform.companies` | `/platform/companies` | `switch_company`：切換公司 |
| platform | `platform.system_users` | `/platform/system-users` | `reset_password`：重設密碼 |
| platform | `platform.roles` | `/platform/roles` | — |
| platform | `platform.permissions` | `/platform/permissions` | — |
| company | `company.departments` | `/admin/departments` | — |
| company | `company.users` | `/admin/users` | `assign_role`：指派角色、`assign_permission`：設定個人權限、`reset_password`：重設密碼 |
| company | `company.roles` | `/admin/roles` | — |
| company | `company.settings` | `/admin/settings` | `numbering`：單號規則、`quote_template`：報價範本 |
| company | `clients` | `/clients` | `import`：匯入、`export`：匯出 |
| company | `products` | `/products` | `import`：匯入 |
| company | `projects` | `/projects` | `assign_member`：指定成員、`close`：結案、`export`：匯出 |
| company | `followups` | `/followups` | — |
| company | `quotes` | `/quotes` | `import_excel`：範本匯入、`import_legacy`：舊報價匯入、`import_ocr`：圖像識別、`export_pdf`：PDF、`export_excel`：Excel、`approve`：核准、`view_cost`：檢視成本與毛利 |
| company | `orders` | `/projects/:id/orders` | `download`：下載附件（出貨單沿用此權限） |
| company | `warranties` | `/projects/:id/warranties` | `download`：下載附件 |
| company | `invoices` | `/projects/:id/invoices` | `download`：下載附件、`mark_paid`：登記收款、`void`：作廢 |
| company | `dashboard` | `/` | `view_amount`：檢視金額、`view_all_staff`：檢視全員排行 |
| company | `office` | `/office` | — |

`/` Dashboard 不要求權限即可進入，但沒有 `dashboard:read` 時只顯示歡迎頁。

`/office` 虛擬辦公室只用 `office:read`：同公司成員都能進入任何部門的辦公室，不套資料範圍（辦公室不含業務資料）；hub 連線時亦檢查此權限。

既有公司角色（系統與自訂）由 Seeder 一次性補上 `office:read`：以 `audit_logs` 的 `permission.default_granted` 紀錄（`EntityId` 為該權限的 Id）當標記，沒有這筆紀錄才處理；即使沒有任何角色需要補授權（例如全新資料庫）也寫入標記，所以管理員日後收回的授權不會被補回。授權、相關使用者的 `AuthVersion` 遞增與標記在同一個交易內完成。部署這個版本時必須執行一次 Seeder（`--seed-only` 或 `BOOTSTRAP_ON_START=true`），否則既有角色看不到虛擬辦公室。

`followups` 跟進紀錄掛在專案下，可見範圍完全繼承專案（`ProjectAccessService.VisibleProjects`），沒有另外的規則：業務主管（部門樹）看得到自己部門與下層部門專案的紀錄，公司管理員看全部，業務看自己負責或參與的專案。判斷依據是專案所屬的部門，不是撰寫人的部門。寫入需要對應的 `followups` CRUD 加上專案寫入身分（負責人、成員或資料範圍內的主管），能寫專案的人可以修改、刪除任何人寫的紀錄；已結案專案只能讀。

既有角色的 `followups` 同樣由 Seeder 一次性補上（同一個 `permission.default_granted` 機制）：系統角色依下表（業務、業務主管 CRUD，唯讀 R，會計沒有）；自訂角色比照它對 `projects` 的 CRUD，沒有 `projects:read` 的不補。部署此版本時必須執行一次 Seeder。

權限版本變更對辦公室連線的影響：`AuthVersion` 遞增時（改角色權限、部門等），hub 連線收到 `ReauthRequired` 後斷線，前端以 refresh token 換發新 token 再重連，使用者不會被登出，與 REST 收到 401 時的行為一致；帳號、成員身分或公司被停用則送 `ForceDisconnect`，前端登出且不重連。

### 3.3 內建角色

| 角色 | Scope | 資料範圍 | 權限摘要 |
|---|---|---|---|
| `platform-admin` | platform | — | 全部 platform 權限，可切換到任一公司 |
| `company-admin` | company | company | 公司內全部權限，也是公司角色上限 |
| `sales-manager` | company | department_tree | 客戶、專案、報價（含 approve、view_cost）、訂單、保固、發票讀取、Dashboard 全員、跟進紀錄讀寫、虛擬辦公室（`office:read`） |
| `sales` | company | own | 客戶、專案、報價（不含 approve）、訂單、保固讀寫、Dashboard 自己、跟進紀錄讀寫、虛擬辦公室（`office:read`） |
| `accountant` | company | company | 專案讀取、訂單、發票（含 mark_paid、void）、Dashboard 金額、虛擬辦公室（`office:read`） |
| `viewer` | company | own | 全部唯讀（含虛擬辦公室） |

系統內建角色的代碼、名稱不可修改或刪除；公司可自建角色，但不得超過 `company-admin`。

**資料範圍上限**：操作者自己的資料範圍（own &lt; department &lt; department_tree &lt; company）是上限，否則有角色管理權限的人可以建立「全公司」角色再指派給自己，繞過資料範圍。後端（`RoleService`、`MemberService`）強制：
- 建立或修改角色時，角色的資料範圍不可大於自己的；改成更小的可以。
- 修改、刪除或調整權限的角色，資料範圍不可比自己的大（公司管理員為全公司範圍，不受限）。
- 指派角色時，角色的資料範圍不可大於自己的（和「權限不可超過自己」並列檢查）。
- 設定或新增成員時的個別資料範圍（`DataScopeOverride`）也不可大於自己的；清除個別設定不受限。
- 前端的資料範圍下拉與角色下拉會把超過的選項停用，後端才是安全邊界。

自訂角色只有**建立者**能指派：成員表單的角色選單（`GET /api/admin/roles?forAssignment=true`）只列出系統內建角色與自己建立的自訂角色，後端指派時也會檢查（`roles.CreatedBy`）；角色管理頁（不帶參數）仍列出全部，讓管理員能維護同事建立的角色。成員目前的角色是別人建立的自訂角色時，改其他欄位不受影響。規則上線前建立、沒有建立者的舊自訂角色不限制。

## 4. 資料範圍（Data Scope）

權限回答「能不能做這種操作」，資料範圍回答「能對哪些專案做」。

| data_scope | 可見專案條件 |
|---|---|
| `own` | `projects.OwnerUserId = 我` 或我在 `project_members` |
| `department` | 上述 `own` 條件，或 `projects.DepartmentId = 我的部門` |
| `department_tree` | 上述 `own` 條件，或 `projects.DepartmentId` 在我的部門及下層部門（`department_closure`） |
| `company` | 公司全部專案 |

- 有效資料範圍 = `company_memberships.DataScopeOverride` ?? `roles.DataScope`。
- 報價、訂單、保固、發票、附件、Dashboard 都**繼承所屬專案的可見性**，由共用的 `ProjectAccessService.VisibleProjects()` 產生 `IQueryable<Project>` 給各模組 join，不各自重寫條件。
- 客戶：可見範圍看**客戶的所屬部門**（`clients.DepartmentId`），不看負責人：
  - `own`（本人）與 `department`（部門）：只看自己部門的客戶，看不到上層或其他部門的。
  - `department_tree`：自己部門與所有下層部門的客戶。
  - `company`：全部。
  - 看不到的客戶，API 一律回 404（不透露是否存在），也不能用來建立專案或改選報價的客戶。專案本身的客戶不受此限：跨部門合作的專案成員仍看得到專案上的客戶名稱、仍能在該專案報價。
  - 所屬部門預設為建立者的部門；只有 `company` 範圍且有 `clients:update` 的管理者可以指定或移動（業務不能指定其他部門）。移走後原部門的人立刻看不到。
  - 修改權仍以負責人為準：`clients:update` 只限 `company` 範圍或客戶的 `OwnerUserId`（見下）。
- 專案成員角色可再降級寫入：
  - `manager`：依權限可讀寫，並可管理成員（仍需 `projects:assign_member`）
  - `member`：依權限可讀寫
  - `viewer`：只能讀，即使角色有 update
- 資料範圍判斷一律在 Service 層執行，Controller 不得自行拼條件。

### 4.1 實作（第二階段）

唯一實作是 `Infrastructure/ProjectAccessService.cs`：

- `VisibleProjects(scope)`：回傳套好資料範圍的 `IQueryable<Project>`，列表、搜尋、Dashboard 與之後的報價／訂單／發票都從這裡出發。
- `GetAsync(scope, projectId)`：看不到 → `null`（API 一律回 404，不透露專案是否存在）；看得到時回傳：
  - `CanWrite` = `projects:update`，且（負責人、manager／member 成員，或**不靠成員身分**就看得到）。所以「只因為被加為 viewer 才看得到」的人只能讀；部門主管即使被加為 viewer，仍可依部門範圍修改。
  - `CanManageMembers` = `CanWrite` + `projects:assign_member`，且不是一般 member（負責人、manager 或靠部門範圍的人才能改成員名單）。
- `AllowedDepartmentIdsAsync(scope)`：建立或移動專案時可選的部門；company 不限，department／own 只有自己部門，department_tree 為自己部門與下層。避免把專案建到自己看不到的部門。
- 沒有部門的帳號，department／department_tree 自動退回 own。

客戶修改權（`ClientService`，在看得到的客戶之內再判斷）：company 全部；department／department_tree 為「負責人是自己或可見部門的成員」的客戶；own 只有自己負責的；沒有負責人的客戶只有 company 範圍可改。客戶列表的「專案數」是公司內總數，不套資料範圍。

報價（第三階段）：報價可見性與寫入權完全繼承所屬專案（`QuoteService.LoadAsync` 經 `ProjectAccessService`）。`quotes:approve` 可以核准或退回任何**看得到**的報價，不需要是專案成員；主管通常靠 department_tree 範圍看到下屬報價。`quotes:view_cost` 控制報價明細成本、成本合計、毛利與品項預設成本的讀寫。匯入工作只有上傳者本人可讀取與確認。

訂單、保固、發票與附件（第四階段）：可見性跟著所屬專案。寫入用 `ProjectAccess.CanWriteChild(scope, code, action)`：該資源的權限（例如 `invoices:create`）**加上**專案寫入身分（負責人、manager／member，或靠部門／全公司範圍看得到），**不需要** `projects:update`。所以會計（全公司範圍、專案唯讀）可以登錄訂單與發票，但只被加為 viewer 的人不行。出貨單沒有自己的權限碼，讀寫都看 `orders`。附件：讀取需所屬資源的 read（出貨單附件看 `orders`），下載訂單／出貨／保固／發票附件另需該資源的 `download` 子功能，上傳與刪除需所屬資源的 update 與寫入身分。下載連結只有 5 分鐘有效且每次都重新檢查權限。

公司通訊錄 `GET /api/directory/users | departments` 任何公司成員可讀，只回傳姓名與部門，用於選負責人與成員；帳號、Email 等管理資料仍需 `company.users`。

## 5. 後台 UI

- `route-manifest.ts` 定義路由、required permission、action 與 options（沿用美達特）。
- 左側選單由 `GET /api/auth/menu` 依 `read` grants 產生。
- `AuthGate`：沒有 token → 401 導向登入；權限不足 → 403 頁面。
- `PermissionGate`：控制按鈕顯示。
- 帳號管理頁：角色指派、資料範圍覆寫、個人權限樹（每個資源可設加權／禁止，CRUD checkbox 與 options），並顯示「有效權限」預覽，讓管理員看到合併後結果。

## 6. 驗證案例

- 公司 A 的使用者以任何參數都讀不到公司 B 的資料。
- `sales`（own）只看到自己負責或被加入的專案；被加入為 `viewer` 時不能修改。
- `sales-manager` 看得到下層部門的專案，看不到平行部門。
- 個人禁止 `quotes:update` 後，即使角色有權限，API 仍回 403。
- 個人臨時授權過期後，refresh 取得的 grants 不再包含該權限。
- 嘗試設定超過 `company-admin` 的權限時 API 回驗證錯誤。
- 角色或個人權限異動後，舊 access token 立即失效。
- 沒有 `quotes:view_cost` 時，API 回應不含成本、毛利欄位。
- 沒有 `dashboard:view_amount` 時，Dashboard API 不回傳金額。
