using System.Text.Json;
using System.Text.RegularExpressions;

namespace TACustomerPortal.Web.Services;

/// <summary>The banking apps offered by "Chuyển khoản" on the payment page.</summary>
public interface IBankAppCatalog
{
    /// <summary>Current list, autofill apps first. Never blocks: a stale list is refreshed in the background.</summary>
    IReadOnlyList<BankApp> Apps { get; }
}

/// <summary>
/// Operator 2026-11-10: follow VietQR's own app lists (https://api.vietqr.io/v2/android-app-deeplinks and
/// ios-app-deeplinks), so a bank that starts filling the transfer in moves to "Điền sẵn" without a release. Read at most
/// every 12 hours; until the first answer, or when VietQR fails or answers nonsense, the built-in list
/// <see cref="BankAppLinks.Apps"/> (or the last good list) stays in use.
/// </summary>
public sealed class VietQrBankAppCatalog(IHttpClientFactory http, ILogger<VietQrBankAppCatalog> log, TimeProvider clock) : IBankAppCatalog
{
    public const string ClientName = "vietqr-apps";
    private static readonly TimeSpan Every = TimeSpan.FromHours(12), Retry = TimeSpan.FromMinutes(10);
    private IReadOnlyList<BankApp> apps = BankAppLinks.Apps;
    private DateTimeOffset next = DateTimeOffset.MinValue;
    private int refreshing;

    public IReadOnlyList<BankApp> Apps
    {
        get
        {
            if (clock.GetUtcNow() >= next && Interlocked.Exchange(ref refreshing, 1) == 0) _ = RefreshAsync();
            return Volatile.Read(ref apps);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var client = http.CreateClient(ClientName);
            var android = await client.GetStringAsync("v2/android-app-deeplinks");
            var ios = await client.GetStringAsync("v2/ios-app-deeplinks");
            if (BankAppLinks.Merge(android, ios) is { } fresh)
            {
                Volatile.Write(ref apps, fresh);
                next = clock.GetUtcNow() + Every;
            }
            else
            {
                log.LogWarning("VietQR bank app lists unreadable; keeping the current list");
                next = clock.GetUtcNow() + Retry;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "VietQR bank app lists unavailable; keeping the current list");
            next = clock.GetUtcNow() + Retry;
        }
        finally { Volatile.Write(ref refreshing, 0); }
    }
}

public static partial class BankAppLinks
{
    [GeneratedRegex("^[a-z0-9-]{1,30}$")] private static partial Regex AppId();

    /// <summary>
    /// The apps on both VietQR lists (a payer's phone may be either), autofill only when both say so, autofill first and
    /// otherwise in Android's order (most installed first). Known apps keep their short name. Null when the lists are
    /// unreadable or too short to be real (fewer than 10 apps), so the caller keeps what it has.
    /// </summary>
    public static IReadOnlyList<BankApp>? Merge(string androidJson, string iosJson)
    {
        try
        {
            var android = ReadApps(androidJson);
            var ios = ReadApps(iosJson).ToDictionary(a => a.Id, a => a.Autofill);
            var names = Apps.ToDictionary(a => a.Id, a => a.Name);
            var both = android.Where(a => ios.ContainsKey(a.Id))
                .Select(a => new BankApp(a.Id, names.GetValueOrDefault(a.Id) ?? a.Name, a.Autofill && ios[a.Id]))
                .ToList();
            return both.Count < 10 ? null : [.. both.Where(a => a.Autofill), .. both.Where(a => !a.Autofill)];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }

    private static List<BankApp> ReadApps(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<BankApp>();
        foreach (var a in doc.RootElement.GetProperty("apps").EnumerateArray())
        {
            var id = a.TryGetProperty("appId", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()! : "";
            var name = a.TryGetProperty("appName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
            var fill = a.TryGetProperty("autofill", out var f) && f.ValueKind == JsonValueKind.Number && f.GetInt32() == 1;
            if (AppId().IsMatch(id) && name.Length is > 0 and <= 60 && list.All(x => x.Id != id)) list.Add(new(id, name, fill));
        }
        return list;
    }
}
