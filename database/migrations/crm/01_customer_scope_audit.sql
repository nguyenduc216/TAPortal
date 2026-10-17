/*
 FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
 CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-005
 ORDER: 01
 SQL Server 2014 / compatibility 120 compatible. Additive and idempotent.
 Run manually only after 00_schema_inspection.sql and 00b_rbac_constraint_inspection.sql.
 AuditLogs mapping for later application writers: ModuleCode=CRM; FunctionCode=the
 secured CRM function; ActionCode=ASSIGN/CHANGE/REVOKE; EntityType identifies
 CustomerAssignment/CustomerBranchShare/CustomerServiceTeam; EntityId is the
 relation Id; OldValues/NewValues are serialized before/after values.
*/
SET NOCOUNT ON; SET XACT_ABORT ON;
IF DB_NAME()<>N'TAPortalSys' THROW 52340,'Wrong database; expected TAPortalSys.',1;
 IF OBJECT_ID(N'dbo.Customers',N'U') IS NULL OR OBJECT_ID(N'dbo.Branches',N'U') IS NULL OR OBJECT_ID(N'dbo.Teams',N'U') IS NULL OR OBJECT_ID(N'dbo.Employees',N'U') IS NULL OR OBJECT_ID(N'dbo.EmployeeAssignments',N'U') IS NULL OR OBJECT_ID(N'dbo.AuditLogs',N'U') IS NULL
 THROW 52341,'Required CRM/HR/audit table is missing.',1;
IF COL_LENGTH(N'dbo.Customers',N'Id') IS NULL OR COL_LENGTH(N'dbo.Customers',N'Code') IS NULL OR COL_LENGTH(N'dbo.Customers',N'IsDeleted') IS NULL
 THROW 52342,'Customers contract is incomplete.',1;
IF COL_LENGTH(N'dbo.Branches',N'Id') IS NULL OR COL_LENGTH(N'dbo.Branches',N'IsActive') IS NULL OR COL_LENGTH(N'dbo.Branches',N'IsDeleted') IS NULL
 THROW 52343,'Branches contract is incomplete.',1;
 IF COL_LENGTH(N'dbo.AuditLogs',N'UserId') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'ModuleCode') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'FunctionCode') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'ActionCode') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'EntityType') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'EntityId') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'OldValues') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'NewValues') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'CreatedAt') IS NULL
  THROW 52344,'AuditLogs contract is incomplete; expected UserId/ModuleCode/FunctionCode/ActionCode/EntityType/EntityId/OldValues/NewValues/CreatedAt.',1;
IF TYPE_NAME((SELECT system_type_id FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Customers') AND name=N'Id'))<>N'uniqueidentifier'
 OR TYPE_NAME((SELECT system_type_id FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Branches') AND name=N'Id'))<>N'uniqueidentifier'
 OR TYPE_NAME((SELECT system_type_id FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Employees') AND name=N'Id'))<>N'uniqueidentifier'
 THROW 52345,'CRM key types must be uniqueidentifier.',1;

BEGIN TRY
 BEGIN TRANSACTION;
 IF COL_LENGTH(N'dbo.Customers',N'OwnerBranchId') IS NULL
 BEGIN
   ALTER TABLE dbo.Customers ADD OwnerBranchId uniqueidentifier NULL;
 END;
 IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Customers') AND name=N'FK_Customers_OwnerBranch')
   ALTER TABLE dbo.Customers ADD CONSTRAINT FK_Customers_OwnerBranch FOREIGN KEY(OwnerBranchId) REFERENCES dbo.Branches(Id);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Customers') AND name=N'IX_Customers_OwnerBranch_Active')
   CREATE INDEX IX_Customers_OwnerBranch_Active ON dbo.Customers(OwnerBranchId,IsDeleted,IsActive);

 IF OBJECT_ID(N'dbo.CustomerEmployeeAssignments',N'U') IS NULL
 BEGIN
   CREATE TABLE dbo.CustomerEmployeeAssignments(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerEmployeeAssignments PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    CustomerId uniqueidentifier NOT NULL, EmployeeId uniqueidentifier NOT NULL,
    RoleCode varchar(50) NOT NULL, ValidFrom date NOT NULL, ValidTo date NULL,
    IsActive bit NOT NULL CONSTRAINT DF_CEA_IsActive DEFAULT 1,
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CEA_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy uniqueidentifier NULL,
    CONSTRAINT FK_CEA_Customer FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id),
    CONSTRAINT FK_CEA_Employee FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(Id),
    CONSTRAINT CK_CEA_Dates CHECK(ValidTo IS NULL OR ValidTo>=ValidFrom),
    CONSTRAINT CK_CEA_Role CHECK(RoleCode IN ('OWNER','PRIMARY','SECONDARY','VIEWER'))
   );
 END;
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerEmployeeAssignments') AND name=N'IX_CEA_Employee_Current')
   CREATE INDEX IX_CEA_Employee_Current ON dbo.CustomerEmployeeAssignments(EmployeeId,IsActive,ValidFrom,ValidTo,CustomerId);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerEmployeeAssignments') AND name=N'IX_CEA_Customer_Current')
   CREATE INDEX IX_CEA_Customer_Current ON dbo.CustomerEmployeeAssignments(CustomerId,IsActive,ValidFrom,ValidTo,EmployeeId);
  IF COL_LENGTH(N'dbo.CustomerEmployeeAssignments',N'RowVersion') IS NULL AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerEmployeeAssignments') AND system_type_id=189)
    ALTER TABLE dbo.CustomerEmployeeAssignments ADD RowVersion rowversion NOT NULL;

 IF OBJECT_ID(N'dbo.CustomerBranchShares',N'U') IS NULL
 BEGIN
   CREATE TABLE dbo.CustomerBranchShares(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerBranchShares PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    CustomerId uniqueidentifier NOT NULL, BranchId uniqueidentifier NOT NULL,
    ValidFrom date NOT NULL, ValidTo date NULL, IsActive bit NOT NULL CONSTRAINT DF_CBS_IsActive DEFAULT 1,
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CBS_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy uniqueidentifier NULL,
    CONSTRAINT FK_CBS_Customer FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id),
    CONSTRAINT FK_CBS_Branch FOREIGN KEY(BranchId) REFERENCES dbo.Branches(Id),
    CONSTRAINT CK_CBS_Dates CHECK(ValidTo IS NULL OR ValidTo>=ValidFrom)
   );
 END;
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerBranchShares') AND name=N'IX_CBS_Branch_Current')
   CREATE INDEX IX_CBS_Branch_Current ON dbo.CustomerBranchShares(BranchId,IsActive,ValidFrom,ValidTo,CustomerId);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerBranchShares') AND name=N'IX_CBS_Customer_Current')
   CREATE INDEX IX_CBS_Customer_Current ON dbo.CustomerBranchShares(CustomerId,IsActive,ValidFrom,ValidTo,BranchId);
  IF COL_LENGTH(N'dbo.CustomerBranchShares',N'RowVersion') IS NULL AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerBranchShares') AND system_type_id=189)
    ALTER TABLE dbo.CustomerBranchShares ADD RowVersion rowversion NOT NULL;

  IF OBJECT_ID(N'dbo.CustomerServiceTeams',N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.CustomerServiceTeams(
     Id uniqueidentifier NOT NULL CONSTRAINT PK_CustomerServiceTeams PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
     CustomerId uniqueidentifier NOT NULL, TeamId uniqueidentifier NOT NULL,
     ValidFrom date NOT NULL, ValidTo date NULL,
     IsActive bit NOT NULL CONSTRAINT DF_CST_IsActive DEFAULT 1,
     CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CST_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy uniqueidentifier NULL,
     RowVersion rowversion NOT NULL,
     CONSTRAINT FK_CST_Customer FOREIGN KEY(CustomerId) REFERENCES dbo.Customers(Id),
     CONSTRAINT FK_CST_Team FOREIGN KEY(TeamId) REFERENCES dbo.Teams(Id),
     CONSTRAINT CK_CST_Dates CHECK(ValidTo IS NULL OR ValidTo>=ValidFrom)
    );
  END;
  IF COL_LENGTH(N'dbo.CustomerServiceTeams',N'CustomerId') IS NULL OR COL_LENGTH(N'dbo.CustomerServiceTeams',N'TeamId') IS NULL OR COL_LENGTH(N'dbo.CustomerServiceTeams',N'ValidFrom') IS NULL OR COL_LENGTH(N'dbo.CustomerServiceTeams',N'ValidTo') IS NULL OR COL_LENGTH(N'dbo.CustomerServiceTeams',N'IsActive') IS NULL OR COL_LENGTH(N'dbo.CustomerServiceTeams',N'RowVersion') IS NULL
    THROW 52346,'CustomerServiceTeams contract is incomplete.',1;
  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerServiceTeams') AND name=N'IX_CST_Team_Current')
    CREATE INDEX IX_CST_Team_Current ON dbo.CustomerServiceTeams(TeamId,IsActive,ValidFrom,ValidTo,CustomerId);
  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CustomerServiceTeams') AND name=N'IX_CST_Customer_Current')
    CREATE INDEX IX_CST_Customer_Current ON dbo.CustomerServiceTeams(CustomerId,IsActive,ValidFrom,ValidTo,TeamId);
 COMMIT;
END TRY
BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;
SELECT N'CRM scope/audit tables ready' AS MigrationStatus;