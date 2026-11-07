using System.Text.RegularExpressions;

namespace TACustomerPortal.Tests;

// Operator 2026-11-07: the Customer Portal is "Hóa đơn tự động". Customers never see "T.A" or "Customer Portal", except the
// single "Sản phẩm của Tư vấn giải pháp T.A" line at the bottom of the sidebar.
public sealed class SafeCustomerBrandTests
{
    private static string Web()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("TAPortal.sln not found"), "src", "TACustomerPortal.Web");
    }

    // Razor comments and C# comments are not shown to customers.
    private static string Visible(string text) =>
        Regex.Replace(Regex.Replace(Regex.Replace(text, @"@\*.*?\*@", "", RegexOptions.Singleline), @"(?m)^\s*//.*$", ""), @"(?m)^\s*///.*$", "");

    [Fact]
    public void CustomersSeeOnlyTheHoaDonTuDongBrand()
    {
        var web = Web();
        var files = Directory.EnumerateFiles(Path.Combine(web, "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(web, "Controllers"), "*.cs"))
            .Concat(Directory.EnumerateFiles(Path.Combine(web, "Services"), "*.cs"));
        var offenders = new List<string>();
        foreach (var file in files)
            foreach (var line in Visible(File.ReadAllText(file)).Split('\n'))
                if ((line.Contains("T.A") || line.Contains("Customer Portal")) && !line.Contains("class=\"cp-maker\""))
                    offenders.Add($"{Path.GetFileName(file)}: {line.Trim()[..Math.Min(100, line.Trim().Length)]}");
        Assert.Empty(offenders);
    }

    [Fact]
    public void SidebarCarriesTheMakerLine()
    {
        var layout = File.ReadAllText(Path.Combine(Web(), "Views", "Shared", "_Layout.cshtml"));
        Assert.Contains("<a class=\"cp-maker\" href=\"https://tuvangiaiphapta.vn\" target=\"_blank\" rel=\"noopener\" data-ta-hard>Sản phẩm của Tư vấn giải pháp T.A</a>", layout);
        Assert.Contains("<span class=\"cp-brand__text\">Hóa đơn tự động<small>", layout);
        Assert.Contains("· Hóa đơn tự động</title>", layout);
    }
}
