using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TAPortal.Shared;

// Service packages (migration 018): T.A sells invoice packages, transaction-reconciliation packages and combos of both.
// A purchase snapshots the package; once confirmed paid it becomes a balance lot (remaining quantities, optional expiry).
// A customer's available balance is the sum of the remaining quantities of paid lots that have not expired. Prices
// include VAT. Used by Admin (catalogue, purchases, confirmation) and by the Customer Portal (buy page, own history).

public static class ServicePackageTypes
{
    public const string Invoice = "INVOICE";
    public const string Transaction = "TRANSACTION";
    public const string Combo = "COMBO";
    public static readonly IReadOnlyList<string> All = [Invoice, Transaction, Combo];

    public static string Label(string? type) => type switch
    {
        Invoice => "Gói hóa đơn",
        Transaction => "Gói tra soát giao dịch",
        Combo => "Gói combo",
        _ => type ?? "—"
    };

    public static bool HasInvoices(string type) => type is Invoice or Combo;
    public static bool HasTransactions(string type) => type is Transaction or Combo;
}

public static class ServicePurchaseStatus
{
    public const string Pending = "PENDING";
    public const string Paid = "PAID";
    public const string Cancelled = "CANCELLED";
    public static readonly IReadOnlyList<string> All = [Pending, Paid, Cancelled];

    public static (string Label, string Tone) Display(string status, bool expired) => status switch
    {
        Pending => ("Chờ thanh toán", "warning"),
        Paid when expired => ("Hết hạn", "muted"),
        Paid => ("Đã kích hoạt", "success"),
        Cancelled => ("Đã hủy", "danger"),
        _ => (status, "muted")
    };
}

public static class ServicePurchaseVia
{
    public const string Customer = "CUSTOMER";
    public const string Admin = "ADMIN";
}

public sealed record ServicePackage(Guid Id, string Code, string Name, string PackageType, int InvoiceQuota, int TransactionQuota,
    decimal Price, int? ValidityDays, string? Description, bool IsActive, int SortOrder, DateTime CreatedAt);

public sealed record ServicePurchase(Guid Id, string PurchaseNo, Guid CustomerId, string CustomerCode, string CustomerName, Guid PackageId,
    string PackageCode, string PackageName, string PackageType, int InvoiceQuota, int TransactionQuota, decimal Amount, int? ValidityDays,
    string Status, string RequestedVia, string? RequestedByName, DateTime CreatedAt, decimal? PaidAmount, DateTime? PaidAt, string? PaymentMethod,
    string? PaymentReference, string? ConfirmedByName, DateTime? ExpiresAt, int InvoicesRemaining, int TransactionsRemaining,
    DateTime? CancelledAt, string? CancelledByName, string? CancelReason)
{
    public bool IsExpired(DateTime nowUtc) => Status == ServicePurchaseStatus.Paid && ExpiresAt is DateTime e && e <= nowUtc;
    public bool CountsTowardsBalance(DateTime nowUtc) => Status == ServicePurchaseStatus.Paid && !IsExpired(nowUtc);
}

public sealed record ServiceBalance(int Transactions, int Invoices, DateTime? NextExpiry)
{
    public static readonly ServiceBalance Empty = new(0, 0, null);
}

/// <summary>Form for creating or editing a package (Admin).</summary>
public sealed class ServicePackageInput
{
    [Required(ErrorMessage = "Vui lòng nhập mã gói.")]
    [RegularExpression("^[A-Za-z0-9_-]{2,30}$", ErrorMessage = "Mã gói 2–30 ký tự: chữ, số, gạch ngang hoặc gạch dưới.")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập tên gói.")]
    [StringLength(200, ErrorMessage = "Tên gói tối đa 200 ký tự.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng chọn loại gói.")]
    public string PackageType { get; set; } = ServicePackageTypes.Invoice;

    [Range(0, 100_000_000, ErrorMessage = "Số lượng hóa đơn không hợp lệ.")]
    public int InvoiceQuota { get; set; }

    [Range(0, 100_000_000, ErrorMessage = "Số lượng giao dịch không hợp lệ.")]
    public int TransactionQuota { get; set; }

    [Range(typeof(decimal), "0", "100000000000", ErrorMessage = "Giá gói không hợp lệ.")]
    public decimal Price { get; set; }

    [Range(1, 3650, ErrorMessage = "Thời hạn từ 1 đến 3650 ngày, hoặc để trống nếu không hết hạn.")]
    public int? ValidityDays { get; set; }

    [StringLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, 9999, ErrorMessage = "Thứ tự hiển thị từ 0 đến 9999.")]
    public int SortOrder { get; set; }

    public static ServicePackageInput From(ServicePackage p) => new()
    {
        Code = p.Code, Name = p.Name, PackageType = p.PackageType, InvoiceQuota = p.InvoiceQuota, TransactionQuota = p.TransactionQuota,
        Price = p.Price, ValidityDays = p.ValidityDays, Description = p.Description, IsActive = p.IsActive, SortOrder = p.SortOrder
    };
}

public static class ServicePackageRules
{
    /// <summary>Cross-field rules (the type decides which quantities apply); returns (field, message) pairs.</summary>
    public static IEnumerable<(string Field, string Message)> Validate(ServicePackageInput input)
    {
        if (!ServicePackageTypes.All.Contains(input.PackageType)) { yield return (nameof(input.PackageType), "Loại gói không hợp lệ."); yield break; }
        if (ServicePackageTypes.HasInvoices(input.PackageType) && input.InvoiceQuota <= 0)
            yield return (nameof(input.InvoiceQuota), "Vui lòng nhập số lượng hóa đơn lớn hơn 0.");
        if (ServicePackageTypes.HasTransactions(input.PackageType) && input.TransactionQuota <= 0)
            yield return (nameof(input.TransactionQuota), "Vui lòng nhập số lượng giao dịch lớn hơn 0.");
    }

    /// <summary>Trims text and clears the quantity that the package type does not include.</summary>
    public static void Normalize(ServicePackageInput input)
    {
        input.Code = input.Code.Trim().ToUpperInvariant();
        input.Name = input.Name.Trim();
        input.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (!ServicePackageTypes.HasInvoices(input.PackageType)) input.InvoiceQuota = 0;
        if (!ServicePackageTypes.HasTransactions(input.PackageType)) input.TransactionQuota = 0;
    }

    public static DateTime? ExpiresAt(DateTime paidAtUtc, int? validityDays) => validityDays is int d ? paidAtUtc.AddDays(d) : null;

    public static ServiceBalance Balance(IEnumerable<ServicePurchase> purchases, DateTime nowUtc)
    {
        var lots = purchases.Where(p => p.CountsTowardsBalance(nowUtc)).ToList();
        var expiring = lots.Where(p => p.ExpiresAt is not null && (p.InvoicesRemaining > 0 || p.TransactionsRemaining > 0)).Select(p => p.ExpiresAt).Min();
        return new(lots.Sum(p => p.TransactionsRemaining), lots.Sum(p => p.InvoicesRemaining), expiring);
    }

    /// <summary>Purchase number shown to customers and used as the transfer content later: DV + yyMMdd + 6 hex.</summary>
    public static string NewPurchaseNo(DateTime nowUtc) =>
        "DV" + nowUtc.AddHours(7).ToString("yyMMdd", CultureInfo.InvariantCulture) + Convert.ToHexString(RandomNumberGenerator.GetBytes(3));

    public static string Quantities(int invoices, int transactions) => (invoices, transactions) switch
    {
        ( > 0, > 0) => $"{Number(invoices)} hóa đơn + {Number(transactions)} giao dịch",
        ( > 0, _) => $"{Number(invoices)} hóa đơn",
        (_, > 0) => $"{Number(transactions)} giao dịch",
        _ => "—"
    };

    public static string Validity(int? days) => days is int d ? $"{d.ToString("N0", Vi)} ngày" : "Không hết hạn";

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    public static string Number(int value) => value.ToString("N0", Vi);
    public static string Money(decimal? value) => value is decimal v ? v.ToString("N0", Vi) + "đ" : "—";
    public static string Local(DateTime? utc, string format = "dd/MM/yyyy HH:mm") => utc is DateTime d ? d.AddHours(7).ToString(format, CultureInfo.InvariantCulture) : "—";
}

public enum ServicePackageSaveOutcome { Saved, NotFound, CodeTaken, AuditUnavailable }
public enum ServicePurchaseOutcome { Done, NotFound, NotPending, PackageUnavailable, AuditUnavailable }

public interface IServicePackageStore
{
    Task<IReadOnlyList<ServicePackage>> PackagesAsync(bool activeOnly);
    Task<ServicePackage?> PackageAsync(Guid id);
    Task<(ServicePackageSaveOutcome Outcome, Guid? Id)> SavePackageAsync(Guid? id, ServicePackageInput input, Guid actor);
    Task<ServicePackageSaveOutcome> SetPackageActiveAsync(Guid id, bool active, Guid actor);

    /// <summary>Purchases newest first; customerId limits to one customer (always set by the Customer Portal).</summary>
    Task<IReadOnlyList<ServicePurchase>> PurchasesAsync(Guid? customerId, string? status, int take = 500);
    Task<ServicePurchase?> PurchaseAsync(Guid id, Guid? customerId = null);
    Task<ServiceBalance> BalanceAsync(Guid customerId);

    /// <summary>Creates a PENDING purchase of an active package with a snapshot of it; null id when the package is unavailable.</summary>
    Task<(ServicePurchaseOutcome Outcome, ServicePurchase? Purchase)> RequestAsync(Guid customerId, Guid packageId, string via, Guid actor);

    /// <summary>PENDING → PAID: records the payment, starts the lot (full quantities) and its expiry.</summary>
    Task<ServicePurchaseOutcome> ConfirmPaidAsync(Guid id, decimal paidAmount, string method, string? reference, Guid actor);

    /// <summary>PENDING → CANCELLED; customerId (Customer Portal) restricts it to that customer's own purchases.</summary>
    Task<ServicePurchaseOutcome> CancelAsync(Guid id, Guid? customerId, string? reason, Guid actor);
}

public sealed class SqlServicePackageStore(IConfiguration configuration, TimeProvider clock) : IServicePackageStore
{
    private bool RequireAudit => AuditPolicy.RequireDurable(configuration);

    private SqlConnection Connection() =>
        new(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));

    private const string PackageColumns = "Id,Code,Name,PackageType,InvoiceQuota,TransactionQuota,Price,ValidityDays,Description,IsActive,SortOrder,CreatedAt";

    private const string PurchaseSelect = """
SELECT p.Id,p.PurchaseNo,p.CustomerId,c.Code,c.Name,p.PackageId,p.PackageCode,p.PackageName,p.PackageType,p.InvoiceQuota,p.TransactionQuota,p.Amount,p.ValidityDays,
 p.Status,p.RequestedVia,ru.DisplayName,p.CreatedAt,p.PaidAmount,p.PaidAt,p.PaymentMethod,p.PaymentReference,cu.DisplayName,p.ExpiresAt,
 p.InvoicesRemaining,p.TransactionsRemaining,p.CancelledAt,xu.DisplayName,p.CancelReason
FROM dbo.ServicePackagePurchases p
JOIN dbo.Customers c ON c.Id=p.CustomerId
LEFT JOIN dbo.Users ru ON ru.Id=p.RequestedBy
LEFT JOIN dbo.Users cu ON cu.Id=p.ConfirmedBy
LEFT JOIN dbo.Users xu ON xu.Id=p.CancelledBy
""";

    public async Task<IReadOnlyList<ServicePackage>> PackagesAsync(bool activeOnly)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT {PackageColumns} FROM dbo.ServicePackages{(activeOnly ? " WHERE IsActive=1" : "")} ORDER BY SortOrder,Price,Name", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<ServicePackage>();
        while (await r.ReadAsync()) list.Add(ReadPackage(r));
        return list;
    }

    public async Task<ServicePackage?> PackageAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT {PackageColumns} FROM dbo.ServicePackages WHERE Id=@id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadPackage(r) : null;
    }

    public async Task<(ServicePackageSaveOutcome Outcome, Guid? Id)> SavePackageAsync(Guid? id, ServicePackageInput input, Guid actor)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await using (var taken = new SqlCommand("SELECT COUNT(*) FROM dbo.ServicePackages WITH (UPDLOCK,HOLDLOCK) WHERE Code=@code AND (@id IS NULL OR Id<>@id)", cn, tx))
            {
                taken.Parameters.AddWithValue("@code", input.Code);
                taken.Parameters.AddWithValue("@id", (object?)id ?? DBNull.Value);
                if (Convert.ToInt32(await taken.ExecuteScalarAsync()) > 0) { await tx.RollbackAsync(); return (ServicePackageSaveOutcome.CodeTaken, null); }
            }
            var target = id ?? Guid.NewGuid();
            var sql = id is null
                ? "INSERT dbo.ServicePackages(Id,Code,Name,PackageType,InvoiceQuota,TransactionQuota,Price,ValidityDays,Description,IsActive,SortOrder,CreatedBy) VALUES(@id,@code,@name,@type,@inv,@trx,@price,@days,@desc,@active,@sort,@actor)"
                : "UPDATE dbo.ServicePackages SET Code=@code,Name=@name,PackageType=@type,InvoiceQuota=@inv,TransactionQuota=@trx,Price=@price,ValidityDays=@days,Description=@desc,IsActive=@active,SortOrder=@sort,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE Id=@id";
            await using (var save = new SqlCommand(sql, cn, tx))
            {
                save.Parameters.AddWithValue("@id", target);
                save.Parameters.AddWithValue("@code", input.Code);
                save.Parameters.AddWithValue("@name", input.Name);
                save.Parameters.AddWithValue("@type", input.PackageType);
                save.Parameters.AddWithValue("@inv", input.InvoiceQuota);
                save.Parameters.AddWithValue("@trx", input.TransactionQuota);
                save.Parameters.AddWithValue("@price", input.Price);
                save.Parameters.AddWithValue("@days", (object?)input.ValidityDays ?? DBNull.Value);
                save.Parameters.AddWithValue("@desc", (object?)input.Description ?? DBNull.Value);
                save.Parameters.AddWithValue("@active", input.IsActive);
                save.Parameters.AddWithValue("@sort", input.SortOrder);
                save.Parameters.AddWithValue("@actor", actor);
                if (await save.ExecuteNonQueryAsync() == 0) { await tx.RollbackAsync(); return (ServicePackageSaveOutcome.NotFound, null); }
            }
            await AuditSql.WriteOrThrowAsync(cn, tx, RequireAudit, actor, id is null ? "SERVICE_PACKAGE_CREATED" : "SERVICE_PACKAGE_UPDATED", "dbo.ServicePackages", target.ToString(), "{}",
                AuditSql.Json(("code", input.Code), ("type", input.PackageType), ("invoices", input.InvoiceQuota), ("transactions", input.TransactionQuota),
                    ("price", input.Price), ("validityDays", input.ValidityDays), ("active", input.IsActive)), null);
            await tx.CommitAsync();
            return (ServicePackageSaveOutcome.Saved, target);
        }
        catch (AuditUnavailableException) { await tx.RollbackAsync(); return (ServicePackageSaveOutcome.AuditUnavailable, null); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { await tx.RollbackAsync(); return (ServicePackageSaveOutcome.CodeTaken, null); }
    }

    public async Task<ServicePackageSaveOutcome> SetPackageActiveAsync(Guid id, bool active, Guid actor)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await using (var cmd = new SqlCommand("UPDATE dbo.ServicePackages SET IsActive=@a,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE Id=@id", cn, tx))
            {
                cmd.Parameters.AddWithValue("@a", active);
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@id", id);
                if (await cmd.ExecuteNonQueryAsync() == 0) { await tx.RollbackAsync(); return ServicePackageSaveOutcome.NotFound; }
            }
            await AuditSql.WriteOrThrowAsync(cn, tx, RequireAudit, actor, active ? "SERVICE_PACKAGE_ACTIVATED" : "SERVICE_PACKAGE_DEACTIVATED", "dbo.ServicePackages", id.ToString(), "{}", AuditSql.Json(("active", active)), null);
            await tx.CommitAsync();
            return ServicePackageSaveOutcome.Saved;
        }
        catch (AuditUnavailableException) { await tx.RollbackAsync(); return ServicePackageSaveOutcome.AuditUnavailable; }
    }

    public async Task<IReadOnlyList<ServicePurchase>> PurchasesAsync(Guid? customerId, string? status, int take = 500)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT TOP (@take) * FROM ({PurchaseSelect} WHERE (@c IS NULL OR p.CustomerId=@c) AND (@s IS NULL OR p.Status=@s)) x ORDER BY x.CreatedAt DESC", cn);
        cmd.Parameters.AddWithValue("@take", take);
        cmd.Parameters.AddWithValue("@c", (object?)customerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", (object?)status ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<ServicePurchase>();
        while (await r.ReadAsync()) list.Add(ReadPurchase(r));
        return list;
    }

    public async Task<ServicePurchase?> PurchaseAsync(Guid id, Guid? customerId = null)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"{PurchaseSelect} WHERE p.Id=@id AND (@c IS NULL OR p.CustomerId=@c)", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@c", (object?)customerId ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadPurchase(r) : null;
    }

    public async Task<ServiceBalance> BalanceAsync(Guid customerId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
SELECT ISNULL(SUM(TransactionsRemaining),0), ISNULL(SUM(InvoicesRemaining),0),
 MIN(CASE WHEN ExpiresAt IS NOT NULL AND (InvoicesRemaining>0 OR TransactionsRemaining>0) THEN ExpiresAt END)
FROM dbo.ServicePackagePurchases WHERE CustomerId=@c AND Status='PAID' AND (ExpiresAt IS NULL OR ExpiresAt>@now)
""", cn);
        cmd.Parameters.AddWithValue("@c", customerId);
        cmd.Parameters.AddWithValue("@now", clock.GetUtcNow().UtcDateTime);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return new(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetDateTime(2));
    }

    public async Task<(ServicePurchaseOutcome Outcome, ServicePurchase? Purchase)> RequestAsync(Guid customerId, Guid packageId, string via, Guid actor)
    {
        Guid id = Guid.NewGuid();
        await using (var cn = Connection())
        {
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    var no = ServicePackageRules.NewPurchaseNo(clock.GetUtcNow().UtcDateTime);
                    await using var insert = new SqlCommand("""
INSERT dbo.ServicePackagePurchases(Id,PurchaseNo,CustomerId,PackageId,PackageCode,PackageName,PackageType,InvoiceQuota,TransactionQuota,Amount,ValidityDays,Status,RequestedVia,RequestedBy)
SELECT @id,@no,c.Id,s.Id,s.Code,s.Name,s.PackageType,s.InvoiceQuota,s.TransactionQuota,s.Price,s.ValidityDays,'PENDING',@via,@actor
FROM dbo.ServicePackages s CROSS JOIN dbo.Customers c
WHERE s.Id=@pkg AND s.IsActive=1 AND c.Id=@c AND c.IsDeleted=0
""", cn, tx);
                    insert.Parameters.AddWithValue("@id", id);
                    insert.Parameters.AddWithValue("@no", no);
                    insert.Parameters.AddWithValue("@c", customerId);
                    insert.Parameters.AddWithValue("@pkg", packageId);
                    insert.Parameters.AddWithValue("@via", via);
                    insert.Parameters.AddWithValue("@actor", actor);
                    try
                    {
                        if (await insert.ExecuteNonQueryAsync() == 0) { await tx.RollbackAsync(); return (ServicePurchaseOutcome.PackageUnavailable, null); }
                        break;
                    }
                    catch (SqlException ex) when (ex.Number is 2601 or 2627 && attempt < 3) { /* purchase number collision: draw another */ }
                }
                await AuditSql.WriteOrThrowAsync(cn, tx, RequireAudit, actor, "SERVICE_PACKAGE_PURCHASE_REQUESTED", "dbo.ServicePackagePurchases", id.ToString(), "{}",
                    AuditSql.Json(("customerId", customerId), ("packageId", packageId), ("via", via)), null);
                await tx.CommitAsync();
            }
            catch (AuditUnavailableException) { await tx.RollbackAsync(); return (ServicePurchaseOutcome.AuditUnavailable, null); }
        }
        return (ServicePurchaseOutcome.Done, await PurchaseAsync(id));
    }

    public async Task<ServicePurchaseOutcome> ConfirmPaidAsync(Guid id, decimal paidAmount, string method, string? reference, Guid actor)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await using (var cmd = new SqlCommand("""
UPDATE dbo.ServicePackagePurchases SET Status='PAID',PaidAmount=@amount,PaidAt=@now,PaymentMethod=@method,PaymentReference=@ref,ConfirmedBy=@actor,
 ExpiresAt=CASE WHEN ValidityDays IS NULL THEN NULL ELSE DATEADD(day,ValidityDays,@now) END,
 InvoicesRemaining=InvoiceQuota,TransactionsRemaining=TransactionQuota,UpdatedAt=@now
WHERE Id=@id AND Status='PENDING'
""", cn, tx))
            {
                cmd.Parameters.AddWithValue("@amount", paidAmount);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.Parameters.AddWithValue("@method", method);
                cmd.Parameters.AddWithValue("@ref", (object?)reference ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@id", id);
                if (await cmd.ExecuteNonQueryAsync() == 0) { await tx.RollbackAsync(); return await ExistsAsync(id) ? ServicePurchaseOutcome.NotPending : ServicePurchaseOutcome.NotFound; }
            }
            await AuditSql.WriteOrThrowAsync(cn, tx, RequireAudit, actor, "SERVICE_PACKAGE_PURCHASE_PAID", "dbo.ServicePackagePurchases", id.ToString(), "{}",
                AuditSql.Json(("paidAmount", paidAmount), ("method", method), ("reference", reference)), null);
            await tx.CommitAsync();
            return ServicePurchaseOutcome.Done;
        }
        catch (AuditUnavailableException) { await tx.RollbackAsync(); return ServicePurchaseOutcome.AuditUnavailable; }
    }

    public async Task<ServicePurchaseOutcome> CancelAsync(Guid id, Guid? customerId, string? reason, Guid actor)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await using (var cmd = new SqlCommand("UPDATE dbo.ServicePackagePurchases SET Status='CANCELLED',CancelledAt=SYSUTCDATETIME(),CancelledBy=@actor,CancelReason=@reason,UpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND Status='PENDING' AND (@c IS NULL OR CustomerId=@c)", cn, tx))
            {
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@c", (object?)customerId ?? DBNull.Value);
                if (await cmd.ExecuteNonQueryAsync() == 0)
                {
                    await tx.RollbackAsync();
                    return await PurchaseAsync(id, customerId) is null ? ServicePurchaseOutcome.NotFound : ServicePurchaseOutcome.NotPending;
                }
            }
            await AuditSql.WriteOrThrowAsync(cn, tx, RequireAudit, actor, "SERVICE_PACKAGE_PURCHASE_CANCELLED", "dbo.ServicePackagePurchases", id.ToString(), "{}",
                AuditSql.Json(("reason", reason), ("byCustomer", customerId is not null)), null);
            await tx.CommitAsync();
            return ServicePurchaseOutcome.Done;
        }
        catch (AuditUnavailableException) { await tx.RollbackAsync(); return ServicePurchaseOutcome.AuditUnavailable; }
    }

    private async Task<bool> ExistsAsync(Guid id) => await PurchaseAsync(id) is not null;

    private static ServicePackage ReadPackage(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5),
        r.GetDecimal(6), r.IsDBNull(7) ? null : r.GetInt32(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetBoolean(9), r.GetInt32(10), r.GetDateTime(11));

    private static ServicePurchase ReadPurchase(SqlDataReader r)
    {
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        DateTime? D(int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
        return new(r.GetGuid(0), r.GetString(1), r.GetGuid(2), r.GetString(3), r.GetString(4), r.GetGuid(5), r.GetString(6), r.GetString(7), r.GetString(8),
            r.GetInt32(9), r.GetInt32(10), r.GetDecimal(11), r.IsDBNull(12) ? null : r.GetInt32(12), r.GetString(13), r.GetString(14), S(15), r.GetDateTime(16),
            r.IsDBNull(17) ? null : r.GetDecimal(17), D(18), S(19), S(20), S(21), D(22), r.GetInt32(23), r.GetInt32(24), D(25), S(26), S(27));
    }
}
