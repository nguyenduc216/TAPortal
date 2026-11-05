using Microsoft.Data.SqlClient;

namespace TACustomerPortal.Web.Services;

// Customer Portal payments data (migration 011). Every method takes the signed-in customer's id and filters by it in SQL,
// so one customer can never read or change another customer's bank accounts, contacts or orders.

public sealed record BankAccount(
    Guid Id, string BankBin, string BankCode, string BankName, string AccountType, string? AccountNumber, string? AccountName,
    string? VaAccountNumber, bool IsNotifyAccountNumber, long? ShopId, string Status, string? ConfirmId, DateTime? ConfirmExpiresAt,
    string? ActionLink, string? LastError, bool IsDefault, DateTime CreatedAt)
{
    /// <summary>The account payers transfer to: the VA when there is one, otherwise the notified root account.</summary>
    public string? ReceiveAccount => VaAccountNumber ?? (IsNotifyAccountNumber ? AccountNumber : null);
    public bool IsActive => Status == BankStatus.Active;
}

public static class BankStatus
{
    public const string PendingOtp = "PENDING_OTP", PendingBank = "PENDING_BANK", Active = "ACTIVE", Failed = "FAILED", PendingDelete = "PENDING_DELETE", Deleted = "DELETED";
}

public sealed record Contact(Guid Id, string Code, string Name, string? LegalName, string? TaxCode, string? Phone, string? Email, string? Address, string? Note, bool IsActive,
    string? BuyerType = null, string? BuyerGroup = null, string? IdentityNumber = null, string? Zalo = null, string? BankName = null, string? BankAccountNumber = null,
    string? BankAccountName = null)
{
    /// <summary>Stored type, or for rows saved before the type existed: a tax code ⇒ business, otherwise a retail customer.</summary>
    public string Type => BuyerTypes.Normalize(BuyerType) ?? (TaxCode is null ? BuyerTypes.Individual : BuyerTypes.Business);
}

/// <summary>Kinds of buyer a company sells to (operator 2026-11-06): only a business must give a tax code.</summary>
public static class BuyerTypes
{
    public const string Individual = "INDIVIDUAL";
    public const string Household = "HOUSEHOLD";
    public const string Business = "BUSINESS";
    public const string Organization = "ORGANIZATION";
    public static readonly IReadOnlyList<string> All = [Individual, Household, Business, Organization];

    public static string? Normalize(string? value) => All.FirstOrDefault(t => string.Equals(t, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Label(string type) => type switch
    {
        Household => "Hộ kinh doanh",
        Business => "Doanh nghiệp",
        Organization => "Tổ chức khác",
        _ => "Khách lẻ"
    };

    public static string Hint(string type) => type switch
    {
        Household => "Cửa hàng, quán, hộ cá thể",
        Business => "Công ty, chi nhánh — cần MST",
        Organization => "Trường, hội, đơn vị sự nghiệp",
        _ => "Cá nhân, không cần MST"
    };

    /// <summary>Suggestions for the free "nhóm khách" field.</summary>
    public static readonly IReadOnlyList<string> Groups = ["Nhà hàng", "Quán cà phê", "Khách sạn", "Bán lẻ", "Bán buôn", "Spa / làm đẹp", "Giáo dục", "Y tế", "Văn phòng"];
}

public sealed record OrderLine(int LineNumber, string ItemName, string? UnitName, decimal Quantity, decimal UnitPrice, string TaxRateCode, decimal AmountBeforeTax, decimal TaxAmount)
{
    public decimal Total => AmountBeforeTax + TaxAmount;
}

public sealed record Order(
    Guid Id, string OrderNo, Guid? ContactId, string? ContactName, Guid? BankAccountId, string? Description, decimal Amount, decimal PaidAmount,
    string Status, string? QrType, string? QrPayload, string? QrAccount, string? BillId, int QrExpireMinutes, DateTime? ExpiresAt,
    bool AutoIssueInvoice, string InvoiceStatus, string? LastError, DateTime CreatedAt, IReadOnlyList<OrderLine> Lines,
    decimal? PartnerPaidAmount = null, DateTime? PartnerCheckedAt = null)
{
    public decimal Remaining => Math.Max(0, Amount - PaidAmount);
    public decimal Overpaid => Math.Max(0, PaidAmount - Amount);
    /// <summary>The payment service reported a different amount paid than the system counted from bank notifications.</summary>
    public bool PartnerDiffers => PartnerPaidAmount is decimal p && p != PaidAmount;
}

public static class OrderStatus
{
    public const string Draft = "DRAFT", Awaiting = "AWAITING_PAYMENT", Partial = "PARTIALLY_PAID", Paid = "PAID", Expired = "EXPIRED", Cancelled = "CANCELLED";
    /// <summary>Money arrived after the QR expired / on a cancelled order (migration 019): waits for staff in "Đối soát giao dịch".</summary>
    public const string ExpiredPaid = "EXPIRED_PAID", CancelledPaid = "CANCELLED_PAID";
    public static bool IsOpen(string status) => status is Draft or Awaiting or Partial;
}

public sealed record NewOrder(Guid? ContactId, Guid BankAccountId, string? Description, int QrExpireMinutes, bool AutoIssueInvoice, IReadOnlyList<OrderLine> Lines)
{
    public decimal Amount => Lines.Sum(l => l.Total);
}

public sealed record PaymentSettings(bool AutoIssueInvoiceDefault, int DefaultQrExpireMinutes)
{
    public static readonly PaymentSettings Default = new(true, 1440);
}

public interface IPaymentStore
{
    Task<long?> MerchantIdAsync(Guid customerId);

    Task<IReadOnlyList<BankAccount>> BankAccountsAsync(Guid customerId);
    Task<BankAccount?> BankAccountAsync(Guid customerId, Guid id);
    Task<Guid> AddBankAccountAsync(Guid customerId, BankAccount account, Guid actor);
    Task UpdateBankAccountAsync(Guid customerId, BankAccount account, Guid actor);
    Task SetDefaultBankAccountAsync(Guid customerId, Guid id, Guid actor);

    Task<IReadOnlyList<Contact>> ContactsAsync(Guid customerId);
    Task<Contact?> ContactAsync(Guid customerId, Guid id);
    /// <summary>Insert (Id empty) or update. False when the code is already used by another contact of this customer.</summary>
    Task<bool> SaveContactAsync(Guid customerId, Contact contact, Guid actor);
    Task SetContactActiveAsync(Guid customerId, Guid id, bool active, Guid actor);

    Task<IReadOnlyList<Order>> OrdersAsync(Guid customerId);
    Task<Order?> OrderAsync(Guid customerId, Guid id);
    Task<(Guid Id, string OrderNo)> CreateOrderAsync(Guid customerId, NewOrder order, Guid actor);
    /// <summary>
    /// Writes the mutable state of an order: status, QR, expiry, last error. The amount paid is never written here (only the
    /// payment processor and staff reconciliation change it), and a payment status is recomputed from the stored amount.
    /// </summary>
    Task UpdateOrderAsync(Guid customerId, Order order, Guid actor);
    /// <summary>Stores the amount paid reported by the payment service, next to the system's own count.</summary>
    Task RecordPartnerPaidAsync(Guid customerId, Guid orderId, decimal partnerPaid);

    Task<PaymentSettings> SettingsAsync(Guid customerId);
    Task SaveSettingsAsync(Guid customerId, PaymentSettings settings, Guid actor);
}

public sealed class SqlPaymentStore(IConfiguration configuration) : IPaymentStore
{
    private async Task<SqlConnection> Open()
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
        await cn.OpenAsync();
        return cn;
    }

    private static SqlCommand Command(SqlConnection cn, string sql, params (string Name, object? Value)[] args) => Command(cn, null, sql, args);

    private static SqlCommand Command(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    public async Task<long?> MerchantIdAsync(Guid customerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "SELECT MerchantId FROM dbo.TingeeMerchants WHERE CustomerId=@c", ("@c", customerId));
        return await cmd.ExecuteScalarAsync() is long id ? id : null;
    }

    // ---- bank accounts ------------------------------------------------------------------------------------
    private const string BankColumns = "Id,BankBin,BankCode,BankName,AccountType,AccountNumber,AccountName,VaAccountNumber,IsNotifyAccountNumber,TingeeShopId,Status,ConfirmId,ConfirmExpiresAt,ActionLink,LastError,IsDefault,CreatedAt";

    private static BankAccount ReadBank(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), S(r, 5), S(r, 6), S(r, 7),
        r.GetBoolean(8), r.IsDBNull(9) ? null : r.GetInt64(9), r.GetString(10), S(r, 11), r.IsDBNull(12) ? null : r.GetDateTime(12), S(r, 13), S(r, 14), r.GetBoolean(15), r.GetDateTime(16));

    public async Task<IReadOnlyList<BankAccount>> BankAccountsAsync(Guid customerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, $"SELECT {BankColumns} FROM dbo.CustomerBankAccounts WHERE CustomerId=@c AND Status<>'DELETED' ORDER BY IsDefault DESC, CreatedAt DESC", ("@c", customerId));
        var list = new List<BankAccount>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadBank(r));
        return list;
    }

    public async Task<BankAccount?> BankAccountAsync(Guid customerId, Guid id)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, $"SELECT {BankColumns} FROM dbo.CustomerBankAccounts WHERE CustomerId=@c AND Id=@id AND Status<>'DELETED'", ("@c", customerId), ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadBank(r) : null;
    }

    public async Task<Guid> AddBankAccountAsync(Guid customerId, BankAccount a, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, """
INSERT dbo.CustomerBankAccounts(CustomerId,BankBin,BankCode,BankName,AccountType,AccountNumber,AccountName,VaAccountNumber,IsNotifyAccountNumber,TingeeShopId,Status,ConfirmId,ConfirmExpiresAt,ActionLink,LastError,IsDefault,CreatedBy)
OUTPUT inserted.Id
VALUES(@c,@bin,@code,@name,@type,@number,@holder,@va,@notify,@shop,@status,@confirm,@expires,@link,@error,
 CASE WHEN EXISTS(SELECT 1 FROM dbo.CustomerBankAccounts WHERE CustomerId=@c AND Status='ACTIVE') THEN 0 ELSE 1 END,@actor)
""", ("@c", customerId), ("@bin", a.BankBin), ("@code", a.BankCode), ("@name", a.BankName), ("@type", a.AccountType), ("@number", a.AccountNumber), ("@holder", a.AccountName),
            ("@va", a.VaAccountNumber), ("@notify", a.IsNotifyAccountNumber), ("@shop", a.ShopId), ("@status", a.Status), ("@confirm", a.ConfirmId), ("@expires", a.ConfirmExpiresAt),
            ("@link", a.ActionLink), ("@error", a.LastError), ("@actor", actor));
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task UpdateBankAccountAsync(Guid customerId, BankAccount a, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, """
UPDATE dbo.CustomerBankAccounts SET AccountNumber=@number,VaAccountNumber=@va,TingeeShopId=@shop,Status=@status,ConfirmId=@confirm,ConfirmExpiresAt=@expires,
 ActionLink=@link,LastError=@error,IsDefault=CASE WHEN @status='ACTIVE' THEN IsDefault ELSE 0 END,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHERE CustomerId=@c AND Id=@id
""", ("@c", customerId), ("@id", a.Id), ("@number", a.AccountNumber), ("@va", a.VaAccountNumber), ("@shop", a.ShopId), ("@status", a.Status), ("@confirm", a.ConfirmId),
            ("@expires", a.ConfirmExpiresAt), ("@link", a.ActionLink), ("@error", a.LastError), ("@actor", actor));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetDefaultBankAccountAsync(Guid customerId, Guid id, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, """
IF EXISTS(SELECT 1 FROM dbo.CustomerBankAccounts WHERE CustomerId=@c AND Id=@id AND Status='ACTIVE')
UPDATE dbo.CustomerBankAccounts SET IsDefault=CASE WHEN Id=@id THEN 1 ELSE 0 END,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHERE CustomerId=@c AND (IsDefault=1 OR Id=@id)
""", ("@c", customerId), ("@id", id), ("@actor", actor));
        await cmd.ExecuteNonQueryAsync();
    }

    // ---- contacts -----------------------------------------------------------------------------------------
    private const string ContactColumns = "Id,Code,Name,LegalName,TaxCode,Phone,Email,Address,Note,IsActive,BuyerType,BuyerGroup,IdentityNumber,Zalo,BankName,BankAccountNumber,BankAccountName";
    private static Contact ReadContact(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8), r.GetBoolean(9),
        S(r, 10), S(r, 11), S(r, 12), S(r, 13), S(r, 14), S(r, 15), S(r, 16));

    public async Task<IReadOnlyList<Contact>> ContactsAsync(Guid customerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, $"SELECT {ContactColumns} FROM dbo.CustomerBuyers WHERE CustomerId=@c AND IsDeleted=0 ORDER BY Name", ("@c", customerId));
        var list = new List<Contact>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadContact(r));
        return list;
    }

    public async Task<Contact?> ContactAsync(Guid customerId, Guid id)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, $"SELECT {ContactColumns} FROM dbo.CustomerBuyers WHERE CustomerId=@c AND Id=@id AND IsDeleted=0", ("@c", customerId), ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadContact(r) : null;
    }

    public async Task<bool> SaveContactAsync(Guid customerId, Contact x, Guid actor)
    {
        await using var cn = await Open();
        var sql = x.Id == Guid.Empty
            ? """
INSERT dbo.CustomerBuyers(CustomerId,Code,Name,LegalName,TaxCode,Phone,Email,Address,Note,IsActive,CreatedBy,BuyerType,BuyerGroup,IdentityNumber,Zalo,BankName,BankAccountNumber,BankAccountName)
VALUES(@c,@code,@name,@legal,@tax,@phone,@email,@address,@note,1,@actor,@type,@group,@identity,@zalo,@bank,@bankNo,@bankName)
"""
            : """
UPDATE dbo.CustomerBuyers SET Code=@code,Name=@name,LegalName=@legal,TaxCode=@tax,Phone=@phone,Email=@email,Address=@address,Note=@note,
 BuyerType=@type,BuyerGroup=@group,IdentityNumber=@identity,Zalo=@zalo,BankName=@bank,BankAccountNumber=@bankNo,BankAccountName=@bankName,
 UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE CustomerId=@c AND Id=@id AND IsDeleted=0
""";
        await using var cmd = Command(cn, sql, ("@c", customerId), ("@id", x.Id), ("@code", x.Code), ("@name", x.Name), ("@legal", x.LegalName), ("@tax", x.TaxCode),
            ("@phone", x.Phone), ("@email", x.Email), ("@address", x.Address), ("@note", x.Note), ("@actor", actor), ("@type", x.Type), ("@group", x.BuyerGroup),
            ("@identity", x.IdentityNumber), ("@zalo", x.Zalo), ("@bank", x.BankName), ("@bankNo", x.BankAccountNumber), ("@bankName", x.BankAccountName));
        try { await cmd.ExecuteNonQueryAsync(); return true; }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return false; }
    }

    public async Task SetContactActiveAsync(Guid customerId, Guid id, bool active, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "UPDATE dbo.CustomerBuyers SET IsActive=@a,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE CustomerId=@c AND Id=@id AND IsDeleted=0",
            ("@c", customerId), ("@id", id), ("@a", active), ("@actor", actor));
        await cmd.ExecuteNonQueryAsync();
    }

    // ---- orders -------------------------------------------------------------------------------------------
    private const string OrderSelect = """
SELECT o.Id,o.OrderNo,o.ContactId,c.Name,o.BankAccountId,o.Description,o.Amount,o.PaidAmount,o.Status,o.QrType,o.QrPayload,o.QrAccount,o.BillId,
 o.QrExpireMinutes,o.ExpiresAt,o.AutoIssueInvoice,o.InvoiceStatus,o.LastError,o.CreatedAt,o.PartnerPaidAmount,o.PartnerCheckedAt
FROM dbo.CustomerOrders o LEFT JOIN dbo.CustomerBuyers c ON c.Id=o.ContactId AND c.CustomerId=o.CustomerId
""";

    private static Order ReadOrder(SqlDataReader r, IReadOnlyList<OrderLine> lines) => new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetGuid(2), S(r, 3),
        r.IsDBNull(4) ? null : r.GetGuid(4), S(r, 5), r.GetDecimal(6), r.GetDecimal(7), r.GetString(8), S(r, 9), S(r, 10), S(r, 11), S(r, 12), r.GetInt32(13),
        r.IsDBNull(14) ? null : r.GetDateTime(14), r.GetBoolean(15), r.GetString(16), S(r, 17), r.GetDateTime(18), lines,
        r.IsDBNull(19) ? null : r.GetDecimal(19), r.IsDBNull(20) ? null : r.GetDateTime(20));

    public async Task<IReadOnlyList<Order>> OrdersAsync(Guid customerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, OrderSelect + " WHERE o.CustomerId=@c ORDER BY o.CreatedAt DESC", ("@c", customerId));
        var list = new List<Order>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadOrder(r, []));
        return list;
    }

    public async Task<Order?> OrderAsync(Guid customerId, Guid id)
    {
        await using var cn = await Open();
        var lines = new List<OrderLine>();
        await using (var linesCmd = Command(cn, """
SELECT l.LineNumber,l.ItemName,l.UnitName,l.Quantity,l.UnitPrice,l.TaxRateCode,l.AmountBeforeTax,l.TaxAmount
FROM dbo.CustomerOrderLines l JOIN dbo.CustomerOrders o ON o.Id=l.OrderId WHERE o.CustomerId=@c AND o.Id=@id ORDER BY l.LineNumber
""", ("@c", customerId), ("@id", id)))
        await using (var lr = await linesCmd.ExecuteReaderAsync())
            while (await lr.ReadAsync())
                lines.Add(new(lr.GetInt32(0), lr.GetString(1), S(lr, 2), lr.GetDecimal(3), lr.GetDecimal(4), lr.GetString(5), lr.GetDecimal(6), lr.GetDecimal(7)));
        await using var cmd = Command(cn, OrderSelect + " WHERE o.CustomerId=@c AND o.Id=@id", ("@c", customerId), ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadOrder(r, lines) : null;
    }

    public async Task<(Guid Id, string OrderNo)> CreateOrderAsync(Guid customerId, NewOrder order, Guid actor)
    {
        await using var cn = await Open();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        // Order number DH-yyMMdd-NNNN, sequential per customer and day (UTC+7); the range lock serializes concurrent creates.
        var prefix = "DH" + DateTime.UtcNow.AddHours(7).ToString("yyMMdd") + "-";
        await using var next = Command(cn, tx, "SELECT ISNULL(MAX(CAST(RIGHT(OrderNo,4) AS int)),0)+1 FROM dbo.CustomerOrders WITH (UPDLOCK,HOLDLOCK) WHERE CustomerId=@c AND OrderNo LIKE @p",
            ("@c", customerId), ("@p", prefix + "%"));
        var orderNo = prefix + Convert.ToInt32(await next.ExecuteScalarAsync()).ToString("0000");
        await using var insert = Command(cn, tx, """
INSERT dbo.CustomerOrders(CustomerId,OrderNo,ContactId,BankAccountId,Description,Amount,Status,QrExpireMinutes,AutoIssueInvoice,CreatedBy)
OUTPUT inserted.Id VALUES(@c,@no,@contact,@bank,@desc,@amount,'DRAFT',@expire,@invoice,@actor)
""", ("@c", customerId), ("@no", orderNo), ("@contact", order.ContactId), ("@bank", order.BankAccountId), ("@desc", order.Description), ("@amount", order.Amount),
            ("@expire", order.QrExpireMinutes), ("@invoice", order.AutoIssueInvoice), ("@actor", actor));
        var id = (Guid)(await insert.ExecuteScalarAsync())!;
        foreach (var l in order.Lines)
        {
            await using var line = Command(cn, tx, "INSERT dbo.CustomerOrderLines(OrderId,LineNumber,ItemName,UnitName,Quantity,UnitPrice,TaxRateCode,AmountBeforeTax,TaxAmount) VALUES(@o,@n,@item,@unit,@qty,@price,@tax,@before,@taxAmount)",
                ("@o", id), ("@n", l.LineNumber), ("@item", l.ItemName), ("@unit", l.UnitName), ("@qty", l.Quantity), ("@price", l.UnitPrice), ("@tax", l.TaxRateCode), ("@before", l.AmountBeforeTax), ("@taxAmount", l.TaxAmount));
            await line.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return (id, orderNo);
    }

    public async Task UpdateOrderAsync(Guid customerId, Order o, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, """
UPDATE o SET BankAccountId=@bank,Status=n.Status,QrType=@qrType,QrPayload=@payload,QrAccount=@qrAccount,BillId=@bill,ExpiresAt=@expires,LastError=@error,
 InvoiceStatus=CASE WHEN n.Status='PAID' AND o.AutoIssueInvoice=1 AND o.InvoiceStatus='NONE' THEN 'PENDING' ELSE o.InvoiceStatus END,
 PaidAt=CASE WHEN n.Status='PAID' AND o.PaidAt IS NULL THEN SYSUTCDATETIME() ELSE o.PaidAt END,
 CancelledAt=CASE WHEN n.Status='CANCELLED' AND o.CancelledAt IS NULL THEN SYSUTCDATETIME() ELSE o.CancelledAt END,
 UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
FROM dbo.CustomerOrders o
-- The payment processor may have counted money since the order was read: payment states follow the stored amount, and an
-- order that has received money is never cancelled here.
CROSS APPLY (SELECT CASE
  WHEN @status IN ('AWAITING_PAYMENT','PARTIALLY_PAID','PAID') THEN CASE WHEN o.PaidAmount>=o.Amount THEN 'PAID' WHEN o.PaidAmount>0 THEN 'PARTIALLY_PAID' ELSE 'AWAITING_PAYMENT' END
  WHEN @status='CANCELLED' AND o.PaidAmount>0 THEN o.Status
  ELSE @status END AS Status) n
WHERE o.CustomerId=@c AND o.Id=@id AND o.Status NOT IN ('EXPIRED_PAID','CANCELLED_PAID')
""", ("@c", customerId), ("@id", o.Id), ("@bank", o.BankAccountId), ("@status", o.Status), ("@qrType", o.QrType), ("@payload", o.QrPayload), ("@qrAccount", o.QrAccount),
            ("@bill", o.BillId), ("@expires", o.ExpiresAt), ("@error", o.LastError), ("@actor", actor));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordPartnerPaidAsync(Guid customerId, Guid orderId, decimal partnerPaid)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "UPDATE dbo.CustomerOrders SET PartnerPaidAmount=@p,PartnerCheckedAt=SYSUTCDATETIME() WHERE CustomerId=@c AND Id=@id",
            ("@c", customerId), ("@id", orderId), ("@p", partnerPaid));
        await cmd.ExecuteNonQueryAsync();
    }

    // ---- settings -----------------------------------------------------------------------------------------
    public async Task<PaymentSettings> SettingsAsync(Guid customerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "SELECT AutoIssueInvoiceDefault,DefaultQrExpireMinutes FROM dbo.CustomerPaymentSettings WHERE CustomerId=@c", ("@c", customerId));
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetBoolean(0), r.GetInt32(1)) : PaymentSettings.Default;
    }

    public async Task SaveSettingsAsync(Guid customerId, PaymentSettings s, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, """
UPDATE dbo.CustomerPaymentSettings SET AutoIssueInvoiceDefault=@auto,DefaultQrExpireMinutes=@expire,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE CustomerId=@c;
IF @@ROWCOUNT=0 INSERT dbo.CustomerPaymentSettings(CustomerId,AutoIssueInvoiceDefault,DefaultQrExpireMinutes,UpdatedBy) VALUES(@c,@auto,@expire,@actor);
""", ("@c", customerId), ("@auto", s.AutoIssueInvoiceDefault), ("@expire", s.DefaultQrExpireMinutes), ("@actor", actor));
        await cmd.ExecuteNonQueryAsync();
    }
}
