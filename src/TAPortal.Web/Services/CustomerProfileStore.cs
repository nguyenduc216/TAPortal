using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

public enum ProfileChangeOutcome { Saved, NotFound, Conflict, InvalidParent, AuditUnavailable }

public enum SecretField { Password, Pin }

// Value == null && !Clear keeps the stored secret; Clear removes it; otherwise Value is the new ciphertext.
public readonly record struct SecretChange(byte[]? Value, bool Clear)
{
    public static readonly SecretChange Keep = new(null, false);
    public bool Changes => Clear || Value is not null;
}

public sealed record SecretRevealResult(ProfileChangeOutcome Outcome, byte[]? Cipher);

// Tables of migration 013. Every call is scoped by the customer id from the route, so a row id of another customer
// is treated as not found. Secrets are passed in and out only as ciphertext; audit rows never contain secrets or
// identity numbers.
public interface ICustomerProfileStore
{
    Task<CustomerProfileVm?> GetProfileAsync(Guid customerId);
    Task<IReadOnlyList<CustomerOption>> CustomerOptionsAsync(Guid exceptCustomerId);
    Task<ProfileChangeOutcome> SaveProfileAsync(CustomerProfileVm vm, Guid actor);
    Task<IReadOnlyList<RepresentativeRow>> ListRepresentativesAsync(Guid customerId);
    Task<ProfileChangeOutcome> SaveRepresentativeAsync(Guid customerId, RepresentativeInput input, Guid actor);
    Task<ProfileChangeOutcome> DeleteRepresentativeAsync(Guid customerId, Guid id, string? stamp, Guid actor);
    Task<IReadOnlyList<SystemAccountRow>> ListSystemAccountsAsync(Guid customerId);
    // accountId: input.Id for an existing account, a new id (already used to encrypt the secrets) for a new one.
    Task<ProfileChangeOutcome> SaveSystemAccountAsync(Guid customerId, Guid accountId, SystemAccountInput input, SecretChange password, SecretChange pin, Guid actor);
    Task<ProfileChangeOutcome> DeleteSystemAccountAsync(Guid customerId, Guid id, string? stamp, Guid actor);
    // Writes a durable CUSTOMER_SECRET_REVEALED audit row before returning the ciphertext; no audit row, no secret.
    Task<SecretRevealResult> RevealSecretAsync(Guid customerId, Guid accountId, SecretField field, Guid actor, string? ip);
}

public static class CustomerProfileSql
{
    public const string GetProfile = """
SELECT CustomerId,ShortName,LegalForm,CharterCapital,EstablishedDate,MainIndustryCode,MainIndustryName,BusinessLine,TaxAuthority,TaxOfficerName,TaxOfficerPhone,
 BusinessStatus,BusinessStatusDate,ScaleNote,SourceCode,SourceNote,ParentCustomerId,Note,RowVersion,COALESCE(UpdatedAt,CreatedAt)
FROM dbo.CustomerProfiles WHERE CustomerId=@customer;
""";

    public const string Options = "SELECT Id,Code,Name FROM dbo.Customers WHERE IsDeleted=0 AND Id<>@customer ORDER BY Name;";

    // A parent must be another live customer whose own parent is not this customer (no two-customer cycle).
    public const string ParentValid = """
SELECT COUNT(*) FROM dbo.Customers c WHERE c.Id=@parent AND c.IsDeleted=0 AND c.Id<>@customer
 AND NOT EXISTS (SELECT 1 FROM dbo.CustomerProfiles p WHERE p.CustomerId=@parent AND p.ParentCustomerId=@customer);
""";

    private const string ProfileColumns = "ShortName,LegalForm,CharterCapital,EstablishedDate,MainIndustryCode,MainIndustryName,BusinessLine,TaxAuthority,TaxOfficerName,TaxOfficerPhone,BusinessStatus,BusinessStatusDate,ScaleNote,SourceCode,SourceNote,ParentCustomerId,Note";

    public const string InsertProfile = "INSERT dbo.CustomerProfiles(CustomerId," + ProfileColumns + ",CreatedBy) SELECT @customer,@ShortName,@LegalForm,@CharterCapital,@EstablishedDate,@MainIndustryCode,@MainIndustryName,@BusinessLine,@TaxAuthority,@TaxOfficerName,@TaxOfficerPhone,@BusinessStatus,@BusinessStatusDate,@ScaleNote,@SourceCode,@SourceNote,@ParentCustomerId,@Note,@by WHERE NOT EXISTS (SELECT 1 FROM dbo.CustomerProfiles WITH (UPDLOCK, HOLDLOCK) WHERE CustomerId=@customer);";

    public const string UpdateProfile = """
UPDATE dbo.CustomerProfiles SET ShortName=@ShortName,LegalForm=@LegalForm,CharterCapital=@CharterCapital,EstablishedDate=@EstablishedDate,MainIndustryCode=@MainIndustryCode,
 MainIndustryName=@MainIndustryName,BusinessLine=@BusinessLine,TaxAuthority=@TaxAuthority,TaxOfficerName=@TaxOfficerName,TaxOfficerPhone=@TaxOfficerPhone,BusinessStatus=@BusinessStatus,
 BusinessStatusDate=@BusinessStatusDate,ScaleNote=@ScaleNote,SourceCode=@SourceCode,SourceNote=@SourceNote,ParentCustomerId=@ParentCustomerId,Note=@Note,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE CustomerId=@customer AND RowVersion=@rv;
""";

    public const string ListRepresentatives = """
SELECT Id,RoleCode,IsLegalRepresentative,IsPrimaryContact,FullName,Gender,DateOfBirth,IdentityNumber,IdentityIssueDate,IdentityIssuePlace,Address,Email,Phone,Note,SortOrder,RowVersion
FROM dbo.CustomerRepresentatives WHERE CustomerId=@customer AND IsDeleted=0 ORDER BY SortOrder,FullName;
""";

    public const string InsertRepresentative = """
INSERT dbo.CustomerRepresentatives(Id,CustomerId,RoleCode,IsLegalRepresentative,IsPrimaryContact,FullName,Gender,DateOfBirth,IdentityNumber,IdentityIssueDate,IdentityIssuePlace,Address,Email,Phone,Note,SortOrder,CreatedBy)
VALUES(@id,@customer,@RoleCode,@IsLegalRepresentative,@IsPrimaryContact,@FullName,@Gender,@DateOfBirth,@IdentityNumber,@IdentityIssueDate,@IdentityIssuePlace,@Address,@Email,@Phone,@Note,@SortOrder,@by);
""";

    public const string UpdateRepresentative = """
UPDATE dbo.CustomerRepresentatives SET RoleCode=@RoleCode,IsLegalRepresentative=@IsLegalRepresentative,IsPrimaryContact=@IsPrimaryContact,FullName=@FullName,Gender=@Gender,
 DateOfBirth=@DateOfBirth,IdentityNumber=@IdentityNumber,IdentityIssueDate=@IdentityIssueDate,IdentityIssuePlace=@IdentityIssuePlace,Address=@Address,Email=@Email,Phone=@Phone,
 Note=@Note,SortOrder=@SortOrder,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND CustomerId=@customer AND IsDeleted=0 AND RowVersion=@rv;
""";

    public const string DeleteRepresentative = "UPDATE dbo.CustomerRepresentatives SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@id AND CustomerId=@customer AND IsDeleted=0 AND RowVersion=@rv;";
    public const string RepresentativeExists = "SELECT COUNT(*) FROM dbo.CustomerRepresentatives WHERE Id=@id AND CustomerId=@customer AND IsDeleted=0;";

    public const string ListAccounts = """
SELECT Id,SystemType,ProviderName,SystemName,LoginUrl,Username,LoginPhone,StoreName,UnitCode,BankAccountNumber,CertificateType,CertificateSerial,ValidFrom,ExpiresAt,
 CAST(CASE WHEN PasswordCipher IS NULL THEN 0 ELSE 1 END AS bit),CAST(CASE WHEN PinCipher IS NULL THEN 0 ELSE 1 END AS bit),SecretChangedAt,Status,ManagedBy,Note,SortOrder,RowVersion
FROM dbo.CustomerSystemAccounts WHERE CustomerId=@customer AND IsDeleted=0 ORDER BY SystemType,SortOrder,CreatedAt;
""";

    public const string InsertAccount = """
INSERT dbo.CustomerSystemAccounts(Id,CustomerId,SystemType,ProviderName,SystemName,LoginUrl,Username,LoginPhone,StoreName,UnitCode,BankAccountNumber,CertificateType,CertificateSerial,
 ValidFrom,ExpiresAt,PasswordCipher,PinCipher,SecretChangedAt,SecretChangedBy,Status,ManagedBy,Note,SortOrder,CreatedBy)
VALUES(@id,@customer,@SystemType,@ProviderName,@SystemName,@LoginUrl,@Username,@LoginPhone,@StoreName,@UnitCode,@BankAccountNumber,@CertificateType,@CertificateSerial,
 @ValidFrom,@ExpiresAt,@pwd,@pin,CASE WHEN @pwd IS NULL AND @pin IS NULL THEN NULL ELSE SYSUTCDATETIME() END,CASE WHEN @pwd IS NULL AND @pin IS NULL THEN NULL ELSE @by END,
 @Status,@ManagedBy,@Note,@SortOrder,@by);
""";

    // @setPwd / @setPin: 1 = replace with @pwd / @pin (NULL clears), 0 = keep the stored ciphertext.
    public const string UpdateAccount = """
UPDATE dbo.CustomerSystemAccounts SET SystemType=@SystemType,ProviderName=@ProviderName,SystemName=@SystemName,LoginUrl=@LoginUrl,Username=@Username,LoginPhone=@LoginPhone,
 StoreName=@StoreName,UnitCode=@UnitCode,BankAccountNumber=@BankAccountNumber,CertificateType=@CertificateType,CertificateSerial=@CertificateSerial,ValidFrom=@ValidFrom,ExpiresAt=@ExpiresAt,
 PasswordCipher=CASE WHEN @setPwd=1 THEN @pwd ELSE PasswordCipher END,PinCipher=CASE WHEN @setPin=1 THEN @pin ELSE PinCipher END,
 SecretChangedAt=CASE WHEN @setPwd=1 OR @setPin=1 THEN SYSUTCDATETIME() ELSE SecretChangedAt END,SecretChangedBy=CASE WHEN @setPwd=1 OR @setPin=1 THEN @by ELSE SecretChangedBy END,
 Status=@Status,ManagedBy=@ManagedBy,Note=@Note,SortOrder=@SortOrder,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND CustomerId=@customer AND IsDeleted=0 AND RowVersion=@rv;
""";

    // Soft delete also wipes the ciphertext: a removed account keeps no recoverable password.
    public const string DeleteAccount = "UPDATE dbo.CustomerSystemAccounts SET IsDeleted=1,PasswordCipher=NULL,PinCipher=NULL,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@id AND CustomerId=@customer AND IsDeleted=0 AND RowVersion=@rv;";
    public const string AccountExists = "SELECT COUNT(*) FROM dbo.CustomerSystemAccounts WHERE Id=@id AND CustomerId=@customer AND IsDeleted=0;";
    public const string GetPasswordCipher = "SELECT PasswordCipher FROM dbo.CustomerSystemAccounts WHERE Id=@id AND CustomerId=@customer AND IsDeleted=0;";
    public const string GetPinCipher = "SELECT PinCipher FROM dbo.CustomerSystemAccounts WHERE Id=@id AND CustomerId=@customer AND IsDeleted=0;";
}

public sealed class SqlCustomerProfileStore(IConfiguration configuration, AdminAuditOptions audit) : ICustomerProfileStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<CustomerProfileVm?> GetProfileAsync(Guid customerId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Command(cn, null, CustomerProfileSql.GetProfile, ("@customer", customerId));
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new CustomerProfileVm
        {
            CustomerId = r.GetGuid(0), ShortName = Text(r, 1), LegalForm = Text(r, 2), CharterCapital = r.IsDBNull(3) ? null : r.GetDecimal(3), EstablishedDate = Date(r, 4),
            MainIndustryCode = Text(r, 5), MainIndustryName = Text(r, 6), BusinessLine = Text(r, 7), TaxAuthority = Text(r, 8), TaxOfficerName = Text(r, 9),
            TaxOfficerPhone = Text(r, 10), BusinessStatus = Text(r, 11), BusinessStatusDate = Date(r, 12), ScaleNote = Text(r, 13), SourceCode = Text(r, 14),
            SourceNote = Text(r, 15), ParentCustomerId = r.IsDBNull(16) ? null : r.GetGuid(16), Note = Text(r, 17), Stamp = Stamp(r, 18), UpdatedAt = r.GetDateTime(19),
        };
    }

    public async Task<IReadOnlyList<CustomerOption>> CustomerOptionsAsync(Guid exceptCustomerId)
    {
        var list = new List<CustomerOption>();
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Command(cn, null, CustomerProfileSql.Options, ("@customer", exceptCustomerId));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public Task<ProfileChangeOutcome> SaveProfileAsync(CustomerProfileVm vm, Guid actor) => InTransaction(async (cn, tx) =>
    {
        if (vm.ParentCustomerId is { } parent && Convert.ToInt32(await Scalar(cn, tx, CustomerProfileSql.ParentValid, ("@parent", parent), ("@customer", vm.CustomerId))) != 1)
            return ProfileChangeOutcome.InvalidParent;
        var isNew = string.IsNullOrEmpty(vm.Stamp);
        await using (var cmd = Command(cn, tx, isNew ? CustomerProfileSql.InsertProfile : CustomerProfileSql.UpdateProfile, ("@customer", vm.CustomerId), ("@by", actor)))
        {
            AddProfile(cmd, vm);
            if (!isNew)
            {
                if (RowVersion(vm.Stamp) is not { } rv) return ProfileChangeOutcome.Conflict;
                cmd.Parameters.Add("@rv", SqlDbType.Timestamp).Value = rv;
            }
            // Insert affects 0 rows when another request created the profile first; update when the stamp is stale.
            if (await cmd.ExecuteNonQueryAsync() != 1) return ProfileChangeOutcome.Conflict;
        }
        await Audit(cn, tx, actor, isNew ? "CUSTOMER_PROFILE_CREATED" : "CUSTOMER_PROFILE_UPDATED", "dbo.CustomerProfiles", vm.CustomerId,
            AuditSql.Json(("legalForm", vm.LegalForm), ("businessStatus", vm.BusinessStatus), ("parentCustomerId", vm.ParentCustomerId), ("sourceCode", vm.SourceCode)));
        return ProfileChangeOutcome.Saved;
    });

    public async Task<IReadOnlyList<RepresentativeRow>> ListRepresentativesAsync(Guid customerId)
    {
        var list = new List<RepresentativeRow>();
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Command(cn, null, CustomerProfileSql.ListRepresentatives, ("@customer", customerId));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), r.GetBoolean(2), r.GetBoolean(3), r.GetString(4), Text(r, 5), Date(r, 6), Text(r, 7), Date(r, 8), Text(r, 9),
                Text(r, 10), Text(r, 11), Text(r, 12), Text(r, 13), r.GetInt32(14), Stamp(r, 15)));
        return list;
    }

    public Task<ProfileChangeOutcome> SaveRepresentativeAsync(Guid customerId, RepresentativeInput input, Guid actor) => InTransaction(async (cn, tx) =>
    {
        var isNew = input.Id is null;
        var id = input.Id ?? Guid.NewGuid();
        await using (var cmd = Command(cn, tx, isNew ? CustomerProfileSql.InsertRepresentative : CustomerProfileSql.UpdateRepresentative, ("@id", id), ("@customer", customerId), ("@by", actor)))
        {
            AddRepresentative(cmd, input);
            if (!isNew)
            {
                if (RowVersion(input.Stamp) is not { } rv) return ProfileChangeOutcome.Conflict;
                cmd.Parameters.Add("@rv", SqlDbType.Timestamp).Value = rv;
            }
            if (await cmd.ExecuteNonQueryAsync() != 1) return await Missing(cn, tx, CustomerProfileSql.RepresentativeExists, id, customerId);
        }
        // Personal data (identity number, date of birth, address) is never copied into the audit log.
        await Audit(cn, tx, actor, isNew ? "CUSTOMER_REPRESENTATIVE_CREATED" : "CUSTOMER_REPRESENTATIVE_UPDATED", "dbo.CustomerRepresentatives", id,
            AuditSql.Json(("customerId", customerId), ("roleCode", input.RoleCode), ("fullName", input.FullName), ("isLegalRepresentative", input.IsLegalRepresentative)));
        input.Id = id;
        return ProfileChangeOutcome.Saved;
    });

    public Task<ProfileChangeOutcome> DeleteRepresentativeAsync(Guid customerId, Guid id, string? stamp, Guid actor) =>
        SoftDelete(CustomerProfileSql.DeleteRepresentative, CustomerProfileSql.RepresentativeExists, "CUSTOMER_REPRESENTATIVE_DELETED", "dbo.CustomerRepresentatives", customerId, id, stamp, actor);

    public async Task<IReadOnlyList<SystemAccountRow>> ListSystemAccountsAsync(Guid customerId)
    {
        var list = new List<SystemAccountRow>();
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Command(cn, null, CustomerProfileSql.ListAccounts, ("@customer", customerId));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), Text(r, 2), Text(r, 3), Text(r, 4), Text(r, 5), Text(r, 6), Text(r, 7), Text(r, 8), Text(r, 9), Text(r, 10), Text(r, 11),
                Date(r, 12), Date(r, 13), r.GetBoolean(14), r.GetBoolean(15), r.IsDBNull(16) ? null : r.GetDateTime(16), r.GetString(17), r.GetString(18), Text(r, 19), r.GetInt32(20), Stamp(r, 21)));
        return list;
    }

    public Task<ProfileChangeOutcome> SaveSystemAccountAsync(Guid customerId, Guid accountId, SystemAccountInput input, SecretChange password, SecretChange pin, Guid actor) => InTransaction(async (cn, tx) =>
    {
        var isNew = input.Id is null;
        if (!isNew && input.Id != accountId) throw new ArgumentException("accountId must equal input.Id for an existing account.", nameof(accountId));
        var id = accountId;
        await using (var cmd = Command(cn, tx, isNew ? CustomerProfileSql.InsertAccount : CustomerProfileSql.UpdateAccount, ("@id", id), ("@customer", customerId), ("@by", actor)))
        {
            AddAccount(cmd, input, password.Value, pin.Value);
            if (!isNew)
            {
                if (RowVersion(input.Stamp) is not { } rv) return ProfileChangeOutcome.Conflict;
                cmd.Parameters.Add("@rv", SqlDbType.Timestamp).Value = rv;
                cmd.Parameters.AddWithValue("@setPwd", password.Changes);
                cmd.Parameters.AddWithValue("@setPin", pin.Changes);
            }
            if (await cmd.ExecuteNonQueryAsync() != 1) return await Missing(cn, tx, CustomerProfileSql.AccountExists, id, customerId);
        }
        await Audit(cn, tx, actor, isNew ? "CUSTOMER_SYSTEM_ACCOUNT_CREATED" : "CUSTOMER_SYSTEM_ACCOUNT_UPDATED", "dbo.CustomerSystemAccounts", id,
            AuditSql.Json(("customerId", customerId), ("systemType", input.SystemType), ("providerName", input.ProviderName), ("username", input.Username),
                ("passwordChanged", password.Changes), ("pinChanged", pin.Changes)));
        input.Id = id;
        return ProfileChangeOutcome.Saved;
    });

    public Task<ProfileChangeOutcome> DeleteSystemAccountAsync(Guid customerId, Guid id, string? stamp, Guid actor) =>
        SoftDelete(CustomerProfileSql.DeleteAccount, CustomerProfileSql.AccountExists, "CUSTOMER_SYSTEM_ACCOUNT_DELETED", "dbo.CustomerSystemAccounts", customerId, id, stamp, actor);

    public async Task<SecretRevealResult> RevealSecretAsync(Guid customerId, Guid accountId, SecretField field, Guid actor, string? ip)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var value = await Scalar(cn, tx, field == SecretField.Pin ? CustomerProfileSql.GetPinCipher : CustomerProfileSql.GetPasswordCipher, ("@id", accountId), ("@customer", customerId));
            if (value is not byte[] cipher) return new(ProfileChangeOutcome.NotFound, null);
            // Always durable, whatever Security:Audit:RequireDurableAudit says: a secret is never shown without a trace.
            await AuditSql.WriteOrThrowAsync(cn, tx, requireDurable: true, actor, "CUSTOMER_SECRET_REVEALED", "dbo.CustomerSystemAccounts", accountId.ToString(), "{}",
                AuditSql.Json(("customerId", customerId), ("field", field.ToString())), ip);
            await tx.CommitAsync();
            return new(ProfileChangeOutcome.Saved, cipher);
        }
        catch (AuditUnavailableException)
        {
            return new(ProfileChangeOutcome.AuditUnavailable, null);
        }
    }

    // ---- helpers (also used by SqlCustomerImportStore) ------------------------------------------------------

    internal static void AddProfile(SqlCommand cmd, CustomerProfileVm vm)
    {
        Add(cmd, "@ShortName", vm.ShortName); Add(cmd, "@LegalForm", vm.LegalForm); Add(cmd, "@CharterCapital", vm.CharterCapital); Add(cmd, "@EstablishedDate", vm.EstablishedDate);
        Add(cmd, "@MainIndustryCode", vm.MainIndustryCode); Add(cmd, "@MainIndustryName", vm.MainIndustryName); Add(cmd, "@BusinessLine", vm.BusinessLine);
        Add(cmd, "@TaxAuthority", vm.TaxAuthority); Add(cmd, "@TaxOfficerName", vm.TaxOfficerName); Add(cmd, "@TaxOfficerPhone", vm.TaxOfficerPhone);
        Add(cmd, "@BusinessStatus", vm.BusinessStatus); Add(cmd, "@BusinessStatusDate", vm.BusinessStatusDate); Add(cmd, "@ScaleNote", vm.ScaleNote);
        Add(cmd, "@SourceCode", vm.SourceCode); Add(cmd, "@SourceNote", vm.SourceNote); Add(cmd, "@ParentCustomerId", vm.ParentCustomerId); Add(cmd, "@Note", vm.Note);
    }

    internal static void AddRepresentative(SqlCommand cmd, RepresentativeInput input)
    {
        Add(cmd, "@RoleCode", input.RoleCode); cmd.Parameters.AddWithValue("@IsLegalRepresentative", input.IsLegalRepresentative); cmd.Parameters.AddWithValue("@IsPrimaryContact", input.IsPrimaryContact);
        Add(cmd, "@FullName", input.FullName); Add(cmd, "@Gender", input.Gender); Add(cmd, "@DateOfBirth", input.DateOfBirth); Add(cmd, "@IdentityNumber", input.IdentityNumber);
        Add(cmd, "@IdentityIssueDate", input.IdentityIssueDate); Add(cmd, "@IdentityIssuePlace", input.IdentityIssuePlace); Add(cmd, "@Address", input.Address);
        Add(cmd, "@Email", input.Email); Add(cmd, "@Phone", input.Phone); Add(cmd, "@Note", input.Note); cmd.Parameters.AddWithValue("@SortOrder", input.SortOrder);
    }

    internal static void AddAccount(SqlCommand cmd, SystemAccountInput input, byte[]? password, byte[]? pin)
    {
        Add(cmd, "@SystemType", input.SystemType); Add(cmd, "@ProviderName", input.ProviderName); Add(cmd, "@SystemName", input.SystemName); Add(cmd, "@LoginUrl", input.LoginUrl);
        Add(cmd, "@Username", input.Username); Add(cmd, "@LoginPhone", input.LoginPhone); Add(cmd, "@StoreName", input.StoreName); Add(cmd, "@UnitCode", input.UnitCode);
        Add(cmd, "@BankAccountNumber", input.BankAccountNumber); Add(cmd, "@CertificateType", input.CertificateType); Add(cmd, "@CertificateSerial", input.CertificateSerial);
        Add(cmd, "@ValidFrom", input.ValidFrom); Add(cmd, "@ExpiresAt", input.ExpiresAt); Add(cmd, "@Status", input.Status); Add(cmd, "@ManagedBy", input.ManagedBy);
        Add(cmd, "@Note", input.Note); cmd.Parameters.AddWithValue("@SortOrder", input.SortOrder);
        cmd.Parameters.Add("@pwd", SqlDbType.VarBinary, 4000).Value = (object?)password ?? DBNull.Value;
        cmd.Parameters.Add("@pin", SqlDbType.VarBinary, 4000).Value = (object?)pin ?? DBNull.Value;
    }

    private async Task<ProfileChangeOutcome> SoftDelete(string sql, string existsSql, string action, string entity, Guid customerId, Guid id, string? stamp, Guid actor) =>
        await InTransaction(async (cn, tx) =>
        {
            if (RowVersion(stamp) is not { } rv) return ProfileChangeOutcome.Conflict;
            await using (var cmd = Command(cn, tx, sql, ("@id", id), ("@customer", customerId), ("@by", actor)))
            {
                cmd.Parameters.Add("@rv", SqlDbType.Timestamp).Value = rv;
                if (await cmd.ExecuteNonQueryAsync() != 1) return await Missing(cn, tx, existsSql, id, customerId);
            }
            await Audit(cn, tx, actor, action, entity, id, AuditSql.Json(("customerId", customerId), ("isDeleted", true)));
            return ProfileChangeOutcome.Saved;
        });

    private async Task<ProfileChangeOutcome> InTransaction(Func<SqlConnection, SqlTransaction, Task<ProfileChangeOutcome>> work)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var outcome = await work(cn, tx);
            if (outcome == ProfileChangeOutcome.Saved) await tx.CommitAsync();
            else await tx.RollbackAsync();
            return outcome;
        }
        catch (AuditUnavailableException)
        {
            return ProfileChangeOutcome.AuditUnavailable; // disposing the transaction rolls the change back
        }
    }

    private Task Audit(SqlConnection cn, SqlTransaction tx, Guid actor, string action, string entity, Guid target, string newValues) =>
        AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, action, entity, target.ToString(), "{}", newValues, null);

    // The update matched no row: either it is gone (or belongs to another customer) or the stamp is stale.
    private static async Task<ProfileChangeOutcome> Missing(SqlConnection cn, SqlTransaction tx, string existsSql, Guid id, Guid customerId) =>
        Convert.ToInt32(await Scalar(cn, tx, existsSql, ("@id", id), ("@customer", customerId))) == 0 ? ProfileChangeOutcome.NotFound : ProfileChangeOutcome.Conflict;

    internal static SqlCommand Command(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    internal static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Command(cn, tx, sql, args);
        return await cmd.ExecuteScalarAsync();
    }

    internal static void Add(SqlCommand cmd, string name, string? value) =>
        cmd.Parameters.AddWithValue(name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim());

    internal static void Add(SqlCommand cmd, string name, DateOnly? value) =>
        cmd.Parameters.Add(name, SqlDbType.Date).Value = value is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

    internal static void Add(SqlCommand cmd, string name, decimal? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 18;
        p.Scale = 0;
        p.Value = (object?)value ?? DBNull.Value;
    }

    internal static void Add(SqlCommand cmd, string name, Guid? value) =>
        cmd.Parameters.Add(name, SqlDbType.UniqueIdentifier).Value = (object?)value ?? DBNull.Value;

    internal static string? Text(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateOnly? Date(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(r.GetDateTime(i));
    internal static string Stamp(SqlDataReader r, int i) => Convert.ToHexString((byte[])r[i]);

    internal static byte[]? RowVersion(string? stamp)
    {
        if (string.IsNullOrEmpty(stamp) || stamp.Length != 16) return null;
        try { return Convert.FromHexString(stamp); }
        catch (FormatException) { return null; }
    }
}
