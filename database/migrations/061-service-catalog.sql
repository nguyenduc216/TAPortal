-- CONTRACTS-AND-TASKS - Service catalogue for contracts and quotes (operator 2026-11-16, quote "Bao gia cong ty dich vu").
-- A service belongs to a service group (Ke toan thue thang, Ke toan noi bo, Nhan su, Phap ly, ...). Each service has its
-- scope of work, its fee rules / notes and its pricing: by tiers (one to three measures such as "so luong chung tu",
-- "quy mo nhan su", each tier a numeric range per measure and a fee, optionally one fee per industry), a fixed fee
-- (tron goi) or quoted separately. Industries (Thuong mai - dich vu, San xuat - gia cong, ...) are a catalogue of their
-- own and are set on each customer.
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 060.
--   dbo.ServiceCategories, dbo.Services, dbo.Industries (new tables)
--   dbo.Customers + IndustryId (nullable, FK dbo.Industries)
--   dbo.Permissions + CONTRACTS.SERVICES.MANAGE (granted to SYS_ADMIN; other roles get it on Vai tro & phan quyen).
-- The catalogue entries themselves are business data entered on Hop dong > Danh muc dich vu (not seeded here).
-- Rows it inserts carry CreatedBy = 'A0000061-0000-4000-8000-000000000061' (marker) so the rollback removes exactly them.
-- New tables and a nullable column; no existing row is changed. Idempotent.
-- Verify: database/scripts/109-verify-service-catalog.sql. Rollback: database/scripts/110-rollback-service-catalog.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL OR OBJECT_ID(N'dbo.Permissions', N'U') IS NULL OR OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL OR OBJECT_ID(N'dbo.Roles', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Customers, dbo.Permissions, dbo.RolePermissions or dbo.Roles missing', 1;
DECLARE @SysAdmin uniqueidentifier = (SELECT TOP 1 Id FROM dbo.Roles WHERE Code = 'SYS_ADMIN' AND IsDeleted = 0);
IF @SysAdmin IS NULL
 THROW 51001, 'Precondition failed: role SYS_ADMIN missing', 1;
DECLARE @Marker uniqueidentifier = 'A0000061-0000-4000-8000-000000000061';

IF OBJECT_ID(N'dbo.ServiceCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ServiceCategories (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ServiceCategories PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Name nvarchar(150) NOT NULL,
        Description nvarchar(500) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_ServiceCategories_SortOrder DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_ServiceCategories_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_ServiceCategories_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ServiceCategories_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT CK_ServiceCategories_Name CHECK (LEN(Name) > 0)
    );
    CREATE UNIQUE INDEX UX_ServiceCategories_Name ON dbo.ServiceCategories(Name) WHERE IsDeleted = 0;
    PRINT N'dbo.ServiceCategories: created';
END

IF OBJECT_ID(N'dbo.Industries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Industries (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Industries PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Name nvarchar(150) NOT NULL,
        Description nvarchar(500) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_Industries_SortOrder DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_Industries_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Industries_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Industries_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT CK_Industries_Name CHECK (LEN(Name) > 0)
    );
    CREATE UNIQUE INDEX UX_Industries_Name ON dbo.Industries(Name) WHERE IsDeleted = 0;
    PRINT N'dbo.Industries: created';
END

IF OBJECT_ID(N'dbo.Services', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Services (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Services PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CategoryId uniqueidentifier NOT NULL,
        Name nvarchar(200) NOT NULL,
        Description nvarchar(1000) NULL,
        PricingMode varchar(10) NOT NULL CONSTRAINT DF_Services_PricingMode DEFAULT 'TIER',
        FeeLabel nvarchar(100) NULL,
        FeeUnit nvarchar(50) NULL,
        FixedFee decimal(18,0) NULL,
        PricingJson nvarchar(max) NULL,
        ScopeText nvarchar(max) NULL,
        RulesText nvarchar(max) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_Services_SortOrder DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_Services_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Services_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Services_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Services_Category FOREIGN KEY (CategoryId) REFERENCES dbo.ServiceCategories(Id),
        CONSTRAINT CK_Services_Name CHECK (LEN(Name) > 0),
        CONSTRAINT CK_Services_PricingMode CHECK (PricingMode IN ('TIER','FIXED','QUOTE')),
        CONSTRAINT CK_Services_FixedFee CHECK (FixedFee IS NULL OR FixedFee >= 0)
    );
    CREATE UNIQUE INDEX UX_Services_CategoryName ON dbo.Services(CategoryId, Name) WHERE IsDeleted = 0;
    PRINT N'dbo.Services: created';
END

IF COL_LENGTH(N'dbo.Customers', N'IndustryId') IS NULL
BEGIN
    ALTER TABLE dbo.Customers ADD IndustryId uniqueidentifier NULL;
    PRINT N'dbo.Customers.IndustryId: added';
END
IF OBJECT_ID(N'dbo.FK_Customers_Industry', N'F') IS NULL
BEGIN
    EXEC (N'ALTER TABLE dbo.Customers ADD CONSTRAINT FK_Customers_Industry FOREIGN KEY (IndustryId) REFERENCES dbo.Industries(Id);');
    PRINT N'FK_Customers_Industry: added';
END

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = 'CONTRACTS.SERVICES.MANAGE' AND IsDeleted = 0)
BEGIN
    INSERT dbo.Permissions (Id, Code, Name, ModuleCode, FunctionCode, ActionCode, Description, IsSystem, IsActive, CreatedBy)
    VALUES (NEWID(), 'CONTRACTS.SERVICES.MANAGE', N'Manage the service catalogue, fee tiers and industries', 'CONTRACTS', 'SERVICES', 'MANAGE', N'TAPortal Admin (migration 061)', 1, 1, @Marker);
    PRINT N'dbo.Permissions: CONTRACTS.SERVICES.MANAGE inserted';
END
INSERT dbo.RolePermissions (RoleId, PermissionId, CreatedBy)
SELECT @SysAdmin, p.Id, @Marker FROM dbo.Permissions p
WHERE p.Code = 'CONTRACTS.SERVICES.MANAGE' AND p.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @SysAdmin AND rp.PermissionId = p.Id);
PRINT N'INFO: SYS_ADMIN grants inserted = ' + CAST(@@ROWCOUNT AS nvarchar(10));
COMMIT TRANSACTION;
PRINT N'061-service-catalog.sql: OK';
