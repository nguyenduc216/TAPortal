using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using static TAPortal.Web.Services.SqlCustomerProfileStore;

namespace TAPortal.Web.Services;

public sealed record CustomerMatch(Guid Id, string Code, string Name);

// One confirmed sheet of the workbook: CustomerId is the existing customer or a new id, NewCode is set only for a new
// customer. Passwords/PINs are encrypted here, bound to the account id, and only when the key is configured.
public sealed record CustomerImportItem(Guid CustomerId, string? NewCode, ImportedCustomer Data);

public enum CustomerImportStatus { Created, Updated, DuplicateCode, DuplicateTaxCode, InvalidTaxCode, NotFound, AuditUnavailable }

public sealed record CustomerImportResult(CustomerImportStatus Status, int RepresentativesAdded = 0, int RepresentativesSkipped = 0, int AccountsAdded = 0, int AccountsSkipped = 0,
    int SecretsSaved = 0, int SecretsNotSaved = 0, string? ConflictingCode = null);

public interface ICustomerImportStore
{
    // Live customers by normalized tax code.
    Task<IReadOnlyDictionary<string, CustomerMatch>> CustomersByTaxCodeAsync();
    // Everything for one customer in one transaction: create the customer when new, fill only the empty profile fields,
    // add the representatives and system accounts that are not there yet (re-importing the same file adds nothing),
    // and fill a password/PIN that an existing matching account does not have yet (e.g. imported before the key was set).
    Task<CustomerImportResult> ImportAsync(CustomerImportItem item, Guid actor);
}

public sealed class SqlCustomerImportStore(IConfiguration configuration, AdminAuditOptions audit, ICustomerSecretProtector secrets) : ICustomerImportStore
{
    private const string ByTaxCode = "SELECT Id,Code,Name,TaxCode FROM dbo.Customers WHERE IsDeleted=0 AND TaxCode IS NOT NULL;";
    private const string CustomerLive = "SELECT COUNT(*) FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE Id=@customer AND IsDeleted=0;";
    private const string FillCustomer = "UPDATE dbo.Customers SET Email=COALESCE(Email,@email),Address=COALESCE(Address,@address),UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@customer AND IsDeleted=0 AND ((Email IS NULL AND @email IS NOT NULL) OR (Address IS NULL AND @address IS NOT NULL));";
    private const string ProfileExists = "SELECT COUNT(*) FROM dbo.CustomerProfiles WITH (UPDLOCK, HOLDLOCK) WHERE CustomerId=@customer;";
    // Import never overwrites what staff already typed: each column is set only while it is empty.
    private const string FillProfile = """
UPDATE dbo.CustomerProfiles SET LegalForm=COALESCE(LegalForm,@LegalForm),CharterCapital=COALESCE(CharterCapital,@CharterCapital),EstablishedDate=COALESCE(EstablishedDate,@EstablishedDate),
 MainIndustryCode=COALESCE(MainIndustryCode,@MainIndustryCode),MainIndustryName=COALESCE(MainIndustryName,@MainIndustryName),TaxAuthority=COALESCE(TaxAuthority,@TaxAuthority),
 BusinessStatus=COALESCE(BusinessStatus,@BusinessStatus),ScaleNote=COALESCE(ScaleNote,@ScaleNote),Note=COALESCE(Note,@Note),UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE CustomerId=@customer;
""";
    private const string RepresentativeNames = "SELECT FullName FROM dbo.CustomerRepresentatives WHERE CustomerId=@customer AND IsDeleted=0;";
    private const string AccountKeys = "SELECT SystemType,Username,ProviderName,SystemName,BankAccountNumber,LoginUrl,Id,CAST(CASE WHEN PasswordCipher IS NULL THEN 0 ELSE 1 END AS bit),CAST(CASE WHEN PinCipher IS NULL THEN 0 ELSE 1 END AS bit) FROM dbo.CustomerSystemAccounts WHERE CustomerId=@customer AND IsDeleted=0;";
    private const string FillSecrets = """
UPDATE dbo.CustomerSystemAccounts SET PasswordCipher=COALESCE(PasswordCipher,@pwd),PinCipher=COALESCE(PinCipher,@pin),SecretChangedAt=SYSUTCDATETIME(),SecretChangedBy=@by,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND CustomerId=@customer AND IsDeleted=0 AND ((PasswordCipher IS NULL AND @pwd IS NOT NULL) OR (PinCipher IS NULL AND @pin IS NOT NULL));
""";

    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<IReadOnlyDictionary<string, CustomerMatch>> CustomersByTaxCodeAsync()
    {
        var map = new Dictionary<string, CustomerMatch>(StringComparer.Ordinal);
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Command(cn, null, ByTaxCode);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            if (TaxCodePolicy.Normalize(r.GetString(3)) is { } tax) map.TryAdd(tax, new(r.GetGuid(0), r.GetString(1), r.GetString(2)));
        return map;
    }

    // Duplicate keys: same person (name, case-insensitive); same account (type + login + provider/software + bank account + link).
    internal static string PersonKey(string name) => CustomerWorkbookParser.Key(name);
    internal static string AccountKey(string type, string? user, string? provider, string? system, string? bank, string? url) =>
        string.Join("|", type, CustomerWorkbookParser.Key(user), CustomerWorkbookParser.Key(provider), CustomerWorkbookParser.Key(system), CustomerWorkbookParser.Key(bank), CustomerWorkbookParser.Key(url));

    public async Task<CustomerImportResult> ImportAsync(CustomerImportItem item, Guid actor)
    {
        var d = item.Data;
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var created = item.NewCode is not null;
            var tax = TaxCodePolicy.Normalize(d.TaxCode);
            if (created)
            {
                var code = item.NewCode!.Trim().ToUpperInvariant();
                if (await Scalar(cn, tx, CustomerAdminSql.CodeTaken, ("@code", code)) is string) return new(CustomerImportStatus.DuplicateCode, ConflictingCode: code);
                if (tax is not null && !TaxCodePolicy.IsValid(tax)) return new(CustomerImportStatus.InvalidTaxCode);
                if (tax is not null && await Scalar(cn, tx, CustomerAdminSql.TaxCodeTaken, ("@tax", tax), ("@id", item.CustomerId)) is string other)
                    return new(CustomerImportStatus.DuplicateTaxCode, ConflictingCode: other);
                await using (var cmd = Command(cn, tx, CustomerAdminSql.Insert, ("@id", item.CustomerId), ("@code", code), ("@name", d.Name!.Trim()), ("@active", true), ("@by", actor)))
                {
                    Add(cmd, "@tax", tax); Add(cmd, "@email", d.Email); Add(cmd, "@phone", (string?)null); Add(cmd, "@address", d.Address);
                    await cmd.ExecuteNonQueryAsync();
                }
                await Audit(cn, tx, actor, "CUSTOMER_CREATED", "dbo.Customers", item.CustomerId, AuditSql.Json(("code", code), ("name", d.Name), ("taxCode", tax), ("source", "excel-import")));
            }
            else
            {
                if (Convert.ToInt32(await Scalar(cn, tx, CustomerLive, ("@customer", item.CustomerId))) != 1) return new(CustomerImportStatus.NotFound);
                await using var cmd = Command(cn, tx, FillCustomer, ("@customer", item.CustomerId), ("@by", actor));
                Add(cmd, "@email", d.Email); Add(cmd, "@address", d.Address);
                await cmd.ExecuteNonQueryAsync();
            }

            d.Profile.CustomerId = item.CustomerId;
            var hasProfile = Convert.ToInt32(await Scalar(cn, tx, ProfileExists, ("@customer", item.CustomerId))) == 1;
            await using (var cmd = Command(cn, tx, hasProfile ? FillProfile : CustomerProfileSql.InsertProfile, ("@customer", item.CustomerId), ("@by", actor)))
            {
                AddProfile(cmd, d.Profile);
                await cmd.ExecuteNonQueryAsync();
            }

            var names = new HashSet<string>(await Strings(cn, tx, RepresentativeNames, item.CustomerId, r => PersonKey(r.GetString(0))));
            int repsAdded = 0, repsSkipped = 0;
            foreach (var person in d.Representatives)
            {
                if (!names.Add(PersonKey(person.FullName))) { repsSkipped++; continue; }
                person.SortOrder = names.Count - 1;
                await using var cmd = Command(cn, tx, CustomerProfileSql.InsertRepresentative, ("@id", Guid.NewGuid()), ("@customer", item.CustomerId), ("@by", actor));
                AddRepresentative(cmd, person);
                await cmd.ExecuteNonQueryAsync();
                repsAdded++;
            }

            var existing = new Dictionary<string, (Guid Id, bool HasPassword, bool HasPin)>();
            await using (var cmd = Command(cn, tx, AccountKeys, ("@customer", item.CustomerId)))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    existing.TryAdd(AccountKey(r.GetString(0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4), Text(r, 5)), (r.GetGuid(6), r.GetBoolean(7), r.GetBoolean(8)));
            int accountsAdded = 0, accountsSkipped = 0, secretsSaved = 0, secretsNotSaved = 0;
            foreach (var a in d.Accounts)
            {
                var i = a.Input;
                var key = AccountKey(i.SystemType, i.Username, i.ProviderName, i.SystemName, i.BankAccountNumber, i.LoginUrl);
                var found = existing.TryGetValue(key, out var match);
                var id = found ? match.Id : Guid.NewGuid();
                byte[]? Secret(string? plain, SecretField field, bool alreadyStored)
                {
                    if (string.IsNullOrEmpty(plain) || alreadyStored) return null;
                    if (!secrets.Enabled) { secretsNotSaved++; return null; }
                    secretsSaved++;
                    return secrets.Protect(plain, CustomerSecretContext.For(item.CustomerId, id, field));
                }
                var pwd = Secret(a.Password, SecretField.Password, found && match.HasPassword);
                var pin = Secret(a.Pin, SecretField.Pin, found && match.HasPin);
                if (found)
                {
                    accountsSkipped++;
                    if (pwd is null && pin is null) continue;
                    await using var fill = Command(cn, tx, FillSecrets, ("@id", id), ("@customer", item.CustomerId), ("@by", actor));
                    fill.Parameters.Add("@pwd", SqlDbType.VarBinary, 4000).Value = (object?)pwd ?? DBNull.Value;
                    fill.Parameters.Add("@pin", SqlDbType.VarBinary, 4000).Value = (object?)pin ?? DBNull.Value;
                    await fill.ExecuteNonQueryAsync();
                    continue;
                }
                existing.Add(key, (id, pwd is not null, pin is not null));
                await using var cmd = Command(cn, tx, CustomerProfileSql.InsertAccount, ("@id", id), ("@customer", item.CustomerId), ("@by", actor));
                AddAccount(cmd, i, pwd, pin);
                await cmd.ExecuteNonQueryAsync();
                accountsAdded++;
            }

            // Counts only: no names, identity numbers, logins or secrets in the audit log.
            await Audit(cn, tx, actor, "CUSTOMER_PROFILE_IMPORTED", "dbo.Customers", item.CustomerId, AuditSql.Json(("created", created), ("representativesAdded", repsAdded),
                ("accountsAdded", accountsAdded), ("secretsSaved", secretsSaved), ("source", "excel-import")));
            await tx.CommitAsync();
            return new(created ? CustomerImportStatus.Created : CustomerImportStatus.Updated, repsAdded, repsSkipped, accountsAdded, accountsSkipped, secretsSaved, secretsNotSaved);
        }
        catch (AuditUnavailableException)
        {
            return new(CustomerImportStatus.AuditUnavailable); // disposing the transaction rolls everything back
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return ex.Message.Contains("'UX_Customers_TaxCode'", StringComparison.Ordinal) ? new(CustomerImportStatus.DuplicateTaxCode) : new(CustomerImportStatus.DuplicateCode, ConflictingCode: item.NewCode);
        }
    }

    private static async Task<List<string>> Strings(SqlConnection cn, SqlTransaction tx, string sql, Guid customerId, Func<SqlDataReader, string> read)
    {
        var list = new List<string>();
        await using var cmd = Command(cn, tx, sql, ("@customer", customerId));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(read(r));
        return list;
    }

    private Task Audit(SqlConnection cn, SqlTransaction tx, Guid actor, string action, string entity, Guid target, string newValues) =>
        AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, action, entity, target.ToString(), "{}", newValues, null);
}
