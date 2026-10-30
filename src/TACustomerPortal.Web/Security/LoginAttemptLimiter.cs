using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace TACustomerPortal.Web.Security;
// Independent from source IP: moving between IPs/proxies cannot reset this per-account window.
// All usernames (including nonexistent accounts) have identical limits. No raw identifiers are retained.
public sealed class LoginAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    public bool TryAcquire(string username)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(username.Trim().ToUpperInvariant())));
        using var lease = limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
