using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;

namespace TAPortal.Web.Services;

public sealed record ImpersonationTarget(Guid Id, string Username, string DisplayName);

public sealed record ImpersonationSessionRow(Guid Id, Guid ActorId, string ActorName, string ActorUsername, Guid TargetId, string TargetName, string TargetUsername,
    string Reason, DateTime StartedAt, DateTime ExpiresAt, DateTime? EndedAt, string? EndReason, string? IpAddress, int Requests, int Changes);

public sealed record ImpersonationRequestRow(DateTime At, string Method, string Path, int StatusCode);

public sealed record ImpersonationChangeRow(DateTime At, string Action, string? Entity, string? EntityId);

public sealed record ImpersonationSessionDetail(ImpersonationSessionRow Session, IReadOnlyList<ImpersonationChangeRow> Changes, IReadOnlyList<ImpersonationRequestRow> Requests);

// "Truy cập hộ" history: dbo.ImpersonationSessions (one row per session) and dbo.ImpersonationActions (its requests);
// the audited changes of a session are the dbo.AuditLogs rows carrying its ImpersonationSessionId (migration 042).
public interface IImpersonationStore
{
    Task<ImpersonationTarget?> FindTargetAsync(Guid userId);
    Task<Guid> StartAsync(Guid actorId, Guid targetId, string reason, DateTime expiresAtUtc, string? ip, string? userAgent);
    Task EndAsync(Guid sessionId, string endReason);
    Task<bool> IsOpenAsync(Guid sessionId);
    Task LogRequestAsync(Guid sessionId, string method, string path, int statusCode);
    Task<IReadOnlyList<ImpersonationSessionRow>> ListAsync(int take = 300);
    Task<ImpersonationSessionDetail?> GetAsync(Guid sessionId);
}

public sealed class SqlImpersonationStore(IConfiguration configuration, AdminAuditOptions audit) : IImpersonationStore
{
    public const int PathMax = 400;

    private const string Target = "SELECT Id,Username,DisplayName FROM dbo.Users WHERE Id=@id AND IsDeleted=0;";
    private const string Start = """
INSERT dbo.ImpersonationSessions(Id,ActorUserId,TargetUserId,Reason,ExpiresAt,IpAddress,UserAgent) VALUES(@id,@actor,@target,@reason,@expires,@ip,@ua);
""";
    private const string End = "UPDATE dbo.ImpersonationSessions SET EndedAt=SYSUTCDATETIME(),EndReason=@r OUTPUT inserted.ActorUserId,inserted.TargetUserId WHERE Id=@id AND EndedAt IS NULL;";
    private const string Open = "SELECT COUNT(1) FROM dbo.ImpersonationSessions WHERE Id=@id AND EndedAt IS NULL AND ExpiresAt>SYSUTCDATETIME();";
    private const string Log = "INSERT dbo.ImpersonationActions(SessionId,Method,Path,StatusCode) VALUES(@id,@m,@p,@s);";
    private const string SessionColumns = """
s.Id,s.ActorUserId,au.DisplayName,au.Username,s.TargetUserId,tu.DisplayName,tu.Username,s.Reason,s.StartedAt,s.ExpiresAt,s.EndedAt,s.EndReason,s.IpAddress,
 (SELECT COUNT(1) FROM dbo.ImpersonationActions a WHERE a.SessionId=s.Id),
 (SELECT COUNT(1) FROM dbo.AuditLogs l WHERE l.ImpersonationSessionId=s.Id)
FROM dbo.ImpersonationSessions s JOIN dbo.Users au ON au.Id=s.ActorUserId JOIN dbo.Users tu ON tu.Id=s.TargetUserId
""";
    private const string List = "SELECT TOP (@take) " + SessionColumns + " ORDER BY s.StartedAt DESC;";
    private const string Detail = "SELECT " + SessionColumns + """
 WHERE s.Id=@id;
SELECT CreatedAt,Action,EntityName,EntityId FROM dbo.AuditLogs WHERE ImpersonationSessionId=@id ORDER BY CreatedAt;
SELECT CreatedAt,Method,Path,StatusCode FROM dbo.ImpersonationActions WHERE SessionId=@id ORDER BY CreatedAt,Id;
""";

    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<ImpersonationTarget?> FindTargetAsync(Guid userId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(Target, cn);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = userId;
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetGuid(0), r.GetString(1), r.GetString(2)) : null;
    }

    // The session row and its IMPERSONATION_START audit row commit together (fail closed when durable audit is required).
    public async Task<Guid> StartAsync(Guid actorId, Guid targetId, string reason, DateTime expiresAtUtc, string? ip, string? userAgent)
    {
        var id = Guid.NewGuid();
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        await using (var cmd = new SqlCommand(Start, cn, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            cmd.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actorId;
            cmd.Parameters.Add("@target", SqlDbType.UniqueIdentifier).Value = targetId;
            cmd.Parameters.Add("@reason", SqlDbType.NVarChar, 500).Value = reason;
            cmd.Parameters.Add("@expires", SqlDbType.DateTime2).Value = expiresAtUtc;
            cmd.Parameters.Add("@ip", SqlDbType.VarChar, 64).Value = (object?)Trim(ip, 64) ?? DBNull.Value;
            cmd.Parameters.Add("@ua", SqlDbType.NVarChar, 300).Value = (object?)Trim(userAgent, 300) ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }
        var previous = ImpersonationAmbient.Current;
        ImpersonationAmbient.Current = new(actorId, id);   // the start row itself already names both people
        try
        {
            await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, targetId, "IMPERSONATION_START", "dbo.ImpersonationSessions", id.ToString(),
                "{}", AuditSql.Json(("actorUserId", actorId), ("targetUserId", targetId), ("reason", reason), ("expiresAt", expiresAtUtc)), Trim(ip, 64));
        }
        finally { ImpersonationAmbient.Current = previous; }
        await tx.CommitAsync();
        return id;
    }

    public async Task EndAsync(Guid sessionId, string endReason)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        Guid? actor = null, target = null;
        await using (var cmd = new SqlCommand(End, cn, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = sessionId;
            cmd.Parameters.Add("@r", SqlDbType.VarChar, 20).Value = endReason;
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) (actor, target) = (r.GetGuid(0), r.GetGuid(1));
        }
        if (actor is Guid a)
        {
            var previous = ImpersonationAmbient.Current;
            ImpersonationAmbient.Current = new(a, sessionId);
            try
            {
                // Ending is recorded even when durable audit is not available; it never keeps a session open.
                await AuditSql.WriteAsync(cn, tx, target, "IMPERSONATION_END", "dbo.ImpersonationSessions", sessionId.ToString(), "{}", AuditSql.Json(("reason", endReason)), null);
            }
            finally { ImpersonationAmbient.Current = previous; }
        }
        await tx.CommitAsync();
    }

    public async Task<bool> IsOpenAsync(Guid sessionId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(Open, cn);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = sessionId;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task LogRequestAsync(Guid sessionId, string method, string path, int statusCode)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(Log, cn);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = sessionId;
        cmd.Parameters.Add("@m", SqlDbType.VarChar, 10).Value = Trim(method, 10) ?? "GET";
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, PathMax).Value = Trim(path, PathMax) ?? "/";
        cmd.Parameters.Add("@s", SqlDbType.Int).Value = statusCode;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ImpersonationSessionRow>> ListAsync(int take = 300)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(List, cn);
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = Math.Clamp(take, 1, 1000);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<ImpersonationSessionRow>();
        while (await r.ReadAsync()) list.Add(Row(r));
        return list;
    }

    public async Task<ImpersonationSessionDetail?> GetAsync(Guid sessionId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(Detail, cn);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = sessionId;
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var session = Row(r);
        var changes = new List<ImpersonationChangeRow>();
        await r.NextResultAsync();
        while (await r.ReadAsync()) changes.Add(new(r.GetDateTime(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        var requests = new List<ImpersonationRequestRow>();
        await r.NextResultAsync();
        while (await r.ReadAsync()) requests.Add(new(r.GetDateTime(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
        return new(session, changes, requests);
    }

    private static ImpersonationSessionRow Row(SqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetGuid(4), r.GetString(5), r.GetString(6),
        r.GetString(7), r.GetDateTime(8), r.GetDateTime(9), r.IsDBNull(10) ? null : r.GetDateTime(10), r.IsDBNull(11) ? null : r.GetString(11),
        r.IsDBNull(12) ? null : r.GetString(12), r.GetInt32(13), r.GetInt32(14));

    private static string? Trim(string? value, int max) => string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
