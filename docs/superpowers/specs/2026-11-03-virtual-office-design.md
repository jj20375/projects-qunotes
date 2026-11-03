# 部門虛擬辦公室設計

- 日期：2026-11-03
- 狀態：設計已確認，待撰寫實作計畫
- 參考：[WorkAdventure](https://workadventu.re/)（互動概念）、[Star-Office-UI](https://github.com/ringhyacinth/Star-Office-UI)（「依狀態走到對應區域」的點子；其美術素材禁止商用，不採用任何素材與程式碼）

## 1. 目標與範圍

每個部門有一間 2D 像素風的虛擬辦公室，成員以角色在地圖上走動、互動，並能看到同事的上線狀態。

### 第一期包含

- 依部門成員自動產生的辦公室地圖（每人一個專屬座位、會議室、茶水間、入口）。
- 角色以格子為單位走動，同辦公室的人即時看到彼此位置。
- 全公司可串門子：同公司的人都能進入任何部門的辦公室，預設進入自己部門；公司之間完全隔離。
- 互動：狀態與表情、靠近即聊（文字）、敲門／呼叫。
- 上線狀態（上線、閒置、離線）在後台任何頁面都會維持，並提供共用元件 `PresenceDot` 供其他頁面顯示上線符號。

### 第一期不包含

- 靠近視訊（只在協定上預留，之後接 LiveKit／Jitsi）。
- 地圖編輯器、Tiled 匯入、走廊或傳送門（切換部門用頁面上的部門選單）。
- 聊天歷史保存、隱身模式、捏臉換裝。
- API 多實例部署（之後加 SignalR Redis backplane，程式介面不變）。

## 2. 架構

```
瀏覽器（後台任一頁）                       ASP.NET Core API
┌─────────────────────────────┐          ┌──────────────────────────────┐
│ AdminShell                  │          │ Modules/Office/              │
│  └ use-presence（全域連線）  │◄─SignalR─►│  OfficeHub   /api/hubs/office │
│     · 上線、閒置、心跳        │ WebSocket │  PresenceTracker（記憶體）    │
│     · 收敲門通知             │          │  OfficeMapService（產生地圖） │
│                             │          │  OfficeController（REST）     │
│ /office 頁（Vue 島嶼）       │          └──────────┬───────────────────┘
│  └ Phaser 3（動態載入）       │                     │ EF Core
│     · 地圖、角色、走動、聊天    │                     ▼
└─────────────────────────────┘          MariaDB：office_rooms、office_profiles
```

### 2.1 後端 `Modules/Office/`

| 元件 | 職責 |
|---|---|
| `OfficeHub` | SignalR hub，路徑 `/api/hubs/office`。驗證沿用現有 JWT；僅此路徑接受 query `access_token`（於 `JwtBearerEvents.OnMessageReceived` 判斷）。連線依 JWT 的 `company_id` 加入公司群組 `company:{companyId}`，進入辦公室時加入 `office:{companyId}:{departmentId}`。 |
| `PresenceTracker` | Singleton，記憶體保存每個 membership 的連線集合、最後操作時間、手動狀態、所在辦公室與格子座標。多分頁視為同一人。 |
| `OfficeMapService` | 依部門成員產生地圖（見第 3 節），無副作用、可單元測試。 |
| `OfficeController` | REST：辦公室列表與上線人數、取得地圖、上線狀態快照、讀寫自己的 `office_profiles`、管理員設定部門主題。 |
| `OfficeAuthSweeper` | `BackgroundService`，每 60 秒以單次查詢（`WHERE Id IN (...)`）比對已連線使用者的 `AuthVersion`、帳號狀態與公司狀態，不一致者送出 `ForceDisconnect` 後斷線。 |
| `PresenceSweeper` | 每 5 秒巡檢（純記憶體運算）：最後操作超過 5 分鐘者改為閒置；斷線超過 15 秒寬限期者改為離線並寫入 `LastSeenAt`。與 `OfficeAuthSweeper` 合併為 `OfficeSweeper`；AuthVersion 巡檢間隔可由 `Quote__OfficeAuthSweepSeconds` 調整（預設 60）。 |

長連線不會在每個請求重新驗證 JWT，因此 `OfficeAuthSweeper` 是「帳號停用、權限異動立即失效」原則在即時連線上的實作。

### 2.2 資料表

兩張表皆實作 `ICompanyOwned`，由 Global Query Filter 過濾、`SaveChanges` 攔截器填入 `CompanyId`。

**`office_rooms`**（一個部門一筆，管理員第一次設定主題時建立；沒有資料列時主題視為 `wood`）

| 欄位 | 說明 |
|---|---|
| `Id` | 主鍵 |
| `CompanyId` | 公司 |
| `DepartmentId` | 部門，唯一索引 `(CompanyId, DepartmentId)` |
| `Theme` | 主題鍵，例如 `wood`、`carpet`、`dark`；預設 `wood` |
| `CreatedAt`、`UpdatedAt` | 時間戳記（UTC） |

之後的房間層級設定（例如是否開啟視訊）放在此表，不修改 `departments`。

**`office_profiles`**（一個 membership 一筆，第一次使用時建立）

| 欄位 | 說明 |
|---|---|
| `Id` | 主鍵 |
| `CompanyId` | 公司 |
| `MembershipId` | membership，唯一索引 |
| `AvatarKey` | 角色鍵；未設定時以 userId 雜湊選出固定的預設角色 |
| `Status` | 手動狀態：`available`（有空）／`busy`（忙碌）／`meeting`（開會中）／`break`（休息中）／`away`（外出），預設 `available` |
| `StatusText` | 自訂狀態文字，最多 40 字 |
| `LastSeenAt` | 最後在線時間（轉為離線時寫入） |
| `UpdatedAt` | 時間戳記（UTC） |

走動、表情、聊天、上線狀態本身都不寫資料庫。

### 2.3 前端

| 項目 | 說明 |
|---|---|
| `@microsoft/signalr` | 新增至 `apps/web` 相依套件。 |
| `phaser` | 新增至 `apps/web` 相依套件；只在 `/office` 以動態 `import()` 載入。 |
| `use-presence.ts` | 由 `AdminShell` 建立全域 hub 連線；送出 `Activity`；維護上線狀態對照表並提供 `presenceOf(membershipId)`；接收 `Knocked` 並顯示全域通知；處理 `ForceDisconnect`。辦公室頁共用同一條連線。 |
| `PresenceDot.vue` | 統一繪製上線狀態圓點與手動狀態圖示。 |
| `/office` 頁 | Vue 島嶼負責部門切換、側邊成員清單、聊天面板、個人設定（角色、狀態）；Phaser 場景負責地圖、角色、補間動畫、對話泡泡、點擊尋路。 |
| 素材 | Kenney CC0 室內與角色素材，放在 `apps/web/public/office/`，各素材包的 `License.txt` 一併保存。地圖 JSON 只存語意化的圖塊鍵，前端以對照表轉成素材圖塊，日後換素材只改對照表。 |

### 2.4 權限

新增權限碼 `office`（只用 read），選單路徑 `/office`；無此權限者看不到選單，hub 連線時亦拒絕。既有角色由 Seeder 在 `office` 第一次寫入權限目錄時補上 read，同時遞增相關使用者的 `AuthVersion` 並寫 audit；正式環境部署此版本時必須執行一次 Seeder（`--seed-only` 或 `BOOTSTRAP_ON_START=true`）。部門主題設定沿用部門管理的編輯權限（實作時對照 `PermissionCatalog.cs` 的既有權限碼）。依 `AGENTS.md` 同步更新：

- `Infrastructure/PermissionCatalog.cs`
- `src/shared/types/permissions.ts` 的 `PermissionCode`
- `src/app/route-manifest.ts`
- `docs/permissions-and-data-scope.md`

### 2.5 部署

`infra/nginx/default.conf` 的 `/api/` 加上 WebSocket upgrade 標頭（`Upgrade`、`Connection`、適當的 `proxy_read_timeout`）。第一期假設 API 單一實例。

## 3. 地圖自動產生

### 3.1 輸入與穩定性

- 輸入：部門內啟用中的 membership（僅直屬成員，不含下層部門），依 membership 建立時間排序。
- 同樣的輸入必定產生同樣的地圖，不另存地圖檔。
- 新成員排在最後，原有成員的座位不變；成員離開時，其後成員的座位會往前遞補（第一期接受此行為）。

### 3.2 版面

```
┌───────────────────────────────────┐
│  茶水間      │      會議室          │  會議室：≤8 人小間，超過則加大
│  (pantry)   │     (meeting)        │
├──────┐      └───門───┘             │
│ 入口  │   ▣▣  ▣▣  ▣▣               │  桌位：4 人一組（兩兩對坐）
│(spawn)│   ▣▣  ▣▣  ▣▣               │  一排 3 組，人多往下加排
└──門───┘   ▣▣  ▣▣  …                │
└───────────────────────────────────┘
```

- 圖塊尺寸 16px，前端依畫面放大整數倍顯示。
- 每人一張專屬座位；座位主人不在場時，空椅子上淡淡顯示名字。
- 人數不設上限，地圖大於畫面時鏡頭跟隨自己的角色。
- 部門無成員時仍產生含入口、會議室、茶水間的空辦公室。

### 3.3 輸出格式

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

`zones[].type` 為 `desk`／`meeting`／`pantry`／`entrance`。後端以同一份地圖驗證移動與計算聊天收訊對象。

### 3.4 角色外觀

素材包中 8～12 個預設角色供成員選擇（存 `office_profiles.AvatarKey`）；未選擇者依 userId 雜湊得到固定的預設角色。

## 4. 即時協定

原則：前端只送意圖，後端驗證後才轉發；座標、對象、文字一律不信任前端。

### 4.1 前端 → 後端

| 方法 | 說明與驗證 |
|---|---|
| `JoinOffice(departmentId)` | 確認部門屬於 JWT 公司；離開前一間辦公室；回傳地圖與在場者快照（membershipId、姓名、角色、座標、朝向、上線狀態、手動狀態）。角色出現在入口。 |
| `LeaveOffice()` | 離開目前辦公室群組。 |
| `Move(x, y, facing)` | 必須為相鄰格、可通行；每連線每秒最多 10 次。通過則廣播 `Moved`，否則回 `PositionCorrected`。 |
| `GoToSeat()` | 後端將角色放到自己的座位（座位被占則放到最近空格）。 |
| `SetAvatar(avatarKey)` | 寫入 `office_profiles` 並廣播 `AvatarChanged`。 |
| `SetStatus(status, text?)` | `status` 必須在五種之內，`text` 最多 40 字；寫入 `office_profiles`，廣播給公司群組。 |
| `Emote(key)` | `key` 必須在固定清單（👋 ☕ 👍 🎉 ❓）；每秒最多 1 次；廣播給辦公室群組，前端顯示 3 秒。 |
| `SendChat(text)` | 最多 500 字、每秒最多 2 則；收訊對象由後端計算（見 4.3）。 |
| `Knock(targetMembershipId, message?)` | 敲門者須已在辦公室內；對象不存在、不同公司、離線一律回「找不到此成員或對方不在線上。」；同一對象 30 秒內限一次；送到對方所有連線。 |
| `AnswerKnock(fromMembershipId, accept, message?)` | 必須對應 5 分鐘內的敲門，否則回「沒有待回應的敲門，可能已經逾時。」；接受時後端將回應者放到敲門者旁的空格；拒絕時回傳附言。 |
| `Activity()` | 前端偵測到操作時最多每 60 秒送一次，更新最後操作時間。 |

前端走動方式：方向鍵／WASD 一次一格；點擊地圖任一格時在 `collision` 上以 BFS 找路，逐步送出 `Move`。

依狀態自動走位：在辦公室內將狀態設為 `meeting` 時自動走到會議室、設為 `break` 時走到茶水間（同一套 BFS）。

### 4.2 後端 → 前端

| 對象 | 事件 |
|---|---|
| 辦公室群組 | `OccupantJoined`、`OccupantLeft`、`Moved`、`Emoted` |
| 計算出的收訊者 | `ChatReceived` |
| 公司群組 | `PresenceChanged`（狀態變更併入此事件）、`AvatarChanged` |
| 個人 | `PositionCorrected`、`Knocked`、`KnockAnswered`、`ForceDisconnect`、`OfficeSessionReplaced`（同一人在另一個分頁進入辦公室時，舊分頁收到） |

其他成員收到 `Moved` 時以 150ms 補間移動角色。

### 4.3 聊天收訊對象

- 發送者在 `desk`／`entrance` 區：同辦公室中切比雪夫距離 ≤ 2 格的人。
- 發送者在 `meeting`／`pantry` 區：同一區塊內的所有人。
- 訊息以頭上泡泡與側邊聊天面板呈現；附近有人時提示「可以聊天」並列出會收到的人。
- 文字以純文字呈現，不解析 HTML；不存資料庫，重新整理即清空。

### 4.4 敲門流程

1. A 呼叫 `Knock(B)`；後端確認同公司、B 在線、未超過頻率。
2. B 的所有連線收到 `Knocked`（含 A 的姓名、所在部門），在後台任何頁面顯示通知。
3. B 選「前往」：若不在辦公室頁，前端導向 `/office?dept={A 的部門}`；`JoinOffice` 後呼叫 `AnswerKnock(A, true)`，後端將 B 放到 A 旁空格。
4. B 選「稍後」：A 收到 `KnockAnswered`（含附言）。

### 4.5 視訊預留

聊天收訊對象的計算結果（同一區塊或附近的人）日後直接作為 LiveKit 房間成員的依據，協定不需調整。

## 5. 上線狀態

以 membership（即 JWT 的公司）為單位；同一帳號在 A 公司上線，不代表在 B 公司上線。

| 狀態 | 條件 |
|---|---|
| 上線 | 至少一條連線，且 5 分鐘內有操作 |
| 閒置 | 有連線，但所有分頁 5 分鐘內皆無操作 |
| 離線 | 無連線超過 15 秒寬限期 |

- 手動狀態（忙碌、開會中、休息中、外出）優先於自動狀態顯示；`available` 時只顯示自動狀態。
- 15 秒寬限期避免重新整理或網路瞬斷造成狀態閃爍。
- 轉為離線時寫入 `office_profiles.LastSeenAt`，離線者顯示「x 小時前在線」。
- 同公司所有人皆可看到彼此的上線狀態；第一期不做隱身。
- `GET /api/office/presence` 回傳公司內目前上線與閒置者（以 membershipId 為鍵的對照表，非列表 UI，不分頁），之後由 `PresenceChanged` 增量更新。

## 6. 錯誤處理

### 6.1 連線

- 自動重連間隔 0、2、5、10、30 秒，之後每 30 秒重試；期間顯示「連線中斷，重新連線中…」，地圖凍結不清空。
- 重連後重新 `JoinOffice` 取得快照，回到斷線前的位置；該格被占則放到最近空格。
- Token 過期時先走現有 refresh 流程，失敗則導回登入頁。
- 收到 `ForceDisconnect`（帳號停用、`AuthVersion` 變更、公司停權、持續超量）時不自動重連，導回登入頁或顯示原因。

### 6.2 操作被拒

- 移動不合法：回 `PositionCorrected`，前端靜默拉回。
- 超過頻率：丟棄多餘訊息；同一連線 10 秒內被擋 50 次即斷開。
- 聊天、敲門失敗以 `HubException` 回傳繁體中文原因，前端取出訊息顯示（例如「對方已離線」「請稍候 30 秒再敲門」「訊息超過 500 字」），前端顯示輕量提示。
- 對象不屬於同公司時一律回「找不到此成員」，不透露其他公司資料。

### 6.3 地圖與素材

- 素材載入失敗時以色塊替代圖塊並提示「部分素材載入失敗」，功能照常。

## 7. 效能

- Phaser 只在 `/office` 動態載入；畫面上限 30 fps；分頁進入背景時停止繪圖；長時間無變化時降低更新頻率。
- 事件只轉發給同辦公室；公司群組只收上線與狀態變化。
- 走動、表情、聊天不寫資料庫；只有離線轉換與個人設定變更才寫入。
- `AuthVersion` 巡檢每 60 秒合併為一次查詢。
- 估算：20 人同辦公室同時持續走動約每秒 2,000 則、約 100 KB/s 的轉發量，單一 API 實例可承受。

## 8. 測試

### 8.1 `npm run test:api`（`--self-test`）

- 地圖產生：相同輸入相同輸出；座位數等於人數；入口可走到每個座位與每個區塊；新增成員後原有座位不變；零成員可產生空辦公室。
- 移動驗證：相鄰可走；斜走、跳格、穿牆被拒。
- 聊天收訊：距離 2 格內、同區塊、跨區塊各情境。
- 上線狀態：上線 → 閒置 → 離線；寬限期內重連不轉離線；多分頁視為一人。

### 8.2 `tests/office-smoke.mjs`（新增 `npm run test:smoke:office`）

以 Node 24 內建 WebSocket 搭配簡易 SignalR JSON 協定 helper，不新增測試相依套件。驗證：

- 同公司兩人可看到彼此走動；另一公司的連線收不到任何事件。
- 聊天只送達附近或同區塊的人。
- 敲門送達對方所有連線，接受後位置正確。
- 帳號停用後 60 秒內連線被斷開。
- 無 `office:read` 權限者無法連線。

涉及授權，另跑既有的 `npm run test:smoke`。

### 8.3 其他

- 前端：`npm run check:web`、`npm run build:web`；以內建瀏覽器開兩個分頁、登入不同帳號，實際確認走動、聊天、敲門與上線符號。
- API：`npm run build:api` 0 警告。
- Schema：產生 migration，同步更新 `docs/database-planning.md`、`docs/database-schema.sql`，以空資料庫與既有資料庫各驗證一次。
- 文件：`docs/system-architecture.md` 補上即時連線層，`docs/permissions-and-data-scope.md` 加入 `office`。
