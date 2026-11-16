using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Security;

namespace TAPortal.Web.Services;

// Hợp đồng > Danh mục dịch vụ (operator 2026-11-16, migration 061; first step of the contract rework, following the T.A
// quote "Báo giá phí dịch vụ"). A service belongs to a group (Kế toán thuế tháng, Kế toán nội bộ, Nhân sự, Pháp lý, ...)
// and carries its scope of work, its fee rules / notes and its pricing:
//   TIER  - "hạn mức": 1 to 3 measures (label + unit, e.g. "Quy mô nhân sự được xử lý trong tháng" / "nhân sự"); each tier
//           gives a numeric range per measure and the fee, or one fee per industry (Thương mại - dịch vụ, Sản xuất, ...).
//           Ranges are numbers so a quote can later pick the tier from the customer's figures.
//   FIXED - one package fee ("Phí trọn gói: 10.000.000 đồng/gói").
//   QUOTE - quoted separately ("Phí dịch vụ báo giá riêng").
// Industries are their own catalogue and are set on each customer (dbo.Customers.IndustryId).
public sealed record ServiceCategory(Guid Id, string Name, string? Description, int SortOrder, bool IsActive, int ServiceCount);
public sealed record Industry(Guid Id, string Name, string? Description, int SortOrder, bool IsActive, int CustomerCount);
public sealed record ServiceRow(Guid Id, Guid CategoryId, string Name, string PricingMode, string? FeeUnit, decimal? FixedFee, int TierCount, int ScopeCount, int SortOrder, bool IsActive);
public sealed record ServiceDetail(Guid Id, Guid CategoryId, string Name, string? Description, string PricingMode, string? FeeLabel, string? FeeUnit, decimal? FixedFee,
    string? PricingJson, string? ScopeText, string? RulesText, int SortOrder, bool IsActive);
public sealed record ServiceCatalog(IReadOnlyList<ServiceCategory> Categories, IReadOnlyList<ServiceRow> Services, IReadOnlyList<Industry> Industries);

public sealed class ServiceInput
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string PricingMode { get; set; } = ServicePricingPolicy.Tier;
    public string? FeeLabel { get; set; }
    public string? FeeUnit { get; set; }
    public string? FixedFee { get; set; }
    public string? PricingJson { get; set; }
    public string? ScopeText { get; set; }
    public string? RulesText { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public static ServiceInput From(ServiceDetail d) => new()
    {
        CategoryId = d.CategoryId, Name = d.Name, Description = d.Description, PricingMode = d.PricingMode, FeeLabel = d.FeeLabel, FeeUnit = d.FeeUnit,
        FixedFee = d.FixedFee?.ToString("#,0", ServicePricingPolicy.Vi), PricingJson = d.PricingJson, ScopeText = d.ScopeText, RulesText = d.RulesText,
        SortOrder = d.SortOrder, IsActive = d.IsActive
    };
}

public sealed class CatalogEntryInput
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum ServiceCatalogOutcome { Saved, Invalid, NotFound, Forbidden }
public sealed record ServiceCatalogResult(ServiceCatalogOutcome Outcome, string? Message = null, Guid? Id = null);

// ---- Pricing ("hạn mức") ------------------------------------------------------------------------------------------------
public sealed class ServiceMetric
{
    public string Key { get; set; } = "";
    // Mã chỉ số (operator 2026-11-16): short, e.g. SLCT for "Số lượng chứng từ". The same measure in several services carries
    // the same code; work catalogue entries point at it (dbo.WorkTaskCatalog.MetricCode) so daily quantities can be priced.
    public string? Code { get; set; }
    public string Label { get; set; } = "";
    public string? Unit { get; set; }
}

// A measure as offered to the work catalogue: its code, name, unit and the services using it.
public sealed record ServiceMetricOption(string Code, string Label, string? Unit, IReadOnlyList<string> Services);

public sealed class ServiceRange
{
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
}

public sealed class ServiceTier
{
    public string? Label { get; set; }
    public Dictionary<string, ServiceRange> Ranges { get; set; } = [];
    public decimal? Fee { get; set; }
    public Dictionary<string, decimal?> Fees { get; set; } = [];   // industry id -> fee (ByIndustry)
}

public sealed class ServicePricing
{
    public List<ServiceMetric> Metrics { get; set; } = [];
    public bool ByIndustry { get; set; }
    public List<ServiceTier> Tiers { get; set; } = [];
}

public static class ServicePricingPolicy
{
    public const string Tier = "TIER", Fixed = "FIXED", Quote = "QUOTE";
    public static readonly string[] Modes = [Tier, Fixed, Quote];
    public const int NameMax = 200, CategoryNameMax = 150, DescriptionMax = 1000, LabelMax = 150, UnitMax = 50, FeeLabelMax = 100,
        TextMax = 8000, MetricsMax = 3, TiersMax = 60;
    public const decimal ValueMax = 1_000_000_000_000m;
    public static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static string ModeLabel(string? mode) => mode switch
    {
        Tier => "Theo hạn mức",
        Fixed => "Trọn gói",
        Quote => "Báo giá riêng",
        _ => mode ?? ""
    };

    public static string Money(decimal? value) => value is decimal v ? v.ToString("#,0", Vi) : "—";
    public static string Number(decimal value) => value.ToString(value == decimal.Truncate(value) ? "#,0" : "#,0.##", Vi);

    // "10.000.000", "10,000,000", "10000000 đ" -> 10000000; empty -> null; anything else -> error.
    public static (decimal? Value, string? Error) ParseMoney(string? text)
    {
        var digits = new string((text ?? "").Where(c => !char.IsWhiteSpace(c) && c is not ('.' or ',' or 'đ' or 'Đ')).ToArray());
        if (digits.Length == 0) return (null, null);
        if (!decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v) || v > ValueMax) return (null, "Số tiền không hợp lệ.");
        return (v, null);
    }

    public const int CodeMax = 10, AutoCodeMax = 6;

    public static ServicePricing Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        ServicePricing pricing;
        try { pricing = JsonSerializer.Deserialize<ServicePricing>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
        foreach (var m in pricing.Metrics) m.Code = CleanCode(m.Code) ?? AutoCode(m.Label);   // tables saved before codes existed
        return pricing;
    }

    // "Số lượng chứng từ" -> "SLCT": first letter of each word, without accents (đ -> D), at most 6 characters.
    public static string AutoCode(string? label)
    {
        var plain = new string((label ?? "").Replace('đ', 'd').Replace('Đ', 'D').Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var code = new string(plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.FirstOrDefault(char.IsLetterOrDigit)).Where(c => c != default).Select(char.ToUpperInvariant).Take(AutoCodeMax).ToArray());
        return code.Length == 0 ? "CS" : code;
    }

    // Letters A-Z, digits and "_" only, upper case, at most 10; null when empty.
    public static string? CleanCode(string? code)
    {
        var plain = new string((code ?? "").Replace('đ', 'd').Replace('Đ', 'D').Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
        var clean = new string(plain.Where(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_').ToArray());
        return clean.Length == 0 ? null : clean.Length > CodeMax ? clean[..CodeMax] : clean;
    }

    public static string Write(ServicePricing pricing) => JsonSerializer.Serialize(pricing, Json);

    // Checks and normalises the pricing table sent by the editor (wwwroot/js/ta-services.js). Measures get the keys m1..m3 in
    // order; empty tiers are dropped; industry fees keep only active industries of the catalogue.
    public static (ServicePricing? Pricing, string? Error) Validate(string? json, IReadOnlyCollection<Guid> industries)
    {
        ServicePricing raw;
        try { raw = string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<ServicePricing>(json, Json) ?? new(); }
        catch (JsonException) { return (null, "Bảng hạn mức không đọc được, vui lòng tải lại trang."); }

        var metrics = raw.Metrics.Where(m => !string.IsNullOrWhiteSpace(m.Label)).ToList();
        if (metrics.Count == 0) return (null, "Nhập ít nhất một chỉ số hạn mức (ví dụ: Số lượng chứng từ, Quy mô nhân sự).");
        if (metrics.Count > MetricsMax) return (null, $"Tối đa {MetricsMax} chỉ số hạn mức.");
        var keys = new Dictionary<string, string>();
        var clean = new ServicePricing { ByIndustry = raw.ByIndustry };
        for (var i = 0; i < metrics.Count; i++)
        {
            var m = metrics[i];
            var label = m.Label.Trim();
            if (label.Length > LabelMax) return (null, $"Tên chỉ số tối đa {LabelMax} ký tự.");
            var unit = string.IsNullOrWhiteSpace(m.Unit) ? null : m.Unit.Trim();
            if (unit?.Length > UnitMax) return (null, $"Đơn vị tính tối đa {UnitMax} ký tự.");
            var key = "m" + (i + 1);
            if (!string.IsNullOrEmpty(m.Key)) keys[m.Key] = key;
            // Codes may repeat across services (same measure), never inside one service.
            var code = CleanCode(m.Code) ?? AutoCode(label);
            if (clean.Metrics.Any(x => x.Code == code))
            {
                if (CleanCode(m.Code) is not null) return (null, $"Mã chỉ số \"{code}\" bị trùng trong dịch vụ này.");
                var n = 2;
                while (clean.Metrics.Any(x => x.Code == code + n)) n++;
                code += n;
            }
            clean.Metrics.Add(new() { Key = key, Code = code, Label = label, Unit = unit });
        }
        if (raw.ByIndustry && industries.Count == 0) return (null, "Chưa có ngành nghề nào đang dùng; thêm ở tab Ngành nghề trước khi chia phí theo ngành.");

        foreach (var t in raw.Tiers)
        {
            var tier = new ServiceTier { Label = string.IsNullOrWhiteSpace(t.Label) ? null : t.Label.Trim() };
            if (tier.Label?.Length > LabelMax) return (null, $"Mô tả bậc tối đa {LabelMax} ký tự.");
            foreach (var (oldKey, range) in t.Ranges)
            {
                if (!keys.TryGetValue(oldKey, out var key) || range is null || (range.Min is null && range.Max is null)) continue;
                if (range.Min < 0 || range.Max < 0 || range.Min > ValueMax || range.Max > ValueMax) return (null, "Khoảng giá trị phải là số không âm.");
                if (range.Min is decimal lo && range.Max is decimal hi && lo > hi) return (null, $"Bậc \"{TierLabel(clean, tier)}\": giá trị \"Từ\" lớn hơn \"Đến\".");
                tier.Ranges[key] = new() { Min = range.Min, Max = range.Max };
            }
            if (raw.ByIndustry)
            {
                foreach (var (industry, fee) in t.Fees)
                    if (Guid.TryParse(industry, out var id) && industries.Contains(id) && fee is decimal f)
                    {
                        if (f < 0 || f > ValueMax) return (null, "Phí dịch vụ không hợp lệ.");
                        tier.Fees[id.ToString()] = decimal.Round(f, 0);
                    }
            }
            else if (t.Fee is decimal fee)
            {
                if (fee < 0 || fee > ValueMax) return (null, "Phí dịch vụ không hợp lệ.");
                tier.Fee = decimal.Round(fee, 0);
            }
            var empty = tier.Ranges.Count == 0 && tier.Label is null && tier.Fee is null && tier.Fees.Count == 0;
            if (empty) continue;
            if (tier.Ranges.Count == 0 && tier.Label is null) return (null, "Mỗi bậc cần khoảng giá trị hoặc mô tả.");
            if (tier.Fee is null && tier.Fees.Count == 0) return (null, $"Bậc \"{TierLabel(clean, tier)}\" chưa có phí dịch vụ.");
            clean.Tiers.Add(tier);
        }
        if (clean.Tiers.Count == 0) return (null, "Thêm ít nhất một bậc hạn mức có phí dịch vụ.");
        if (clean.Tiers.Count > TiersMax) return (null, $"Tối đa {TiersMax} bậc hạn mức.");
        return (clean, null);
    }

    // The tier as read in a quote: its own description, else "Từ 10 đến 14 nhân sự; ...".
    public static string TierLabel(ServicePricing pricing, ServiceTier tier)
    {
        if (tier.Label is { Length: > 0 } label) return label;
        var parts = pricing.Metrics.Where(m => tier.Ranges.ContainsKey(m.Key)).Select(m =>
        {
            var r = tier.Ranges[m.Key];
            var unit = m.Unit is { Length: > 0 } u ? " " + u : "";
            return (r.Min, r.Max) switch
            {
                (decimal lo, decimal hi) when lo == hi => $"{Number(lo)}{unit}",
                (decimal lo, decimal hi) => $"Từ {Number(lo)} đến {Number(hi)}{unit}",
                (null, decimal hi) => $"Đến {Number(hi)}{unit}",
                (decimal lo, null) => $"Từ {Number(lo)}{unit} trở lên",
                _ => ""
            };
        }).Where(p => p.Length > 0);
        return string.Join("; ", parts) is { Length: > 0 } text ? text : "—";
    }

    // One option per code + name; a code shared by measures of different names is listed once per name.
    public static IReadOnlyList<ServiceMetricOption> Options(IEnumerable<(ServiceMetric Metric, string Service)> metrics) =>
        metrics.Where(x => !string.IsNullOrWhiteSpace(x.Metric.Code))
            .GroupBy(x => (Code: x.Metric.Code!, Label: x.Metric.Label.Trim()), x => x)
            .Select(g => new ServiceMetricOption(g.Key.Code, g.Key.Label, g.Select(x => x.Metric.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                g.Select(x => x.Service).Distinct().ToList()))
            .OrderBy(o => o.Code, StringComparer.Ordinal).ThenBy(o => o.Label, StringComparer.CurrentCulture).ToList();

    public static IReadOnlyList<string> Lines(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.Trim().TrimStart('-', '*', '•').Trim()).Where(l => l.Length > 0).ToList();

    public static string? CleanText(string? text, int max)
    {
        var t = (text ?? "").Replace("\r\n", "\n").Trim();
        return t.Length == 0 ? null : t.Length > max ? t[..max] : t;
    }

    // The whole service input; returns the values to store or the reason in Vietnamese.
    public static (ServiceDetail? Service, string? Error) Check(Guid id, ServiceInput i, IReadOnlyCollection<Guid> industries)
    {
        var name = (i.Name ?? "").Trim();
        if (name.Length == 0) return (null, "Nhập tên dịch vụ.");
        if (name.Length > NameMax) return (null, $"Tên dịch vụ tối đa {NameMax} ký tự.");
        if (i.CategoryId == Guid.Empty) return (null, "Chọn nhóm dịch vụ.");
        var mode = Modes.Contains(i.PricingMode) ? i.PricingMode : Tier;
        var feeLabel = CleanText(i.FeeLabel, FeeLabelMax) ?? "Phí dịch vụ";
        var feeUnit = CleanText(i.FeeUnit, UnitMax);
        decimal? fixedFee = null;
        string? pricingJson = null;
        if (mode == Fixed)
        {
            var (fee, error) = ParseMoney(i.FixedFee);
            if (error is not null) return (null, error);
            if (fee is null) return (null, "Nhập phí trọn gói.");
            fixedFee = fee;
        }
        else if (mode == Tier)
        {
            var (pricing, error) = Validate(i.PricingJson, industries);
            if (pricing is null) return (null, error);
            pricingJson = Write(pricing);
        }
        return (new ServiceDetail(id, i.CategoryId, name, CleanText(i.Description, DescriptionMax), mode, feeLabel, feeUnit, fixedFee, pricingJson,
            CleanText(i.ScopeText, TextMax), CleanText(i.RulesText, TextMax), Math.Clamp(i.SortOrder, 0, 100000), i.IsActive), null);
    }
}

public static class ServiceCatalogSql
{
    public const string Categories = """
SELECT c.Id,c.Name,c.Description,c.SortOrder,c.IsActive,(SELECT COUNT(1) FROM dbo.Services s WHERE s.CategoryId=c.Id AND s.IsDeleted=0)
FROM dbo.ServiceCategories c WHERE c.IsDeleted=0 ORDER BY c.SortOrder,c.Name;
""";
    public const string Industries = """
SELECT i.Id,i.Name,i.Description,i.SortOrder,i.IsActive,(SELECT COUNT(1) FROM dbo.Customers cu WHERE cu.IndustryId=i.Id AND cu.IsDeleted=0)
FROM dbo.Industries i WHERE i.IsDeleted=0 ORDER BY i.SortOrder,i.Name;
""";
    public const string Services = """
SELECT Id,CategoryId,Name,PricingMode,FeeUnit,FixedFee,PricingJson,ScopeText,SortOrder,IsActive
FROM dbo.Services WHERE IsDeleted=0 ORDER BY SortOrder,Name;
""";
    public const string Service = """
SELECT Id,CategoryId,Name,Description,PricingMode,FeeLabel,FeeUnit,FixedFee,PricingJson,ScopeText,RulesText,SortOrder,IsActive
FROM dbo.Services WHERE Id=@id AND IsDeleted=0;
""";
    public const string Metrics = "SELECT Name,PricingJson FROM dbo.Services WHERE IsDeleted=0 AND IsActive=1 AND PricingMode='TIER' AND PricingJson IS NOT NULL ORDER BY SortOrder,Name;";
    public const string ActiveIndustryIds = "SELECT Id FROM dbo.Industries WHERE IsDeleted=0 AND IsActive=1;";
    public const string CategoryExists = "SELECT COUNT(1) FROM dbo.ServiceCategories WHERE Id=@id AND IsDeleted=0;";
    public const string ServiceTaken = "SELECT COUNT(1) FROM dbo.Services WITH (UPDLOCK, HOLDLOCK) WHERE CategoryId=@category AND Name=@name AND IsDeleted=0 AND (@id IS NULL OR Id<>@id);";
    public const string InsertService = """
INSERT dbo.Services(Id,CategoryId,Name,Description,PricingMode,FeeLabel,FeeUnit,FixedFee,PricingJson,ScopeText,RulesText,SortOrder,IsActive,CreatedBy)
VALUES(@id,@category,@name,@desc,@mode,@feeLabel,@feeUnit,@fixed,@pricing,@scope,@rules,@sort,@active,@by);
""";
    public const string UpdateService = """
UPDATE dbo.Services SET CategoryId=@category,Name=@name,Description=@desc,PricingMode=@mode,FeeLabel=@feeLabel,FeeUnit=@feeUnit,FixedFee=@fixed,
 PricingJson=@pricing,ScopeText=@scope,RulesText=@rules,SortOrder=@sort,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND IsDeleted=0;
""";
    public const string DeleteService = "UPDATE dbo.Services SET IsDeleted=1,IsActive=0,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";

    // Groups and industries share one shape; {0} is the table.
    public const string EntryTaken = "SELECT COUNT(1) FROM dbo.{0} WITH (UPDLOCK, HOLDLOCK) WHERE Name=@name AND IsDeleted=0 AND (@id IS NULL OR Id<>@id);";
    public const string InsertEntry = "INSERT dbo.{0}(Id,Name,Description,SortOrder,IsActive,CreatedBy) VALUES(@id,@name,@desc,@sort,@active,@by);";
    public const string UpdateEntry = "UPDATE dbo.{0} SET Name=@name,Description=@desc,SortOrder=@sort,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
    public const string DeleteEntry = "UPDATE dbo.{0} SET IsDeleted=1,IsActive=0,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
    public const string CategoryInUse = "SELECT COUNT(1) FROM dbo.Services WHERE CategoryId=@id AND IsDeleted=0;";
    public const string IndustryInUse = "SELECT COUNT(1) FROM dbo.Customers WHERE IndustryId=@id AND IsDeleted=0;";
}

public enum CatalogKind { Category, Industry }

public interface IServiceCatalogStore
{
    Task<ServiceCatalog> ListAsync();
    Task<ServiceDetail?> GetServiceAsync(Guid id);
    // Active industries, for the customer form and the industry fee columns. Empty when migration 061 is not applied.
    Task<IReadOnlyList<Industry>> IndustriesAsync();
    // Every measure of the active tier tables, one per code + name (empty when unavailable).
    Task<IReadOnlyList<ServiceMetricOption>> MetricsAsync();
    Task<ServiceCatalogResult> SaveServiceAsync(Guid actor, Guid? id, ServiceInput input);
    Task<ServiceCatalogResult> DeleteServiceAsync(Guid actor, Guid id);
    Task<ServiceCatalogResult> SaveEntryAsync(Guid actor, CatalogKind kind, Guid? id, CatalogEntryInput input);
    Task<ServiceCatalogResult> DeleteEntryAsync(Guid actor, CatalogKind kind, Guid id);
}

public sealed class SqlServiceCatalogStore(IConfiguration configuration, AdminAuditOptions audit, ILogger<SqlServiceCatalogStore> logger) : IServiceCatalogStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<ServiceCatalog> ListAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var categories = new List<ServiceCategory>();
        await using (var cmd = new SqlCommand(ServiceCatalogSql.Categories, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) categories.Add(new(r.GetGuid(0), r.GetString(1), S(r, 2), r.GetInt32(3), r.GetBoolean(4), r.GetInt32(5)));
        var industries = await ReadIndustries(cn, null, activeOnly: false);
        var services = new List<ServiceRow>();
        await using (var cmd = new SqlCommand(ServiceCatalogSql.Services, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                services.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), S(r, 4), r.IsDBNull(5) ? null : r.GetDecimal(5),
                    ServicePricingPolicy.Read(S(r, 6)).Tiers.Count, ServicePricingPolicy.Lines(S(r, 7)).Count, r.GetInt32(8), r.GetBoolean(9)));
        return new(categories, services, industries);
    }

    public async Task<ServiceDetail?> GetServiceAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(ServiceCatalogSql.Service, cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), S(r, 3), r.GetString(4), S(r, 5), S(r, 6), r.IsDBNull(7) ? null : r.GetDecimal(7),
            S(r, 8), S(r, 9), S(r, 10), r.GetInt32(11), r.GetBoolean(12));
    }

    public async Task<IReadOnlyList<Industry>> IndustriesAsync()
    {
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync();
            return await ReadIndustries(cn, null, activeOnly: true);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Industries unavailable.");
            return [];
        }
    }

    public async Task<IReadOnlyList<ServiceMetricOption>> MetricsAsync()
    {
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(ServiceCatalogSql.Metrics, cn);
            await using var r = await cmd.ExecuteReaderAsync();
            var all = new List<(ServiceMetric Metric, string Service)>();
            while (await r.ReadAsync())
                all.AddRange(ServicePricingPolicy.Read(S(r, 1)).Metrics.Select(m => (m, r.GetString(0))));
            return ServicePricingPolicy.Options(all);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Service metrics unavailable.");
            return [];
        }
    }

    public async Task<ServiceCatalogResult> SaveServiceAsync(Guid actor, Guid? id, ServiceInput input)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.ServiceCatalogManage)) return new(ServiceCatalogOutcome.Forbidden);
        var industries = (await ReadIndustries(cn, tx, activeOnly: true)).Select(x => x.Id).ToHashSet();
        var (service, error) = ServicePricingPolicy.Check(id ?? Guid.NewGuid(), input, industries);
        if (service is null) return new(ServiceCatalogOutcome.Invalid, error);
        if (Convert.ToInt32(await Scalar(cn, tx, ServiceCatalogSql.CategoryExists, ("@id", service.CategoryId))) == 0) return new(ServiceCatalogOutcome.Invalid, "Nhóm dịch vụ không còn trong danh mục.");
        if (Convert.ToInt32(await Scalar(cn, tx, ServiceCatalogSql.ServiceTaken, ("@category", service.CategoryId), ("@name", service.Name), ("@id", (object?)id ?? DBNull.Value))) > 0)
            return new(ServiceCatalogOutcome.Invalid, $"Nhóm này đã có dịch vụ \"{service.Name}\".");
        var args = new (string, object)[]
        {
            ("@id", service.Id), ("@category", service.CategoryId), ("@name", service.Name), ("@desc", Db(service.Description)), ("@mode", service.PricingMode),
            ("@feeLabel", Db(service.FeeLabel)), ("@feeUnit", Db(service.FeeUnit)), ("@fixed", (object?)service.FixedFee ?? DBNull.Value), ("@pricing", Db(service.PricingJson)),
            ("@scope", Db(service.ScopeText)), ("@rules", Db(service.RulesText)), ("@sort", service.SortOrder), ("@active", service.IsActive), ("@by", actor)
        };
        if (await Exec(cn, tx, id is null ? ServiceCatalogSql.InsertService : ServiceCatalogSql.UpdateService, args) != 1) return new(ServiceCatalogOutcome.NotFound);
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, id is null ? "SERVICE_CREATED" : "SERVICE_UPDATED", "dbo.Services", service.Id.ToString(), "{}",
            AuditSql.Json(("name", service.Name), ("categoryId", service.CategoryId), ("pricingMode", service.PricingMode), ("fixedFee", service.FixedFee),
                ("tiers", ServicePricingPolicy.Read(service.PricingJson).Tiers.Count), ("active", service.IsActive)), null);
        await tx.CommitAsync();
        return new(ServiceCatalogOutcome.Saved, null, service.Id);
    }

    public async Task<ServiceCatalogResult> DeleteServiceAsync(Guid actor, Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.ServiceCatalogManage)) return new(ServiceCatalogOutcome.Forbidden);
        if (await Exec(cn, tx, ServiceCatalogSql.DeleteService, ("@id", id), ("@by", actor)) != 1) return new(ServiceCatalogOutcome.NotFound);
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, "SERVICE_DELETED", "dbo.Services", id.ToString(), "{}", "{}", null);
        await tx.CommitAsync();
        return new(ServiceCatalogOutcome.Saved);
    }

    public async Task<ServiceCatalogResult> SaveEntryAsync(Guid actor, CatalogKind kind, Guid? id, CatalogEntryInput input)
    {
        var what = kind == CatalogKind.Category ? "nhóm dịch vụ" : "ngành nghề";
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) return new(ServiceCatalogOutcome.Invalid, $"Nhập tên {what}.");
        if (name.Length > ServicePricingPolicy.CategoryNameMax) return new(ServiceCatalogOutcome.Invalid, $"Tên {what} tối đa {ServicePricingPolicy.CategoryNameMax} ký tự.");
        var table = Table(kind);
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.ServiceCatalogManage)) return new(ServiceCatalogOutcome.Forbidden);
        if (Convert.ToInt32(await Scalar(cn, tx, string.Format(ServiceCatalogSql.EntryTaken, table), ("@name", name), ("@id", (object?)id ?? DBNull.Value))) > 0)
            return new(ServiceCatalogOutcome.Invalid, $"Đã có {what} \"{name}\".");
        var entry = id ?? Guid.NewGuid();
        var args = new (string, object)[] { ("@id", entry), ("@name", name), ("@desc", Db(ServicePricingPolicy.CleanText(input.Description, 500))),
            ("@sort", Math.Clamp(input.SortOrder, 0, 100000)), ("@active", input.IsActive), ("@by", actor) };
        if (await Exec(cn, tx, string.Format(id is null ? ServiceCatalogSql.InsertEntry : ServiceCatalogSql.UpdateEntry, table), args) != 1) return new(ServiceCatalogOutcome.NotFound);
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, (kind == CatalogKind.Category ? "SERVICE_CATEGORY_" : "INDUSTRY_") + (id is null ? "CREATED" : "UPDATED"),
            "dbo." + table, entry.ToString(), "{}", AuditSql.Json(("name", name), ("sortOrder", input.SortOrder), ("active", input.IsActive)), null);
        await tx.CommitAsync();
        return new(ServiceCatalogOutcome.Saved, null, entry);
    }

    public async Task<ServiceCatalogResult> DeleteEntryAsync(Guid actor, CatalogKind kind, Guid id)
    {
        var table = Table(kind);
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.ServiceCatalogManage)) return new(ServiceCatalogOutcome.Forbidden);
        var used = Convert.ToInt32(await Scalar(cn, tx, kind == CatalogKind.Category ? ServiceCatalogSql.CategoryInUse : ServiceCatalogSql.IndustryInUse, ("@id", id)));
        if (used > 0)
            return new(ServiceCatalogOutcome.Invalid, kind == CatalogKind.Category
                ? $"Nhóm còn {used} dịch vụ; chuyển hoặc xóa các dịch vụ trước, hoặc tắt \"Đang dùng\"."
                : $"Còn {used} khách hàng thuộc ngành này; đổi ngành của họ trước, hoặc tắt \"Đang dùng\".");
        if (await Exec(cn, tx, string.Format(ServiceCatalogSql.DeleteEntry, table), ("@id", id), ("@by", actor)) != 1) return new(ServiceCatalogOutcome.NotFound);
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, (kind == CatalogKind.Category ? "SERVICE_CATEGORY" : "INDUSTRY") + "_DELETED",
            "dbo." + table, id.ToString(), "{}", "{}", null);
        await tx.CommitAsync();
        return new(ServiceCatalogOutcome.Saved);
    }

    private static string Table(CatalogKind kind) => kind == CatalogKind.Category ? "ServiceCategories" : "Industries";

    private static async Task<List<Industry>> ReadIndustries(SqlConnection cn, SqlTransaction? tx, bool activeOnly)
    {
        var list = new List<Industry>();
        await using var cmd = new SqlCommand(ServiceCatalogSql.Industries, cn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var item = new Industry(r.GetGuid(0), r.GetString(1), S(r, 2), r.GetInt32(3), r.GetBoolean(4), r.GetInt32(5));
            if (!activeOnly || item.IsActive) list.Add(item);
        }
        return list;
    }

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static object Db(string? v) => (object?)v ?? DBNull.Value;

    private static SqlCommand Command(SqlConnection cn, SqlTransaction? tx, string sql, (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args)
        {
            if (value is string s) cmd.Parameters.Add(name, SqlDbType.NVarChar, s.Length > 4000 ? -1 : Math.Max(s.Length, 1)).Value = s;
            else if (value is decimal d) cmd.Parameters.Add(name, SqlDbType.Decimal).Value = d;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Command(cn, tx, sql, args);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<int> Exec(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Command(cn, tx, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }
}
