using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TAPortal.Integrations.ZaloMiniApp;

/// <summary>dbo.Users (read only) and the four tables of migration 038.</summary>
public interface IMiniAppStore
{
    Task<MiniAppAccount?> AccountAsync(Guid userId, CancellationToken ct = default);
    /// <summary>By username, or by the account's email when exactly one account has it (as the web sign-in does).</summary>
    Task<MiniAppAccount?> AccountByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// The user changes their own phone (stored as 0xxxxxxxxx): in one transaction the old Zalo links end and an audit row
    /// USER_PHONE_CHANGED is written (no phone number in it). The new phone is unverified until its OTP.
    /// </summary>
    Task ChangePhoneAsync(Guid userId, string phone, string? ip, CancellationToken ct = default);

    Task<string?> VerifiedPhoneAsync(Guid userId, CancellationToken ct = default);
    Task SetVerifiedPhoneAsync(Guid userId, string phone, string method, CancellationToken ct = default);

    Task CreateQrAsync(byte[] tokenHash, Guid userId, string portal, DateTime expiresUtc, CancellationToken ct = default);
    /// <summary>Marks the QR used (once, before it expires) and returns its account; null when unknown, used or expired.</summary>
    Task<Guid?> ConsumeQrAsync(byte[] tokenHash, string zaloUserId, DateTime nowUtc, CancellationToken ct = default);

    Task LinkAsync(string zaloUserId, Guid userId, string phone, string source, CancellationToken ct = default);
    Task<IReadOnlyList<(Guid UserId, string Phone)>> LinksAsync(string zaloUserId, CancellationToken ct = default);
    /// <summary>Records the use of an active link; false when the link is revoked or missing.</summary>
    Task<bool> TouchLinkAsync(string zaloUserId, Guid userId, CancellationToken ct = default);
    Task<int> RevokeLinkAsync(string zaloUserId, Guid userId, CancellationToken ct = default);
    /// <summary>Unlinks every Zalo user of an account (from the web, by the account itself).</summary>
    Task<int> RevokeLinksAsync(Guid userId, Guid actor, CancellationToken ct = default);
    Task<int> ActiveLinkCountAsync(Guid userId, CancellationToken ct = default);

    Task CreateOtpAsync(Guid id, Guid userId, string phone, string purpose, byte[] codeHash, DateTime expiresUtc, string? ip, CancellationToken ct = default);
    Task<(int Count, DateTime? LastUtc)> OtpStatsAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default);
    Task<OtpChallenge?> OtpAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task FailOtpAsync(Guid id, CancellationToken ct = default);
    /// <summary>Consumes an unexpired, unused challenge; false when someone else consumed it first.</summary>
    Task<bool> ConsumeOtpAsync(Guid id, DateTime nowUtc, CancellationToken ct = default);
}

public sealed class SqlMiniAppStore(IConfiguration configuration) : IMiniAppStore
{
    private const string ActiveUser = "u.IsDeleted=0 AND u.IsActive=1 AND u.Status='ACTIVE'";
    private const string AccountColumns = "u.Id,u.Username,u.DisplayName,u.Phone,u.PasswordHash,u.MustChangePassword";

    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
        await cn.OpenAsync(ct);
        return cn;
    }

    private static SqlCommand Command(SqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private async Task<int> Execute(string sql, CancellationToken ct, params (string, object?)[] args)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, sql, args);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<object?> Scalar(string sql, CancellationToken ct, params (string, object?)[] args)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, sql, args);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }

    private async Task<MiniAppAccount?> Account(string predicate, CancellationToken ct, params (string, object?)[] args)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, $"SELECT {AccountColumns} FROM dbo.Users u WHERE {predicate} AND {ActiveUser}", args);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetBoolean(5))
            : null;
    }

    public Task<MiniAppAccount?> AccountAsync(Guid userId, CancellationToken ct = default) => Account("u.Id=@v", ct, ("@v", userId));

    public async Task<MiniAppAccount?> AccountByUsernameAsync(string username, CancellationToken ct = default)
    {
        var normalized = username.Trim().ToUpperInvariant();
        return await Account("u.NormalizedUsername=@v", ct, ("@v", normalized))
            ?? (normalized.Contains('@')
                ? await Account("u.NormalizedEmail=@v AND (SELECT COUNT(1) FROM dbo.Users d WHERE d.NormalizedEmail=@v AND d.IsDeleted=0)=1", ct, ("@v", normalized))
                : null);
    }

    public async Task ChangePhoneAsync(Guid userId, string phone, string? ip, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await using var cmd = new SqlCommand("""
UPDATE dbo.Users SET Phone=@p,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@u WHERE Id=@u AND IsDeleted=0;
UPDATE dbo.ZaloMiniAppLinks SET RevokedAt=SYSUTCDATETIME(),RevokedBy=@u WHERE UserId=@u AND RevokedAt IS NULL;
INSERT dbo.AuditLogs(UserId,Action,EntityName,EntityId,OldValues,NewValues,IpAddress) VALUES(@u,'USER_PHONE_CHANGED','dbo.Users',CONVERT(nvarchar(36),@u),'{}','{}',@ip);
""", cn, tx);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@p", phone);
        cmd.Parameters.AddWithValue("@ip", ip is { Length: <= 64 } ? ip : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<string?> VerifiedPhoneAsync(Guid userId, CancellationToken ct = default) =>
        await Scalar("SELECT Phone FROM dbo.UserVerifiedPhones WHERE UserId=@u", ct, ("@u", userId)) as string;

    public Task SetVerifiedPhoneAsync(Guid userId, string phone, string method, CancellationToken ct = default) => Execute("""
UPDATE dbo.UserVerifiedPhones SET Phone=@p,Method=@m,VerifiedAt=SYSUTCDATETIME() WHERE UserId=@u;
IF @@ROWCOUNT=0 INSERT dbo.UserVerifiedPhones(UserId,Phone,Method) VALUES(@u,@p,@m);
""", ct, ("@u", userId), ("@p", phone), ("@m", method));

    public Task CreateQrAsync(byte[] tokenHash, Guid userId, string portal, DateTime expiresUtc, CancellationToken ct = default) =>
        Execute("INSERT dbo.ZaloMiniAppQrTokens(TokenHash,UserId,Portal,ExpiresAt) VALUES(@h,@u,@p,@e)", ct, ("@h", tokenHash), ("@u", userId), ("@p", portal), ("@e", expiresUtc));

    public async Task<Guid?> ConsumeQrAsync(byte[] tokenHash, string zaloUserId, DateTime nowUtc, CancellationToken ct = default) =>
        await Scalar("""
UPDATE dbo.ZaloMiniAppQrTokens SET ConsumedAt=@now,ConsumedByZaloUserId=@z
OUTPUT inserted.UserId
WHERE TokenHash=@h AND ConsumedAt IS NULL AND ExpiresAt>@now
""", ct, ("@h", tokenHash), ("@z", zaloUserId), ("@now", nowUtc)) as Guid?;

    public Task LinkAsync(string zaloUserId, Guid userId, string phone, string source, CancellationToken ct = default) => Execute("""
UPDATE dbo.ZaloMiniAppLinks SET Phone=@p,LastUsedAt=SYSUTCDATETIME() WHERE ZaloUserId=@z AND UserId=@u AND RevokedAt IS NULL;
IF @@ROWCOUNT=0 INSERT dbo.ZaloMiniAppLinks(ZaloUserId,UserId,Phone,Source,LastUsedAt) VALUES(@z,@u,@p,@s,SYSUTCDATETIME());
""", ct, ("@z", zaloUserId), ("@u", userId), ("@p", phone), ("@s", source));

    public async Task<IReadOnlyList<(Guid UserId, string Phone)>> LinksAsync(string zaloUserId, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, "SELECT UserId,Phone FROM dbo.ZaloMiniAppLinks WHERE ZaloUserId=@z AND RevokedAt IS NULL ORDER BY CreatedAt", ("@z", zaloUserId));
        var list = new List<(Guid, string)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add((r.GetGuid(0), r.GetString(1)));
        return list;
    }

    public async Task<bool> TouchLinkAsync(string zaloUserId, Guid userId, CancellationToken ct = default) =>
        await Execute("UPDATE dbo.ZaloMiniAppLinks SET LastUsedAt=SYSUTCDATETIME() WHERE ZaloUserId=@z AND UserId=@u AND RevokedAt IS NULL", ct, ("@z", zaloUserId), ("@u", userId)) > 0;

    public Task<int> RevokeLinkAsync(string zaloUserId, Guid userId, CancellationToken ct = default) =>
        Execute("UPDATE dbo.ZaloMiniAppLinks SET RevokedAt=SYSUTCDATETIME(),RevokedBy=@u WHERE ZaloUserId=@z AND UserId=@u AND RevokedAt IS NULL", ct, ("@z", zaloUserId), ("@u", userId));

    public Task<int> RevokeLinksAsync(Guid userId, Guid actor, CancellationToken ct = default) =>
        Execute("UPDATE dbo.ZaloMiniAppLinks SET RevokedAt=SYSUTCDATETIME(),RevokedBy=@a WHERE UserId=@u AND RevokedAt IS NULL", ct, ("@u", userId), ("@a", actor));

    public async Task<int> ActiveLinkCountAsync(Guid userId, CancellationToken ct = default) =>
        (int)(await Scalar("SELECT COUNT(1) FROM dbo.ZaloMiniAppLinks WHERE UserId=@u AND RevokedAt IS NULL", ct, ("@u", userId)) ?? 0);

    public Task CreateOtpAsync(Guid id, Guid userId, string phone, string purpose, byte[] codeHash, DateTime expiresUtc, string? ip, CancellationToken ct = default) =>
        Execute("INSERT dbo.PhoneOtpChallenges(Id,UserId,Phone,Purpose,CodeHash,ExpiresAt,RequestIp) VALUES(@id,@u,@p,@purpose,@h,@e,@ip)", ct,
            ("@id", id), ("@u", userId), ("@p", phone), ("@purpose", purpose), ("@h", codeHash), ("@e", expiresUtc), ("@ip", ip is { Length: <= 45 } ? ip : null));

    public async Task<(int Count, DateTime? LastUtc)> OtpStatsAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, "SELECT COUNT(1),MAX(CreatedAt) FROM dbo.PhoneOtpChallenges WHERE UserId=@u AND CreatedAt>=@since", ("@u", userId), ("@since", sinceUtc));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return (r.GetInt32(0), r.IsDBNull(1) ? null : DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc));
    }

    public async Task<OtpChallenge?> OtpAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, "SELECT Id,UserId,Phone,CodeHash,Attempts,ExpiresAt,ConsumedAt FROM dbo.PhoneOtpChallenges WHERE Id=@id AND UserId=@u", ("@id", id), ("@u", userId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), (byte[])r[3], r.GetInt32(4), DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc),
                r.IsDBNull(6) ? null : DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc))
            : null;
    }

    public Task FailOtpAsync(Guid id, CancellationToken ct = default) =>
        Execute("UPDATE dbo.PhoneOtpChallenges SET Attempts=Attempts+1 WHERE Id=@id AND Attempts<10", ct, ("@id", id));

    public async Task<bool> ConsumeOtpAsync(Guid id, DateTime nowUtc, CancellationToken ct = default) =>
        await Execute("UPDATE dbo.PhoneOtpChallenges SET ConsumedAt=@now WHERE Id=@id AND ConsumedAt IS NULL AND ExpiresAt>@now", ct, ("@id", id), ("@now", nowUtc)) > 0;
}
