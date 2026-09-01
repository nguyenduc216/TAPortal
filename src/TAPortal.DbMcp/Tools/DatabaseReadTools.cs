using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using TAPortal.DbMcp.Data;

namespace TAPortal.DbMcp.Tools;

[McpServerToolType]
public sealed class DatabaseReadTools
{
    private readonly SqlConnectionFactory _factory;

    public DatabaseReadTools(SqlConnectionFactory factory)
    {
        _factory = factory;
    }

    [McpServerTool, Description("Tests the configured SQL Server connection and returns basic database/server information. Read-only.")]
    public async Task<object> DbPing(CancellationToken cancellationToken)
    {
        await using var connection = _factory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = _factory.CommandTimeoutSeconds;
        command.CommandText = "SELECT DB_NAME() AS DatabaseName, @@SERVERNAME AS ServerName, @@VERSION AS Version";

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new
        {
            databaseName = reader.GetString(0),
            serverName = reader.IsDBNull(1) ? null : reader.GetString(1),
            version = reader.IsDBNull(2) ? null : reader.GetString(2),
            utc = DateTimeOffset.UtcNow
        };
    }

    [McpServerTool, Description("Lists user-defined schemas in the configured SQL Server database. Read-only.")]
    public async Task<IReadOnlyList<string>> DbListSchemas(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name
            FROM sys.schemas
            WHERE principal_id = 1
               OR name NOT IN ('db_owner','db_accessadmin','db_securityadmin','db_ddladmin','db_backupoperator','db_datareader','db_datawriter','db_denydatareader','db_denydatawriter')
            ORDER BY name;
            """;

        await using var connection = _factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _factory.CommandTimeoutSeconds
        };

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    [McpServerTool, Description("Lists tables in the configured SQL Server database. Optional schema filter. Read-only.")]
    public async Task<IReadOnlyList<object>> DbListTables(
        [Description("Optional schema name, for example auth, sys, org. Leave empty to return all tables.")] string? schema,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.name AS SchemaName, t.name AS TableName
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE (@Schema IS NULL OR s.name = @Schema)
            ORDER BY s.name, t.name;
            """;

        await using var connection = _factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _factory.CommandTimeoutSeconds
        };
        command.Parameters.Add(new SqlParameter("@Schema", SqlDbType.NVarChar, 128)
        {
            Value = string.IsNullOrWhiteSpace(schema) ? DBNull.Value : schema.Trim()
        });

        var result = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new { schema = reader.GetString(0), table = reader.GetString(1) });
        }

        return result;
    }

    [McpServerTool, Description("Returns columns, primary-key membership, identity and nullability for one SQL Server table. Read-only.")]
    public async Task<IReadOnlyList<object>> DbGetTableSchema(
        [Description("SQL schema name, for example auth.")] string schema,
        [Description("Table name, for example Users.")] string table,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                c.column_id,
                c.name AS ColumnName,
                ty.name AS DataType,
                c.max_length,
                c.precision,
                c.scale,
                c.is_nullable,
                c.is_identity,
                CASE WHEN pk.column_id IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS IsPrimaryKey
            FROM sys.columns c
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
            WHERE s.name = @Schema AND t.name = @Table
            ORDER BY c.column_id;
            """;

        await using var connection = _factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _factory.CommandTimeoutSeconds
        };
        command.Parameters.Add("@Schema", SqlDbType.NVarChar, 128).Value = schema;
        command.Parameters.Add("@Table", SqlDbType.NVarChar, 128).Value = table;

        var result = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new
            {
                ordinal = reader.GetInt32(0),
                column = reader.GetString(1),
                dataType = reader.GetString(2),
                maxLength = reader.GetInt16(3),
                precision = reader.GetByte(4),
                scale = reader.GetByte(5),
                nullable = reader.GetBoolean(6),
                identity = reader.GetBoolean(7),
                primaryKey = reader.GetBoolean(8)
            });
        }

        return result;
    }

    [McpServerTool, Description("Returns foreign keys for the database, optionally filtered to one table. Read-only.")]
    public async Task<IReadOnlyList<object>> DbGetForeignKeys(
        [Description("Optional schema name.")] string? schema,
        [Description("Optional table name.")] string? table,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                fk.name AS ForeignKeyName,
                ps.name AS ParentSchema,
                pt.name AS ParentTable,
                pc.name AS ParentColumn,
                rs.name AS ReferencedSchema,
                rt.name AS ReferencedTable,
                rc.name AS ReferencedColumn,
                fk.delete_referential_action_desc,
                fk.update_referential_action_desc
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            INNER JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
            INNER JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
            INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            INNER JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            INNER JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
            WHERE (@Schema IS NULL OR ps.name = @Schema)
              AND (@Table IS NULL OR pt.name = @Table)
            ORDER BY ps.name, pt.name, fk.name, fkc.constraint_column_id;
            """;

        await using var connection = _factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _factory.CommandTimeoutSeconds
        };
        command.Parameters.Add("@Schema", SqlDbType.NVarChar, 128).Value = string.IsNullOrWhiteSpace(schema) ? DBNull.Value : schema.Trim();
        command.Parameters.Add("@Table", SqlDbType.NVarChar, 128).Value = string.IsNullOrWhiteSpace(table) ? DBNull.Value : table.Trim();

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
}
