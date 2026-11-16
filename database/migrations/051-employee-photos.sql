-- HR-EMPLOYEES - Ảnh đại diện nhân viên (operator 2026-11-14).
-- MIGRATION_NOT_EXECUTED.
-- Change: see .codec (HR avatar change).
-- Run under AGENTS.md "## AGENT MIGRATION EXECUTION" on WIN-HSHL54Q184K\MSSQLSERVER2014 / TAPortalSys, after 050.
--   dbo.EmployeePhotos   one photo per employee: a square image already shrunk in the browser (<= 300 KB), its type,
--                        when and by whom it was set. Shown as the avatar wherever the employee or their account appears.
-- New table only; no existing row is changed. No permission added: HR.EMPLOYEE.UPDATE sets anyone's photo, every
-- employee sets their own on Hồ sơ cá nhân. Idempotent.
-- Verify: database/scripts/091-verify-employee-photos.sql. Rollback: database/scripts/092-rollback-employee-photos.sql.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET NOCOUNT ON;
IF DB_NAME() <> N'TAPortalSys'
 THROW 51000, 'Wrong database: expected TAPortalSys; no changes executed', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Employees', N'U') IS NULL
 THROW 51001, 'Precondition failed: dbo.Employees missing', 1;
IF OBJECT_ID(N'dbo.EmployeePhotos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeePhotos (
        EmployeeId uniqueidentifier NOT NULL CONSTRAINT PK_EmployeePhotos PRIMARY KEY
            CONSTRAINT FK_EmployeePhotos_Employees REFERENCES dbo.Employees(Id),
        Data varbinary(max) NOT NULL,
        ContentType varchar(50) NOT NULL,
        ByteSize int NOT NULL,
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_EmployeePhotos_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy uniqueidentifier NULL,
        CONSTRAINT CK_EmployeePhotos_Size CHECK (ByteSize > 0 AND ByteSize <= 307200),
        CONSTRAINT CK_EmployeePhotos_Type CHECK (ContentType IN ('image/jpeg', 'image/png', 'image/webp'))
    );
    PRINT N'dbo.EmployeePhotos: created';
END
COMMIT TRANSACTION;
PRINT N'051-employee-photos.sql: OK';
