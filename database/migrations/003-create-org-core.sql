/* TAPortal - Common database bootstrap 003
   Organization: company / branch / team and user assignments
   SQL Server 2014+ compatible; dbo only.
*/
USE [TAPortal];
GO

IF OBJECT_ID(N'dbo.Companies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Companies (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Companies PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Code varchar(50) NOT NULL,
        Name nvarchar(250) NOT NULL,
        TaxCode varchar(50) NULL,
        Email nvarchar(256) NULL,
        Phone nvarchar(50) NULL,
        Address nvarchar(1000) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Companies_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Companies_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Companies_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL
    );
    CREATE UNIQUE INDEX UX_Companies_Code ON dbo.Companies(Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.Branches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Branches (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Branches PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        CompanyId uniqueidentifier NOT NULL,
        Code varchar(50) NOT NULL,
        Name nvarchar(250) NOT NULL,
        Email nvarchar(256) NULL,
        Phone nvarchar(50) NULL,
        Address nvarchar(1000) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Branches_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Branches_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Branches_Company FOREIGN KEY (CompanyId) REFERENCES dbo.Companies(Id)
    );
    CREATE UNIQUE INDEX UX_Branches_Company_Code ON dbo.Branches(CompanyId, Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.Teams', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Teams (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Teams PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        BranchId uniqueidentifier NOT NULL,
        Code varchar(50) NOT NULL,
        Name nvarchar(250) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Teams_IsActive DEFAULT 1,
        IsDeleted bit NOT NULL CONSTRAINT DF_Teams_IsDeleted DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Teams_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        UpdatedAt datetime2(3) NULL,
        UpdatedBy uniqueidentifier NULL,
        DeletedAt datetime2(3) NULL,
        DeletedBy uniqueidentifier NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_Teams_Branch FOREIGN KEY (BranchId) REFERENCES dbo.Branches(Id)
    );
    CREATE UNIQUE INDEX UX_Teams_Branch_Code ON dbo.Teams(BranchId, Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.UserBranches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserBranches (
        UserId uniqueidentifier NOT NULL,
        BranchId uniqueidentifier NOT NULL,
        IsPrimary bit NOT NULL CONSTRAINT DF_UserBranches_IsPrimary DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserBranches_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_UserBranches PRIMARY KEY (UserId, BranchId),
        CONSTRAINT FK_UserBranches_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_UserBranches_Branch FOREIGN KEY (BranchId) REFERENCES dbo.Branches(Id)
    );
END
GO

IF OBJECT_ID(N'dbo.UserTeams', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserTeams (
        UserId uniqueidentifier NOT NULL,
        TeamId uniqueidentifier NOT NULL,
        IsPrimary bit NOT NULL CONSTRAINT DF_UserTeams_IsPrimary DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserTeams_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_UserTeams PRIMARY KEY (UserId, TeamId),
        CONSTRAINT FK_UserTeams_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_UserTeams_Team FOREIGN KEY (TeamId) REFERENCES dbo.Teams(Id)
    );
END
GO

PRINT '003-create-org-core.sql: OK';
GO
