using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// Vietnamese tax code: 10 digits, or 10 digits + "-" + 3 digits for dependent units/branches.
// Normalization only removes whitespace; the branch dash and leading zeros are meaningful and kept.
public static partial class TaxCodePolicy
{
    [GeneratedRegex(@"^\d{10}(-\d{3})?$")]
    private static partial Regex Format();

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
    }

    public static bool IsValid(string? normalized) => normalized is null || Format().IsMatch(normalized);
}

public enum CustomerChangeOutcome { Saved, NotFound, DuplicateCode, DuplicateTaxCode, InvalidTaxCode }

public sealed record CustomerChangeResult(CustomerChangeOutcome Outcome, string? ConflictingCustomerCode = null);

public interface ICustomerAdminStore
{
    Task<CustomerEditVm?> GetAsync(Guid customerId);
    Task<CustomerChangeResult> CreateAsync(CustomerEditVm vm, Guid? actorId);
    Task<CustomerChangeResult> UpdateAsync(CustomerEditVm vm, Guid? actorId);
}

public static class CustomerAdminSql
{
    public const string Get = "SELECT Id,Code,Name,TaxCode,Email,Phone,Address,Status,IsActive FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id AND IsDeleted=0;";
    public const string CodeTaken = "SELECT TOP 1 Code FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE Code=@code AND IsDeleted=0;";
    // Range-locked under SERIALIZABLE so two application requests for the same tax code serialize.
    // Inactive (not deleted) customers count: reactivation must not create an active duplicate.
    public const string TaxCodeTaken = "SELECT TOP 1 Code FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE IsDeleted=0 AND TaxCode IS NOT NULL AND REPLACE(REPLACE(REPLACE(TaxCode,' ',''),CHAR(9),''),CHAR(160),'')=@tax AND Id<>@id;";
    public const string Insert = "INSERT dbo.Customers(Id,Code,Name,TaxCode,Email,Phone,Address,Status,IsActive,CreatedBy) VALUES(@id,@code,@name,@tax,@email,@phone,@address,'ACTIVE',@active,@by);";
    public const string Update = "UPDATE dbo.Customers SET Name=@name,TaxCode=@tax,Email=@email,Phone=@phone,Address=@address,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
}

public static class CustomerChangePolicy
{
    // Format applies only to new or changed tax codes so legacy values never block unrelated edits.
    public static CustomerChangeOutcome ValidateTaxCode(string? currentNormalized, string? requestedNormalized) =>
        requestedNormalized != null && requestedNormalized != currentNormalized && !TaxCodePolicy.IsValid(requestedNormalized)
            ? CustomerChangeOutcome.InvalidTaxCode
            : CustomerChangeOutcome.Saved;
}

public sealed class SqlCustomerAdminStore(IConfiguration configuration) : ICustomerAdminStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<CustomerEditVm?> GetAsync(Guid customerId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var vm = await Read(cn, tx, customerId);
        await tx.CommitAsync();
        return vm;
    }

    public Task<CustomerChangeResult> CreateAsync(CustomerEditVm vm, Guid? actorId) => Save(vm, actorId, isNew: true);
    public Task<CustomerChangeResult> UpdateAsync(CustomerEditVm vm, Guid? actorId) => Save(vm, actorId, isNew: false);

    private async Task<CustomerChangeResult> Save(CustomerEditVm vm, Guid? actorId, bool isNew)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            string? currentTax = null;
            if (isNew)
            {
                vm.Id = Guid.NewGuid();
                vm.Code = vm.Code.Trim().ToUpperInvariant();
                if (await Scalar(cn, tx, CustomerAdminSql.CodeTaken, ("@code", vm.Code)) is string)
                    return new(CustomerChangeOutcome.DuplicateCode, vm.Code);
            }
            else
            {
                var current = await Read(cn, tx, vm.Id);
                if (current is null) return new(CustomerChangeOutcome.NotFound);
                currentTax = TaxCodePolicy.Normalize(current.TaxCode);
            }

            var tax = TaxCodePolicy.Normalize(vm.TaxCode);
            if (CustomerChangePolicy.ValidateTaxCode(currentTax, tax) == CustomerChangeOutcome.InvalidTaxCode)
                return new(CustomerChangeOutcome.InvalidTaxCode);
            if (tax != null && await Scalar(cn, tx, CustomerAdminSql.TaxCodeTaken, ("@tax", tax), ("@id", vm.Id)) is string other)
                return new(CustomerChangeOutcome.DuplicateTaxCode, other);

            var args = new List<(string, object)>
            {
                ("@id", vm.Id), ("@name", vm.Name.Trim()), ("@tax", (object?)tax ?? DBNull.Value), ("@email", Db(vm.Email)),
                ("@phone", Db(vm.Phone)), ("@address", Db(vm.Address)), ("@active", vm.IsActive), ("@by", (object?)actorId ?? DBNull.Value)
            };
            if (isNew) args.Add(("@code", vm.Code));
            await using (var cmd = new SqlCommand(isNew ? CustomerAdminSql.Insert : CustomerAdminSql.Update, cn, tx))
            {
                foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
                if (await cmd.ExecuteNonQueryAsync() != 1) return new(CustomerChangeOutcome.NotFound);
            }
            await tx.CommitAsync();
            return new(CustomerChangeOutcome.Saved);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Database-level uniqueness (UX_Customers_Code, or the optional UX_Customers_TaxCode from migration 009).
            return ex.Message.Contains("'UX_Customers_TaxCode'", StringComparison.Ordinal) ? new(CustomerChangeOutcome.DuplicateTaxCode) : new(CustomerChangeOutcome.DuplicateCode, vm.Code);
        }
    }

    private static async Task<CustomerEditVm?> Read(SqlConnection cn, SqlTransaction tx, Guid id)
    {
        await using var cmd = new SqlCommand(CustomerAdminSql.Get, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        string? Text(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new CustomerEditVm { Id = r.GetGuid(0), Code = r.GetString(1), Name = r.GetString(2), TaxCode = Text(3), Email = Text(4), Phone = Text(5), Address = Text(6), Status = r.GetString(7), IsActive = r.GetBoolean(8) };
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteScalarAsync();
    }
}
