/*
FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-002
PURPOSE: Reconcile and seed the database-driven FunctionAction authorization contract on TAPortalSys.
BUSINESS RULES:
- FunctionActions identify existing UI/endpoint operations without encoding role names.
- One action may map to multiple permissions; any current effective permission grants that action.
- Existing compatible tables and metadata columns are preserved.
- Blank UiKey/EndpointKey values may be populated; conflicting nonblank values fail closed.
SAFETY / IMPORTANT:
- The database-name guard runs before every schema or data modification.
- The migration is additive, transactional, idempotent, and restart-safe.
- It never drops/truncates objects or rewrites historical migrations 001-052.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME() <> N'TAPortalSys'
    THROW 52120, 'This migration must run against TAPortalSys.', 1;
GO

IF OBJECT_ID(N'dbo.Modules', N'U') IS NULL
    THROW 52121, 'Required table dbo.Modules is missing.', 1;
IF OBJECT_ID(N'dbo.Functions', N'U') IS NULL
    THROW 52122, 'Required table dbo.Functions is missing.', 1;
IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
    THROW 52123, 'Required table dbo.Permissions is missing.', 1;
IF COL_LENGTH(N'dbo.Modules',N'Id') IS NULL OR COL_LENGTH(N'dbo.Modules',N'Code') IS NULL
   OR COL_LENGTH(N'dbo.Modules',N'IsActive') IS NULL OR COL_LENGTH(N'dbo.Modules',N'IsDeleted') IS NULL
    THROW 52150, 'dbo.Modules is missing columns required by the FunctionAction contract.', 1;
IF COL_LENGTH(N'dbo.Functions',N'Id') IS NULL OR COL_LENGTH(N'dbo.Functions',N'ModuleId') IS NULL
   OR COL_LENGTH(N'dbo.Functions',N'Code') IS NULL OR COL_LENGTH(N'dbo.Functions',N'IsActive') IS NULL
   OR COL_LENGTH(N'dbo.Functions',N'IsDeleted') IS NULL
    THROW 52151, 'dbo.Functions is missing columns required by the FunctionAction contract.', 1;
IF COL_LENGTH(N'dbo.Permissions',N'Id') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'Code') IS NULL
   OR COL_LENGTH(N'dbo.Permissions',N'IsActive') IS NULL OR COL_LENGTH(N'dbo.Permissions',N'IsDeleted') IS NULL
    THROW 52152, 'dbo.Permissions is missing columns required by the FunctionAction contract.', 1;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.FunctionActions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FunctionActions (
            Id uniqueidentifier NOT NULL CONSTRAINT PK_FunctionActions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
            FunctionId uniqueidentifier NOT NULL,
            Code varchar(100) NOT NULL,
            Name nvarchar(200) NOT NULL,
            UiKey varchar(200) NULL,
            EndpointKey varchar(200) NULL,
            SortOrder int NOT NULL CONSTRAINT DF_FunctionActions_SortOrder DEFAULT 0,
            IsActive bit NOT NULL CONSTRAINT DF_FunctionActions_IsActive DEFAULT 1,
            IsDeleted bit NOT NULL CONSTRAINT DF_FunctionActions_IsDeleted DEFAULT 0,
            CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_FunctionActions_CreatedAt DEFAULT SYSUTCDATETIME(),
            CreatedBy uniqueidentifier NULL,
            UpdatedAt datetime2(3) NULL,
            UpdatedBy uniqueidentifier NULL,
            DeletedAt datetime2(3) NULL,
            DeletedBy uniqueidentifier NULL,
            RowVersion rowversion NOT NULL
        );
    END;

    IF COL_LENGTH(N'dbo.FunctionActions', N'Id') IS NULL
       OR COL_LENGTH(N'dbo.FunctionActions', N'FunctionId') IS NULL
       OR COL_LENGTH(N'dbo.FunctionActions', N'Code') IS NULL
       OR COL_LENGTH(N'dbo.FunctionActions', N'Name') IS NULL
       OR COL_LENGTH(N'dbo.FunctionActions', N'UiKey') IS NULL
       OR COL_LENGTH(N'dbo.FunctionActions', N'EndpointKey') IS NULL
       OR COL_LENGTH(N'dbo.FunctionActions', N'SortOrder') IS NULL
       OR COL_LENGTH(N'dbo.FunctionActions', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.FunctionActions', N'IsDeleted') IS NULL
        THROW 52124, 'dbo.FunctionActions is missing one or more required columns.', 1;

    IF EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.FunctionActions') AND (
            (c.name IN (N'Id',N'FunctionId') AND (t.name<>N'uniqueidentifier' OR c.is_nullable=1))
            OR (c.name IN (N'Code',N'Name',N'UiKey',N'EndpointKey') AND t.name NOT IN (N'varchar',N'nvarchar'))
            OR (c.name=N'SortOrder' AND (t.name<>N'int' OR c.is_nullable=1))
            OR (c.name IN (N'IsActive',N'IsDeleted') AND (t.name<>N'bit' OR c.is_nullable=1))
        )
    )
        THROW 52125, 'dbo.FunctionActions has incompatible required column types/nullability.', 1;

    IF OBJECT_ID(N'dbo.FunctionActionPermissions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FunctionActionPermissions (
            FunctionActionId uniqueidentifier NOT NULL,
            PermissionId uniqueidentifier NOT NULL,
            CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_FunctionActionPermissions_CreatedAt DEFAULT SYSUTCDATETIME(),
            CreatedBy uniqueidentifier NULL,
            CONSTRAINT PK_FunctionActionPermissions PRIMARY KEY(FunctionActionId, PermissionId)
        );
    END;

    IF COL_LENGTH(N'dbo.FunctionActionPermissions', N'FunctionActionId') IS NULL
       OR COL_LENGTH(N'dbo.FunctionActionPermissions', N'PermissionId') IS NULL
        THROW 52126, 'dbo.FunctionActionPermissions is missing required columns.', 1;

    IF EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.FunctionActionPermissions')
          AND c.name IN (N'FunctionActionId',N'PermissionId')
          AND (t.name<>N'uniqueidentifier' OR c.is_nullable=1)
    )
        THROW 52127, 'dbo.FunctionActionPermissions has incompatible required column types/nullability.', 1;

    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
        WHERE c.object_id=OBJECT_ID(N'dbo.FunctionActions')
          AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0
          AND TYPE_NAME(c.system_type_id) NOT IN (N'timestamp',N'rowversion')
          AND dc.object_id IS NULL
          AND c.name NOT IN (N'Id',N'FunctionId',N'Code',N'Name',N'UiKey',N'EndpointKey',N'SortOrder',N'IsActive',N'IsDeleted')
    )
        THROW 52128, 'dbo.FunctionActions has an extra required column without a default; seed safely cannot infer its value.', 1;

    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
        WHERE c.object_id=OBJECT_ID(N'dbo.FunctionActionPermissions')
          AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0
          AND TYPE_NAME(c.system_type_id) NOT IN (N'timestamp',N'rowversion')
          AND dc.object_id IS NULL
          AND c.name NOT IN (N'FunctionActionId',N'PermissionId')
    )
        THROW 52148, 'dbo.FunctionActionPermissions has an extra required column without a default; seed safely cannot infer its value.', 1;

    IF EXISTS (SELECT Id FROM dbo.FunctionActions GROUP BY Id HAVING COUNT(*)>1)
        THROW 52149, 'Duplicate FunctionActions.Id values prevent the required reference key.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id=OBJECT_ID(N'dbo.FunctionActions') AND i.is_unique=1
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=1
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND COL_NAME(ic.object_id,ic.column_id)=N'Id')
    )
        CREATE UNIQUE INDEX UX_FunctionActions_Id ON dbo.FunctionActions(Id);

    IF EXISTS (SELECT 1 FROM dbo.FunctionActions fa LEFT JOIN dbo.Functions f ON f.Id=fa.FunctionId WHERE f.Id IS NULL)
        THROW 52129, 'Cannot add FunctionActions->Functions FK because orphan rows exist.', 1;

    IF EXISTS (
        SELECT 1 FROM sys.foreign_key_columns fkc
        WHERE fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActions')
          AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'FunctionId'
          AND (fkc.referenced_object_id<>OBJECT_ID(N'dbo.Functions') OR COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id)<>N'Id')
    )
        THROW 52130, 'FunctionActions.FunctionId has a conflicting foreign key.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_key_columns fkc
        WHERE fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActions')
          AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'FunctionId'
          AND fkc.referenced_object_id=OBJECT_ID(N'dbo.Functions')
          AND COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id)=N'Id'
    )
        ALTER TABLE dbo.FunctionActions WITH CHECK ADD CONSTRAINT FK_FunctionActions_Function FOREIGN KEY(FunctionId) REFERENCES dbo.Functions(Id);

    IF EXISTS (
        SELECT 1 FROM dbo.FunctionActionPermissions fap
        LEFT JOIN dbo.FunctionActions fa ON fa.Id=fap.FunctionActionId
        LEFT JOIN dbo.Permissions p ON p.Id=fap.PermissionId
        WHERE fa.Id IS NULL OR p.Id IS NULL
    )
        THROW 52131, 'Cannot add FunctionActionPermissions foreign keys because orphan rows exist.', 1;

    IF EXISTS (
        SELECT 1 FROM sys.foreign_key_columns fkc
        WHERE fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActionPermissions')
          AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'FunctionActionId'
          AND (fkc.referenced_object_id<>OBJECT_ID(N'dbo.FunctionActions') OR COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id)<>N'Id')
    )
        THROW 52132, 'FunctionActionPermissions.FunctionActionId has a conflicting foreign key.', 1;

    IF EXISTS (
        SELECT 1 FROM sys.foreign_key_columns fkc
        WHERE fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActionPermissions')
          AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'PermissionId'
          AND (fkc.referenced_object_id<>OBJECT_ID(N'dbo.Permissions') OR COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id)<>N'Id')
    )
        THROW 52133, 'FunctionActionPermissions.PermissionId has a conflicting foreign key.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_key_columns fkc
        WHERE fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActionPermissions')
          AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'FunctionActionId'
          AND fkc.referenced_object_id=OBJECT_ID(N'dbo.FunctionActions')
          AND COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id)=N'Id'
    )
        ALTER TABLE dbo.FunctionActionPermissions WITH CHECK ADD CONSTRAINT FK_FunctionActionPermissions_Action FOREIGN KEY(FunctionActionId) REFERENCES dbo.FunctionActions(Id);

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_key_columns fkc
        WHERE fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActionPermissions')
          AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'PermissionId'
          AND fkc.referenced_object_id=OBJECT_ID(N'dbo.Permissions')
          AND COL_NAME(fkc.referenced_object_id,fkc.referenced_column_id)=N'Id'
    )
        ALTER TABLE dbo.FunctionActionPermissions WITH CHECK ADD CONSTRAINT FK_FunctionActionPermissions_Permission FOREIGN KEY(PermissionId) REFERENCES dbo.Permissions(Id);

    DECLARE @FkName sysname, @TrustSql nvarchar(1000);
    DECLARE fk_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT fk.name
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
        WHERE (fk.is_disabled=1 OR fk.is_not_trusted=1)
          AND (
              (fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActions') AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'FunctionId' AND fkc.referenced_object_id=OBJECT_ID(N'dbo.Functions'))
              OR (fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActionPermissions') AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'FunctionActionId' AND fkc.referenced_object_id=OBJECT_ID(N'dbo.FunctionActions'))
              OR (fkc.parent_object_id=OBJECT_ID(N'dbo.FunctionActionPermissions') AND COL_NAME(fkc.parent_object_id,fkc.parent_column_id)=N'PermissionId' AND fkc.referenced_object_id=OBJECT_ID(N'dbo.Permissions'))
          );
    OPEN fk_cursor;
    FETCH NEXT FROM fk_cursor INTO @FkName;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @TrustSql=N'ALTER TABLE '+CASE WHEN EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=@FkName AND parent_object_id=OBJECT_ID(N'dbo.FunctionActions')) THEN N'dbo.FunctionActions' ELSE N'dbo.FunctionActionPermissions' END
                    +N' WITH CHECK CHECK CONSTRAINT '+QUOTENAME(@FkName)+N';';
        EXEC sys.sp_executesql @TrustSql;
        FETCH NEXT FROM fk_cursor INTO @FkName;
    END;
    CLOSE fk_cursor;
    DEALLOCATE fk_cursor;

    -- Empty/whitespace keys have no authorization meaning. Normalize them so filtered unique indexes
    -- can coexist with multiple legacy actions whose keys have not been assigned yet.
    UPDATE dbo.FunctionActions SET UiKey=NULL
    WHERE UiKey IS NOT NULL AND LTRIM(RTRIM(UiKey))=N'';
    UPDATE dbo.FunctionActions SET EndpointKey=NULL
    WHERE EndpointKey IS NOT NULL AND LTRIM(RTRIM(EndpointKey))=N'';

    IF EXISTS (SELECT FunctionId,Code FROM dbo.FunctionActions WHERE IsDeleted=0 GROUP BY FunctionId,Code HAVING COUNT(*)>1)
        THROW 52134, 'Duplicate nondeleted FunctionActions FunctionId+Code values prevent a unique index.', 1;
    IF EXISTS (SELECT UiKey FROM dbo.FunctionActions WHERE IsDeleted=0 AND NULLIF(LTRIM(RTRIM(UiKey)),N'') IS NOT NULL GROUP BY UiKey HAVING COUNT(*)>1)
        THROW 52135, 'Duplicate nondeleted FunctionActions UiKey values prevent a unique index.', 1;
    IF EXISTS (SELECT EndpointKey FROM dbo.FunctionActions WHERE IsDeleted=0 AND NULLIF(LTRIM(RTRIM(EndpointKey)),N'') IS NOT NULL GROUP BY EndpointKey HAVING COUNT(*)>1)
        THROW 52136, 'Duplicate nondeleted FunctionActions EndpointKey values prevent a unique index.', 1;
    IF EXISTS (SELECT FunctionActionId,PermissionId FROM dbo.FunctionActionPermissions GROUP BY FunctionActionId,PermissionId HAVING COUNT(*)>1)
        THROW 52137, 'Duplicate FunctionActionPermissions rows prevent a unique index.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id=OBJECT_ID(N'dbo.FunctionActions') AND i.is_unique=1 AND i.has_filter=1 AND i.filter_definition LIKE N'%IsDeleted%0%'
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=2
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND COL_NAME(ic.object_id,ic.column_id)=N'FunctionId')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=2 AND COL_NAME(ic.object_id,ic.column_id)=N'Code')
    )
        CREATE UNIQUE INDEX UX_FunctionActions_Function_Code ON dbo.FunctionActions(FunctionId,Code) WHERE IsDeleted=0;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        WHERE i.object_id=OBJECT_ID(N'dbo.FunctionActions') AND i.is_unique=1 AND i.has_filter=1 AND i.filter_definition LIKE N'%IsDeleted%0%'
          AND (SELECT COUNT(*) FROM sys.index_columns kic WHERE kic.object_id=i.object_id AND kic.index_id=i.index_id AND kic.key_ordinal>0)=1
          AND COL_NAME(ic.object_id,ic.column_id)=N'UiKey' AND ic.key_ordinal=1
    )
        CREATE UNIQUE INDEX UX_FunctionActions_UiKey ON dbo.FunctionActions(UiKey) WHERE IsDeleted=0 AND UiKey IS NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        WHERE i.object_id=OBJECT_ID(N'dbo.FunctionActions') AND i.is_unique=1 AND i.has_filter=1 AND i.filter_definition LIKE N'%IsDeleted%0%'
          AND (SELECT COUNT(*) FROM sys.index_columns kic WHERE kic.object_id=i.object_id AND kic.index_id=i.index_id AND kic.key_ordinal>0)=1
          AND COL_NAME(ic.object_id,ic.column_id)=N'EndpointKey' AND ic.key_ordinal=1
    )
        CREATE UNIQUE INDEX UX_FunctionActions_EndpointKey ON dbo.FunctionActions(EndpointKey) WHERE IsDeleted=0 AND EndpointKey IS NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id=OBJECT_ID(N'dbo.FunctionActionPermissions') AND i.is_unique=1
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=2
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND COL_NAME(ic.object_id,ic.column_id)=N'FunctionActionId')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=2 AND COL_NAME(ic.object_id,ic.column_id)=N'PermissionId')
    )
        CREATE UNIQUE INDEX UX_FunctionActionPermissions_Action_Permission ON dbo.FunctionActionPermissions(FunctionActionId,PermissionId);

    DECLARE @Mappings TABLE (
        ModuleCode varchar(100), FunctionCode varchar(100), ActionCode varchar(100), ActionName nvarchar(200),
        UiKey varchar(200), EndpointKey varchar(200), PermissionCode varchar(200), SortOrder int
    );
    INSERT @Mappings VALUES
    ('AUTH','USERS','VIEW',N'Xem người dùng','users.view','Users.Index','AUTH.USERS.VIEW',10),
    ('AUTH','USERS','CREATE',N'Tạo người dùng','users.create','Users.Create','AUTH.USERS.CREATE',20),
    ('AUTH','ROLES','VIEW',N'Xem vai trò','roles.view','Roles.Index','AUTH.ROLES.VIEW',10),
    ('CRM','CUSTOMERS','VIEW',N'Xem khách hàng','customers.view','Customers.Index','CRM.CUSTOMERS.VIEW',10),
    ('SYSTEM','MENUS','MANAGE',N'Quản lý menu','menus.manage','Menus.Index','SYSTEM.MENUS.MANAGE',10);

    IF EXISTS (
        SELECT 1 FROM @Mappings m
        LEFT JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
        LEFT JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
        LEFT JOIN dbo.Permissions p ON p.Code=m.PermissionCode AND p.IsDeleted=0 AND p.IsActive=1
        WHERE md.Id IS NULL OR f.Id IS NULL OR p.Id IS NULL
    )
        THROW 52138, 'One or more expected active Modules, Functions, or Permissions are missing.', 1;

    IF EXISTS (
        SELECT 1 FROM @Mappings m
        JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0
        JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0
        JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=m.ActionCode AND fa.IsDeleted=0
        WHERE NULLIF(LTRIM(RTRIM(fa.UiKey)),N'') IS NOT NULL AND fa.UiKey<>m.UiKey
    )
        THROW 52139, 'Existing FunctionAction has a conflicting nonblank UiKey.', 1;

    IF EXISTS (
        SELECT 1 FROM @Mappings m
        JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0
        JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0
        JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=m.ActionCode AND fa.IsDeleted=0
        WHERE NULLIF(LTRIM(RTRIM(fa.EndpointKey)),N'') IS NOT NULL AND fa.EndpointKey<>m.EndpointKey
    )
        THROW 52140, 'Existing FunctionAction has a conflicting nonblank EndpointKey.', 1;

    IF EXISTS (
        SELECT 1 FROM @Mappings m
        JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0
        JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0
        JOIN dbo.FunctionActions fa ON fa.IsDeleted=0 AND fa.UiKey=m.UiKey
        WHERE fa.FunctionId<>f.Id OR fa.Code<>m.ActionCode
    )
        THROW 52141, 'Expected UiKey is already assigned to a different FunctionAction.', 1;

    IF EXISTS (
        SELECT 1 FROM @Mappings m
        JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0
        JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0
        JOIN dbo.FunctionActions fa ON fa.IsDeleted=0 AND fa.EndpointKey=m.EndpointKey
        WHERE fa.FunctionId<>f.Id OR fa.Code<>m.ActionCode
    )
        THROW 52142, 'Expected EndpointKey is already assigned to a different FunctionAction.', 1;

    UPDATE fa
    SET UiKey=CASE WHEN NULLIF(LTRIM(RTRIM(fa.UiKey)),N'') IS NULL THEN m.UiKey ELSE fa.UiKey END,
        EndpointKey=CASE WHEN NULLIF(LTRIM(RTRIM(fa.EndpointKey)),N'') IS NULL THEN m.EndpointKey ELSE fa.EndpointKey END
    FROM dbo.FunctionActions fa
    JOIN dbo.Functions f ON f.Id=fa.FunctionId
    JOIN dbo.Modules md ON md.Id=f.ModuleId
    JOIN @Mappings m ON m.ModuleCode=md.Code AND m.FunctionCode=f.Code AND m.ActionCode=fa.Code
    WHERE fa.IsDeleted=0 AND (NULLIF(LTRIM(RTRIM(fa.UiKey)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(fa.EndpointKey)),N'') IS NULL);

    INSERT dbo.FunctionActions(Id,FunctionId,Code,Name,UiKey,EndpointKey,SortOrder,IsActive,IsDeleted)
    SELECT NEWID(),f.Id,m.ActionCode,m.ActionName,m.UiKey,m.EndpointKey,m.SortOrder,1,0
    FROM @Mappings m
    JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
    JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
    WHERE NOT EXISTS (SELECT 1 FROM dbo.FunctionActions fa WHERE fa.FunctionId=f.Id AND fa.Code=m.ActionCode AND fa.IsDeleted=0);

    IF EXISTS (
        SELECT 1 FROM @Mappings m
        JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0
        JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0
        JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=m.ActionCode AND fa.IsDeleted=0
        WHERE fa.IsActive=0
    )
        THROW 52143, 'An expected FunctionAction exists but is inactive.', 1;

    INSERT dbo.FunctionActionPermissions(FunctionActionId,PermissionId)
    SELECT fa.Id,p.Id
    FROM @Mappings m
    JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
    JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
    JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=m.ActionCode AND fa.IsDeleted=0 AND fa.IsActive=1
    JOIN dbo.Permissions p ON p.Code=m.PermissionCode AND p.IsDeleted=0 AND p.IsActive=1
    WHERE NOT EXISTS (SELECT 1 FROM dbo.FunctionActionPermissions x WHERE x.FunctionActionId=fa.Id AND x.PermissionId=p.Id);

    IF EXISTS (
        SELECT 1 FROM @Mappings m
        JOIN dbo.Modules md ON md.Code=m.ModuleCode AND md.IsDeleted=0 AND md.IsActive=1
        JOIN dbo.Functions f ON f.ModuleId=md.Id AND f.Code=m.FunctionCode AND f.IsDeleted=0 AND f.IsActive=1
        LEFT JOIN dbo.FunctionActions fa ON fa.FunctionId=f.Id AND fa.Code=m.ActionCode AND fa.IsDeleted=0 AND fa.IsActive=1
        LEFT JOIN dbo.Permissions p ON p.Code=m.PermissionCode AND p.IsDeleted=0 AND p.IsActive=1
        LEFT JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id AND fap.PermissionId=p.Id
        GROUP BY m.ModuleCode,m.FunctionCode,m.ActionCode,m.PermissionCode
        HAVING COUNT(DISTINCT fa.Id)<>1 OR COUNT(DISTINCT fap.PermissionId)<>1
    )
        THROW 52144, 'FunctionAction seed postflight failed: expected action or permission mapping is missing/duplicated.', 1;

    IF EXISTS (SELECT UiKey FROM dbo.FunctionActions WHERE IsDeleted=0 AND IsActive=1 AND NULLIF(LTRIM(RTRIM(UiKey)),N'') IS NOT NULL GROUP BY UiKey HAVING COUNT(*)>1)
        THROW 52145, 'FunctionAction postflight failed: duplicate active UiKey.', 1;
    IF EXISTS (SELECT EndpointKey FROM dbo.FunctionActions WHERE IsDeleted=0 AND IsActive=1 AND NULLIF(LTRIM(RTRIM(EndpointKey)),N'') IS NOT NULL GROUP BY EndpointKey HAVING COUNT(*)>1)
        THROW 52146, 'FunctionAction postflight failed: duplicate active EndpointKey.', 1;
    IF EXISTS (
        SELECT 1 FROM dbo.FunctionActionPermissions fap
        JOIN dbo.FunctionActions fa ON fa.Id=fap.FunctionActionId
        JOIN dbo.Functions f ON f.Id=fa.FunctionId
        JOIN dbo.Modules md ON md.Id=f.ModuleId
        JOIN dbo.Permissions p ON p.Id=fap.PermissionId
        WHERE fa.IsDeleted<>0 OR fa.IsActive<>1 OR f.IsDeleted<>0 OR f.IsActive<>1 OR md.IsDeleted<>0 OR md.IsActive<>1 OR p.IsDeleted<>0 OR p.IsActive<>1
    )
        THROW 52147, 'FunctionAction postflight failed: a mapping references inactive/deleted authorization metadata.', 1;

    COMMIT TRANSACTION;
    PRINT '[PASS] 053 core dynamic action authorization migration completed.';
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
