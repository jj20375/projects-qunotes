-- 專案報價管理系統 MariaDB DDL 草稿
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
  Id                                CHAR(36)      NOT NULL,
  Name                              VARCHAR(200)  NOT NULL,
  Slug                              VARCHAR(100)  NOT NULL,
  TaxId                             VARCHAR(20)   NULL,
  Phone                             VARCHAR(50)   NULL,
  Address                           VARCHAR(500)  NULL,
  EnglishName                       VARCHAR(200)  NULL,                       -- 報價單抬頭的英文名稱
  LogoFileId                        CHAR(36)      NULL,                       -- 報價單抬頭 Logo（files.Id）
  Status                            VARCHAR(20)   NOT NULL DEFAULT 'active',  -- active / suspended
  RootAdminMembershipId             CHAR(36)      NULL,
  IsRoleCrudConfigurationEnabled    TINYINT(1)    NOT NULL DEFAULT 1,
  IsRoleOptionConfigurationEnabled  TINYINT(1)    NOT NULL DEFAULT 1,
  CreatedAt                         DATETIME(6)   NOT NULL,
  UpdatedAt                         DATETIME(6)   NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_companies_Slug (Slug),
  UNIQUE KEY UX_companies_RootAdminMembershipId (RootAdminMembershipId)
) ENGINE=InnoDB;

CREATE TABLE company_settings (
  CompanyId             CHAR(36)      NOT NULL,
  DefaultTaxRate        DECIMAL(5,2)  NOT NULL DEFAULT 5.00,
  DefaultCurrency       CHAR(3)       NOT NULL DEFAULT 'TWD',
  QuoteValidDays        INT           NOT NULL DEFAULT 30,
  DefaultTerms          TEXT          NULL,                                    -- 一條一行、不含編號
  PdfHeaderJson         LONGTEXT      NOT NULL DEFAULT '{}' CHECK (JSON_VALID(PdfHeaderJson)),
  FiscalYearStartMonth  TINYINT       NOT NULL DEFAULT 1 CHECK (FiscalYearStartMonth BETWEEN 1 AND 12),
  QuoteNumberPattern    VARCHAR(40)   NOT NULL DEFAULT 'Q{yyyy}{MM}-{seq:4}',  -- 報價單號規則，見 system-architecture.md 6.7
  UpdatedAt             DATETIME(6)   NOT NULL,
  PRIMARY KEY (CompanyId),
  CONSTRAINT FK_company_settings_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE roles (
  Id            CHAR(36)     NOT NULL,
  CompanyId     CHAR(36)     NULL,                                -- NULL = 系統內建角色
  Scope        VARCHAR(20)  NOT NULL DEFAULT 'company',          -- platform / company
  Code          VARCHAR(100) NOT NULL,
  Name          VARCHAR(100) NOT NULL,
  DataScope     VARCHAR(20)  NOT NULL DEFAULT 'own',              -- own / department / department_tree / company
  IsSystem      TINYINT(1)   NOT NULL DEFAULT 0,
  IsFullAccess  TINYINT(1)   NOT NULL DEFAULT 0,
  CreatedAt     DATETIME(6)  NOT NULL,
  UpdatedAt     DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  -- NULL 不觸發 unique，系統內建角色（CompanyId IS NULL）的 Code 唯一由 Seeder 與 Service 保證
  UNIQUE KEY UX_roles_CompanyId_Code (CompanyId, Code),
  CONSTRAINT FK_roles_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT CK_roles_Scope CHECK (Scope IN ('platform', 'company')),
  CONSTRAINT CK_roles_DataScope CHECK (DataScope IN ('own', 'department', 'department_tree', 'company'))
) ENGINE=InnoDB;

CREATE TABLE permissions (
  Id               CHAR(36)     NOT NULL,
  Code             VARCHAR(100) NOT NULL,
  Name             VARCHAR(100) NOT NULL,
  Description      VARCHAR(500) NOT NULL DEFAULT '',
  Scope            VARCHAR(20)  NOT NULL DEFAULT 'company',
  ParentId         CHAR(36)     NULL,
  SortOrder        INT          NOT NULL DEFAULT 0,
  IsMenu           TINYINT(1)   NOT NULL DEFAULT 0,
  RoutePath        VARCHAR(200) NULL,
  SubFeaturesJson  LONGTEXT     NOT NULL DEFAULT '[]' CHECK (JSON_VALID(SubFeaturesJson)),  -- [{ "key": "approve", "name": "核准" }]
  CreatedAt        DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_permissions_Code (Code),
  KEY IX_permissions_ParentId (ParentId),
  CONSTRAINT FK_permissions_parent FOREIGN KEY (ParentId) REFERENCES permissions (Id) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE role_permissions (
  RoleId        CHAR(36)    NOT NULL,
  PermissionId  CHAR(36)    NOT NULL,
  PerCreate     TINYINT(1)  NOT NULL DEFAULT 0,
  PerRead       TINYINT(1)  NOT NULL DEFAULT 0,
  PerUpdate     TINYINT(1)  NOT NULL DEFAULT 0,
  PerDelete     TINYINT(1)  NOT NULL DEFAULT 0,
  OptionsJson   LONGTEXT    NOT NULL DEFAULT '[]' CHECK (JSON_VALID(OptionsJson)),
  PRIMARY KEY (RoleId, PermissionId),
  KEY IX_role_permissions_PermissionId (PermissionId),
  CONSTRAINT FK_role_permissions_roles FOREIGN KEY (RoleId) REFERENCES roles (Id) ON DELETE CASCADE,
  CONSTRAINT FK_role_permissions_permissions FOREIGN KEY (PermissionId) REFERENCES permissions (Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE users (
  Id                CHAR(36)     NOT NULL,
  Account           VARCHAR(100) NOT NULL,
  Email             VARCHAR(200) NULL,
  DisplayName       VARCHAR(100) NOT NULL,
  PasswordHash      VARCHAR(500) NOT NULL,
  Status            VARCHAR(20)  NOT NULL DEFAULT 'active',  -- active / disabled
  SystemRoleId      CHAR(36)     NULL,                       -- 只指向 Scope = platform 的角色
  AuthVersion       INT          NOT NULL DEFAULT 1,
  FailedLoginCount  INT          NOT NULL DEFAULT 0,
  LockedUntil       DATETIME(6)  NULL,
  LastLoginAt       DATETIME(6)  NULL,
  CreatedAt         DATETIME(6)  NOT NULL,
  UpdatedAt         DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_users_Account (Account),
  KEY IX_users_Email (Email),
  CONSTRAINT FK_users_system_role FOREIGN KEY (SystemRoleId) REFERENCES roles (Id) ON DELETE SET NULL
) ENGINE=InnoDB;

CREATE TABLE refresh_tokens (
  Id            CHAR(36)     NOT NULL,
  UserId        CHAR(36)     NOT NULL,
  CompanyId     CHAR(36)     NULL,           -- 簽發時的 scope；NULL = platform
  TokenHash     CHAR(64)     NOT NULL,       -- SHA-256 hex，不存明文
  ExpiresAt     DATETIME(6)  NOT NULL,
  RevokedAt     DATETIME(6)  NULL,
  ReplacedById  CHAR(36)     NULL,
  CreatedIp     VARCHAR(64)  NULL,
  UserAgent     VARCHAR(500) NULL,
  CreatedAt     DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_refresh_tokens_TokenHash (TokenHash),
  KEY IX_refresh_tokens_UserId_ExpiresAt (UserId, ExpiresAt),
  CONSTRAINT FK_refresh_tokens_users FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE departments (
  Id         CHAR(36)     NOT NULL,
  CompanyId  CHAR(36)     NOT NULL,
  ParentId   CHAR(36)     NULL,
  Code       VARCHAR(50)  NULL,
  Name       VARCHAR(100) NOT NULL,
  SortOrder  INT          NOT NULL DEFAULT 0,
  IsActive   TINYINT(1)   NOT NULL DEFAULT 1,
  CreatedAt  DATETIME(6)  NOT NULL,
  UpdatedAt  DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_departments_CompanyId_Code (CompanyId, Code),
  KEY IX_departments_CompanyId_ParentId (CompanyId, ParentId),
  CONSTRAINT FK_departments_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_departments_parent FOREIGN KEY (ParentId) REFERENCES departments (Id) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE department_closure (
  AncestorId    CHAR(36)  NOT NULL,
  DescendantId  CHAR(36)  NOT NULL,
  CompanyId     CHAR(36)  NOT NULL,
  Depth         INT       NOT NULL,    -- 0 = 自身
  PRIMARY KEY (AncestorId, DescendantId),
  KEY IX_department_closure_AncestorId_Depth (AncestorId, Depth),
  KEY IX_department_closure_DescendantId_Depth (DescendantId, Depth),
  CONSTRAINT FK_department_closure_ancestor FOREIGN KEY (AncestorId) REFERENCES departments (Id) ON DELETE CASCADE,
  CONSTRAINT FK_department_closure_descendant FOREIGN KEY (DescendantId) REFERENCES departments (Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE company_memberships (
  Id                 CHAR(36)    NOT NULL,
  CompanyId          CHAR(36)    NOT NULL,
  UserId             CHAR(36)    NOT NULL,
  DepartmentId       CHAR(36)    NULL,
  RoleId             CHAR(36)    NOT NULL,
  DataScopeOverride  VARCHAR(20) NULL,   -- NULL = 依角色
  IsActive           TINYINT(1)  NOT NULL DEFAULT 1,
  CreatedAt          DATETIME(6) NOT NULL,
  UpdatedAt          DATETIME(6) NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_company_memberships_CompanyId_UserId (CompanyId, UserId),
  KEY IX_company_memberships_UserId (UserId),
  KEY IX_company_memberships_CompanyId_DepartmentId (CompanyId, DepartmentId),
  CONSTRAINT FK_company_memberships_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_company_memberships_users FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
  CONSTRAINT FK_company_memberships_departments FOREIGN KEY (DepartmentId) REFERENCES departments (Id) ON DELETE SET NULL,
  CONSTRAINT FK_company_memberships_roles FOREIGN KEY (RoleId) REFERENCES roles (Id) ON DELETE RESTRICT,
  CONSTRAINT CK_company_memberships_DataScope CHECK (DataScopeOverride IS NULL OR DataScopeOverride IN ('own', 'department', 'department_tree', 'company'))
) ENGINE=InnoDB;

CREATE TABLE user_permissions (
  Id            CHAR(36)     NOT NULL,
  CompanyId     CHAR(36)     NOT NULL,
  UserId        CHAR(36)     NOT NULL,
  PermissionId  CHAR(36)     NOT NULL,
  Effect        VARCHAR(10)  NOT NULL,   -- grant / deny
  PerCreate     TINYINT(1)   NOT NULL DEFAULT 0,
  PerRead       TINYINT(1)   NOT NULL DEFAULT 0,
  PerUpdate     TINYINT(1)   NOT NULL DEFAULT 0,
  PerDelete     TINYINT(1)   NOT NULL DEFAULT 0,
  OptionsJson   LONGTEXT     NOT NULL DEFAULT '[]' CHECK (JSON_VALID(OptionsJson)),
  Reason        VARCHAR(500) NULL,
  ExpiresAt     DATETIME(6)  NULL,
  CreatedBy     CHAR(36)     NOT NULL,
  CreatedAt     DATETIME(6)  NOT NULL,
  UpdatedAt     DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_user_permissions_scope (CompanyId, UserId, PermissionId, Effect),
  KEY IX_user_permissions_PermissionId (PermissionId),
  CONSTRAINT FK_user_permissions_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_user_permissions_users FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
  CONSTRAINT FK_user_permissions_permissions FOREIGN KEY (PermissionId) REFERENCES permissions (Id) ON DELETE CASCADE,
  CONSTRAINT CK_user_permissions_Effect CHECK (Effect IN ('grant', 'deny'))
) ENGINE=InnoDB;

CREATE TABLE audit_logs (
  Id           CHAR(36)     NOT NULL,
  CompanyId    CHAR(36)     NULL,         -- NULL = 平台層操作
  ActorUserId  CHAR(36)     NULL,
  EntityType   VARCHAR(50)  NOT NULL,
  EntityId     CHAR(36)     NULL,
  Action       VARCHAR(100) NOT NULL,
  BeforeJson   LONGTEXT     NULL CHECK (BeforeJson IS NULL OR JSON_VALID(BeforeJson)),
  AfterJson    LONGTEXT     NULL CHECK (AfterJson IS NULL OR JSON_VALID(AfterJson)),
  Ip           VARCHAR(64)  NULL,
  CreatedAt    DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  KEY IX_audit_logs_entity (CompanyId, EntityType, EntityId),
  KEY IX_audit_logs_CompanyId_CreatedAt (CompanyId, CreatedAt),
  KEY IX_audit_logs_ActorUserId (ActorUserId)
) ENGINE=InnoDB;   -- 不建 FK：稽核紀錄在主檔刪除後仍需保留

-- =========================================================
-- 2. 檔案
-- =========================================================

CREATE TABLE files (
  Id            CHAR(36)     NOT NULL,
  CompanyId     CHAR(36)     NOT NULL,
  StorageKey    VARCHAR(500) NOT NULL,     -- {companyId}/{yyyy}/{MM}/{fileId}
  OriginalName  VARCHAR(255) NOT NULL,
  ContentType   VARCHAR(150) NOT NULL,
  Size          BIGINT       NOT NULL,
  Sha256        CHAR(64)     NULL,
  Status        VARCHAR(20)  NOT NULL DEFAULT 'uploading',   -- uploading / ready / deleted
  UploadedBy    CHAR(36)     NOT NULL,
  CreatedAt     DATETIME(6)  NOT NULL,
  DeletedAt     DATETIME(6)  NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_files_StorageKey (StorageKey),
  KEY IX_files_CompanyId_Status (CompanyId, Status),
  CONSTRAINT FK_files_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE upload_sessions (
  Id           CHAR(36)     NOT NULL,
  CompanyId    CHAR(36)     NOT NULL,
  FileId       CHAR(36)     NOT NULL,
  FileSize     BIGINT       NOT NULL,
  ChunkSize    INT          NOT NULL,
  TotalChunks  INT          NOT NULL,
  TokenHash    CHAR(64)     NOT NULL,
  Status       VARCHAR(20)  NOT NULL DEFAULT 'pending',   -- pending / uploading / completed / cancelled / failed / expired
  ExpiresAt    DATETIME(6)  NOT NULL,
  CompletedAt  DATETIME(6)  NULL,
  CancelledAt  DATETIME(6)  NULL,
  LastError    VARCHAR(1000) NULL,
  CreatedAt    DATETIME(6)  NOT NULL,
  UpdatedAt    DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_upload_sessions_FileId (FileId),
  KEY IX_upload_sessions_CompanyId_Status (CompanyId, Status),
  CONSTRAINT FK_upload_sessions_files FOREIGN KEY (FileId) REFERENCES files (Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE attachments (
  Id         CHAR(36)     NOT NULL,
  CompanyId  CHAR(36)     NOT NULL,
  OwnerType  VARCHAR(20)  NOT NULL,   -- project / quote / order / warranty / invoice / client
  OwnerId    CHAR(36)     NOT NULL,   -- 多型關聯，由 Service 驗證存在與資料範圍
  FileId     CHAR(36)     NOT NULL,
  Category   VARCHAR(50)  NULL,       -- 例如 po_scan / signed_quote / warranty_doc / invoice_scan
  Note       VARCHAR(500) NULL,
  SortOrder  INT          NOT NULL DEFAULT 0,
  CreatedBy  CHAR(36)     NOT NULL,
  CreatedAt  DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  KEY IX_attachments_owner (CompanyId, OwnerType, OwnerId),
  KEY IX_attachments_FileId (FileId),
  CONSTRAINT FK_attachments_files FOREIGN KEY (FileId) REFERENCES files (Id) ON DELETE RESTRICT,
  CONSTRAINT CK_attachments_OwnerType CHECK (OwnerType IN ('project', 'quote', 'order', 'warranty', 'invoice', 'client'))
) ENGINE=InnoDB;

-- =========================================================
-- 3. 客戶與品項
-- =========================================================

CREATE TABLE clients (
  Id              CHAR(36)     NOT NULL,
  CompanyId       CHAR(36)     NOT NULL,
  Code            VARCHAR(50)  NOT NULL,
  Name            VARCHAR(200) NOT NULL,
  ShortName       VARCHAR(100) NULL,
  TaxId           VARCHAR(20)  NULL,
  ParentClientId  CHAR(36)     NULL,
  Industry        VARCHAR(100) NULL,
  Phone           VARCHAR(50)  NULL,
  Fax             VARCHAR(50)  NULL,
  Email           VARCHAR(200) NULL,
  Address         VARCHAR(500) NULL,
  InvoiceTitle    VARCHAR(200) NULL,
  InvoiceAddress  VARCHAR(500) NULL,
  PaymentTerms    VARCHAR(200) NULL,
  OwnerUserId     CHAR(36)     NULL,
  Status          VARCHAR(20)  NOT NULL DEFAULT 'active',   -- active / inactive
  Note            TEXT         NULL,
  CreatedBy       CHAR(36)     NOT NULL,
  CreatedAt       DATETIME(6)  NOT NULL,
  UpdatedBy       CHAR(36)     NULL,
  UpdatedAt       DATETIME(6)  NOT NULL,
  IsDeleted       TINYINT(1)   NOT NULL DEFAULT 0,
  DeletedAt       DATETIME(6)  NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_clients_CompanyId_Code (CompanyId, Code),
  KEY IX_clients_CompanyId_TaxId (CompanyId, TaxId),
  KEY IX_clients_CompanyId_Name (CompanyId, Name),
  KEY IX_clients_CompanyId_OwnerUserId (CompanyId, OwnerUserId),
  CONSTRAINT FK_clients_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_clients_parent FOREIGN KEY (ParentClientId) REFERENCES clients (Id) ON DELETE SET NULL,
  CONSTRAINT FK_clients_owner FOREIGN KEY (OwnerUserId) REFERENCES users (Id) ON DELETE SET NULL
) ENGINE=InnoDB;

CREATE TABLE client_contacts (
  Id         CHAR(36)     NOT NULL,
  CompanyId  CHAR(36)     NOT NULL,
  ClientId   CHAR(36)     NOT NULL,
  Name       VARCHAR(100) NOT NULL,
  Title      VARCHAR(100) NULL,
  Phone      VARCHAR(50)  NULL,
  Mobile     VARCHAR(50)  NULL,
  Email      VARCHAR(200) NULL,
  IsPrimary  TINYINT(1)   NOT NULL DEFAULT 0,
  SortOrder  INT          NOT NULL DEFAULT 0,
  Note       VARCHAR(500) NULL,
  CreatedAt  DATETIME(6)  NOT NULL,
  UpdatedAt  DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  KEY IX_client_contacts_CompanyId_ClientId (CompanyId, ClientId),
  CONSTRAINT FK_client_contacts_clients FOREIGN KEY (ClientId) REFERENCES clients (Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE products (
  Id            CHAR(36)      NOT NULL,
  CompanyId     CHAR(36)      NOT NULL,
  Code          VARCHAR(50)   NOT NULL,
  Name          VARCHAR(200)  NOT NULL,
  Spec          VARCHAR(500)  NULL,
  Unit          VARCHAR(20)   NULL,
  Category      VARCHAR(100)  NULL,
  DefaultPrice  DECIMAL(18,4) NOT NULL DEFAULT 0,
  DefaultCost   DECIMAL(18,4) NULL,
  IsActive      TINYINT(1)    NOT NULL DEFAULT 1,
  CreatedAt     DATETIME(6)   NOT NULL,
  UpdatedAt     DATETIME(6)   NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_products_CompanyId_Code (CompanyId, Code),
  KEY IX_products_CompanyId_Name (CompanyId, Name),
  CONSTRAINT FK_products_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- =========================================================
-- 4. 專案
-- =========================================================

CREATE TABLE projects (
  Id              CHAR(36)      NOT NULL,
  CompanyId       CHAR(36)      NOT NULL,
  ProjectNo       VARCHAR(50)   NOT NULL,
  Name            VARCHAR(200)  NOT NULL,
  ClientId        CHAR(36)      NOT NULL,
  DepartmentId    CHAR(36)      NULL,
  OwnerUserId     CHAR(36)      NOT NULL,
  Status          VARCHAR(20)   NOT NULL DEFAULT 'draft',
  StartDate       DATE          NULL,
  EndDate         DATE          NULL,
  ExpectedAmount  DECIMAL(18,2) NULL,
  ContractAmount  DECIMAL(18,2) NULL,     -- 成交未稅（由接受的報價回寫）
  ContractTotal   DECIMAL(18,2) NULL,     -- 成交含稅
  Currency        CHAR(3)       NOT NULL DEFAULT 'TWD',
  Description     TEXT          NULL,
  WonAt           DATETIME(6)   NULL,
  ClosedAt        DATETIME(6)   NULL,
  CreatedBy       CHAR(36)      NOT NULL,
  CreatedAt       DATETIME(6)   NOT NULL,
  UpdatedBy       CHAR(36)      NULL,
  UpdatedAt       DATETIME(6)   NOT NULL,
  IsDeleted       TINYINT(1)    NOT NULL DEFAULT 0,
  DeletedAt       DATETIME(6)   NULL,
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
) ENGINE=InnoDB;

CREATE TABLE project_members (
  ProjectId    CHAR(36)    NOT NULL,
  UserId       CHAR(36)    NOT NULL,
  CompanyId    CHAR(36)    NOT NULL,
  ProjectRole  VARCHAR(20) NOT NULL DEFAULT 'member',   -- manager / member / viewer
  AddedBy      CHAR(36)    NOT NULL,
  AddedAt      DATETIME(6) NOT NULL,
  PRIMARY KEY (ProjectId, UserId),
  KEY IX_project_members_CompanyId_UserId (CompanyId, UserId),
  CONSTRAINT FK_project_members_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE CASCADE,
  CONSTRAINT FK_project_members_users FOREIGN KEY (UserId) REFERENCES users (Id) ON DELETE CASCADE,
  CONSTRAINT CK_project_members_ProjectRole CHECK (ProjectRole IN ('manager', 'member', 'viewer'))
) ENGINE=InnoDB;

CREATE TABLE project_status_histories (
  Id          CHAR(36)     NOT NULL,
  CompanyId   CHAR(36)     NOT NULL,
  ProjectId   CHAR(36)     NOT NULL,
  FromStatus  VARCHAR(20)  NULL,
  ToStatus    VARCHAR(20)  NOT NULL,
  Reason      VARCHAR(500) NULL,
  ChangedBy   CHAR(36)     NOT NULL,
  ChangedAt   DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  KEY IX_project_status_histories_CompanyId_ProjectId (CompanyId, ProjectId, ChangedAt),
  CONSTRAINT FK_project_status_histories_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- =========================================================
-- 5. 報價
-- =========================================================

CREATE TABLE quote_templates (
  Id              CHAR(36)     NOT NULL,
  CompanyId       CHAR(36)     NOT NULL,
  Name            VARCHAR(100) NOT NULL,
  Kind            VARCHAR(10)  NOT NULL,   -- pdf / excel
  LayoutJson      LONGTEXT     NOT NULL DEFAULT '{}' CHECK (JSON_VALID(LayoutJson)),
  TemplateFileId  CHAR(36)     NULL,
  DefaultTerms    TEXT         NULL,
  IsDefault       TINYINT(1)   NOT NULL DEFAULT 0,
  CreatedAt       DATETIME(6)  NOT NULL,
  UpdatedAt       DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  KEY IX_quote_templates_CompanyId_Kind (CompanyId, Kind),
  CONSTRAINT FK_quote_templates_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_quote_templates_files FOREIGN KEY (TemplateFileId) REFERENCES files (Id) ON DELETE SET NULL,
  CONSTRAINT CK_quote_templates_Kind CHECK (Kind IN ('pdf', 'excel'))
) ENGINE=InnoDB;

CREATE TABLE import_mapping_profiles (
  Id             CHAR(36)     NOT NULL,
  CompanyId      CHAR(36)     NOT NULL,
  Name           VARCHAR(100) NOT NULL,
  SheetName      VARCHAR(100) NULL,       -- NULL = 第一個工作表
  HeaderRow      INT          NOT NULL DEFAULT 1,
  DataStartRow   INT          NOT NULL DEFAULT 2,
  ColumnMapJson  LONGTEXT     NOT NULL DEFAULT '{}' CHECK (JSON_VALID(ColumnMapJson)),  -- {"Name":"B","Quantity":"E","UnitPrice":"F"}
  CellMapJson    LONGTEXT     NOT NULL DEFAULT '{}' CHECK (JSON_VALID(CellMapJson)),    -- {"ClientName":"C3","QuoteDate":"H3"}
  CreatedBy      CHAR(36)     NOT NULL,
  CreatedAt      DATETIME(6)  NOT NULL,
  UpdatedAt      DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_import_mapping_profiles_CompanyId_Name (CompanyId, Name),
  CONSTRAINT FK_import_mapping_profiles_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE import_jobs (
  Id                CHAR(36)     NOT NULL,
  CompanyId         CHAR(36)     NOT NULL,
  Kind              VARCHAR(20)  NOT NULL,   -- excel_template / legacy_excel / ocr
  FileId            CHAR(36)     NOT NULL,
  MappingProfileId  CHAR(36)     NULL,
  TargetProjectId   CHAR(36)     NULL,
  Status            VARCHAR(20)  NOT NULL DEFAULT 'uploaded',   -- uploaded / parsing / preview / confirmed / failed / cancelled
  PreviewJson       LONGTEXT     NULL CHECK (PreviewJson IS NULL OR JSON_VALID(PreviewJson)),
  ErrorsJson        LONGTEXT     NULL CHECK (ErrorsJson IS NULL OR JSON_VALID(ErrorsJson)),
  CreatedQuoteId    CHAR(36)     NULL,
  CreatedBy         CHAR(36)     NOT NULL,
  CreatedAt         DATETIME(6)  NOT NULL,
  UpdatedAt         DATETIME(6)  NOT NULL,
  PRIMARY KEY (Id),
  KEY IX_import_jobs_CompanyId_Status (CompanyId, Status, CreatedAt),
  CONSTRAINT FK_import_jobs_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE,
  CONSTRAINT FK_import_jobs_files FOREIGN KEY (FileId) REFERENCES files (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_import_jobs_profiles FOREIGN KEY (MappingProfileId) REFERENCES import_mapping_profiles (Id) ON DELETE SET NULL,
  CONSTRAINT FK_import_jobs_projects FOREIGN KEY (TargetProjectId) REFERENCES projects (Id) ON DELETE SET NULL,
  CONSTRAINT CK_import_jobs_Kind CHECK (Kind IN ('excel_template', 'legacy_excel', 'ocr'))
) ENGINE=InnoDB;

CREATE TABLE quotes (
  Id              CHAR(36)      NOT NULL,
  CompanyId       CHAR(36)      NOT NULL,
  ProjectId       CHAR(36)      NOT NULL,
  ClientId        CHAR(36)      NOT NULL,
  QuoteNo         VARCHAR(50)   NOT NULL,
  RevisionNo      INT           NOT NULL DEFAULT 0,
  RevisionOfId    CHAR(36)      NULL,
  IsLatest        TINYINT(1)    NOT NULL DEFAULT 1,
  Status          VARCHAR(20)   NOT NULL DEFAULT 'draft',
  Title           VARCHAR(200)  NULL,
  QuoteDate       DATE          NOT NULL,
  ValidUntil      DATE          NULL,
  -- 客戶快照：客戶主檔之後修改，已發出的報價不受影響
  ClientName      VARCHAR(200)  NOT NULL,
  ClientTaxId     VARCHAR(20)   NULL,
  ClientAddress   VARCHAR(500)  NULL,
  ContactName     VARCHAR(100)  NULL,
  ContactPhone    VARCHAR(50)   NULL,
  ContactEmail    VARCHAR(200)  NULL,
  -- 金額
  Currency        CHAR(3)       NOT NULL DEFAULT 'TWD',
  ExchangeRate    DECIMAL(18,6) NOT NULL DEFAULT 1,
  Subtotal        DECIMAL(18,2) NOT NULL DEFAULT 0,
  DiscountType    VARCHAR(10)   NOT NULL DEFAULT 'none',     -- none / percent / amount
  DiscountValue   DECIMAL(18,4) NOT NULL DEFAULT 0,
  DiscountAmount  DECIMAL(18,2) NOT NULL DEFAULT 0,
  TaxType         VARCHAR(10)   NOT NULL DEFAULT 'taxable',  -- taxable / zero / exempt
  TaxIncluded     TINYINT(1)    NOT NULL DEFAULT 0,          -- 0 = 外加, 1 = 內含
  TaxRate         DECIMAL(5,2)  NOT NULL DEFAULT 5.00,
  TaxAmount       DECIMAL(18,2) NOT NULL DEFAULT 0,
  Total           DECIMAL(18,2) NOT NULL DEFAULT 0,
  CostTotal       DECIMAL(18,2) NULL,
  GrossMargin     DECIMAL(18,2) NULL,
  -- 內容
  PaymentTerms    VARCHAR(500)  NULL,
  DeliveryTerms   VARCHAR(500)  NULL,
  Terms           TEXT          NULL,
  Note            TEXT          NULL,
  TemplateId      CHAR(36)      NULL,
  -- 來源與流程
  Source          VARCHAR(20)   NOT NULL DEFAULT 'manual',   -- manual / excel_template / legacy_excel / ocr / copy
  ImportJobId     CHAR(36)      NULL,
  LastPdfFileId   CHAR(36)      NULL,
  ApprovedBy      CHAR(36)      NULL,
  ApprovedAt      DATETIME(6)   NULL,
  SentAt          DATETIME(6)   NULL,
  AcceptedAt      DATETIME(6)   NULL,
  RejectedAt      DATETIME(6)   NULL,
  CreatedBy       CHAR(36)      NOT NULL,
  CreatedAt       DATETIME(6)   NOT NULL,
  UpdatedBy       CHAR(36)      NULL,
  UpdatedAt       DATETIME(6)   NOT NULL,
  IsDeleted       TINYINT(1)    NOT NULL DEFAULT 0,
  DeletedAt       DATETIME(6)   NULL,
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
) ENGINE=InnoDB;

CREATE TABLE quote_items (
  Id            CHAR(36)      NOT NULL,
  CompanyId     CHAR(36)      NOT NULL,
  QuoteId       CHAR(36)      NOT NULL,
  SortOrder     INT           NOT NULL,
  LineType      VARCHAR(10)   NOT NULL DEFAULT 'item',   -- item / section / subtotal / note
  ProductId     CHAR(36)      NULL,
  ItemCode      VARCHAR(50)   NULL,
  Name          VARCHAR(500)  NOT NULL,
  Spec          TEXT          NULL,
  Unit          VARCHAR(20)   NULL,
  Quantity      DECIMAL(18,4) NOT NULL DEFAULT 0,
  UnitPrice     DECIMAL(18,4) NOT NULL DEFAULT 0,
  DiscountRate  DECIMAL(5,2)  NOT NULL DEFAULT 0,        -- 單項折扣 %
  Amount        DECIMAL(18,2) NOT NULL DEFAULT 0,        -- 後端重算
  UnitCost      DECIMAL(18,4) NULL,
  Remark        VARCHAR(500)  NULL,
  PRIMARY KEY (Id),
  KEY IX_quote_items_CompanyId_QuoteId (CompanyId, QuoteId, SortOrder),
  KEY IX_quote_items_ProductId (ProductId),
  CONSTRAINT FK_quote_items_quotes FOREIGN KEY (QuoteId) REFERENCES quotes (Id) ON DELETE CASCADE,
  CONSTRAINT FK_quote_items_products FOREIGN KEY (ProductId) REFERENCES products (Id) ON DELETE SET NULL,
  CONSTRAINT CK_quote_items_LineType CHECK (LineType IN ('item', 'section', 'subtotal', 'note'))
) ENGINE=InnoDB;

CREATE TABLE number_sequences (
  CompanyId     CHAR(36)    NOT NULL,
  DocType       VARCHAR(20) NOT NULL,   -- project / quote
  Period        VARCHAR(10) NOT NULL,   -- 依單號規則最細的日期代號：20261030、202610 或 2026；不分期間時用 'all'
  Prefix        VARCHAR(20) NOT NULL,
  CurrentValue  INT         NOT NULL DEFAULT 0,
  UpdatedAt     DATETIME(6) NOT NULL,
  PRIMARY KEY (CompanyId, DocType, Period),
  CONSTRAINT FK_number_sequences_companies FOREIGN KEY (CompanyId) REFERENCES companies (Id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- =========================================================
-- 6. 訂單、保固、發票（每專案多筆）
-- =========================================================

CREATE TABLE project_orders (
  Id         CHAR(36)      NOT NULL,
  CompanyId  CHAR(36)      NOT NULL,
  ProjectId  CHAR(36)      NOT NULL,
  QuoteId    CHAR(36)      NULL,
  OrderNo    VARCHAR(100)  NOT NULL,     -- 客戶 PO 號或內部訂單號
  OrderDate  DATE          NOT NULL,
  Amount     DECIMAL(18,2) NOT NULL DEFAULT 0,
  TaxAmount  DECIMAL(18,2) NOT NULL DEFAULT 0,
  Total      DECIMAL(18,2) NOT NULL DEFAULT 0,
  Status     VARCHAR(20)   NOT NULL DEFAULT 'received',   -- received / in_progress / delivered / cancelled
  Note       TEXT          NULL,
  CreatedBy  CHAR(36)      NOT NULL,
  CreatedAt  DATETIME(6)   NOT NULL,
  UpdatedBy  CHAR(36)      NULL,
  UpdatedAt  DATETIME(6)   NOT NULL,
  IsDeleted  TINYINT(1)    NOT NULL DEFAULT 0,
  DeletedAt  DATETIME(6)   NULL,
  PRIMARY KEY (Id),
  KEY IX_project_orders_CompanyId_ProjectId (CompanyId, ProjectId),
  KEY IX_project_orders_CompanyId_OrderDate (CompanyId, OrderDate),
  CONSTRAINT FK_project_orders_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_project_orders_quotes FOREIGN KEY (QuoteId) REFERENCES quotes (Id) ON DELETE SET NULL,
  CONSTRAINT CK_project_orders_Status CHECK (Status IN ('received', 'in_progress', 'delivered', 'cancelled'))
) ENGINE=InnoDB;

-- 出貨單：掛在訂單下，一張訂單可分批出貨（AddProjectShipments）。
CREATE TABLE project_shipments (
  Id               CHAR(36)     NOT NULL,
  CompanyId        CHAR(36)     NOT NULL,
  ProjectId        CHAR(36)     NOT NULL,
  OrderId          CHAR(36)     NOT NULL,
  ShipmentNo       VARCHAR(100) NOT NULL,
  ShipDate         DATE         NOT NULL,
  Status           VARCHAR(20)  NOT NULL DEFAULT 'shipped',   -- shipped / delivered / cancelled
  ReceiverName     VARCHAR(100) NULL,
  DeliveryAddress  VARCHAR(300) NULL,
  TrackingNo       VARCHAR(100) NULL,
  Note             TEXT         NULL,
  CreatedBy        CHAR(36)     NOT NULL,
  CreatedAt        DATETIME(6)  NOT NULL,
  UpdatedBy        CHAR(36)     NULL,
  UpdatedAt        DATETIME(6)  NOT NULL,
  IsDeleted        TINYINT(1)   NOT NULL DEFAULT 0,
  DeletedAt        DATETIME(6)  NULL,
  PRIMARY KEY (Id),
  KEY IX_project_shipments_CompanyId_ProjectId (CompanyId, ProjectId),
  KEY IX_project_shipments_CompanyId_OrderId (CompanyId, OrderId),
  CONSTRAINT FK_project_shipments_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_project_shipments_orders FOREIGN KEY (OrderId) REFERENCES project_orders (Id) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE project_warranties (
  Id          CHAR(36)     NOT NULL,
  CompanyId   CHAR(36)     NOT NULL,
  ProjectId   CHAR(36)     NOT NULL,
  OrderId     CHAR(36)     NULL,
  WarrantyNo  VARCHAR(100) NOT NULL,
  Subject     VARCHAR(200) NOT NULL,
  StartDate   DATE         NOT NULL,
  EndDate     DATE         NOT NULL,
  Terms       TEXT         NULL,
  Note        TEXT         NULL,
  CreatedBy   CHAR(36)     NOT NULL,
  CreatedAt   DATETIME(6)  NOT NULL,
  UpdatedBy   CHAR(36)     NULL,
  UpdatedAt   DATETIME(6)  NOT NULL,
  IsDeleted   TINYINT(1)   NOT NULL DEFAULT 0,
  DeletedAt   DATETIME(6)  NULL,
  PRIMARY KEY (Id),
  KEY IX_project_warranties_CompanyId_ProjectId (CompanyId, ProjectId),
  KEY IX_project_warranties_CompanyId_EndDate (CompanyId, EndDate),
  CONSTRAINT FK_project_warranties_projects FOREIGN KEY (ProjectId) REFERENCES projects (Id) ON DELETE RESTRICT,
  CONSTRAINT FK_project_warranties_orders FOREIGN KEY (OrderId) REFERENCES project_orders (Id) ON DELETE SET NULL,
  CONSTRAINT CK_project_warranties_dates CHECK (EndDate >= StartDate)
) ENGINE=InnoDB;

CREATE TABLE project_invoices (
  Id             CHAR(36)      NOT NULL,
  CompanyId      CHAR(36)      NOT NULL,
  ProjectId      CHAR(36)      NOT NULL,
  OrderId        CHAR(36)      NULL,
  InvoiceNo      VARCHAR(20)   NOT NULL,     -- 例如 AB-12345678
  InvoiceDate    DATE          NOT NULL,
  InvoiceType    VARCHAR(20)   NOT NULL DEFAULT 'triplicate',   -- duplicate / triplicate / e_invoice
  BuyerName      VARCHAR(200)  NULL,
  BuyerTaxId     VARCHAR(20)   NULL,
  SalesAmount    DECIMAL(18,2) NOT NULL DEFAULT 0,
  TaxAmount      DECIMAL(18,2) NOT NULL DEFAULT 0,
  TotalAmount    DECIMAL(18,2) NOT NULL DEFAULT 0,
  Status         VARCHAR(20)   NOT NULL DEFAULT 'issued',       -- issued / void / allowance
  PaymentStatus  VARCHAR(20)   NOT NULL DEFAULT 'unpaid',       -- unpaid / partial / paid
  PaidAmount     DECIMAL(18,2) NOT NULL DEFAULT 0,
  DueDate        DATE          NULL,
  PaidAt         DATETIME(6)   NULL,
  VoidedAt       DATETIME(6)   NULL,
  Note           TEXT          NULL,
  CreatedBy      CHAR(36)      NOT NULL,
  CreatedAt      DATETIME(6)   NOT NULL,
  UpdatedBy      CHAR(36)      NULL,
  UpdatedAt      DATETIME(6)   NOT NULL,
  IsDeleted      TINYINT(1)    NOT NULL DEFAULT 0,
  DeletedAt      DATETIME(6)   NULL,
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
) ENGINE=InnoDB;

-- =========================================================
-- 7. 循環關聯：表都建立後再補 FK
-- =========================================================

ALTER TABLE companies
  ADD CONSTRAINT FK_companies_root_admin_membership FOREIGN KEY (RootAdminMembershipId) REFERENCES company_memberships (Id) ON DELETE RESTRICT,
  ADD CONSTRAINT FK_companies_logo_file FOREIGN KEY (LogoFileId) REFERENCES files (Id) ON DELETE SET NULL;

ALTER TABLE import_jobs
  ADD CONSTRAINT FK_import_jobs_created_quote FOREIGN KEY (CreatedQuoteId) REFERENCES quotes (Id) ON DELETE SET NULL;
