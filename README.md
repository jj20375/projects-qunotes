# 專案報價管理系統

多公司共用的專案報價管理平台：以專案為中心，管理客戶、報價單、訂單、保固單與發票，並提供年度／季度／月度業績 Dashboard。

架構參考美達特多站台管理系統（Metat Platform／ASURADA），沿用其 JWT、角色權限（資源 × CRUD × 子功能）、Seeder 與後台權限 gate 的設計；差異在於本系統**共用單一資料庫**，以每列 `CompanyId` 與 JWT 身分隔離各公司資料，並新增個人權限與資料範圍（Data Scope）。

## 功能範圍

- 帳號建立、JWT 登入（access token + 可撤銷 refresh token）
- 角色權限＋個人權限（加權／禁止），以及依身分決定可見專案的資料範圍
- 專案：建立、指定專案成員、依狀態／時間／金額搜尋
- 報價單：表單輸入、選擇客戶帶入、Excel 範本匯入、舊報價 Excel 匯入、PDF 列印、Excel 匯出、圖像識別輸入（照片、掃描檔、PDF）
- 訂單、保固單、發票：每個專案可多筆，附檔上傳
- 客戶管理（由美達特的商家機制轉化而來）
- 戰情 Dashboard：年度／季度／月度業績與案件數

## 技術棧

| 層 | 技術 |
|---|---|
| API | ASP.NET Core 10 modular monolith、EF Core（Pomelo MariaDB）、JWT |
| 前端 | Astro static + Vue 3 islands、Ant Design Vue、TanStack Query、TanStack Table、ECharts |
| 檔案 | file-server（由美達特 media-server 精簡）+ MinIO |
| 資料庫 | 沿用美達特共用 MariaDB container（shared-db），本系統只用 `quote_platform` 資料庫 |

## 目前進度

- 第一階段（基礎）已完成：帳號、JWT 登入與 refresh token 輪替、平台公司管理與切換、部門樹、成員帳號、角色權限、個人權限（加權／禁止／到期）、資料範圍、稽核紀錄，以及對應的後台畫面。
- 第二階段（客戶＋專案）已完成：客戶與聯絡人、專案建立與自動編號、專案成員、依資料範圍過濾、狀態／時間／金額搜尋、狀態流程與紀錄。
- 第三階段（報價單）已完成：報價表單與即時計算、客戶資料帶入、單號與改版、送審核准流程、客戶接受回寫專案、PDF 與 Excel 匯出、Excel 範本與舊報價匯入、品項價目、客戶 Excel 匯出入。
- 第四階段（檔案＋專案文件）已完成：訂單、保固單、發票（每專案多筆）、發票收款與作廢、專案金額摘要、附件上傳與下載（file-server ＋ 本機檔案系統或 MinIO）。
- 第五階段（Dashboard）已完成：年度／季度／月度業績圖（報價接受、接單、開票三種口徑，與去年同期比較）、案件數與成交率、業務排行、到期保固、應收帳款，以及公司參數頁（會計年度起始月、報價預設值）。

- 第六階段（圖像識別匯入）已完成：報價單的照片、掃描檔或 PDF 由 Claude 或 Gemini 辨識成表頭與明細，與原圖並排預覽、修正後建立報價草稿。需設定 `Quote__AnthropicApiKey` 或 `Quote__GeminiApiKey`；供應商、模型與強度在 [ai-settings.json](./apps/api/QuotePlatform.Api/ai-settings.json)，存檔即生效（見 [圖像識別匯入](./docs/system-architecture.md#66-圖像識別匯入第六階段)），未設定時此功能自動隱藏。
- 報價單格式：A4 PDF 自動避免不必要的分頁（明細列、合計、條款與簽章不拆開，略超一頁時改用緊湊版面）；抬頭可放公司 Logo、英文名稱、電話與地址；報價單號規則可自訂（例 `DB-{yyyy}{MM}{dd}-{seq:2}`）；備註條款以條列編輯，貼上 Excel／Word 的整段條列會自動拆條，舊報價 Excel 的逐行條款也會自動匯入（見[報價單格式](./docs/system-architecture.md#67-報價單格式條款單號抬頭pdf-分頁)）。

全部階段的範圍見[分階段開發](./docs/system-architecture.md#8-分階段開發)。

## 本機啟動

需要 .NET 10 SDK、Node 24（`nvm use`），以及 Mac 上既有的 `shared-mariadb`（127.0.0.1:9529）。

```bash
cp .env.development-api.local.example .env.development-api.local   # 填入密碼與 JWT 金鑰
npm --prefix apps/web ci
npm run dev:api:init    # migration + Seeder（含對客戶展示用的晴川系統整合）
npm run dev:api         # http://127.0.0.1:5280
npm run dev:file        # http://127.0.0.1:5190（需先 cp .env.development-file.local.example .env.development-file.local）
npm run dev:web         # http://127.0.0.1:4341
```

要讓同一個區網的其他裝置連線，後台改用 `npm run dev:web:lan`（監聽 0.0.0.0:4341），其他裝置開 `http://<這台 Mac 的區網 IP>:4341`（`ipconfig getifaddr en0` 可查）。API 與 file-server 仍只聽 127.0.0.1，由後台的 dev server 轉送，所以其他兩個服務不用改。

細節與驗證指令見 [docs/system-architecture.md 第 10 節](./docs/system-architecture.md#10-本機開發與驗證)。

## 文件

- [AGENTS.md](./AGENTS.md)：開發與代理人總則
- [docs/system-architecture.md](./docs/system-architecture.md)：系統架構、主要流程、API、開發階段
- [docs/permissions-and-data-scope.md](./docs/permissions-and-data-scope.md)：身分、權限、個人權限與資料範圍
- [docs/showcase-demo.md](./docs/showcase-demo.md)：對客戶展示的帳號、資料與展示流程；`npm run demo:reset` 重置
- [docs/database-planning.md](./docs/database-planning.md)：資料庫規劃與 ER 圖
- [docs/database-schema.sql](./docs/database-schema.sql)：MariaDB DDL 草稿
- [deploy/DEPLOY-FLOW.md](./deploy/DEPLOY-FLOW.md)：開發機、demo 機、正式機的部署流程（`./deploy/deploy.sh --environment=<環境>`）
