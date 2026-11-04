-- 專案報價管理系統 MariaDB DDL
-- 用途：作為 EF Core entity 與 migration 的設計依據；正式 schema 以 migration 為準。
-- 適用：MariaDB 10.11+／11.x
-- 慣例：Guid = CHAR(36)、UTC 時間 = DATETIME(6)、金額 = DECIMAL(18,2)、數量／單價 = DECIMAL(18,4)
-- 部署：建立在美達特共用 MariaDB（shared-mariadb／central-mariadb）上，不另建 container；
--       資料庫與 quote_app 帳號由 deploy/ensure-shared-db 建立，見 docs/system-architecture.md 第 9 節。

CREATE DATABASE IF NOT EXISTS quote_platform
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE quote_platform;

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 1;

-- =========================================================
-- 1. 公司與身分
-- =========================================================

CREATE TABLE companies (
  Id                                CHAR(36)      NOT NULL COMMENT '公司唯一識別碼',
  Name                              VARCHAR(200)  NOT NULL COMMENT '公司名稱',
  Slug                              VARCHAR(100)  NOT NULL COMMENT '公司英文縮寫／網址代稱（全系統唯一）',
  TaxId                             VARCHAR(20)   NULL COMMENT '公司統一編號',
  Phone                             VARCHAR(50)   NULL COMMENT '公司代表電話',
  Address                           VARCHAR(500)  NULL COMMENT '公司營業登記地址',
  EnglishName                       VARCHAR(200)  NULL COMMENT '報價單抬頭英文名稱',
  LogoFileId                        CHAR(36)      NULL COMMENT '報價單抬頭 Logo 檔案識別碼（files.Id）',
  Status                            VARCHAR(20)   NOT NULL DEFAULT 'active' COMMENT '公司營運狀態（active / suspended）',
  RootAdminMembershipId             CHAR(36)      NULL COMMENT '受保護的主管理員 Membership 識別碼',
  IsRoleCrudConfigurationEnabled    TINYINT(1)    NOT NULL DEFAULT 1 COMMENT '是否啟用角色 CRUD 權限配置',
  IsRoleOptionConfigurationEnabled  TINYINT(1)    NOT NULL DEFAULT 1 COMMENT '是否啟用角色進階子功能選項配置',
  CreatedAt                         DATETIME(6)   NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt                         DATETIME(6)   NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_companies_Slug (Slug),
  UNIQUE KEY UX_companies_RootAdminMembershipId (RootAdminMembershipId)
) ENGINE=InnoDB COMMENT='公司主檔（登入租戶與資料隔離單位）';

CREATE TABLE company_settings (
  CompanyId             CHAR(36)      NOT NULL COMMENT '所屬公司識別碼（主鍵）',
  DefaultTaxRate        DECIMAL(5,2)  NOT NULL DEFAULT 5.00 COMMENT '預設營業稅率百分比（例如 5.00 代表 5%）',
  DefaultCurrency       CHAR(3)       NOT NULL DEFAULT 'TWD' COMMENT '預設計價幣別代碼（例如 TWD）',
  QuoteValidDays        INT           NOT NULL DEFAULT 30 COMMENT '報價單預設有效天數',
  DefaultTerms          TEXT          NULL COMMENT '報價單預設條款與說明文字（一條一行、不含編號）',
  PdfHeaderJson         LONGTEXT      NOT NULL DEFAULT '{}' CHECK (JSON_VALID(PdfHeaderJson)) COMMENT '匯出報價單 PDF 時之抬頭自訂設定 JSON',
  FiscalYearStartMonth  TINYINT       NOT NULL DEFAULT 1 CHECK (FiscalYearStartMonth BETWEEN 1 AND 12) COMMENT '會計年度起始月份（1~12），供 Dashboard 財務季報計算使用',
  QuoteNumberPattern    VARCHAR(40)   NOT NULL DEFAULT 'Q{yyyy}{MM}-{seq:4}' COMMENT '報價單號規則，見 system-architecture.md 6.7',
  UpdatedAt             DATETIME(6)   NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (CompanyId),
  CONSTRAINT FK_company_settings_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='公司層級業務設定';

CREATE TABLE roles (
  Id            CHAR(36)     NOT NULL COMMENT '角色唯一識別碼',
  CompanyId     CHAR(36)     NULL COMMENT '所屬公司識別碼（NULL 表示全系統內建角色）',
  Scope         VARCHAR(20)  NOT NULL DEFAULT 'company' COMMENT '適用範圍（platform / company）',
  Code          VARCHAR(100) NOT NULL COMMENT '角色代碼',
  Name          VARCHAR(100) NOT NULL COMMENT '角色顯示名稱',
  DataScope     VARCHAR(20)  NOT NULL DEFAULT 'own' COMMENT '預設資料可見性範圍（own / department / department_tree / company）',
  IsSystem      TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否為系統預設內建角色',
  IsFullAccess  TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否具備全功能存取權限（超級管理員標記）',
  CreatedBy     CHAR(36)     NULL COMMENT '自訂角色的建立者：只有建立者能在成員表單選用；系統角色與舊資料為 NULL',
  CreatedAt     DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt     DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  -- NULL 不觸發 unique，系統內建角色（CompanyId IS NULL）的 Code 唯一由 Seeder 與 Service 保證
  UNIQUE KEY UX_roles_CompanyId_Code (CompanyId, Code),
  CONSTRAINT FK_roles_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT CK_roles_Scope CHECK (Scope IN ('platform', 'company')),
  CONSTRAINT CK_roles_DataScope CHECK (DataScope IN ('own', 'department', 'department_tree', 'company'))
) ENGINE=InnoDB COMMENT='角色定義檔（支援平台共用角色與公司自訂角色）';

CREATE TABLE permissions (
  Id               CHAR(36)     NOT NULL COMMENT '權限目錄唯一識別碼',
  Code             VARCHAR(100) NOT NULL COMMENT '權限資源代碼',
  Name             VARCHAR(100) NOT NULL COMMENT '功能或資源名稱',
  Description      VARCHAR(500) NOT NULL DEFAULT '' COMMENT '功能描述說明',
  Scope            VARCHAR(20)  NOT NULL DEFAULT 'company' COMMENT '權限範圍層級（platform / company）',
  ParentId         CHAR(36)     NULL COMMENT '上層選單或模組節點識別碼',
  SortOrder        INT          NOT NULL DEFAULT 0 COMMENT '選單或清單排序順序編號',
  IsMenu           TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否作為左側導覽選單項展示',
  RoutePath        VARCHAR(200) NULL COMMENT '前端路由路徑',
  SubFeaturesJson  LONGTEXT     NOT NULL DEFAULT '[]' CHECK (JSON_VALID(SubFeaturesJson)) COMMENT '支援之進階子功能選項定義 JSON',
  CreatedAt        DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_permissions_Code (Code),
  KEY IX_permissions_ParentId (ParentId),
  CONSTRAINT FK_permissions_parent FOREIGN KEY (ParentId) REFERENCES permissions (Id) ON DELETE RESTRICT
) ENGINE=InnoDB COMMENT='系統權限目錄與選單架構定義檔';

CREATE TABLE role_permissions (
  RoleId        CHAR(36)    NOT NULL COMMENT '角色識別碼',
  PermissionId  CHAR(36)    NOT NULL COMMENT '權限資源識別碼',
  PerCreate     TINYINT(1)  NOT NULL DEFAULT 0 COMMENT '是否具備建立權限（Create）',
  PerRead       TINYINT(1)  NOT NULL DEFAULT 0 COMMENT '是否具備讀取檢視權限（Read）',
  PerUpdate     TINYINT(1)  NOT NULL DEFAULT 0 COMMENT '是否具備編輯修改權限（Update）',
  PerDelete     TINYINT(1)  NOT NULL DEFAULT 0 COMMENT '是否具備刪除權限（Delete）',
  OptionsJson   LONGTEXT    NOT NULL DEFAULT '[]' CHECK (JSON_VALID(OptionsJson)) COMMENT '已啟用的進階子功能項目 JSON 陣列',
  PRIMARY KEY (RoleId, PermissionId),
  KEY IX_role_permissions_PermissionId (PermissionId),
  CONSTRAINT FK_role_permissions_roles FOREIGN KEY (RoleId) REFERENCES roles (Id) ON DELETE CASCADE,
  CONSTRAINT FK_role_permissions_permissions FOREIGN KEY (PermissionId) REFERENCES permissions (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='角色權限關聯表（CRUD 權限與進階子功能選項）';

CREATE TABLE users (
  Id                CHAR(36)     NOT NULL COMMENT '使用者唯一識別碼',
  Account           VARCHAR(100) NOT NULL COMMENT '登入帳號名稱（全系統唯一）',
  Email             VARCHAR(200) NULL COMMENT '電子郵件信箱',
  DisplayName       VARCHAR(100) NOT NULL COMMENT '使用者顯示姓名',
  PasswordHash      VARCHAR(500) NOT NULL COMMENT 'BCrypt 密碼雜湊值',
  Status            VARCHAR(20)  NOT NULL DEFAULT 'active' COMMENT '帳號狀態（active / disabled）',
  SystemRoleId      CHAR(36)     NULL COMMENT '只指向 Scope = platform 的角色',
  AuthVersion       INT          NOT NULL DEFAULT 1 COMMENT 'Token 撤銷版本；異動時遞增使舊 access token 立即失效',
  FailedLoginCount  INT          NOT NULL DEFAULT 0 COMMENT '連續登入失敗次數',
  LockedUntil       DATETIME(6)  NULL COMMENT '帳號暫時鎖定截止時間戳記',
  LastLoginAt       DATETIME(6)  NULL COMMENT '最後成功登入時間戳記',
  CreatedAt         DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt         DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_users_Account (Account),
  KEY IX_users_Email (Email),
  CONSTRAINT FK_users_system_role FOREIGN KEY (SystemRoleId) REFERENCES roles (Id) ON DELETE SET NULL
) ENGINE=InnoDB COMMENT='系統使用者帳號主檔';

CREATE TABLE refresh_tokens (
  Id            CHAR(36)     NOT NULL COMMENT '權杖唯一識別碼',
  UserId        CHAR(36)     NOT NULL COMMENT '所屬使用者識別碼',
  CompanyId     CHAR(36)     NULL COMMENT '簽發時的 scope；NULL = platform',
  TokenHash     CHAR(64)     NOT NULL COMMENT 'SHA-256 hex，不存明文',
  ExpiresAt     DATETIME(6)  NOT NULL COMMENT '權杖有效截止時間戳記（UTC）',
  RevokedAt     DATETIME(6)  NULL COMMENT '權杖撤銷時間戳記（UTC）',
  ReplacedById  CHAR(36)     NULL COMMENT '接替本 Token 之新 Token 識別碼（輪替使用）',
  CreatedIp     VARCHAR(64)  NULL COMMENT '簽發時客戶端 IP 位址',
  UserAgent     VARCHAR(500) NULL COMMENT '簽發時客戶端 User-Agent',
  CreatedAt     DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_refresh_tokens_TokenHash (TokenHash),
  KEY IX_refresh_tokens_UserId_ExpiresAt (UserId, ExpiresAt),
  CONSTRAINT FK_refresh_tokens_users FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='使用者 Refresh Token 儲存表（保存雜湊、支援輪替與撤銷）';

CREATE TABLE departments (
  Id         CHAR(36)     NOT NULL COMMENT '部門唯一識別碼',
  CompanyId  CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  ParentId   CHAR(36)     NULL COMMENT '上層父部門識別碼（NULL 表示頂層部門）',
  Code       VARCHAR(50)  NULL COMMENT '部門代碼',
  Name       VARCHAR(100) NOT NULL COMMENT '部門名稱',
  SortOrder  INT          NOT NULL DEFAULT 0 COMMENT '排序順序編號',
  IsActive   TINYINT(1)   NOT NULL DEFAULT 1 COMMENT '是否啟用運作中',
  CreatedAt  DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt  DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_departments_CompanyId_Code (CompanyId, Code),
  KEY IX_departments_CompanyId_ParentId (CompanyId, ParentId),
  CONSTRAINT FK_departments_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_departments_parent FOREIGN KEY (ParentId) REFERENCES departments (Id) ON DELETE RESTRICT
) ENGINE=InnoDB COMMENT='公司組織部門階層檔';

CREATE TABLE department_closure (
  AncestorId    CHAR(36)  NOT NULL COMMENT '祖先部門識別碼',
  DescendantId  CHAR(36)  NOT NULL COMMENT '後代部門識別碼',
  CompanyId     CHAR(36)  NOT NULL COMMENT '所屬公司識別碼',
  Depth         INT       NOT NULL COMMENT '樹階層深度差（0 表示節點本身）',
  PRIMARY KEY (AncestorId, DescendantId),
  KEY IX_department_closure_AncestorId_Depth (AncestorId, Depth),
  KEY IX_department_closure_DescendantId_Depth (DescendantId, Depth),
  CONSTRAINT FK_department_closure_ancestor FOREIGN KEY (AncestorId) REFERENCES departments (Id) ON DELETE CASCADE,
  CONSTRAINT FK_department_closure_descendant FOREIGN KEY (DescendantId) REFERENCES departments (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='部門閉包表（Closure Table，供樹狀部門階層查詢與資料範圍過濾）';

CREATE TABLE company_memberships (
  Id                 CHAR(36)    NOT NULL COMMENT '成員身分關係唯一識別碼',
  CompanyId          CHAR(36)    NOT NULL COMMENT '所屬公司識別碼',
  UserId             CHAR(36)    NOT NULL COMMENT '關聯使用者識別碼',
  DepartmentId       CHAR(36)    NULL COMMENT '所屬部門識別碼',
  RoleId             CHAR(36)    NOT NULL COMMENT '指派角色識別碼',
  DataScopeOverride  VARCHAR(20) NULL COMMENT '個別資料範圍覆寫（NULL 表示採用角色的預設 DataScope）',
  IsActive           TINYINT(1)  NOT NULL DEFAULT 1 COMMENT '此公司身分是否啟用生效',
  CreatedAt          DATETIME(6) NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt          DATETIME(6) NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_company_memberships_CompanyId_UserId (CompanyId, UserId),
  KEY IX_company_memberships_UserId (UserId),
  KEY IX_company_memberships_CompanyId_DepartmentId (CompanyId, DepartmentId),
  CONSTRAINT FK_company_memberships_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_company_memberships_users FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
  CONSTRAINT FK_company_memberships_departments FOREIGN KEY (DepartmentId) REFERENCES departments (Id) ON DELETE SET NULL,
  CONSTRAINT FK_company_memberships_roles FOREIGN KEY (RoleId) REFERENCES roles (Id) ON DELETE RESTRICT,
  CONSTRAINT CK_company_memberships_DataScope CHECK (DataScopeOverride IS NULL OR DataScopeOverride IN ('own', 'department', 'department_tree', 'company'))
) ENGINE=InnoDB COMMENT='公司成員身分表（對應使用者在特定公司的部門、角色與資料範圍）';

CREATE TABLE user_permissions (
  Id            CHAR(36)     NOT NULL COMMENT '個人特例權限唯一識別碼',
  CompanyId     CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  UserId        CHAR(36)     NOT NULL COMMENT '目標使用者識別碼',
  PermissionId  CHAR(36)     NOT NULL COMMENT '權限資源識別碼',
  Effect        VARCHAR(10)  NOT NULL COMMENT '授權效果（grant 額外授權／deny 特別禁止）',
  PerCreate     TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否具備建立權限（Create）',
  PerRead       TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否具備讀取檢視權限（Read）',
  PerUpdate     TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否具備編輯修改權限（Update）',
  PerDelete     TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否具備刪除權限（Delete）',
  OptionsJson   LONGTEXT     NOT NULL DEFAULT '[]' CHECK (JSON_VALID(OptionsJson)) COMMENT '已設定之進階子功能選項 JSON 陣列',
  Reason        VARCHAR(500) NULL COMMENT '特例授權原因備註',
  ExpiresAt     DATETIME(6)  NULL COMMENT '權限有效截止時間戳記（到期自動失效）',
  CreatedBy     CHAR(36)     NOT NULL COMMENT '授權操作者使用者識別碼',
  CreatedAt     DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt     DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_user_permissions_scope (CompanyId, UserId, PermissionId, Effect),
  KEY IX_user_permissions_PermissionId (PermissionId),
  CONSTRAINT FK_user_permissions_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_user_permissions_users FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
  CONSTRAINT FK_user_permissions_permissions FOREIGN KEY (PermissionId) REFERENCES permissions (Id) ON DELETE CASCADE,
  CONSTRAINT CK_user_permissions_Effect CHECK (Effect IN ('grant', 'deny'))
) ENGINE=InnoDB COMMENT='個人權限特例覆寫表（加權授予或特定禁止）';

CREATE TABLE audit_logs (
  Id           CHAR(36)     NOT NULL COMMENT '稽核紀錄唯一識別碼',
  CompanyId    CHAR(36)     NULL COMMENT '操作當下所屬公司識別碼（NULL 表示平台層）',
  ActorUserId  CHAR(36)     NULL COMMENT '執行操作之使用者識別碼',
  EntityType   VARCHAR(50)  NOT NULL COMMENT '異動資料實體類型名稱',
  EntityId     CHAR(36)     NULL COMMENT '異動資料實體識別碼',
  Action       VARCHAR(100) NOT NULL COMMENT '操作動作代碼（例如 create、update、delete、login、void）',
  BeforeJson   LONGTEXT     NULL CHECK (BeforeJson IS NULL OR JSON_VALID(BeforeJson)) COMMENT '異動前資料快照 JSON',
  AfterJson    LONGTEXT     NULL CHECK (AfterJson IS NULL OR JSON_VALID(AfterJson)) COMMENT '異動後資料快照 JSON',
  Ip           VARCHAR(64)  NULL COMMENT '客戶端來源 IP 位址',
  CreatedAt    DATETIME(6)  NOT NULL COMMENT '操作發生時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_audit_logs_entity (CompanyId, EntityType, EntityId),
  KEY IX_audit_logs_CompanyId_CreatedAt (CompanyId, CreatedAt),
  KEY IX_audit_logs_ActorUserId (ActorUserId)
) ENGINE=InnoDB COMMENT='系統稽核操作日誌（不設外鍵，資料刪除後永久保留）';

-- =========================================================
-- 2. 檔案
-- =========================================================

CREATE TABLE files (
  Id            CHAR(36)     NOT NULL COMMENT '檔案唯一識別碼',
  CompanyId     CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  StorageKey    VARCHAR(500) NOT NULL COMMENT '儲存路徑 Key：{companyId}/{yyyy}/{MM}/{fileId}',
  OriginalName  VARCHAR(255) NOT NULL COMMENT '原始上傳檔案名稱',
  ContentType   VARCHAR(150) NOT NULL COMMENT 'MIME 內容型態（Content-Type）',
  Size          BIGINT       NOT NULL COMMENT '檔案大小（Bytes）',
  Sha256        CHAR(64)     NULL COMMENT '內容 SHA-256 雜湊碼',
  Status        VARCHAR(20)  NOT NULL DEFAULT 'uploading' COMMENT '檔案狀態（uploading / ready / deleted）',
  UploadedBy    CHAR(36)     NOT NULL COMMENT '上傳者使用者識別碼',
  CreatedAt     DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  DeletedAt     DATETIME(6)  NULL COMMENT '標記刪除時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_files_StorageKey (StorageKey),
  KEY IX_files_CompanyId_Status (CompanyId, Status),
  CONSTRAINT FK_files_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE RESTRICT
) ENGINE=InnoDB COMMENT='檔案中繼資料檔（檔案本體存放於 MinIO 或檔案系統）';

CREATE TABLE upload_sessions (
  Id           CHAR(36)      NOT NULL COMMENT '上傳階段唯一識別碼',
  CompanyId    CHAR(36)      NOT NULL COMMENT '所屬公司識別碼',
  FileId       CHAR(36)      NOT NULL COMMENT '關聯檔案中繼識別碼',
  FileSize     BIGINT        NOT NULL COMMENT '預計檔案總大小（Bytes）',
  ChunkSize    INT           NOT NULL COMMENT '分塊大小（Bytes）',
  TotalChunks  INT           NOT NULL COMMENT '總分塊數量',
  TokenHash    CHAR(64)      NOT NULL COMMENT '簽發上傳 Token 之雜湊碼',
  Status       VARCHAR(20)   NOT NULL DEFAULT 'pending' COMMENT '上傳狀態（pending / uploading / completed / cancelled / failed / expired）',
  ExpiresAt    DATETIME(6)   NOT NULL COMMENT '階段過期時間戳記（UTC）',
  CompletedAt  DATETIME(6)   NULL COMMENT '上傳完成時間戳記（UTC）',
  CancelledAt  DATETIME(6)   NULL COMMENT '取消上傳時間戳記（UTC）',
  LastError    VARCHAR(1000) NULL COMMENT '最後錯誤訊息摘要',
  CreatedAt    DATETIME(6)   NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt    DATETIME(6)   NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_upload_sessions_FileId (FileId),
  KEY IX_upload_sessions_CompanyId_Status (CompanyId, Status),
  CONSTRAINT FK_upload_sessions_files FOREIGN KEY (FileId) REFERENCES files (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='分塊上傳作業階段紀錄表';

CREATE TABLE attachments (
  Id         CHAR(36)     NOT NULL COMMENT '附件唯一識別碼',
  CompanyId  CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  OwnerType  VARCHAR(20)  NOT NULL COMMENT '掛載擁有者單據類型（project / quote / order / warranty / invoice / client）',
  OwnerId    CHAR(36)     NOT NULL COMMENT '掛載擁有者實體識別碼（多型關聯，由 Service 驗證資料範圍）',
  FileId     CHAR(36)     NOT NULL COMMENT '關聯檔案識別碼',
  Category   VARCHAR(50)  NULL COMMENT '附件業務分類標籤（例如 po_scan / signed_quote / warranty_doc / invoice_scan）',
  Note       VARCHAR(500) NULL COMMENT '備註說明',
  SortOrder  INT          NOT NULL DEFAULT 0 COMMENT '排序順序編號',
  CreatedBy  CHAR(36)     NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt  DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_attachments_owner (CompanyId, OwnerType, OwnerId),
  KEY IX_attachments_FileId (FileId),
  CONSTRAINT FK_attachments_files FOREIGN KEY (FileId) REFERENCES files (Id) ON DELETE RESTRICT,
  CONSTRAINT CK_attachments_OwnerType CHECK (OwnerType IN ('project', 'quote', 'order', 'warranty', 'invoice', 'client'))
) ENGINE=InnoDB COMMENT='單據附件多型關聯表';

-- =========================================================
-- 3. 客戶與品項
-- =========================================================

CREATE TABLE clients (
  Id              CHAR(36)     NOT NULL COMMENT '客戶唯一識別碼',
  CompanyId       CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  Code            VARCHAR(50)  NOT NULL COMMENT '客戶代碼（公司內唯一）',
  Name            VARCHAR(200) NOT NULL COMMENT '客戶全稱',
  ShortName       VARCHAR(100) NULL COMMENT '客戶簡稱',
  TaxId           VARCHAR(20)  NULL COMMENT '統一編號',
  ParentClientId  CHAR(36)     NULL COMMENT '母公司／集團關聯識別碼',
  Industry        VARCHAR(100) NULL COMMENT '產業別',
  Phone           VARCHAR(50)  NULL COMMENT '公司代表電話',
  Fax             VARCHAR(50)  NULL COMMENT '傳真號碼',
  Email           VARCHAR(200) NULL COMMENT '電子郵件信箱',
  Address         VARCHAR(500) NULL COMMENT '公司營業地址',
  InvoiceTitle    VARCHAR(200) NULL COMMENT '發票抬頭',
  InvoiceAddress  VARCHAR(500) NULL COMMENT '發票寄送地址',
  PaymentTerms    VARCHAR(200) NULL COMMENT '付款條件說明（例如月結 30 天）',
  OwnerUserId     CHAR(36)     NULL COMMENT '業務負責人使用者識別碼',
  DepartmentId    CHAR(36)     NULL COMMENT '客戶所屬部門識別碼（決定資料範圍可見性）',
  Status          VARCHAR(20)  NOT NULL DEFAULT 'active' COMMENT '客戶狀態（active / inactive）',
  Note            TEXT         NULL COMMENT '備註說明',
  CreatedBy       CHAR(36)     NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt       DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedBy       CHAR(36)     NULL COMMENT '最後更新者使用者識別碼',
  UpdatedAt       DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  IsDeleted       TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否已軟刪除',
  DeletedAt       DATETIME(6)  NULL COMMENT '軟刪除時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_clients_CompanyId_Code (CompanyId, Code),
  KEY IX_clients_CompanyId_TaxId (CompanyId, TaxId),
  KEY IX_clients_CompanyId_Name (CompanyId, Name),
  KEY IX_clients_CompanyId_OwnerUserId (CompanyId, OwnerUserId),
  KEY IX_clients_CompanyId_DepartmentId (CompanyId, DepartmentId),
  CONSTRAINT FK_clients_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_clients_parent FOREIGN KEY (ParentClientId) REFERENCES clients (Id) ON DELETE SET NULL,
  CONSTRAINT FK_clients_owner FOREIGN KEY (OwnerUserId) REFERENCES users (Id) ON DELETE SET NULL,
  CONSTRAINT FK_clients_department FOREIGN KEY (DepartmentId) REFERENCES departments (Id) ON DELETE SET NULL
) ENGINE=InnoDB COMMENT='客戶主檔';

CREATE TABLE client_contacts (
  Id         CHAR(36)     NOT NULL COMMENT '聯絡人唯一識別碼',
  CompanyId  CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  ClientId   CHAR(36)     NOT NULL COMMENT '所屬客戶識別碼',
  Name       VARCHAR(100) NOT NULL COMMENT '聯絡人姓名',
  Title      VARCHAR(100) NULL COMMENT '職稱',
  Phone      VARCHAR(50)  NULL COMMENT '公司電話／分機',
  Mobile     VARCHAR(50)  NULL COMMENT '行動電話號碼',
  Email      VARCHAR(200) NULL COMMENT '電子郵件信箱',
  IsPrimary  TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否為主要聯絡人',
  SortOrder  INT          NOT NULL DEFAULT 0 COMMENT '排序順序編號',
  Note       VARCHAR(500) NULL COMMENT '備註說明',
  CreatedAt  DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt  DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_client_contacts_CompanyId_ClientId (CompanyId, ClientId),
  CONSTRAINT FK_client_contacts_clients FOREIGN KEY (ClientId) REFERENCES clients (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='客戶聯絡人資料表';

CREATE TABLE products (
  Id            CHAR(36)      NOT NULL COMMENT '產品唯一識別碼',
  CompanyId     CHAR(36)      NOT NULL COMMENT '所屬公司識別碼',
  Code          VARCHAR(50)   NOT NULL COMMENT '產品／品項代碼',
  Name          VARCHAR(200)  NOT NULL COMMENT '產品名稱',
  Spec          VARCHAR(500)  NULL COMMENT '產品規格描述',
  Unit          VARCHAR(20)   NULL COMMENT '計量單位（例如 台、式、組、月）',
  Category      VARCHAR(100)  NULL COMMENT '產品分類',
  DefaultPrice  DECIMAL(18,4) NOT NULL DEFAULT 0 COMMENT '預設參考銷售單價',
  DefaultCost   DECIMAL(18,4) NULL COMMENT '預設參考進貨成本',
  IsActive      TINYINT(1)    NOT NULL DEFAULT 1 COMMENT '是否啟用',
  CreatedAt     DATETIME(6)   NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt     DATETIME(6)   NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_products_CompanyId_Code (CompanyId, Code),
  KEY IX_products_CompanyId_Name (CompanyId, Name),
  CONSTRAINT FK_products_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE RESTRICT
) ENGINE=InnoDB COMMENT='標準產品與計價品項庫主檔';

-- =========================================================
-- 4. 專案
-- =========================================================

CREATE TABLE projects (
  Id              CHAR(36)      NOT NULL COMMENT '專案唯一識別碼',
  CompanyId       CHAR(36)      NOT NULL COMMENT '所屬公司識別碼',
  ProjectNo       VARCHAR(50)   NOT NULL COMMENT '專案編號',
  Name            VARCHAR(200)  NOT NULL COMMENT '專案名稱',
  ClientId        CHAR(36)      NOT NULL COMMENT '關聯客戶識別碼',
  DepartmentId    CHAR(36)      NULL COMMENT '承辦部門識別碼',
  OwnerUserId     CHAR(36)      NOT NULL COMMENT '專案負責人使用者識別碼',
  Status          VARCHAR(20)   NOT NULL DEFAULT 'draft' COMMENT '專案狀態（draft / quoting / won / lost / in_progress / completed / closed / cancelled）',
  StartDate       DATE          NULL COMMENT '專案預計或實際開始日期',
  EndDate         DATE          NULL COMMENT '專案預計或實際結束日期',
  ExpectedAmount  DECIMAL(18,2) NULL COMMENT '預估專案金額',
  ContractAmount  DECIMAL(18,2) NULL COMMENT '成交未稅金額（由接受的報價回寫）',
  ContractTotal   DECIMAL(18,2) NULL COMMENT '成交含稅總額',
  Currency        CHAR(3)       NOT NULL DEFAULT 'TWD' COMMENT '計價幣別代碼（預設 TWD）',
  Description     TEXT          NULL COMMENT '專案說明或目標描述',
  WonAt           DATETIME(6)   NULL COMMENT '得標成交確認時間戳記',
  ClosedAt        DATETIME(6)   NULL COMMENT '結案時間戳記',
  CreatedBy       CHAR(36)      NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt       DATETIME(6)   NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedBy       CHAR(36)      NULL COMMENT '最後更新者使用者識別碼',
  UpdatedAt       DATETIME(6)   NOT NULL COMMENT '最後更新時間戳記（UTC）',
  IsDeleted       TINYINT(1)    NOT NULL DEFAULT 0 COMMENT '是否已軟刪除',
  DeletedAt       DATETIME(6)   NULL COMMENT '軟刪除時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_projects_CompanyId_ProjectNo (CompanyId, ProjectNo),
  KEY IX_projects_CompanyId_Status_CreatedAt (CompanyId, Status, CreatedAt),
  KEY IX_projects_CompanyId_StartDate (CompanyId, StartDate),
  KEY IX_projects_CompanyId_EndDate (CompanyId, EndDate),
  KEY IX_projects_CompanyId_ExpectedAmount (CompanyId, ExpectedAmount),
  KEY IX_projects_CompanyId_ContractAmount (CompanyId, ContractAmount),
  KEY IX_projects_CompanyId_ClientId (CompanyId, ClientId),
  KEY IX_projects_CompanyId_DepartmentId (CompanyId, DepartmentId),
  KEY IX_projects_CompanyId_OwnerUserId (CompanyId, OwnerUserId),
  CONSTRAINT FK_projects_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_projects_clients FOREIGN KEY (ClientId) REFERENCES clients (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_projects_departments FOREIGN KEY (DepartmentId) REFERENCES departments (Id) ON DELETE SET NULL,
  CONSTRAINT FK_projects_owner FOREIGN KEY (OwnerUserId) REFERENCES users (Id) ON DELETE RESTRICT,
  CONSTRAINT CK_projects_Status CHECK (Status IN ('draft', 'quoting', 'won', 'lost', 'in_progress', 'completed', 'closed', 'cancelled'))
) ENGINE=InnoDB COMMENT='業務專案主檔（所有報價、訂單、保固與發票之核心掛載主體）';

CREATE TABLE project_members (
  ProjectId    CHAR(36)    NOT NULL COMMENT '專案識別碼',
  UserId       CHAR(36)    NOT NULL COMMENT '成員使用者識別碼',
  CompanyId    CHAR(36)    NOT NULL COMMENT '所屬公司識別碼',
  ProjectRole  VARCHAR(20) NOT NULL DEFAULT 'member' COMMENT '專案內角色（manager / member / viewer）',
  AddedBy      CHAR(36)    NOT NULL COMMENT '加入操作者使用者識別碼',
  AddedAt      DATETIME(6) NOT NULL COMMENT '加入時間戳記（UTC）',
  PRIMARY KEY (ProjectId, UserId),
  KEY IX_project_members_CompanyId_UserId (CompanyId, UserId),
  CONSTRAINT FK_project_members_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE CASCADE,
  CONSTRAINT FK_project_members_users FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
  CONSTRAINT CK_project_members_ProjectRole CHECK (ProjectRole IN ('manager', 'member', 'viewer'))
) ENGINE=InnoDB COMMENT='專案成員協作名單表';

CREATE TABLE project_status_histories (
  Id          CHAR(36)     NOT NULL COMMENT '狀態異動紀錄唯一識別碼',
  CompanyId   CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  ProjectId   CHAR(36)     NOT NULL COMMENT '所屬專案識別碼',
  FromStatus  VARCHAR(20)  NULL COMMENT '變更前狀態（NULL 表示初始建立）',
  ToStatus    VARCHAR(20)  NOT NULL COMMENT '變更後新狀態',
  Reason      VARCHAR(500) NULL COMMENT '狀態異動原因說明',
  ChangedBy   CHAR(36)     NOT NULL COMMENT '操作變更者使用者識別碼',
  ChangedAt   DATETIME(6)  NOT NULL COMMENT '變更時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_project_status_histories_CompanyId_ProjectId (CompanyId, ProjectId, ChangedAt),
  CONSTRAINT FK_project_status_histories_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='專案狀態變更歷史歷程表';

-- =========================================================
-- 5. 報價
-- =========================================================

CREATE TABLE quote_templates (
  Id              CHAR(36)     NOT NULL COMMENT '報價樣板唯一識別碼',
  CompanyId       CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  Name            VARCHAR(100) NOT NULL COMMENT '樣板名稱',
  Kind            VARCHAR(10)  NOT NULL COMMENT '樣板格式類型（pdf / excel）',
  LayoutJson      LONGTEXT     NOT NULL DEFAULT '{}' CHECK (JSON_VALID(LayoutJson)) COMMENT '版面佈局排版配置 JSON',
  TemplateFileId  CHAR(36)     NULL COMMENT '關聯檔案識別碼（上傳的 Excel 樣板實體）',
  DefaultTerms    TEXT         NULL COMMENT '樣板預設附帶條款',
  IsDefault       TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否為公司預設樣板',
  CreatedAt       DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt       DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_quote_templates_CompanyId_Kind (CompanyId, Kind),
  CONSTRAINT FK_quote_templates_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_quote_templates_files FOREIGN KEY (TemplateFileId) REFERENCES files (Id) ON DELETE SET NULL,
  CONSTRAINT CK_quote_templates_Kind CHECK (Kind IN ('pdf', 'excel'))
) ENGINE=InnoDB COMMENT='報價單列印與匯出格式樣板檔';

CREATE TABLE import_mapping_profiles (
  Id             CHAR(36)     NOT NULL COMMENT '匯入對應範本唯一識別碼',
  CompanyId      CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  Name           VARCHAR(100) NOT NULL COMMENT '範本名稱',
  SheetName      VARCHAR(100) NULL COMMENT '目標工作表名稱（NULL 表示第一個工作表）',
  HeaderRow      INT          NOT NULL DEFAULT 1 COMMENT '標題列索引行號（從 1 起算）',
  DataStartRow   INT          NOT NULL DEFAULT 2 COMMENT '資料起始列索引行號（從 1 起算）',
  ColumnMapJson  LONGTEXT     NOT NULL DEFAULT '{}' CHECK (JSON_VALID(ColumnMapJson)) COMMENT '欄位映射對應表 JSON（例如品名對應 B 欄、數量對應 E 欄）',
  CellMapJson    LONGTEXT     NOT NULL DEFAULT '{}' CHECK (JSON_VALID(CellMapJson)) COMMENT '固定儲存格映射表 JSON（例如客戶名稱對應 C3、報價日期對應 H3）',
  CreatedBy      CHAR(36)     NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt      DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt      DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_import_mapping_profiles_CompanyId_Name (CompanyId, Name),
  CONSTRAINT FK_import_mapping_profiles_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='Excel 報價匯入欄位映射設定設定檔';

CREATE TABLE import_jobs (
  Id                CHAR(36)     NOT NULL COMMENT '匯入工作唯一識別碼',
  CompanyId         CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  Kind              VARCHAR(20)  NOT NULL COMMENT '匯入作業類型（excel_template / legacy_excel / ocr）',
  FileId            CHAR(36)     NOT NULL COMMENT '來源原始檔案識別碼',
  MappingProfileId  CHAR(36)     NULL COMMENT '使用之映射設定檔識別碼',
  TargetProjectId   CHAR(36)     NULL COMMENT '目標匯入專案識別碼',
  Status            VARCHAR(20)  NOT NULL DEFAULT 'uploaded' COMMENT '作業狀態（uploaded / parsing / preview / confirmed / failed / cancelled）',
  PreviewJson       LONGTEXT     NULL CHECK (PreviewJson IS NULL OR JSON_VALID(PreviewJson)) COMMENT '解析後的報價明細預覽資料 JSON',
  ErrorsJson        LONGTEXT     NULL CHECK (ErrorsJson IS NULL OR JSON_VALID(ErrorsJson)) COMMENT '解析過程產生的驗證錯誤訊息 JSON',
  CreatedQuoteId    CHAR(36)     NULL COMMENT '成功建立之正式報價單識別碼',
  CreatedBy         CHAR(36)     NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt         DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedAt         DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_import_jobs_CompanyId_Status (CompanyId, Status, CreatedAt),
  CONSTRAINT FK_import_jobs_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_import_jobs_files FOREIGN KEY (FileId) REFERENCES files (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_import_jobs_profiles FOREIGN KEY (MappingProfileId) REFERENCES import_mapping_profiles (Id) ON DELETE SET NULL,
  CONSTRAINT FK_import_jobs_projects FOREIGN KEY (TargetProjectId) REFERENCES projects (Id) ON DELETE SET NULL,
  CONSTRAINT CK_import_jobs_Kind CHECK (Kind IN ('excel_template', 'legacy_excel', 'ocr'))
) ENGINE=InnoDB COMMENT='報價批次匯入解析作業歷程表';

CREATE TABLE quotes (
  Id              CHAR(36)      NOT NULL COMMENT '報價單唯一識別碼',
  CompanyId       CHAR(36)      NOT NULL COMMENT '所屬公司識別碼',
  ProjectId       CHAR(36)      NOT NULL COMMENT '所屬專案識別碼',
  ClientId        CHAR(36)      NOT NULL COMMENT '關聯客戶識別碼',
  QuoteNo         VARCHAR(50)   NOT NULL COMMENT '報價單號',
  RevisionNo      INT           NOT NULL DEFAULT 0 COMMENT '版次號碼（0 為初始版，改版依序遞增）',
  RevisionOfId    CHAR(36)      NULL COMMENT '原始前一版次報價單識別碼（NULL 表示第一版）',
  IsLatest        TINYINT(1)    NOT NULL DEFAULT 1 COMMENT '是否為該報價單號之最新有效版次',
  Status          VARCHAR(20)   NOT NULL DEFAULT 'draft' COMMENT '報價狀態（draft / pending_approval / approved / sent / accepted / rejected / expired / superseded）',
  Title           VARCHAR(200)  NULL COMMENT '報價單主旨抬頭',
  QuoteDate       DATE          NOT NULL COMMENT '報價日期',
  ValidUntil      DATE          NULL COMMENT '報價有效期限日期',
  -- 客戶快照：客戶主檔之後修改，已發出的報價不受影響
  ClientName      VARCHAR(200)  NOT NULL COMMENT '客戶全稱快照（凍結不受後續異動影響）',
  ClientTaxId     VARCHAR(20)   NULL COMMENT '統一編號快照',
  ClientAddress   VARCHAR(500)  NULL COMMENT '客戶地址快照',
  ContactName     VARCHAR(100)  NULL COMMENT '聯絡人姓名快照',
  ContactPhone    VARCHAR(50)   NULL COMMENT '聯絡電話快照',
  ContactEmail    VARCHAR(200)  NULL COMMENT '電子郵件信箱快照',
  -- 金額
  Currency        CHAR(3)       NOT NULL DEFAULT 'TWD' COMMENT '計價幣別代碼',
  ExchangeRate    DECIMAL(18,6) NOT NULL DEFAULT 1 COMMENT '參考匯率',
  Subtotal        DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '明細合計未稅金額',
  DiscountType    VARCHAR(10)   NOT NULL DEFAULT 'none' COMMENT '整體折扣計算方式（none / percent / amount）',
  DiscountValue   DECIMAL(18,4) NOT NULL DEFAULT 0 COMMENT '折扣數值（比例 % 或指定折抵金額）',
  DiscountAmount  DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '經計算後折抵總額',
  TaxType         VARCHAR(10)   NOT NULL DEFAULT 'taxable' COMMENT '課稅別（taxable 應稅 / zero 零稅率 / exempt 免稅）',
  TaxIncluded     TINYINT(1)    NOT NULL DEFAULT 0 COMMENT '營業稅計算法（0 外加 / 1 內含）',
  TaxRate         DECIMAL(5,2)  NOT NULL DEFAULT 5.00 COMMENT '適用營業稅率百分比（預設 5.00）',
  TaxAmount       DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '計算之營業稅額',
  Total           DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '最終報價含稅總金額',
  CostTotal       DECIMAL(18,2) NULL COMMENT '預估成本總計',
  GrossMargin     DECIMAL(18,2) NULL COMMENT '預估毛利總額',
  -- 內容
  PaymentTerms    VARCHAR(500)  NULL COMMENT '付款條件描述',
  DeliveryTerms   VARCHAR(500)  NULL COMMENT '交貨條件或地點描述',
  Terms           TEXT          NULL COMMENT '報價備註條款說明',
  Note            TEXT          NULL COMMENT '內部備忘說明',
  TemplateId      CHAR(36)      NULL COMMENT '輸出指定樣板識別碼',
  -- 來源與流程
  Source          VARCHAR(20)   NOT NULL DEFAULT 'manual' COMMENT '報價單來源（manual / excel_template / legacy_excel / ocr / copy）',
  ImportJobId     CHAR(36)      NULL COMMENT '來源匯入工作識別碼',
  LastPdfFileId   CHAR(36)      NULL COMMENT '最新產出之 PDF 檔案識別碼',
  ApprovedBy      CHAR(36)      NULL COMMENT '審核核准人使用者識別碼',
  ApprovedAt      DATETIME(6)   NULL COMMENT '審核核准時間戳記（UTC）',
  SentAt          DATETIME(6)   NULL COMMENT '正式發送給客戶之時間戳記（UTC）',
  AcceptedAt      DATETIME(6)   NULL COMMENT '客戶確認接受時間戳記（UTC）',
  RejectedAt      DATETIME(6)   NULL COMMENT '客戶婉拒時間戳記（UTC）',
  CreatedBy       CHAR(36)      NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt       DATETIME(6)   NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedBy       CHAR(36)      NULL COMMENT '最後更新者使用者識別碼',
  UpdatedAt       DATETIME(6)   NOT NULL COMMENT '最後更新時間戳記（UTC）',
  IsDeleted       TINYINT(1)    NOT NULL DEFAULT 0 COMMENT '是否已軟刪除',
  DeletedAt       DATETIME(6)   NULL COMMENT '軟刪除時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_quotes_CompanyId_QuoteNo_RevisionNo (CompanyId, QuoteNo, RevisionNo),
  KEY IX_quotes_CompanyId_ProjectId (CompanyId, ProjectId),
  KEY IX_quotes_CompanyId_Status_QuoteDate (CompanyId, Status, QuoteDate),
  KEY IX_quotes_CompanyId_AcceptedAt (CompanyId, AcceptedAt),
  KEY IX_quotes_CompanyId_ClientId (CompanyId, ClientId),
  CONSTRAINT FK_quotes_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_quotes_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_quotes_clients FOREIGN KEY (ClientId) REFERENCES clients (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_quotes_revision_of FOREIGN KEY (RevisionOfId) REFERENCES quotes (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_quotes_templates FOREIGN KEY (TemplateId) REFERENCES quote_templates (Id) ON DELETE SET NULL,
  CONSTRAINT FK_quotes_import_jobs FOREIGN KEY (ImportJobId) REFERENCES import_jobs (Id) ON DELETE SET NULL,
  CONSTRAINT FK_quotes_last_pdf FOREIGN KEY (LastPdfFileId) REFERENCES files (Id) ON DELETE SET NULL,
  CONSTRAINT CK_quotes_Status CHECK (Status IN ('draft', 'pending_approval', 'approved', 'sent', 'accepted', 'rejected', 'expired', 'superseded')),
  CONSTRAINT CK_quotes_DiscountType CHECK (DiscountType IN ('none', 'percent', 'amount')),
  CONSTRAINT CK_quotes_TaxType CHECK (TaxType IN ('taxable', 'zero', 'exempt')),
  CONSTRAINT CK_quotes_Source CHECK (Source IN ('manual', 'excel_template', 'legacy_excel', 'ocr', 'copy'))
) ENGINE=InnoDB COMMENT='專案報價單主檔（支援多版次管理與歷史快照凍結）';

CREATE TABLE quote_items (
  Id            CHAR(36)      NOT NULL COMMENT '明細唯一識別碼',
  CompanyId     CHAR(36)      NOT NULL COMMENT '所屬公司識別碼',
  QuoteId       CHAR(36)      NOT NULL COMMENT '所屬報價單識別碼',
  SortOrder     INT           NOT NULL COMMENT '排序順序編號',
  LineType      VARCHAR(10)   NOT NULL DEFAULT 'item' COMMENT '明細列型態（item / section / subtotal / note）',
  ProductId     CHAR(36)      NULL COMMENT '關聯品項庫識別碼',
  ItemCode      VARCHAR(50)   NULL COMMENT '品項自訂編號',
  Name          VARCHAR(500)  NOT NULL COMMENT '品名或項目說明',
  Spec          TEXT          NULL COMMENT '規格或型號描述',
  Unit          VARCHAR(20)   NULL COMMENT '計量單位',
  Quantity      DECIMAL(18,4) NOT NULL DEFAULT 0 COMMENT '數量',
  UnitPrice     DECIMAL(18,4) NOT NULL DEFAULT 0 COMMENT '單價',
  DiscountRate  DECIMAL(5,2)  NOT NULL DEFAULT 0 COMMENT '單項折扣比例百分比',
  Amount        DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '項目未稅小計金額（後端重新計算）',
  UnitCost      DECIMAL(18,4) NULL COMMENT '預估單位進貨成本',
  Remark        VARCHAR(500)  NULL COMMENT '明細備註說明',
  PRIMARY KEY (Id),
  KEY IX_quote_items_CompanyId_QuoteId (CompanyId, QuoteId, SortOrder),
  KEY IX_quote_items_ProductId (ProductId),
  CONSTRAINT FK_quote_items_quotes FOREIGN KEY (QuoteId) REFERENCES quotes (Id) ON DELETE CASCADE,
  CONSTRAINT FK_quote_items_products FOREIGN KEY (ProductId) REFERENCES products (Id) ON DELETE SET NULL,
  CONSTRAINT CK_quote_items_LineType CHECK (LineType IN ('item', 'section', 'subtotal', 'note'))
) ENGINE=InnoDB COMMENT='報價單項目明細表';

CREATE TABLE number_sequences (
  CompanyId     CHAR(36)    NOT NULL COMMENT '所屬公司識別碼',
  DocType       VARCHAR(20) NOT NULL COMMENT '單據類型識別碼（例如 project / quote）',
  Period        VARCHAR(10) NOT NULL COMMENT '序號週期代號（依規則最細日期代號：20261030、202610 或 2026；不分期間用 all）',
  Prefix        VARCHAR(20) NOT NULL COMMENT '單號前綴代碼',
  CurrentValue  INT         NOT NULL DEFAULT 0 COMMENT '當前流水序號最新值（原子遞增）',
  UpdatedAt     DATETIME(6) NOT NULL COMMENT '最後取號更新時間戳記（UTC）',
  PRIMARY KEY (CompanyId, DocType, Period),
  CONSTRAINT FK_number_sequences_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE
) ENGINE=InnoDB COMMENT='業務單號原子流水取號序號表';

-- =========================================================
-- 6. 訂單、保固、發票（每專案多筆）
-- =========================================================

CREATE TABLE project_orders (
  Id         CHAR(36)      NOT NULL COMMENT '訂單唯一識別碼',
  CompanyId  CHAR(36)      NOT NULL COMMENT '所屬公司識別碼',
  ProjectId  CHAR(36)      NOT NULL COMMENT '所屬專案識別碼',
  QuoteId    CHAR(36)      NULL COMMENT '關聯來源報價單識別碼',
  OrderNo    VARCHAR(100)  NOT NULL COMMENT '訂單編號（客戶採購單 PO 號或內部訂單編號）',
  OrderDate  DATE          NOT NULL COMMENT '訂單成立下單日期',
  Amount     DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '未稅銷售總金額',
  TaxAmount  DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '營業稅額',
  Total      DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '含稅總額',
  Status     VARCHAR(20)   NOT NULL DEFAULT 'received' COMMENT '處理狀態（received / in_progress / delivered / cancelled）',
  Note       TEXT          NULL COMMENT '備註說明',
  CreatedBy  CHAR(36)      NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt  DATETIME(6)   NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedBy  CHAR(36)      NULL COMMENT '最後更新者使用者識別碼',
  UpdatedAt  DATETIME(6)   NOT NULL COMMENT '最後更新時間戳記（UTC）',
  IsDeleted  TINYINT(1)    NOT NULL DEFAULT 0 COMMENT '是否已軟刪除',
  DeletedAt  DATETIME(6)   NULL COMMENT '軟刪除時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_project_orders_CompanyId_ProjectId (CompanyId, ProjectId),
  KEY IX_project_orders_CompanyId_OrderDate (CompanyId, OrderDate),
  CONSTRAINT FK_project_orders_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_project_orders_quotes FOREIGN KEY (QuoteId) REFERENCES quotes (Id) ON DELETE SET NULL,
  CONSTRAINT CK_project_orders_Status CHECK (Status IN ('received', 'in_progress', 'delivered', 'cancelled'))
) ENGINE=InnoDB COMMENT='專案客戶採購訂單檔';

-- 出貨單：掛在訂單下，一張訂單可分批出貨（AddProjectShipments）。
CREATE TABLE project_shipments (
  Id               CHAR(36)     NOT NULL COMMENT '出貨單唯一識別碼',
  CompanyId        CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  ProjectId        CHAR(36)     NOT NULL COMMENT '所屬專案識別碼',
  OrderId          CHAR(36)     NOT NULL COMMENT '所屬訂單識別碼',
  ShipmentNo       VARCHAR(100) NOT NULL COMMENT '出貨單編號',
  ShipDate         DATE         NOT NULL COMMENT '實際出貨日期',
  Status           VARCHAR(20)  NOT NULL DEFAULT 'shipped' COMMENT '出貨狀態（shipped / delivered / cancelled）',
  ReceiverName     VARCHAR(100) NULL COMMENT '收件人姓名',
  DeliveryAddress  VARCHAR(300) NULL COMMENT '貨物送達地點',
  TrackingNo       VARCHAR(100) NULL COMMENT '貨運物流單號',
  Note             TEXT         NULL COMMENT '備註說明',
  CreatedBy        CHAR(36)     NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt        DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedBy        CHAR(36)     NULL COMMENT '最後更新者使用者識別碼',
  UpdatedAt        DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  IsDeleted        TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否已軟刪除',
  DeletedAt        DATETIME(6)  NULL COMMENT '軟刪除時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_project_shipments_CompanyId_ProjectId (CompanyId, ProjectId),
  KEY IX_project_shipments_CompanyId_OrderId (CompanyId, OrderId),
  CONSTRAINT FK_project_shipments_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_project_shipments_orders FOREIGN KEY (OrderId) REFERENCES project_orders (Id) ON DELETE RESTRICT
) ENGINE=InnoDB COMMENT='專案訂單出貨單履約紀錄表';

CREATE TABLE project_warranties (
  Id          CHAR(36)     NOT NULL COMMENT '保固單唯一識別碼',
  CompanyId   CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  ProjectId   CHAR(36)     NOT NULL COMMENT '所屬專案識別碼',
  OrderId     CHAR(36)     NULL COMMENT '關聯訂單識別碼',
  WarrantyNo  VARCHAR(100) NOT NULL COMMENT '保固書編號',
  Subject     VARCHAR(200) NOT NULL COMMENT '保固主旨或保固標的標籤',
  StartDate   DATE         NOT NULL COMMENT '保固起始日期',
  EndDate     DATE         NOT NULL COMMENT '保固截止到期日期',
  Terms       TEXT         NULL COMMENT '保固條款與範圍說明',
  Note        TEXT         NULL COMMENT '備註說明',
  CreatedBy   CHAR(36)     NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt   DATETIME(6)  NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedBy   CHAR(36)     NULL COMMENT '最後更新者使用者識別碼',
  UpdatedAt   DATETIME(6)  NOT NULL COMMENT '最後更新時間戳記（UTC）',
  IsDeleted   TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否已軟刪除',
  DeletedAt   DATETIME(6)  NULL COMMENT '軟刪除時間戳記（UTC）',
  PRIMARY KEY (Id),
  KEY IX_project_warranties_CompanyId_ProjectId (CompanyId, ProjectId),
  KEY IX_project_warranties_CompanyId_EndDate (CompanyId, EndDate),
  CONSTRAINT FK_project_warranties_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_project_warranties_orders FOREIGN KEY (OrderId) REFERENCES project_orders (Id) ON DELETE SET NULL,
  CONSTRAINT CK_project_warranties_dates CHECK (EndDate >= StartDate)
) ENGINE=InnoDB COMMENT='專案售後保固服務管理檔';

CREATE TABLE project_invoices (
  Id             CHAR(36)      NOT NULL COMMENT '發票唯一識別碼',
  CompanyId      CHAR(36)      NOT NULL COMMENT '所屬公司識別碼',
  ProjectId      CHAR(36)      NOT NULL COMMENT '所屬專案識別碼',
  OrderId        CHAR(36)      NULL COMMENT '關聯訂單識別碼',
  InvoiceNo      VARCHAR(20)   NOT NULL COMMENT '統一發票號碼（儲存格式 AB-12345678）',
  InvoiceDate    DATE          NOT NULL COMMENT '發票開立日期',
  InvoiceType    VARCHAR(20)   NOT NULL DEFAULT 'triplicate' COMMENT '發票種類（duplicate / triplicate / e_invoice）',
  BuyerName      VARCHAR(200)  NULL COMMENT '買受人抬頭',
  BuyerTaxId     VARCHAR(20)   NULL COMMENT '買受人統一編號',
  SalesAmount    DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '銷售額未稅金額',
  TaxAmount      DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '營業稅額',
  TotalAmount    DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '發票開立含稅總金額',
  Status         VARCHAR(20)   NOT NULL DEFAULT 'issued' COMMENT '發票狀態（issued / void / allowance）',
  PaymentStatus  VARCHAR(20)   NOT NULL DEFAULT 'unpaid' COMMENT '沖帳收款狀態（unpaid / partial / paid）',
  PaidAmount     DECIMAL(18,2) NOT NULL DEFAULT 0 COMMENT '累計已收款金額',
  DueDate        DATE          NULL COMMENT '預計收款到期日',
  PaidAt         DATETIME(6)   NULL COMMENT '款項結清時間戳記（UTC）',
  VoidedAt       DATETIME(6)   NULL COMMENT '發票作廢時間戳記（UTC）',
  Note           TEXT          NULL COMMENT '備註說明',
  CreatedBy      CHAR(36)      NOT NULL COMMENT '建立者使用者識別碼',
  CreatedAt      DATETIME(6)   NOT NULL COMMENT '建立時間戳記（UTC）',
  UpdatedBy      CHAR(36)      NULL COMMENT '最後更新者使用者識別碼',
  UpdatedAt      DATETIME(6)   NOT NULL COMMENT '最後更新時間戳記（UTC）',
  IsDeleted      TINYINT(1)    NOT NULL DEFAULT 0 COMMENT '是否已刪除（僅限無收款紀錄之發票可刪除）',
  DeletedAt      DATETIME(6)   NULL COMMENT '刪除時間戳記（UTC）',
  PRIMARY KEY (Id),
  UNIQUE KEY UX_project_invoices_CompanyId_InvoiceNo (CompanyId, InvoiceNo),
  KEY IX_project_invoices_CompanyId_ProjectId (CompanyId, ProjectId),
  KEY IX_project_invoices_CompanyId_InvoiceDate (CompanyId, InvoiceDate),
  KEY IX_project_invoices_CompanyId_PaymentStatus_DueDate (CompanyId, PaymentStatus, DueDate),
  CONSTRAINT FK_project_invoices_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_project_invoices_orders FOREIGN KEY (OrderId) REFERENCES project_orders (Id) ON DELETE SET NULL,
  CONSTRAINT CK_project_invoices_InvoiceType CHECK (InvoiceType IN ('duplicate', 'triplicate', 'e_invoice')),
  CONSTRAINT CK_project_invoices_Status CHECK (Status IN ('issued', 'void', 'allowance')),
  CONSTRAINT CK_project_invoices_PaymentStatus CHECK (PaymentStatus IN ('unpaid', 'partial', 'paid'))
) ENGINE=InnoDB COMMENT='專案統一發票與請款紀錄檔';

-- =========================================================
-- 7. 虛擬辦公室
-- =========================================================

CREATE TABLE office_rooms (
  Id           CHAR(36)     NOT NULL COMMENT '房間設定唯一識別碼',
  CompanyId    CHAR(36)     NOT NULL COMMENT '所屬公司識別碼',
  DepartmentId CHAR(36)     NOT NULL COMMENT '所屬部門識別碼（一個部門一間辦公室）',
  Theme        VARCHAR(20)  NOT NULL DEFAULT 'wood' COMMENT '地圖主題（wood / carpet / dark / park / space）',
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

-- =========================================================
-- 8. 循環關聯：表都建立後再補 FK
-- =========================================================

ALTER TABLE companies
  ADD CONSTRAINT FK_companies_root_admin_membership FOREIGN KEY (RootAdminMembershipId) REFERENCES company_memberships (Id) ON DELETE RESTRICT,
  ADD CONSTRAINT FK_companies_logo_file FOREIGN KEY (LogoFileId) REFERENCES files (Id) ON DELETE SET NULL;

ALTER TABLE import_jobs
  ADD CONSTRAINT FK_import_jobs_created_quote FOREIGN KEY (CreatedQuoteId) REFERENCES quotes (Id) ON DELETE SET NULL;
