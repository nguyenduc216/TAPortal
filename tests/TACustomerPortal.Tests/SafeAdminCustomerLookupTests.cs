extern alias admin;

using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

/// <summary>Admin → Thêm khách hàng: customer code suggested from the name, and the tax-code lookup button.</summary>
public sealed class SafeAdminCustomerLookupTests
{
    [Theory]
    [InlineData("Công Ty TNHH Đầu Tư Và Xây Dựng AD Home", "ADHOME")]
    [InlineData("Công Ty Cổ Phần Agrisea Nha Trang", "AGRISEA")]
    [InlineData("Công Ty TNHH TMDV An Đông Khánh Hòa", "ANDONG")]
    [InlineData("Công Ty Cổ Phần Aroven", "AROVEN")]
    [InlineData("Hộ kinh doanh Nhà hàng ba miền - Ngọc Phương", "BAMIEN")]
    [InlineData("Công ty TNHH Du Lịch Byn Horizon", "BYNHORIZON")]
    [InlineData("CÔNG TY TNHH TMDV GINI", "GINI")]
    [InlineData("Công ty TNHH Thương mại", "THUONGMAI")]          // only generic words: the last one is kept
    [InlineData("", "")]
    public void CodeIsSuggestedFromTheBrandPartOfTheName(string name, string expected) =>
        Assert.Equal(expected, S.CustomerCodeSuggest.FromName(name));

    [Fact]
    public void SuggestedCodeIsShortAndUnique()
    {
        Assert.True(S.CustomerCodeSuggest.FromName("Công ty TNHH Một Hai Ba Bốn Năm Sáu Bảy Tám Chín Mười Mười Một").Length <= S.CustomerCodeSuggest.MaxLength);
        Assert.Equal("GINI", S.CustomerCodeSuggest.Unique("GINI", ["GINIX"]));
        Assert.Equal("GINI2", S.CustomerCodeSuggest.Unique("GINI", ["GINI"]));
        Assert.Equal("GINI3", S.CustomerCodeSuggest.Unique("GINI", ["gini", "GINI2"]));
    }

    [Fact]
    public void CustomerFormHasTheLookupButtonAndCodeSuggestion()
    {
        var view = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TAPortal.Web", "Views", "Customers", "Edit.cshtml"));
        Assert.Contains("data-ta-tax-lookup=\"@Url.Action(\"TaxLookup\", \"Customers\")\"", view);
        Assert.Contains("data-ta-code-suggest=\"@Url.Action(\"SuggestCode\", \"Customers\")\"", view);
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TAPortal.Web", "wwwroot", "js", "ta-portal.js"));
        Assert.Contains("[data-ta-tax-lookup]", js);
        Assert.Contains("[data-ta-code-suggest]", js);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TAPortal.sln not found");
    }
}
