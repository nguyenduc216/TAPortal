/* FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
   CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-005
   ORDER: 03; read-only verification. */
SET NOCOUNT ON;
IF DB_NAME()<>N'TAPortalSys' THROW 52360,'Wrong database; expected TAPortalSys.',1;
IF COL_LENGTH(N'dbo.AuditLogs',N'Action') IS NOT NULL OR COL_LENGTH(N'dbo.AuditLogs',N'EntityName') IS NOT NULL THROW 52369,'Legacy AuditLogs Action/EntityName columns must not exist.',1;
IF COL_LENGTH(N'dbo.AuditLogs',N'ActionCode') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'EntityType') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'EntityId') IS NULL THROW 52370,'AuditLogs CRM contract is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AuditLogs') AND name=N'Id' AND system_type_id=127 AND is_identity=1) THROW 52371,'AuditLogs.Id bigint IDENTITY is missing.',1;
IF COL_LENGTH(N'dbo.Customers',N'OwnerBranchId') IS NULL THROW 52361,'OwnerBranchId is missing.',1;
IF OBJECT_ID(N'dbo.CustomerEmployeeAssignments',N'U') IS NULL THROW 52362,'CustomerEmployeeAssignments is missing.',1;
IF OBJECT_ID(N'dbo.CustomerBranchShares',N'U') IS NULL THROW 52363,'CustomerBranchShares is missing.',1;
IF OBJECT_ID(N'dbo.CustomerServiceTeams',N'U') IS NULL THROW 52366,'CustomerServiceTeams is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.Customers') AND name=N'FK_Customers_OwnerBranch') THROW 52364,'Owner branch FK is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Customers') AND name=N'IX_Customers_OwnerBranch_Active') THROW 52365,'Owner branch index is missing.',1;
IF COL_LENGTH(N'dbo.CustomerEmployeeAssignments',N'RowVersion') IS NULL OR COL_LENGTH(N'dbo.CustomerBranchShares',N'RowVersion') IS NULL OR COL_LENGTH(N'dbo.CustomerServiceTeams',N'RowVersion') IS NULL THROW 52367,'A scope table RowVersion is missing.',1;
IF COL_LENGTH(N'dbo.CustomerServiceTeams',N'CustomerId') IS NULL OR COL_LENGTH(N'dbo.CustomerServiceTeams',N'TeamId') IS NULL THROW 52368,'CustomerServiceTeams key contract is missing.',1;
SELECT DB_NAME() AS DatabaseName,'PASS' AS SchemaStatus;
SELECT OBJECT_NAME(parent_object_id) AS ParentTable,name AS ForeignKeyName FROM sys.foreign_keys WHERE name IN(N'FK_Customers_OwnerBranch',N'FK_CEA_Customer',N'FK_CEA_Employee',N'FK_CBS_Customer',N'FK_CBS_Branch',N'FK_CST_Customer',N'FK_CST_Team');
SELECT OBJECT_NAME(object_id) AS TableName,name AS IndexName,is_unique,filter_definition FROM sys.indexes WHERE object_id IN(OBJECT_ID(N'dbo.Customers'),OBJECT_ID(N'dbo.CustomerEmployeeAssignments'),OBJECT_ID(N'dbo.CustomerBranchShares'),OBJECT_ID(N'dbo.CustomerServiceTeams')) AND index_id>0;
SELECT ScopeType,ScopeReferenceMeaning FROM (VALUES
 ('ASSIGNED','CustomerEmployeeAssignments.EmployeeId'),
 ('TEAM','CustomerServiceTeams.TeamId joined to current EmployeeAssignments.TeamId'),
 ('BRANCH','Customers.OwnerBranchId or CustomerBranchShares.BranchId'),
 ('COMPANY','Branches.CompanyId resolved from an authorized branch scope'),
 ('SELF','NOT USED IN CRM PHASE 1'),
 ('CUSTOM','NOT USED IN CRM PHASE 1')) v(ScopeType,ScopeReferenceMeaning);