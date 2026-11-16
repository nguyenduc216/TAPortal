extern alias admin;

using System.Text.Json;
using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// CONTRACTS-AND-TASKS: Hợp đồng > Danh mục dịch vụ (operator 2026-11-16, migration 061). The pricing of the T.A quote
// ("Báo giá phí dịch vụ"): tiers by measure, fees per industry, package fee, quoted separately. No SQL here.
public sealed class SafeServiceCatalogTests
{
    private static readonly Guid Trade = Guid.Parse("10000000-0000-0000-0000-000000000001"), Build = Guid.Parse("10000000-0000-0000-0000-000000000003"),
        Removed = Guid.Parse("10000000-0000-0000-0000-000000000009"), Group = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public void AccountingFeesUseThreeMeasuresAndOneFeePerIndustry()
    {
        // 1. Phí dịch vụ tư vấn kế toán thuế: số lượng chứng từ, doanh thu tháng / năm (triệu đồng); a fee per industry.
        var json = JsonSerializer.Serialize(new
        {
            metrics = new[] { new { key = "k1", label = "Số lượng chứng từ", unit = "chứng từ" }, new { key = "k2", label = "Doanh thu tháng", unit = "triệu đồng" },
                new { key = "k3", label = "Doanh thu năm", unit = "triệu đồng" }, },
            byIndustry = true,
            tiers = new object[]
            {
                new { label = "Không phát sinh chứng từ", ranges = new { k1 = new { min = 0, max = 0 } }, fees = new Dictionary<string, decimal?> { [Trade.ToString()] = 1_000_000, [Build.ToString()] = 1_000_000 } },
                new { label = (string?)null, ranges = new { k1 = new { min = 1, max = 10 }, k2 = new { min = (decimal?)null, max = 100 }, k3 = new { min = (decimal?)null, max = 1200 } },
                    fees = new Dictionary<string, decimal?> { [Trade.ToString()] = 1_500_000, [Build.ToString()] = 2_000_000.4m, [Removed.ToString()] = 9 } },
                new { label = "", ranges = new { }, fees = new Dictionary<string, decimal?>() }                                     // an empty row is dropped
            }
        });
        var (pricing, error) = S.ServicePricingPolicy.Validate(json, [Trade, Build]);
        Assert.Null(error);
        Assert.Equal(["m1", "m2", "m3"], pricing!.Metrics.Select(m => m.Key));                                    // keys follow the order
        Assert.Equal(2, pricing.Tiers.Count);
        Assert.Equal(0, pricing.Tiers[0].Ranges["m1"].Max);
        Assert.Equal(2_000_000m, pricing.Tiers[1].Fees[Build.ToString()]);                                       // whole đồng
        Assert.False(pricing.Tiers[1].Fees.ContainsKey(Removed.ToString()));                                      // only industries in use
        Assert.Equal("Từ 1 đến 10 chứng từ; Đến 100 triệu đồng; Đến 1.200 triệu đồng", S.ServicePricingPolicy.TierLabel(pricing, pricing.Tiers[1]));
        Assert.Equal("Không phát sinh chứng từ", S.ServicePricingPolicy.TierLabel(pricing, pricing.Tiers[0]));
        Assert.Equal(pricing.Tiers.Count, S.ServicePricingPolicy.Read(S.ServicePricingPolicy.Write(pricing)).Tiers.Count);
    }

    [Fact]
    public void StaffServiceTiersAreRangesOfOneMeasure()
    {
        // 2.1 Dịch vụ nhân sự hàng tháng: "Quy mô nhân sự được xử lý trong tháng" - "Phí dịch vụ đồng/tháng".
        var json = """
{"metrics":[{"key":"a","label":"Quy mô nhân sự được xử lý trong tháng","unit":"nhân sự"}],"byIndustry":false,
 "tiers":[{"label":"Dưới 10 nhân sự","ranges":{"a":{"max":9}},"fee":1000000},{"ranges":{"a":{"min":10,"max":14}},"fee":"1500000"},{"ranges":{"a":{"min":30}},"fee":3500000}]}
""";
        var (pricing, error) = S.ServicePricingPolicy.Validate(json, []);
        Assert.Null(error);
        Assert.Equal("Dưới 10 nhân sự", S.ServicePricingPolicy.TierLabel(pricing!, pricing!.Tiers[0]));
        Assert.Equal("Từ 10 đến 14 nhân sự", S.ServicePricingPolicy.TierLabel(pricing, pricing.Tiers[1]));
        Assert.Equal("Từ 30 nhân sự trở lên", S.ServicePricingPolicy.TierLabel(pricing, pricing.Tiers[2]));
        Assert.Equal(1_500_000m, pricing.Tiers[1].Fee);
    }

    [Fact]
    public void WrongPricingTablesAreRefusedWithAReason()
    {
        Assert.StartsWith("Nhập ít nhất một chỉ số", S.ServicePricingPolicy.Validate("""{"metrics":[],"tiers":[]}""", []).Error);
        Assert.StartsWith("Tối đa 3 chỉ số", S.ServicePricingPolicy.Validate("""{"metrics":[{"label":"a"},{"label":"b"},{"label":"c"},{"label":"d"}]}""", []).Error);
        Assert.Contains("lớn hơn \"Đến\"", S.ServicePricingPolicy.Validate("""{"metrics":[{"key":"a","label":"Số bộ"}],"tiers":[{"ranges":{"a":{"min":5,"max":2}},"fee":1}]}""", []).Error);
        Assert.Contains("chưa có phí", S.ServicePricingPolicy.Validate("""{"metrics":[{"key":"a","label":"Số bộ"}],"tiers":[{"ranges":{"a":{"max":10}}}]}""", []).Error);
        Assert.StartsWith("Thêm ít nhất một bậc", S.ServicePricingPolicy.Validate("""{"metrics":[{"key":"a","label":"Số bộ"}],"tiers":[]}""", []).Error);
        Assert.StartsWith("Chưa có ngành nghề", S.ServicePricingPolicy.Validate("""{"metrics":[{"key":"a","label":"Số bộ"}],"byIndustry":true,"tiers":[]}""", []).Error);
        Assert.StartsWith("Bảng hạn mức không đọc được", S.ServicePricingPolicy.Validate("{oops", []).Error);
        Assert.Contains("không âm", S.ServicePricingPolicy.Validate("""{"metrics":[{"key":"a","label":"Số bộ"}],"tiers":[{"ranges":{"a":{"min":-1}},"fee":1}]}""", []).Error);
    }

    [Fact]
    public void PackageAndQuotedServicesNeedNoTierTable()
    {
        // 2.2 Tư vấn thiết lập hồ sơ nhân sự ban đầu: "Phí trọn gói: 10.000.000 đồng/gói".
        var (fixedService, error) = S.ServicePricingPolicy.Check(Guid.NewGuid(), new S.ServiceInput
        {
            CategoryId = Group, Name = "  Tư vấn thiết lập hồ sơ nhân sự ban đầu ", PricingMode = "FIXED", FixedFee = "10.000.000", FeeUnit = "đồng/gói",
            ScopeText = "- Khảo sát hiện trạng\r\n\r\n- Tư vấn thiết lập hồ sơ nhân viên"
        }, []);
        Assert.Null(error);
        Assert.Equal(("Tư vấn thiết lập hồ sơ nhân sự ban đầu", 10_000_000m, "Phí dịch vụ"), (fixedService!.Name, fixedService.FixedFee, fixedService.FeeLabel));
        Assert.Null(fixedService.PricingJson);
        Assert.Equal(["Khảo sát hiện trạng", "Tư vấn thiết lập hồ sơ nhân viên"], S.ServicePricingPolicy.Lines(fixedService.ScopeText));

        Assert.Equal("Nhập phí trọn gói.", S.ServicePricingPolicy.Check(Guid.NewGuid(), new S.ServiceInput { CategoryId = Group, Name = "Gói", PricingMode = "FIXED" }, []).Error);
        Assert.Equal("Số tiền không hợp lệ.", S.ServicePricingPolicy.Check(Guid.NewGuid(), new S.ServiceInput { CategoryId = Group, Name = "Gói", PricingMode = "FIXED", FixedFee = "mười triệu" }, []).Error);
        var (quoted, quoteError) = S.ServicePricingPolicy.Check(Guid.NewGuid(), new S.ServiceInput { CategoryId = Group, Name = "Báo tăng/giảm BHXH", PricingMode = "QUOTE", PricingJson = "{bad" }, []);
        Assert.Null(quoteError);
        Assert.Equal(("QUOTE", null, null), (quoted!.PricingMode, quoted.FixedFee, quoted.PricingJson));
        Assert.Equal("Chọn nhóm dịch vụ.", S.ServicePricingPolicy.Check(Guid.NewGuid(), new S.ServiceInput { Name = "X", PricingMode = "QUOTE" }, []).Error);
        Assert.Equal("Nhập tên dịch vụ.", S.ServicePricingPolicy.Check(Guid.NewGuid(), new S.ServiceInput { CategoryId = Group, Name = " ", PricingMode = "QUOTE" }, []).Error);
        Assert.Equal("10.000.000", S.ServicePricingPolicy.Money(10_000_000m));
    }

    [Fact]
    public void MeasuresGetShortCodesThatWorkTypesPointTo()
    {
        Assert.Equal("SLCT", S.ServicePricingPolicy.AutoCode("Số lượng chứng từ"));
        Assert.Equal("DTT", S.ServicePricingPolicy.AutoCode("Doanh thu tháng"));
        Assert.Equal("QMNSDX", S.ServicePricingPolicy.AutoCode("Quy mô nhân sự được xử lý trong tháng"));      // at most 6
        Assert.Equal("SLBHS", S.ServicePricingPolicy.AutoCode("Số lượng bộ hồ sơ"));
        Assert.Equal("SL_CT2", S.ServicePricingPolicy.CleanCode(" sl_ct-2 "));
        Assert.Null(S.ServicePricingPolicy.CleanCode(" - "));

        // Empty codes come from the names; a repeated automatic code gets a number; a repeated typed code is refused.
        var (p, error) = S.ServicePricingPolicy.Validate("""{"metrics":[{"key":"a","label":"Doanh thu tháng"},{"key":"b","label":"Đơn thuần tháng","code":""}],"tiers":[{"ranges":{"a":{"max":1}},"fee":1}]}""", []);
        Assert.Null(error);
        Assert.Equal(["DTT", "DTT2"], p!.Metrics.Select(m => m.Code));
        Assert.Contains("bị trùng", S.ServicePricingPolicy.Validate("""{"metrics":[{"key":"a","label":"A","code":"X1"},{"key":"b","label":"B","code":"x1"}],"tiers":[{"ranges":{"a":{"max":1}},"fee":1}]}""", []).Error);
        Assert.Equal("SLCT", S.ServicePricingPolicy.Read("""{"metrics":[{"key":"m1","label":"Số lượng chứng từ"}]}""").Metrics[0].Code);   // saved before codes

        // The same measure in two services is offered once, with both services.
        var options = S.ServicePricingPolicy.Options([
            (new S.ServiceMetric { Code = "SLCT", Label = "Số lượng chứng từ", Unit = "chứng từ" }, "Kế toán thuế"),
            (new S.ServiceMetric { Code = "SLCT", Label = "Số lượng chứng từ" }, "Kế toán nội bộ"),
            (new S.ServiceMetric { Code = "QMNS", Label = "Quy mô nhân sự", Unit = "nhân sự" }, "Nhân sự")]);
        Assert.Equal(2, options.Count);
        Assert.Equal(("SLCT", "chứng từ"), (options[1].Code, options[1].Unit));
        Assert.Equal(["Kế toán thuế", "Kế toán nội bộ"], options[1].Services);

        Assert.Contains("Unit=@unit,MetricCode=@metric", S.WorkCatalogSql.Update);
        Assert.Contains("c.Unit,c.MetricCode", S.WorkCatalogSql.List);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "TAPortal.sln"))) root = root.Parent!;
        var migration = File.ReadAllText(Path.Combine(root.FullName, "database", "migrations", "063-work-catalog-unit-metric.sql"));
        Assert.Contains("ALTER TABLE dbo.WorkTaskCatalog ADD MetricCode varchar(20) NULL", migration);
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE|DROP)", migration);
        Assert.True(migration.All(ch => ch < 128));
        Assert.True(File.Exists(Path.Combine(root.FullName, "database", "scripts", "114-rollback-work-catalog-unit-metric.sql")));
    }

    [Fact]
    public void Migration061AddsTheCatalogueAndTheCustomerIndustry()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "TAPortal.sln"))) root = root.Parent!;
        var migration = File.ReadAllText(Path.Combine(root.FullName, "database", "migrations", "061-service-catalog.sql"));
        var verify = File.ReadAllText(Path.Combine(root.FullName, "database", "scripts", "109-verify-service-catalog.sql"));
        var rollback = File.ReadAllText(Path.Combine(root.FullName, "database", "scripts", "110-rollback-service-catalog.sql"));
        Assert.All(new[] { migration, rollback }, sql => Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql));
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE|DROP)\b", migration);                                    // new tables and a nullable column only
        Assert.Contains("ALTER TABLE dbo.Customers ADD IndustryId uniqueidentifier NULL", migration);
        Assert.Contains("CHECK (PricingMode IN ('TIER','FIXED','QUOTE'))", migration);
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        Assert.Contains("A0000061-0000-4000-8000-000000000061", rollback);
        Assert.True(migration.All(ch => ch < 128) && verify.All(ch => ch < 128) && rollback.All(ch => ch < 128), "SQL text must be ASCII (sqlcmd code page)");
        Assert.Contains("IndustryId", S.CustomerAdminSql.Update);
        Assert.Contains("FROM dbo.Industries WHERE Id=@industry AND IsDeleted=0", S.CustomerAdminSql.Insert);   // only an industry of the catalogue
    }
}
