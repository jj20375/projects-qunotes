# 專案報價管理系統 代理人總則

本專案是多公司共用的專案報價管理平台，包含 Astro/Vue 後台、ASP.NET Core API、Node.js file-server、MariaDB 與 MinIO。設計參考美達特多站台管理系統（`../美達特官網`），但兩者的資料庫、bucket 與部署完全獨立。任何代理人修改程式碼或文件前，必須先閱讀本文件。

## 專案邊界

- `apps/api/QuotePlatform.Api/`：ASP.NET Core 10 modular monolith，使用 EF Core、MariaDB 與 JWT。
- `apps/web/`：Astro static build + Vue 3 islands，單一後台應用。
- `file-server/`：Node.js 檔案服務，負責單次上傳（大小、SHA-256、檔頭檢查）、檔案系統／MinIO 存取與 signed token 驗證，不直接讀取資料庫；允許的檔案類型必須與 API 的 `FileRules` 一致。
- `packages/contracts/`：OpenAPI 契約。
- `infra/`：Docker、MariaDB 初始化、Nginx 與部署腳本。
- `docs/`：架構、權限、資料庫文件；從 `README.md` 進入。

## 修改前必讀

- 架構與 API：`docs/system-architecture.md`
- 帳號、角色、權限、資料範圍：`docs/permissions-and-data-scope.md`
- 資料表、migration：`docs/database-planning.md`、`docs/database-schema.sql`

## 不可違反的原則

1. **公司隔離**：所有業務資料以已驗證 JWT 的 `company_id` 決定範圍；不信任前端傳入的 companyId。業務 entity 必須實作 `ICompanyOwned`，由 Global Query Filter 過濾，寫入時由 `SaveChanges` 攔截器強制填入 JWT 的 `CompanyId`。
2. **資料範圍**：專案的可見性依角色／個人的 `data_scope`（own／department／department_tree／company）決定；報價、訂單、出貨、保固、發票、附件一律繼承所屬專案的可見性，不得各自另訂規則。客戶則依「客戶所屬部門」套用同一組資料範圍（本人與部門看自己部門，部門樹含下層，全公司看全部），由 `ClientAccessService.VisibleClients` 統一過濾。
3. **權限雙重檢查**：前端 `PermissionGate`、route manifest 只負責 UX；API 的 permission evaluator 才是安全邊界。
4. **與美達特分離**：MariaDB 沿用美達特的共用 container（不另建），但只能使用 `quote_platform` 資料庫與 `quote_app` 帳號；不得連線、授權或寫入任何 `metat_*` 資料庫或美達特的 MinIO。Mac 上要連 `shared-mariadb`（9529），不是 `metat-vps-copy-db`（3306）。API 不持有 root 密碼。
5. **金額由後端計算**：報價、訂單、發票金額一律由後端重算並以 `decimal` 儲存，前端計算只作顯示。
6. **已送出的報價不可修改**：只能改版（新 `RevisionNo`），舊版標示為 `superseded`；報價保留客戶資料快照。
7. 業務查詢使用 EF Core/LINQ；原生 SQL 僅限 migration、取號（`SELECT … FOR UPDATE`）與維運腳本。
8. 檔案本體放 MinIO，資料庫只存 metadata 與關聯；下載前必須先經 API 做資料範圍驗證再簽發 token。
9. 帳號停用、角色權限、個人權限、membership 或密碼異動時，必須遞增 `users.AuthVersion`，並寫入 `audit_logs`。
10. 所有程式碼註解、錯誤文案、規格與文件使用繁體中文；保留 API、類別、資料表與環境變數等技術名稱。
11. 使用 Conventional Commits；commit subject 使用繁體中文且保持單一目的。
12. 單一程式碼檔案原則上不超過 1000 行。
13. **列表的搜尋、篩選、排序、分頁一律由後端 API 處理**：前端只把條件以 query 參數送出，由後端在資料庫查詢（EF Core/LINQ），回傳 `{ total, page, pageSize, items }`；前端不可拿到整批資料後再用 JS 過濾、排序或切頁。表格欄位排序用 `sorter: true` 並把 `sort`／`desc` 送給 API，狀態頁籤、下拉選單的搜尋（客戶、人員、專案、公司）也一樣走 API（`filter-option` 設為 false 搭配遠端查詢）。例外只有：已完整載入的小型樹狀結構（部門樹）的顯示組裝、使用者剛上傳檔案的匯入預覽，以及純顯示用的分組。新增列表 API 時使用 `Modules/Common/ListQuery`，並在 smoke 測試中驗證關鍵字、分頁與 `total`。`DataTable` 的 `manualSorting`／`manualPagination` 已固定開啟，表格只回報 `change` 事件，由頁面帶著新條件重新呼叫 API。
14. **表格一律使用共用的 `DataTable`**（`apps/web/src/app/components/DataTable.vue`，以 TanStack Table v9 管理排序、分頁、展開狀態），不可在頁面裡直接用 `a-table` 或另寫 `<table>`。樣式只在 `DataTable` 定義一次，各頁靠 props（`columns`、`pagination`、`sorting`、`scroll-x`、`size`、`get-sub-rows`…）與 slots（`#cell`、`#expanded`、`#empty`）決定內容；欄位用 `dataColumns<T>()`（`shared/table/data-table.ts`）建立，需要新功能時擴充 `DataTable` 讓所有表格受益，不要在單一頁面另做一份。

## 驗證最低要求

- API 變更：`npm run build:api`（0 警告）、`npm run test:api`；涉及授權或資料範圍時再跑 `npm run test:smoke`。
- file-server 變更：`npm run test:file`，涉及上傳流程時再跑 `npm run test:smoke:documents`。
- 前端變更：`npm run check:web`（`astro check` 加 `vue-tsc`，後者才會檢查 `.vue` 的型別）與 `npm run build:web`（Node 24）。
- 權限目錄異動：同步更新 `Infrastructure/PermissionCatalog.cs`、前端 `src/shared/types/permissions.ts` 的 `PermissionCode`、`src/app/route-manifest.ts` 與 `docs/permissions-and-data-scope.md`。
- Schema 變更：產生 migration，並同步更新 `docs/database-planning.md` 與 `docs/database-schema.sql`；以空資料庫與既有資料庫各驗證一次。
