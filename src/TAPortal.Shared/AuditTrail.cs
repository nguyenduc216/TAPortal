using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TAPortal.Shared;

public enum AuditWriteStatus
{
    Written,
    // OBJECT_ID returned NULL: the table is missing OR invisible to this principal (no permission at all).
    TableUnavailable,
    // The table is visible but lacks an expected column.
    SchemaMismatch,
    // The INSERT itself failed (permission denied, constraint, truncation, ...).
    InsertFailed
}

public sealed record AuditWriteResult(AuditWriteStatus Status, int? SqlErrorNumber = null)
{
    public bool Written => Status == AuditWriteStatus.Written;
}

public sealed class AuditUnavailableException(AuditWriteResult result)
    : InvalidOperationException("Durable audit could not be written; the change was rolled back.")
{
    public AuditWriteResult Result { get; } = result;
}

// Security:Audit:RequireDurableAudit=true makes every audited administrative change fail closed.
public static class AuditPolicy
{
    public static bool RequireDurable(IConfiguration configuration) =>
        bool.TryParse(configuration["Security:Audit:RequireDurableAudit"], out var value) && value;

    // Rollback when audit is mandatory and was not written, or when a failed audit statement doomed the transaction.
    public static bool MustRollback(AuditWriteResult result, bool requireDurable, bool transactionDoomed) =>
        transactionDoomed || (requireDurable && !result.Written);
}

public static class AuditSql
{
    // Distinguishes a missing/invisible table (-1) and a column mismatch (-2) from a written row (1).
    public const string Insert = """
IF OBJECT_ID(N'dbo.AuditLogs',N'U') IS NULL SELECT CAST(-1 AS int);
ELSE IF COL_LENGTH(N'dbo.AuditLogs',N'UserId') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'Action') IS NULL
 OR COL_LENGTH(N'dbo.AuditLogs',N'EntityName') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'EntityId') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'OldValues') IS NULL
 OR COL_LENGTH(N'dbo.AuditLogs',N'NewValues') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'IpAddress') IS NULL SELECT CAST(-2 AS int);
ELSE
BEGIN
 EXEC sp_executesql N'INSERT dbo.AuditLogs(UserId,Action,EntityName,EntityId,OldValues,NewValues,IpAddress) VALUES(@actor,@action,@entity,@target,@old,@new,@ip)',
  N'@actor uniqueidentifier,@action nvarchar(100),@entity nvarchar(200),@target nvarchar(200),@old nvarchar(max),@new nvarchar(max),@ip varchar(64)',
  @actor=@actor,@action=@action,@entity=@entity,@target=@target,@old=@old,@new=@new,@ip=@ip;
 SELECT CAST(1 AS int);
END
""";

    public static AuditWriteResult Classify(int code) => code switch
    {
        1 => new(AuditWriteStatus.Written),
        -1 => new(AuditWriteStatus.TableUnavailable),
        _ => new(AuditWriteStatus.SchemaMismatch)
    };

    public static async Task<AuditWriteResult> WriteAsync(SqlConnection cn, SqlTransaction tx, Guid? actor, string action, string entity, string target, string oldValues, string newValues, string? ip)
    {
        try
        {
            await using var cmd = new SqlCommand(Insert, cn, tx);
            cmd.Parameters.AddWithValue("@actor", (object?)actor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@action", action);
            cmd.Parameters.AddWithValue("@entity", entity);
            cmd.Parameters.AddWithValue("@target", target);
            cmd.Parameters.AddWithValue("@old", oldValues);
            cmd.Parameters.AddWithValue("@new", newValues);
            cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
            return Classify(Convert.ToInt32(await cmd.ExecuteScalarAsync()));
        }
        catch (SqlException ex)
        {
            return new(AuditWriteStatus.InsertFailed, ex.Number);
        }
    }

    // XACT_STATE() = -1 means a failed statement doomed the transaction; it can only be rolled back.
    public static async Task<bool> IsDoomedAsync(SqlConnection cn, SqlTransaction tx)
    {
        try
        {
            await using var cmd = new SqlCommand("SELECT XACT_STATE();", cn, tx);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == -1;
        }
        catch (SqlException)
        {
            return true;
        }
    }

    // Writes the audit row inside the caller's transaction and enforces the fail-closed rule before any commit.
    public static async Task<AuditWriteResult> WriteOrThrowAsync(SqlConnection cn, SqlTransaction tx, bool requireDurable, Guid? actor, string action, string entity, string target, string oldValues, string newValues, string? ip)
    {
        var result = await WriteAsync(cn, tx, actor, action, entity, target, oldValues, newValues, ip);
        var doomed = result.Status == AuditWriteStatus.InsertFailed && await IsDoomedAsync(cn, tx);
        if (AuditPolicy.MustRollback(result, requireDurable, doomed))
            throw new AuditUnavailableException(result);
        return result;
    }

    public static string Json(params (string Key, object? Value)[] fields) =>
        System.Text.Json.JsonSerializer.Serialize(fields.ToDictionary(f => f.Key, f => f.Value));
}
