using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace TACustomerPortal.Web.Services;

/// <summary>
/// Outgoing mail (section "Email"). Filled by the operator in the server's appsettings.json; the password never goes to Git.
/// System.Net.Mail supports STARTTLS (port 587), not implicit TLS on 465. PublicBaseUrl builds the reset link, so a forged
/// Host header can never point a reset email at another site.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Hóa đơn tự động";
    public string? PublicBaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress)
        && Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}

public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        using var mail = new MailMessage { From = new MailAddress(o.FromAddress!, o.FromName, Encoding.UTF8), Subject = message.Subject, SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8 };
        mail.To.Add(message.To);
        mail.Body = message.TextBody;
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, "text/html"));
        TAPortal.Integrations.Notifications.MailIds.Stamp(mail);
        using var smtp = new SmtpClient(o.Host!, o.Port) { EnableSsl = o.EnableSsl, DeliveryMethod = SmtpDeliveryMethod.Network, Timeout = 20000 };
        if (!string.IsNullOrWhiteSpace(o.Username)) smtp.Credentials = new NetworkCredential(o.Username, o.Password);
        await smtp.SendMailAsync(mail, ct);
    }
}

/// <summary>An account that may recover its password by email: active, has an email, a Customer Portal member, not TA staff.</summary>
public sealed record RecoveryAccount(Guid UserId, string Username, string DisplayName, string Email);

public interface IPasswordResetStore
{
    Task<RecoveryAccount?> FindAccountAsync(string usernameOrEmail);
    Task<int> RecentRequestsAsync(Guid userId, DateTime sinceUtc);
    /// <summary>Stores a new link and retires the user's older unused links.</summary>
    Task SaveTokenAsync(Guid userId, byte[] tokenHash, DateTime expiresUtc, string? requestIp);
    Task<RecoveryAccount?> FindByTokenAsync(byte[] tokenHash, DateTime nowUtc);
    /// <summary>Consumes the link and sets the new password (MustChangePassword=0) in one transaction; false if the link is no longer valid.</summary>
    Task<bool> ResetAsync(byte[] tokenHash, string newPasswordHash, DateTime nowUtc);
}

public static class PasswordResetTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    public const int MaxRequestsPerHour = 3;

    public static string New() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static byte[]? Hash(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        return SHA256.HashData(Encoding.ASCII.GetBytes(token.Trim()));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class SqlPasswordResetStore(IConfiguration configuration) : IPasswordResetStore
{
    // Same eligibility everywhere: an active, non-staff Customer Portal member of an active customer, with an email.
    private const string Eligible = """
u.IsDeleted=0 AND u.IsActive=1 AND u.Status='ACTIVE' AND u.Email IS NOT NULL AND u.Email<>''
AND NOT EXISTS(SELECT 1 FROM dbo.UserRoles ur WHERE ur.UserId=u.Id)
AND EXISTS(SELECT 1 FROM dbo.CustomerPortalMemberships m JOIN dbo.Customers c ON c.Id=m.CustomerId
           WHERE m.UserId=u.Id AND m.IsActive=1 AND c.IsActive=1 AND c.IsDeleted=0 AND c.Status='ACTIVE')
""";

    private SqlConnection Connection() => new(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));

    public async Task<RecoveryAccount?> FindAccountAsync(string usernameOrEmail)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        // An email shared by several accounts is ambiguous: such requests are answered like unknown ones.
        await using var cmd = new SqlCommand($"SELECT TOP 2 u.Id,u.Username,u.DisplayName,u.Email FROM dbo.Users u WHERE (u.NormalizedUsername=@v OR u.NormalizedEmail=@v) AND {Eligible}", cn);
        cmd.Parameters.AddWithValue("@v", usernameOrEmail.Trim().ToUpperInvariant());
        var found = new List<RecoveryAccount>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) found.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return found.Count == 1 ? found[0] : null;
    }

    public async Task<int> RecentRequestsAsync(Guid userId, DateTime sinceUtc)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.PasswordResetTokens WHERE UserId=@u AND CreatedAt>=@since", cn);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@since", sinceUtc);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task SaveTokenAsync(Guid userId, byte[] tokenHash, DateTime expiresUtc, string? requestIp)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
UPDATE dbo.PasswordResetTokens SET UsedAt=SYSUTCDATETIME() WHERE UserId=@u AND UsedAt IS NULL;
INSERT dbo.PasswordResetTokens(UserId,TokenHash,ExpiresAt,RequestIp) VALUES(@u,@hash,@exp,@ip);
""", cn);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.Add("@hash", System.Data.SqlDbType.Binary, 32).Value = tokenHash;
        cmd.Parameters.AddWithValue("@exp", expiresUtc);
        cmd.Parameters.AddWithValue("@ip", (object?)requestIp ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<RecoveryAccount?> FindByTokenAsync(byte[] tokenHash, DateTime nowUtc)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"""
SELECT u.Id,u.Username,u.DisplayName,u.Email FROM dbo.PasswordResetTokens t JOIN dbo.Users u ON u.Id=t.UserId
WHERE t.TokenHash=@hash AND t.UsedAt IS NULL AND t.ExpiresAt>@now AND {Eligible}
""", cn);
        cmd.Parameters.Add("@hash", System.Data.SqlDbType.Binary, 32).Value = tokenHash;
        cmd.Parameters.AddWithValue("@now", nowUtc);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3)) : null;
    }

    public async Task<bool> ResetAsync(byte[] tokenHash, string newPasswordHash, DateTime nowUtc)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var cmd = new SqlCommand($"""
DECLARE @user uniqueidentifier;
UPDATE t SET UsedAt=SYSUTCDATETIME(), @user=t.UserId
FROM dbo.PasswordResetTokens t JOIN dbo.Users u ON u.Id=t.UserId
WHERE t.TokenHash=@hash AND t.UsedAt IS NULL AND t.ExpiresAt>@now AND {Eligible};
IF @user IS NULL BEGIN SELECT 0; RETURN; END
UPDATE dbo.Users SET PasswordHash=@pw,MustChangePassword=0,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@user WHERE Id=@user;
UPDATE dbo.PasswordResetTokens SET UsedAt=SYSUTCDATETIME() WHERE UserId=@user AND UsedAt IS NULL;
SELECT 1;
""", cn, tx);
        cmd.Parameters.Add("@hash", System.Data.SqlDbType.Binary, 32).Value = tokenHash;
        cmd.Parameters.AddWithValue("@now", nowUtc);
        cmd.Parameters.AddWithValue("@pw", newPasswordHash);
        var ok = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        await tx.CommitAsync();
        return ok;
    }
}
