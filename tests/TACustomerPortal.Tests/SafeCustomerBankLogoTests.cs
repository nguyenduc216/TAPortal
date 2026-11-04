using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// Banks/Add shows wwwroot/images/banks/<code>.png for every catalog bank; a bank added to the catalog needs its logo.
public sealed class SafeCustomerBankLogoTests
{
    [Fact]
    public void EveryCatalogBankHasALogo()
    {
        var dir = Path.Combine(Web(), "wwwroot", "images", "banks");
        var missing = TingeeBankCatalog.Banks.Where(b => !File.Exists(Path.Combine(dir, b.Code.ToLowerInvariant() + ".png"))).Select(b => b.Name).ToList();
        Assert.True(missing.Count == 0, "Missing bank logo: " + string.Join(", ", missing));
    }

    private static string Web()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) return Path.Combine(dir.FullName, "src", "TACustomerPortal.Web");
        throw new InvalidOperationException("TAPortal.sln not found");
    }
}
