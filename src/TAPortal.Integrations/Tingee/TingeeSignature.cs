using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TAPortal.Integrations.Tingee;

/// <summary>
/// Tingee request/webhook signing: hex(HMAC_SHA512("{timestamp}:{body}", secretKey)),
/// timestamp yyyyMMddHHmmssSSS in UTC+7. An empty body is signed as "{}".
/// </summary>
public static class TingeeSignature
{
    public const string TimestampFormat = "yyyyMMddHHmmssfff"; // Tingee documents it as yyyyMMddHHmmssSSS (Java notation)
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static string Timestamp(DateTimeOffset now) =>
        now.ToOffset(VietnamOffset).ToString(TimestampFormat, CultureInfo.InvariantCulture);

    public static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (value is null || value.Length != TimestampFormat.Length) return false;
        if (!DateTime.TryParseExact(value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        timestamp = new DateTimeOffset(local, VietnamOffset);
        return true;
    }

    public static string Sign(string timestamp, string? body, string secretKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(secretKey);
        var message = $"{timestamp}:{(string.IsNullOrEmpty(body) ? "{}" : body)}";
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secretKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
    }

    public static bool Verify(string? timestamp, string? body, string? signature, string secretKey)
    {
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secretKey)) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(timestamp, body, secretKey));
        var actual = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static bool IsFresh(DateTimeOffset timestamp, DateTimeOffset now, TimeSpan maxAge) =>
        (now - timestamp).Duration() <= maxAge;
}
