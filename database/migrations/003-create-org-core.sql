/* TAPortal - Common database bootstrap 003
   Organization: company / branch / team and user assignments
*/
USE [TAPortal];
GO

IF OBJECT_ID(N'org.Companies', N'U') IS NULL
BEGIN
    CREATE TABLE org.Companies (
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
    CREATE UNIQUE INDEX UX_Companies_Code ON org.Companies(Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'org.Branches', N'U') IS NULL
BEGIN
    CREATE TABLE org.Branches (
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
        CONSTRAINT FK_Branches_Company FOREIGN KEY (CompanyId) REFERENCES org.Companies(Id) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX UX_Branches_Company_Code ON org.Branches(CompanyId, Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'org.Teams', N'U') IS NULL
BEGIN
    CREATE TABLE org.Teams (
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
        CONSTRAINT FK_Teams_Branch FOREIGN KEY (BranchId) REFERENCES org.Branches(Id) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX UX_Teams_Branch_Code ON org.Teams(BranchId, Code) WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'org.UserBranches', N'U') IS NULL
BEGIN
    CREATE TABLE org.UserBranches (
        UserId uniqueidentifier NOT NULL,
        BranchId uniqueidentifier NOT NULL,
        IsPrimary bit NOT NULL CONSTRAINT DF_UserBranches_IsPrimary DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserBranches_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_UserBranches PRIMARY KEY (UserId, BranchId),
        CONSTRAINT FK_UserBranches_User FOREIGN KEY (UserId) REFERENCES auth.Users(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_UserBranches_Branch FOREIGN KEY (BranchId) REFERENCES org.Branches(Id) ON DELETE NO ACTION
    );
END
GO

IF OBJECT_ID(N'org.UserTeams', N'U') IS NULL
BEGIN
    CREATE TABLE org.UserTeams (
        UserId uniqueidentifier NOT NULL,
        TeamId uniqueidentifier NOT NULL,
        IsPrimary bit NOT NULL CONSTRAINT DF_UserTeams_IsPrimary DEFAULT 0,
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserTeams_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy uniqueidentifier NULL,
        CONSTRAINT PK_UserTeams PRIMARY KEY (UserId, TeamId),
        CONSTRAINT FK_UserTeams_User FOREIGN KEY (UserId) REFERENCES auth.Users(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_UserTeams_Team FOREIGN KEY (TeamId) REFERENCES org.Teams(Id) ON DELETE NO ACTION
    );
END
GO
