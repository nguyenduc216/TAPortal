using System.Text.RegularExpressions;

namespace TACustomerPortal.Tests;

// The Mini App (src/TAPortal.ZaloMiniApp, React) must follow the sign-in rules of the API (operator 2026-11-13): it sends
// Zalo tokens only, never a phone number of its own, calls only the T.A API, and keeps no secret in its source.
public sealed class SafeZaloMiniAppClientTests
{
    private static string App(params string[] parts) => Path.Combine([FindRoot(), "src", "TAPortal.ZaloMiniApp", .. parts]);

    [Fact]
    public void TheAppSendsZaloTokensNeverAPhoneNumber()
    {
        var api = File.ReadAllText(App("src", "api.ts"));
        foreach (Match body in Regex.Matches(api, @"\{\s*([a-zA-Z, ]+)\s*\}\)"))
            foreach (var field in body.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                Assert.Contains(field, new[] { "accessToken", "phoneToken", "qr", "username", "password", "userId" });
        Assert.DoesNotContain("phone:", api);
        Assert.Contains("getPhoneNumber", File.ReadAllText(App("src", "zalo.ts")));
    }

    [Fact]
    public void TheAppCallsOnlyTheMiniAppApiAndHoldsNoSecret()
    {
        var config = File.ReadAllText(App("vite.config.mts"));
        Assert.Contains("https://hoadon.tuvangiaiphapta.vn", config);
        Assert.Contains("/api/zalo-mini-app", config);
        foreach (var file in Directory.EnumerateFiles(App("src"), "*.*", SearchOption.AllDirectories).Append(App("vite.config.mts")).Append(App("app-config.json")))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(@"(?i)secret_?key\s*[:=]\s*[""'][^""']+", text);
            Assert.DoesNotMatch(@"https?://(?!hoadon\.tuvangiaiphapta\.vn|localhost)[a-z0-9.-]+\.[a-z]{2,}", text.Replace("https://reactrouter.com", ""));
        }
        Assert.False(File.Exists(App(".env")));
        Assert.Contains("node_modules/", File.ReadAllText(App(".gitignore")));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TAPortal.sln not found");
    }
}
