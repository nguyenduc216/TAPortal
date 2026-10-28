using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace TAPortal.Web.Security;

public sealed class AdminLoginThrottleOptions
{
    // Proposed defaults, not an approved business rule; override via Security:AdminLogin:* configuration.
    public int AccountPermitLimit { get; set; } = 5;
    public int IpPermitLimit { get; set; } = 20;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);
}

public readonly record struct ThrottleDecision(bool Allowed, TimeSpan RetryAfter);

// Counts every POST login attempt (successful or not) in fixed windows on two independent partitions:
// the socket IP (changing usernames cannot bypass it) and the normalized account (changing IPs cannot bypass it).
// Nothing persists: windows expire automatically, there is no permanent lockout, and only SHA-256 keys are kept.
// In-memory per process; multi-instance deployments need a shared gateway/WAF limit as well.
public sealed class AdminLoginThrottle(AdminLoginThrottleOptions options, TimeProvider time)
{
    private readonly object gate = new();
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> windows = new(StringComparer.Ordinal);
    private DateTimeOffset nextSweep = DateTimeOffset.MinValue;

    public ThrottleDecision TryAcquire(IPAddress? remoteIp, string? username)
    {
        var ipKey = "ip:" + Hash(remoteIp?.ToString() ?? "unknown");
        var accountKey = "acct:" + Hash((username ?? string.Empty).Trim().ToUpperInvariant());
        var now = time.GetUtcNow();
        lock (gate)
        {
            if (now >= nextSweep)
            {
                foreach (var expired in windows.Where(w => now - w.Value.Start >= options.Window).Select(w => w.Key).ToList())
                    windows.Remove(expired);
                nextSweep = now + options.Window;
            }

            var ip = Current(ipKey, now);
            var account = Current(accountKey, now);
            var retry = TimeSpan.Zero;
            if (ip.Count >= options.IpPermitLimit) retry = Max(retry, ip.Start + options.Window - now);
            if (account.Count >= options.AccountPermitLimit) retry = Max(retry, account.Start + options.Window - now);
            if (retry > TimeSpan.Zero)
                return new(false, retry);

            windows[ipKey] = (ip.Start, ip.Count + 1);
            windows[accountKey] = (account.Start, account.Count + 1);
            return new(true, TimeSpan.Zero);
        }
    }

    public static string PartitionTag(string? username) => Hash((username ?? string.Empty).Trim().ToUpperInvariant())[..12];

    private (DateTimeOffset Start, int Count) Current(string key, DateTimeOffset now) =>
        windows.TryGetValue(key, out var w) && now - w.Start < options.Window ? w : (now, 0);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
