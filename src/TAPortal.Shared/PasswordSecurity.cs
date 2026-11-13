using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TAPortal.Shared;

// Operator 2026-11-13: a list of weak passwords (e.g. 123456), separated by ';', edited in the Admin portal
// ("Cài đặt bảo mật mật khẩu") and applied by both the Admin portal and Hóa đơn tự động:
// - a password a user chooses must pass PasswordPolicy and must not be on the list;
// - signing in with a password on the list (or with Users.MustChangePassword=1) forces a new password first;
// - staff may hand out a weak/initial password (default 123456) when creating or resetting an account; the account then
//   has MustChangePassword=1.
// Stored in dbo.Settings (ScopeType 'SYSTEM', no ScopeId), keys below. No row = the defaults in code.

/// <summary>The weak password list and the initial password staff hand out.</summary>
public sealed record PasswordSecuritySettings(string WeakPasswordsText, string TemporaryPassword)
{
    public const string WeakPasswordsKey = "Security.WeakPasswords";
    public const string TemporaryPasswordKey = "Security.TemporaryPassword";
    public const string DefaultTemporaryPassword = "123456";
    public const string DefaultWeakPasswords =
        "123456;12345678;123456789;1234567890;111111;000000;888888;666666;123123;abc123;password;Password@123;Admin@123;admin123;" +
        "qwerty;1q2w3e4r;iloveyou;Abc@1234;Aa@123456;Ta@123456";
    public const int MaxTemporaryLength = 128;

    public static PasswordSecuritySettings Default { get; } = new(DefaultWeakPasswords, DefaultTemporaryPassword);

    private HashSet<string>? weak;

    /// <summary>The list as a set (trimmed, case-insensitive), always containing the initial password.</summary>
    public IReadOnlySet<string> WeakPasswords => weak ??= Parse(WeakPasswordsText, TemporaryPassword);

    /// <summary>True when <paramref name="password"/> is on the weak list (case-insensitive) or is the initial password.</summary>
    public bool IsWeak(string? password) => !string.IsNullOrEmpty(password) && WeakPasswords.Contains(password.Trim());

    /// <summary>A password a user chooses: PasswordPolicy and not on the weak list.</summary>
    public (bool IsValid, string Error) ValidateNew(string? password)
    {
        var policy = PasswordPolicy.Validate(password);
        if (!policy.IsValid) return policy;
        return IsWeak(password) ? (false, "Mật khẩu này quá phổ biến, dễ bị đoán. Vui lòng chọn mật khẩu khác.") : (true, string.Empty);
    }

    /// <summary>A password staff set for someone else: a strong one, or a weak one from the list (then it must be changed at sign-in).</summary>
    public (bool IsValid, string Error, bool MustChange) ValidateAssigned(string? password)
    {
        if (IsWeak(password)) return (true, string.Empty, true);
        var policy = PasswordPolicy.Validate(password);
        return (policy.IsValid, policy.Error, false);
    }

    public static HashSet<string> Parse(string? text, string? temporary = null)
    {
        var set = (text ?? "").Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(temporary)) set.Add(temporary.Trim());
        return set;
    }

    /// <summary>The list written back in a stable form: trimmed, without duplicates, joined with "; ".</summary>
    public static string Normalize(string? text) =>
        string.Join("; ", (text ?? "").Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));
}

public interface IPasswordSecurityStore
{
    Task<PasswordSecuritySettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(string weakPasswords, string temporaryPassword, Guid actor, string? ip, CancellationToken ct = default);
}

/// <summary>dbo.Settings reader/writer with a 30-second cache per process; read errors fall back to the defaults.</summary>
public sealed class SqlPasswordSecurityStore(IConfiguration configuration) : IPasswordSecurityStore
{
    public const string AuditSaved = "PASSWORD_SECURITY_SAVED";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private static (PasswordSecuritySettings Value, DateTime At)? cache;

    private SqlConnection Connection() =>
        new(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));

    public async Task<PasswordSecuritySettings> GetAsync(CancellationToken ct = default)
    {
        if (cache is { } c && DateTime.UtcNow - c.At < CacheFor) return c.Value;
        var value = PasswordSecuritySettings.Default;
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT [Key],[Value] FROM dbo.Settings WHERE ScopeType='SYSTEM' AND ScopeId IS NULL AND IsActive=1 AND [Key] IN (@w,@t)", cn);
            cmd.Parameters.AddWithValue("@w", PasswordSecuritySettings.WeakPasswordsKey);
            cmd.Parameters.AddWithValue("@t", PasswordSecuritySettings.TemporaryPasswordKey);
            string? weak = null, temporary = null;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var v = r.IsDBNull(1) ? null : r.GetString(1);
                if (r.GetString(0) == PasswordSecuritySettings.WeakPasswordsKey) weak = v; else temporary = v;
            }
            value = new(weak ?? PasswordSecuritySettings.DefaultWeakPasswords,
                string.IsNullOrWhiteSpace(temporary) ? PasswordSecuritySettings.DefaultTemporaryPassword : temporary.Trim());
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException) { }
        cache = (value, DateTime.UtcNow);
        return value;
    }

    public async Task SaveAsync(string weakPasswords, string temporaryPassword, Guid actor, string? ip, CancellationToken ct = default)
    {
        await using var cn = Connection();
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        foreach (var (key, value) in new[] { (PasswordSecuritySettings.WeakPasswordsKey, weakPasswords), (PasswordSecuritySettings.TemporaryPasswordKey, temporaryPassword) })
        {
            await using var cmd = new SqlCommand("""
IF EXISTS (SELECT 1 FROM dbo.Settings WHERE ScopeType='SYSTEM' AND ScopeId IS NULL AND [Key]=@k)
 UPDATE dbo.Settings SET [Value]=@v, IsActive=1, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by WHERE ScopeType='SYSTEM' AND ScopeId IS NULL AND [Key]=@k;
ELSE
 INSERT dbo.Settings(ScopeType,ScopeId,[Key],[Value],ValueType,Description,CreatedBy) VALUES('SYSTEM',NULL,@k,@v,'STRING',@d,@by);
""", cn, tx);
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.Parameters.AddWithValue("@by", actor);
            cmd.Parameters.AddWithValue("@d", key == PasswordSecuritySettings.WeakPasswordsKey ? "Mật khẩu yếu, cách nhau bằng dấu ;" : "Mật khẩu khởi tạo cho tài khoản mới");
            await cmd.ExecuteNonQueryAsync(ct);
        }
        // Counts only: the list and the initial password are not copied into the audit log.
        await AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), actor, AuditSaved, "dbo.Settings", PasswordSecuritySettings.WeakPasswordsKey, "{}",
            AuditSql.Json(("weakPasswords", PasswordSecuritySettings.Parse(weakPasswords).Count)), ip);
        await tx.CommitAsync(ct);
        cache = null;
    }
}
