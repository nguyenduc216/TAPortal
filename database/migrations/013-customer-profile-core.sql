-- CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB — Customer profile phase 1. MIGRATION_NOT_EXECUTED.
-- Replaces the per-customer sheets of the internal Excel workbook: business profile (1:1 with dbo.Customers),
-- representatives (director / owner / contact person) and system accounts (tax portal, e-invoice, digital signature,
-- social insurance, banking, sales software...). Passwords and PINs are stored only as ciphertext produced by
-- TAPortal.Web (AES-256-GCM, fixed key CustomerSecrets__Key); this script never stores or reads a secret.
-- Creates new tables only (when missing); never alters or drops existing objects and writes no data. Idempotent.
-- Order: after 012-tingee-customer-buyers.sql. Verify: database/scripts/015-verify-customer-profile.sql.
-- Rollback: database/scripts/016-rollback-customer-profile.sql (refuses while the new tables contain data).
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
-- Equivalent required positive guard: DB_NAME() = 'TAPortalSys'. No USE statement.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.Customers','U') IS NULL
 THROW 51000, 'Precondition failed: dbo.Customers missing (run 007 first)', 1;

-- 1. Business profile ("Thông tin chung"), one row per customer.
IF OBJECT_ID(N'dbo.CustomerProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerProfiles (
        CustomerId uniqueidentifier NOT NULL CONSTRAINT PK_CustomerProfiles PRIMARY KEY,
        ShortName nvarchar(100) NULL,
        LegalForm varchar(20) NULL,
        CharterCapital decimal(18,0) NULL,
        EstablishedDate date NULL,
        MainIndustryCode varchar(10) NULL,
        MainIndustryName nvarchar(500) NULL,
        BusinessLine nvarchar(200) NULL,
        TaxAuthority nvarchar(200) NULL,
        TaxOfficerName nvarchar(150) NULL,
        TaxOfficerPhone nvarchar(30) NULL,
        BusinessStatus varchar(20) NULL,
        BusinessStatusDate date NULL,
        ScaleNote nvarchar(200) NULL,
        SourceCode varchar(30) NULL,
        SourceNote nvarchar(250) NULL,
        ParentCustomerId uniqueidentifier NULL,
        Note nvarchar(max) NULL,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CPF_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_CPF_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT FK_CPF_Parent FOREIGN KEY (ParentCustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT CK_CPF_NotOwnParent CHECK (ParentCustomerId IS NULL OR ParentCustomerId <> CustomerId),
        CONSTRAINT CK_CPF_LegalForm CHECK (LegalForm IS NULL OR LegalForm IN ('LLC_1','LLC_2','JSC','PRIVATE','PARTNERSHIP','HOUSEHOLD','INDIVIDUAL','OTHER')),
        CONSTRAINT CK_CPF_BusinessStatus CHECK (BusinessStatus IS NULL OR BusinessStatus IN ('OPERATING','SUSPENDED','DISSOLVING','DISSOLVED')),
        CONSTRAINT CK_CPF_Capital CHECK (CharterCapital IS NULL OR CharterCapital >= 0)
    );
    CREATE INDEX IX_CPF_Parent ON dbo.CustomerProfiles(ParentCustomerId) WHERE ParentCustomerId IS NOT NULL;
    PRINT N'dbo.CustomerProfiles created';
END ELSE PRINT N'dbo.CustomerProfiles exists';

-- 2. Representatives ("Giám đốc / người phụ trách"). IdentityNumber and DateOfBirth are personal data.
IF OBJECT_ID(N'dbo.CustomerRepresentatives', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerRepresentatives (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerRepresentatives PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        RoleCode varchar(30) NOT NULL,
        IsLegalRepresentative bit NOT NULL CONSTRAINT DF_CRP_Legal DEFAULT 0,
        IsPrimaryContact bit NOT NULL CONSTRAINT DF_CRP_Primary DEFAULT 0,
        FullName nvarchar(150) NOT NULL,
        Gender varchar(10) NULL,
        DateOfBirth date NULL,
        IdentityNumber varchar(20) NULL,
        IdentityIssueDate date NULL,
        IdentityIssuePlace nvarchar(200) NULL,
        Address nvarchar(500) NULL,
        Email nvarchar(256) NULL,
        Phone nvarchar(30) NULL,
        Note nvarchar(1000) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_CRP_Sort DEFAULT 0,
        IsDeleted bit NOT NULL CONSTRAINT DF_CRP_Deleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CRP_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_CRP_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT CK_CRP_Role CHECK (RoleCode IN ('DIRECTOR','GENERAL_DIRECTOR','HIRED_DIRECTOR','OWNER','CONTACT','CHIEF_ACCOUNTANT','ACCOUNTANT','OTHER')),
        CONSTRAINT CK_CRP_Gender CHECK (Gender IS NULL OR Gender IN ('MALE','FEMALE'))
    );
    CREATE INDEX IX_CRP_Customer ON dbo.CustomerRepresentatives(CustomerId, IsDeleted, SortOrder);
    PRINT N'dbo.CustomerRepresentatives created';
END ELSE PRINT N'dbo.CustomerRepresentatives exists';

-- 3. System accounts: one row per login the customer has (any number per system type).
--    PasswordCipher / PinCipher hold AES-256-GCM ciphertext only; plaintext never reaches the database.
IF OBJECT_ID(N'dbo.CustomerSystemAccounts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerSystemAccounts (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerSystemAccounts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CustomerId uniqueidentifier NOT NULL,
        SystemType varchar(30) NOT NULL,
        ProviderName nvarchar(100) NULL,
        SystemName nvarchar(150) NULL,
        LoginUrl nvarchar(500) NULL,
        Username nvarchar(256) NULL,
        LoginPhone nvarchar(30) NULL,
        StoreName nvarchar(150) NULL,
        UnitCode nvarchar(50) NULL,
        BankAccountNumber nvarchar(50) NULL,
        CertificateType varchar(20) NULL,
        CertificateSerial nvarchar(100) NULL,
        ValidFrom date NULL,
        ExpiresAt date NULL,
        PasswordCipher varbinary(4000) NULL,
        PinCipher varbinary(4000) NULL,
        SecretChangedAt datetime2(3) NULL,
        SecretChangedBy uniqueidentifier NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_CSA_Status DEFAULT 'ACTIVE',
        ManagedBy varchar(20) NOT NULL CONSTRAINT DF_CSA_ManagedBy DEFAULT 'TA',
        LastVerifiedAt datetime2(3) NULL,
        Note nvarchar(1000) NULL,
        SortOrder int NOT NULL CONSTRAINT DF_CSA_Sort DEFAULT 0,
        IsDeleted bit NOT NULL CONSTRAINT DF_CSA_Deleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CSA_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_CSA_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT CK_CSA_SystemType CHECK (SystemType IN ('TAX_PORTAL','EINVOICE_PORTAL','ETAX_MOBILE','DIGITAL_SIGNATURE','EINVOICE_PROVIDER','SOCIAL_INSURANCE','BANKING','SALES_POS','ACCOUNTING_SOFTWARE','EMAIL','GOV_OTHER','OTHER')),
        CONSTRAINT CK_CSA_CertificateType CHECK (CertificateType IS NULL OR CertificateType IN ('USB_TOKEN','REMOTE_SIGNING','HSM')),
        CONSTRAINT CK_CSA_Status CHECK (Status IN ('ACTIVE','PENDING','LOCKED','INVALID','INACTIVE')),
        CONSTRAINT CK_CSA_ManagedBy CHECK (ManagedBy IN ('TA','CUSTOMER')),
        CONSTRAINT CK_CSA_Validity CHECK (ValidFrom IS NULL OR ExpiresAt IS NULL OR ValidFrom <= ExpiresAt)
    );
    CREATE INDEX IX_CSA_Customer ON dbo.CustomerSystemAccounts(CustomerId, IsDeleted, SystemType, SortOrder);
    CREATE INDEX IX_CSA_Expires ON dbo.CustomerSystemAccounts(ExpiresAt) WHERE ExpiresAt IS NOT NULL AND IsDeleted = 0;
    PRINT N'dbo.CustomerSystemAccounts created';
END ELSE PRINT N'dbo.CustomerSystemAccounts exists';

COMMIT;
PRINT N'013-customer-profile-core.sql: OK';
