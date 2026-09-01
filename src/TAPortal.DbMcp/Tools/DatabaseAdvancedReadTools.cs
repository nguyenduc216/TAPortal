using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using TAPortal.DbMcp.Data;
using TAPortal.DbMcp.Security;

namespace TAPortal.DbMcp.Tools;

[McpServerToolType]
public sealed class DatabaseAdvancedReadTools
{
    private readonly SqlConnectionFactory _factory;

    public DatabaseAdvancedReadTools(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    [McpServerTool, Description("Lists indexes for user tables in the configured SQL Server database. Optional schema/table filters. Read-only.")]
    public async Task<IReadOnlyList<object>> DbGetIndexes(string? schema, string? table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.name AS SchemaName,
                   t.name AS TableName,
                   i.name AS IndexName,
                   i.type_desc AS IndexType,
                   i.is_unique AS IsUnique,
                   i.is_primary_key AS IsPrimaryKey,
                   i.is_disabled AS IsDisabled,
                   STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
            FROM sys.indexes i
            INNER JOIN sys.tables t ON t.object_id = i.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name IS NOT NULL
              AND (@Schema IS NULL OR s.name = @Schema)
              AND (@Table IS NULL OR t.name = @Table)
            GROUP BY s.name, t.name, i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_disabled
            ORDER BY s.name, t.name, i.name;
            """;

        return await ExecuteObjects(sql, command =>
        {
            command.Parameters.Add("@Schema", SqlDbType.NVarChar, 128).Value = DbValue(schema);
            command.Parameters.Add("@Table", SqlDbType.NVarChar, 128).Value = DbValue(table);
        }, cancellationToken);
    }

    [McpServerTool, Description("Lists CHECK, DEFAULT and UNIQUE constraints for user tables. Optional schema/table filters. Read-only.")]
    public async Task<IReadOnlyList<object>> DbGetConstraints(string? schema, string? table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.name AS SchemaName,
                   t.name AS TableName,
                   o.name AS ConstraintName,
                   o.type_desc AS ConstraintType,
                   CASE
                       WHEN o.type = 'C' THEN cc.definition
                       WHEN o.type = 'D' THEN dc.definition
                       ELSE NULL
                   END AS Definition
            FROM sys.objects o
            INNER JOIN sys.tables t ON t.object_id = o.parent_object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            LEFT JOIN sys.check_constraints cc ON cc.object_id = o.object_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = o.object_id
            WHERE o.type IN ('C','D','UQ','PK')
              AND (@Schema IS NULL OR s.name = @Schema)
              AND (@Table IS NULL OR t.name = @Table)
            ORDER BY s.name, t.name, o.type_desc, o.name;
            """;

        return await ExecuteObjects(sql, command =>
        {
            command.Parameters.Add("@Schema", SqlDbType.NVarChar, 128).Value = DbValue(schema);
            command.Parameters.Add("@Table", SqlDbType.NVarChar, 128).Value = DbValue(table);
        }, cancellationToken);
    }

    [McpServerTool, Description("Lists views in the configured SQL Server database. Optional schema filter. Read-only.")]
    public async Task<IReadOnlyList<object>> DbGetViews(string? schema, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.name AS SchemaName, v.name AS ViewName, v.create_date AS CreatedAt, v.modify_date AS ModifiedAt
            FROM sys.views v
            INNER JOIN sys.schemas s ON s.schema_id = v.schema_id
            WHERE (@Schema IS NULL OR s.name = @Schema)
            ORDER BY s.name, v.name;
            """;

        return await ExecuteObjects(sql, command =>
        {
            command.Parameters.Add("@Schema", SqlDbType.NVarChar, 128).Value = DbValue(schema);
        }, cancellationToken);
    }

    [McpServerTool, Description("Lists stored procedures in the configured SQL Server database. Optional schema filter. Does not execute them.")]
    public async Task<IReadOnlyList<object>> DbGetStoredProcedures(string? schema, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.name AS SchemaName, p.name AS ProcedureName, p.create_date AS CreatedAt, p.modify_date AS ModifiedAt
            FROM sys.procedures p
            INNER JOIN sys.schemas s ON s.schema_id = p.schema_id
            WHERE p.is_ms_shipped = 0
              AND (@Schema IS NULL OR s.name = @Schema)
            ORDER BY s.name, p.name;
            """;

        return await ExecuteObjects(sql, command =>
        {
            command.Parameters.Add("@Schema", SqlDbType.NVarChar, 128).Value = DbValue(schema);
        }, cancellationToken);
    }

    [McpServerTool, Description("Returns a compact schema snapshot containing user tables, columns, primary keys and foreign keys. Read-only.")]
    public async Task<object> DbGetSchemaSnapshot(CancellationToken cancellationToken)
    {
        await using var connection = _factory.Create();
        await connection.OpenAsync(cancellationToken);

        var tables = new List<object>();
        const string tableSql = """
            SELECT s.name AS SchemaName,
                   t.name AS TableName,
                   c.column_id AS Ordinal,
                   c.name AS ColumnName,
                   ty.name AS DataType,
                   c.max_length AS MaxLength,
                   c.precision AS Precision,
                   c.scale AS Scale,
                   c.is_nullable AS IsNullable,
                   c.is_identity AS IsIdentity,
                   CASE WHEN pk.column_id IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS IsPrimaryKey
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, c.column_id;
            """;

        await using (var command = new SqlCommand(tableSql, connection) { CommandTimeout = _factory.CommandTimeoutSeconds })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            string? currentKey = null;
            string? currentSchema = null;
            string? currentTable = null;
            var columns = new List<object>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                var key = schema + "." + table;

                if (currentKey is not null && !string.Equals(currentKey, key, StringComparison.Ordinal))
                {
                    tables.Add(new { schema = currentSchema, table = currentTable, columns = columns.ToArray() });
                    columns = new List<object>();
                }

                currentKey = key;
                currentSchema = schema;
                currentTable = table;
                columns.Add(new
                {
                    ordinal = reader.GetInt32(2),
                    name = reader.GetString(3),
                    dataType = reader.GetString(4),
                    maxLength = reader.GetInt16(5),
                    precision = reader.GetByte(6),
                    scale = reader.GetByte(7),
                    isNullable = reader.GetBoolean(8),
                    isIdentity = reader.GetBoolean(9),
                    isPrimaryKey = reader.GetBoolean(10)
                });
            }

            if (currentKey is not null)
                tables.Add(new { schema = currentSchema, table = currentTable, columns = columns.ToArray() });
        }

        var foreignKeys = await GetForeignKeys(connection, cancellationToken);

        return new
        {
            database = connection.Database,
            capturedAtUtc = DateTimeOffset.UtcNow,
            tables,
            foreignKeys
        };
    }

    [McpServerTool, Description("Executes one guarded read-only SELECT/CTE query against the configured database. DDL, DML, stored procedure execution and multi-statement SQL are blocked. Result rows are capped.")]
    public async Task<object> DbQueryReadOnly(
        [Description("Single SQL SELECT statement or CTE ending in SELECT.")] string sql,
        CancellationToken cancellationToken)
    {
        var safeSql = SqlReadOnlyGuard.ValidateAndNormalize(sql);

        await using var connection = _factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(safeSql, connection)
        {
            CommandTimeout = _factory.CommandTimeoutSeconds
        };

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);

        while (rows.Count < _factory.MaxRows && await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                row[reader.GetName(i)] = value;
            }
            rows.Add(row);
        }

        return new
        {
            database = connection.Database,
            rowCount = rows.Count,
            maxRows = _factory.MaxRows,
            truncated = rows.Count >= _factory.MaxRows,
            rows
        };
    }

    private async Task<IReadOnlyList<object>> ExecuteObjects(string sql, Action<SqlCommand> configure, CancellationToken cancellationToken)
    {
        await using var connection = _factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _factory.CommandTimeoutSeconds };
        configure(command);

        var result = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            result.Add(row);
        }
        return result;
    }

    private async Task<IReadOnlyList<object>> GetForeignKeys(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT fk.name AS ForeignKeyName,
                   ps.name AS ParentSchema,
                   pt.name AS ParentTable,
                   pc.name AS ParentColumn,
                   rs.name AS ReferencedSchema,
                   rt.name AS ReferencedTable,
                   rc.name AS ReferencedColumn,
                   fk.delete_referential_action_desc AS OnDelete,
                   fk.update_referential_action_desc AS OnUpdate
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            INNER JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
            INNER JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
            INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            INNER JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            INNER JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
            ORDER BY ps.name, pt.name, fk.name, fkc.constraint_column_id;
            """;

        await using var command = new SqlCommand(sql, connection) { CommandTimeout = _factory.CommandTimeoutSeconds };
        var result = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new
            {
                foreignKey = reader.GetString(0),
                parentSchema = reader.GetString(1),
                parentTable = reader.GetString(2),
                parentColumn = reader.GetString(3),
                referencedSchema = reader.GetString(4),
                referencedTable = reader.GetString(5),
                referencedColumn = reader.GetString(6),
                onDelete = reader.GetString(7),
                onUpdate = reader.GetString(8)
            });
        }
        return result;
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
