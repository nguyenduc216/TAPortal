/* CHANGE_ID: TAPORTAL-20261013-CRM-PHASE1-001; ORDER: 02; read-only post-migration verification template. */
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys' THROW 52320,'Wrong database; expected TAPortalSys.',1;
SELECT DB_NAME() AS DatabaseName, N'PENDING: run after reviewed CRM DDL is supplied' AS Status;
SELECT OBJECT_SCHEMA_NAME(object_id) AS SchemaName,name AS ObjectName,type_desc
FROM sys.objects WHERE name IN (N'CustomerEmployeeAssignments',N'CustomerScopes') ORDER BY name;
SELECT N'No CRM migration was verified by this template.' AS VerificationNote;