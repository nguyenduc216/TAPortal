using Microsoft.Data.SqlClient;
using TAPortal.Shared;

namespace TACustomerPortal.Web.Services;

/// <summary>A company signing itself up: the company (dbo.Customers), its first user (dbo.Users) and the OWNER membership.</summary>
public sealed record SignupRequest(string? TaxCode, string CompanyName, string CustomerType, string? Address, string ContactName, string Email, string Phone, string PasswordHash, string? Ip, string? TermsVersion = null);

public enum SignupOutcome { Created, TaxCodeTaken, EmailTaken, AuditUnavailable }

public sealed record SignupResult(SignupOutcome Outcome, Guid UserId = default, Guid CustomerId = default, string? CustomerCode = null);

public interface ISignupStore
{
    Task<SignupResult> RegisterAsync(SignupRequest request);
}

/// <summary>
/// Operator decision 2026-11-06: a company that signs up uses the portal at once (no T.A approval); it picks a service
/// package and pays by transfer, which T.A confirms as before. One transaction: tax code and email are checked under
/// locks, the customer code is the tax code, the email is the username, and the sign-up is audited.
/// Operator 2026-11-12: an individual may sign up without a tax code (code "CN" + 8 random digits), and the password is a
/// generated one sent by email, so the user must change it at first login (MustChangePassword = 1).
/// </summary>
public sealed class SqlSignupStore(IConfiguration configuration) : ISignupStore
{
    public const string AuditAction = "CUSTOMER_SELF_REGISTERED";

    public async Task<SignupResult> RegisterAsync(SignupRequest x)
    {
        await using var cn = new SqlConnection(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            if (x.TaxCode is not null && await Exists(cn, tx, "SELECT 1 FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE TaxCode=@v AND IsDeleted=0", x.TaxCode))
            { await tx.RollbackAsync(); return new(SignupOutcome.TaxCodeTaken); }
            var normalizedEmail = x.Email.ToUpperInvariant();
            if (await Exists(cn, tx, "SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE (NormalizedUsername=@v OR NormalizedEmail=@v) AND IsDeleted=0", normalizedEmail))
            { await tx.RollbackAsync(); return new(SignupOutcome.EmailTaken); }

            // The tax code is the customer code; a legacy customer may already use it as a code without a tax code.
            string code;
            if (x.TaxCode is not null)
                code = await Exists(cn, tx, "SELECT 1 FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE Code=@v AND IsDeleted=0", x.TaxCode)
                    ? "DK" + x.TaxCode : x.TaxCode;
            else
            {
                do code = "CN" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
                while (await Exists(cn, tx, "SELECT 1 FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE Code=@v AND IsDeleted=0", code));
            }
            var customerId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            await Run(cn, tx, "INSERT dbo.Customers(Id,Code,Name,TaxCode,Email,Phone,Address,CustomerType,Status,IsActive,CreatedBy) VALUES(@id,@code,@name,@tax,@email,@phone,@address,@type,'ACTIVE',1,@u)",
                ("@id", customerId), ("@code", code), ("@name", x.CompanyName), ("@tax", x.TaxCode), ("@email", x.Email), ("@phone", x.Phone), ("@address", x.Address), ("@type", x.CustomerType), ("@u", userId));
            await Run(cn, tx, "INSERT dbo.Users(Id,Username,NormalizedUsername,DisplayName,Email,NormalizedEmail,Phone,PasswordHash,MustChangePassword,Status,IsActive,CreatedBy) VALUES(@u,@email,@ne,@display,@email,@ne,@phone,@hash,1,'ACTIVE',1,@u)",
                ("@u", userId), ("@email", x.Email), ("@ne", normalizedEmail), ("@display", x.ContactName), ("@phone", x.Phone), ("@hash", x.PasswordHash));
            await Run(cn, tx, "INSERT dbo.CustomerPortalMemberships(Id,UserId,CustomerId,PortalRole,IsActive,CreatedBy) VALUES(NEWID(),@u,@c,'OWNER',1,@u)",
                ("@u", userId), ("@c", customerId));
            // No password data and no contact details in the audit row: ids, code and type only.
            await AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), userId, AuditAction, "dbo.Customers", customerId.ToString(),
                "{}", AuditSql.Json(("code", code), ("customerType", x.CustomerType), ("userId", userId), ("role", "OWNER"), ("via", "CUSTOMER_PORTAL"), ("termsAccepted", x.TermsVersion)), x.Ip);
            await tx.CommitAsync();
            return new(SignupOutcome.Created, userId, customerId, code);
        }
        catch (AuditUnavailableException) { await tx.RollbackAsync(); return new(SignupOutcome.AuditUnavailable); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent sign-up won the race on a unique index.
            await tx.RollbackAsync();
            return new(ex.Message.Contains("Users", StringComparison.OrdinalIgnoreCase) ? SignupOutcome.EmailTaken : SignupOutcome.TaxCodeTaken);
        }
    }

    private static async Task<bool> Exists(SqlConnection cn, SqlTransaction tx, string sql, string value)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue("@v", value);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task Run(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object? Value)[] values)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
