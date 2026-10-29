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
    // Upper bound of tracked partitions (each entry is ~200 bytes). When full after sweeping expired windows,
    // NEW partitions are rejected (fail closed) instead of evicting live counters an attacker could flush.
    public int MaxTrackedKeys { get; set; } = 100_000;
}

public readonly record struct ThrottleDecision(bool Allowed, TimeSpan RetryAfter);

// Abstraction point for a shared (multi-instance) store; only the in-memory implementation exists today
// (PENDING_INFRASTRUCTURE_APPROVAL for any distributed backend).
public interface IAdminLoginThrottle
{
    ThrottleDecision TryAcquire(IPAddress? remoteIp, string? username);
}

// Counts every POST login attempt (successful or not) in fixed windows on two independent partitions:
// the client IP (changing usernames cannot bypass it) and the normalized account (changing IPs cannot bypass it).
// Nothing persists: windows expire automatically, there is no permanent lockout, and only SHA-256 keys are kept.
// In-memory per process: counters reset on restart and are not shared between instances.
public sealed class AdminLoginThrottle(AdminLoginThrottleOptions options, TimeProvider time) : IAdminLoginThrottle
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
            var newKeys = (windows.ContainsKey(ipKey) ? 0 : 1) + (windows.ContainsKey(accountKey) ? 0 : 1);
            if (now >= nextSweep || windows.Count + newKeys > options.MaxTrackedKeys)
                Sweep(now);
            if (windows.Count + newKeys > options.MaxTrackedKeys)
                return new(false, options.Window);

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

    public int TrackedKeys { get { lock (gate) return windows.Count; } }

    public static string PartitionTag(string? username) => Hash((username ?? string.Empty).Trim().ToUpperInvariant())[..12];

    private void Sweep(DateTimeOffset now)
    {
        foreach (var expired in windows.Where(w => now - w.Value.Start >= options.Window).Select(w => w.Key).ToList())
            windows.Remove(expired);
        nextSweep = now + options.Window;
    }

    private (DateTimeOffset Start, int Count) Current(string key, DateTimeOffset now) =>
        windows.TryGetValue(key, out var w) && now - w.Start < options.Window ? w : (now, 0);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

// AUD-24: forwarded headers are honoured only from an explicit allowlist of single proxy IPs (ReverseProxy:KnownProxies).
// Empty = disabled (default). Wildcards, CIDR ranges, unspecified and invalid addresses are rejected at startup.
public static class TrustedProxyConfig
{
    public static IReadOnlyList<IPAddress> Parse(IEnumerable<string>? values)
    {
        var result = new List<IPAddress>();
        foreach (var raw in values ?? [])
        {
            var value = raw?.Trim() ?? string.Empty;
            if (value.Length == 0) continue;
            if (value.Contains('/') || value.Contains('*') || !IPAddress.TryParse(value, out var ip))
                throw new InvalidOperationException($"ReverseProxy:KnownProxies entry '{value}' must be a single IP address (no ranges or wildcards).");
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast) || ip.Equals(IPAddress.None))
                throw new InvalidOperationException($"ReverseProxy:KnownProxies entry '{value}' is not allowed.");
            result.Add(ip);
        }
        return result;
    }
}
