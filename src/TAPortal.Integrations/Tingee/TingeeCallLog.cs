using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TAPortal.Integrations.Tingee;

/// <summary>
/// One outgoing Tingee Open API call exactly as sent (headers and body) and as answered, so a failed call can be handed to
/// Tingee support by its log code. The secret key is never part of it; the signature is (Tingee needs it to investigate).
/// </summary>
public sealed record TingeeCallLogEntry
{
    public required string LogCode { get; init; }
    public required string Method { get; init; }
    public required string Url { get; init; }
    public string? ClientId { get; init; }
    public string? RequestTimestamp { get; init; }
    public string? Signature { get; init; }
    public string? RequestBody { get; init; }
    public int? HttpStatus { get; init; }
    public string? ResponseCode { get; init; }
    public string? ResponseMessage { get; init; }
    public string? ResponseBody { get; init; }
    public string? Error { get; init; }
    public int DurationMs { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public interface ITingeeCallLog
{
    /// <summary>Stores the entry; must never throw (a logging failure may not break the Tingee call).</summary>
    Task WriteAsync(TingeeCallLogEntry entry, CancellationToken ct = default);
    Task<TingeeCallLogEntry?> FindAsync(string logCode, CancellationToken ct = default);
    Task<IReadOnlyList<TingeeCallLogEntry>> RecentAsync(int take, string? path, bool failedOnly, CancellationToken ct = default);
}

public static class TingeeLogCode
{
    /// <summary>"TG" + UTC time + 6 random hex digits, e.g. TG261104083015A1B2C3: short enough to read out, unique in practice.</summary>
    public static string New(DateTimeOffset utcNow) =>
        "TG" + utcNow.UtcDateTime.ToString("yyMMddHHmmss") + Convert.ToHexString(RandomNumberGenerator.GetBytes(3));

    /// <summary>Log code of the Tingee call behind a failure (Tingee rejection or network error), if it was logged.</summary>
    public static string? Of(Exception ex) =>
        (ex as TingeeApiException)?.LogCode ?? ex.Data[TingeeClient.LogCodeKey] as string ?? (ex.InnerException is { } inner ? Of(inner) : null);

    public static string? Normalize(string? code)
    {
        var c = code?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(c) || c.Length > 32 || !c.All(char.IsAsciiLetterOrDigit) ? null : c;
    }
}

/// <summary>Used where no database is configured: calls still work, nothing is stored.</summary>
public sealed class NullTingeeCallLog : ITingeeCallLog
{
    public Task WriteAsync(TingeeCallLogEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    public Task<TingeeCallLogEntry?> FindAsync(string logCode, CancellationToken ct = default) => Task.FromResult<TingeeCallLogEntry?>(null);
    public Task<IReadOnlyList<TingeeCallLogEntry>> RecentAsync(int take, string? path, bool failedOnly, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TingeeCallLogEntry>>([]);
}

/// <summary>dbo.TingeeApiLogs (migration 014).</summary>
public sealed class SqlTingeeCallLog(IConfiguration configuration) : ITingeeCallLog
{
    private const string Columns = "LogCode,Method,Url,ClientId,RequestTimestamp,Signature,RequestBody,HttpStatus,ResponseCode,ResponseMessage,ResponseBody,Error,DurationMs,CreatedAt";

    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task WriteAsync(TingeeCallLogEntry e, CancellationToken ct = default)
    {
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync(CancellationToken.None);
            await using var cmd = new SqlCommand($"INSERT dbo.TingeeApiLogs({Columns}) VALUES(@code,@method,@url,@client,@ts,@sig,@req,@http,@rcode,@rmsg,@rbody,@err,@ms,@at)", cn);
            cmd.Parameters.AddWithValue("@code", e.LogCode);
            cmd.Parameters.AddWithValue("@method", e.Method);
            cmd.Parameters.AddWithValue("@url", Cut(e.Url, 1000)!);
            cmd.Parameters.AddWithValue("@client", Db(Cut(e.ClientId, 200)));
            cmd.Parameters.AddWithValue("@ts", Db(Cut(e.RequestTimestamp, 50)));
            cmd.Parameters.AddWithValue("@sig", Db(Cut(e.Signature, 300)));
            cmd.Parameters.AddWithValue("@req", Db(e.RequestBody));
            cmd.Parameters.AddWithValue("@http", e.HttpStatus is int h ? h : DBNull.Value);
            cmd.Parameters.AddWithValue("@rcode", Db(Cut(e.ResponseCode, 50)));
            cmd.Parameters.AddWithValue("@rmsg", Db(Cut(e.ResponseMessage, 2000)));
            cmd.Parameters.AddWithValue("@rbody", Db(e.ResponseBody));
            cmd.Parameters.AddWithValue("@err", Db(Cut(e.Error, 2000)));
            cmd.Parameters.AddWithValue("@ms", e.DurationMs);
            cmd.Parameters.AddWithValue("@at", e.CreatedAtUtc);
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (Exception) { /* logging must never break the Tingee call */ }
    }

    public async Task<TingeeCallLogEntry?> FindAsync(string logCode, CancellationToken ct = default)
    {
        await using var cn = Connection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand($"SELECT TOP 1 {Columns} FROM dbo.TingeeApiLogs WHERE LogCode=@code", cn);
        cmd.Parameters.AddWithValue("@code", logCode);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Read(r) : null;
    }

    public async Task<IReadOnlyList<TingeeCallLogEntry>> RecentAsync(int take, string? path, bool failedOnly, CancellationToken ct = default)
    {
        await using var cn = Connection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand($"""
SELECT TOP (@take) {Columns} FROM dbo.TingeeApiLogs
WHERE (@path IS NULL OR Url LIKE '%' + @path + '%') AND (@failed = 0 OR ISNULL(ResponseCode,'') <> '00')
ORDER BY Id DESC
""", cn);
        cmd.Parameters.AddWithValue("@take", Math.Clamp(take, 1, 200));
        cmd.Parameters.AddWithValue("@path", Db(Cut(path?.Trim(), 200)));
        cmd.Parameters.AddWithValue("@failed", failedOnly);
        var rows = new List<TingeeCallLogEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) rows.Add(Read(r));
        return rows;
    }

    private static TingeeCallLogEntry Read(SqlDataReader r) => new()
    {
        LogCode = r.GetString(0),
        Method = r.GetString(1),
        Url = r.GetString(2),
        ClientId = Str(r, 3),
        RequestTimestamp = Str(r, 4),
        Signature = Str(r, 5),
        RequestBody = Str(r, 6),
        HttpStatus = r.IsDBNull(7) ? null : r.GetInt32(7),
        ResponseCode = Str(r, 8),
        ResponseMessage = Str(r, 9),
        ResponseBody = Str(r, 10),
        Error = Str(r, 11),
        DurationMs = r.GetInt32(12),
        CreatedAtUtc = DateTime.SpecifyKind(r.GetDateTime(13), DateTimeKind.Utc)
    };

    private static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static object Db(string? v) => v is null ? DBNull.Value : v;
    private static string? Cut(string? v, int max) => v is null || v.Length <= max ? v : v[..max];
}
