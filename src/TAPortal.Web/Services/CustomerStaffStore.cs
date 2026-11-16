using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// Khách hàng → Tổng quan → "Nhân sự phụ trách" (migration 041, operator 2026-11-13): at most one managing employee and any
// number of executing employees per customer. The task list counts these customers as the employee's own, next to the
// customers of the employee's tasks and contracts.
public sealed record CustomerStaffMember(Guid EmployeeId, string Code, string Name, string Role);
public sealed record CustomerStaffOption(Guid Id, string Code, string Name);
public sealed record CustomerStaffView(IReadOnlyList<CustomerStaffMember> Members, IReadOnlyList<CustomerStaffOption> Employees)
{
    public CustomerStaffMember? Manager => Members.FirstOrDefault(m => m.Role == CustomerStaffPolicy.Manager);
    public IEnumerable<CustomerStaffMember> Executors => Members.Where(m => m.Role == CustomerStaffPolicy.Executor);
}

public enum CustomerStaffOutcome { Saved, NotFound, Invalid }

public static class CustomerStaffPolicy
{
    public const string Manager = "MANAGER";
    public const string Executor = "EXECUTOR";
    public const int MaxExecutors = 50;

    /// <summary>The manager is never also listed as an executor; duplicates and empty ids are dropped.</summary>
    public static List<Guid> Executors(Guid? manager, IEnumerable<Guid>? executors) =>
        (executors ?? []).Where(e => e != Guid.Empty && e != manager).Distinct().ToList();
}

public interface ICustomerStaffStore
{
    Task<CustomerStaffView> GetAsync(Guid customerId);
    Task<CustomerStaffOutcome> SaveAsync(Guid customerId, Guid? manager, IReadOnlyCollection<Guid> executors, Guid actor);
}

public sealed class SqlCustomerStaffStore(IConfiguration configuration) : ICustomerStaffStore
{
    private const string Members = """
SELECT s.EmployeeId,e.EmployeeCode,e.FullName,s.Role FROM dbo.CustomerStaff s JOIN dbo.Employees e ON e.Id=s.EmployeeId
WHERE s.CustomerId=@c ORDER BY CASE s.Role WHEN 'MANAGER' THEN 0 ELSE 1 END, e.FullName;
SELECT Id,EmployeeCode,FullName FROM dbo.Employees WHERE IsDeleted=0 AND IsActive=1 ORDER BY FullName;
""";
    private const string CustomerExists = "SELECT COUNT(1) FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK) WHERE Id=@c AND IsDeleted=0;";
    private const string ActiveEmployees = "SELECT Id FROM dbo.Employees WHERE IsDeleted=0 AND IsActive=1;";
    private const string Clear = "DELETE FROM dbo.CustomerStaff WHERE CustomerId=@c;";
    private const string Insert = "INSERT dbo.CustomerStaff(CustomerId,EmployeeId,Role,CreatedBy) VALUES(@c,@e,@r,@by);";

    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<CustomerStaffView> GetAsync(Guid customerId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(Members, cn);
        cmd.Parameters.AddWithValue("@c", customerId);
        await using var r = await cmd.ExecuteReaderAsync();
        var members = new List<CustomerStaffMember>();
        while (await r.ReadAsync()) members.Add(new(r.GetGuid(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetString(2), r.GetString(3)));
        await r.NextResultAsync();
        var employees = new List<CustomerStaffOption>();
        while (await r.ReadAsync()) employees.Add(new(r.GetGuid(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetString(2)));
        return new(members, employees);
    }

    public async Task<CustomerStaffOutcome> SaveAsync(Guid customerId, Guid? manager, IReadOnlyCollection<Guid> executors, Guid actor)
    {
        var list = CustomerStaffPolicy.Executors(manager, executors);
        if (list.Count > CustomerStaffPolicy.MaxExecutors) return CustomerStaffOutcome.Invalid;
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        SqlCommand Cmd(string sql) { var c = new SqlCommand(sql, cn, tx); c.Parameters.AddWithValue("@c", customerId); return c; }
        await using (var exists = Cmd(CustomerExists))
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) { await tx.RollbackAsync(); return CustomerStaffOutcome.NotFound; }
        var active = new HashSet<Guid>();
        await using (var e = new SqlCommand(ActiveEmployees, cn, tx))
        await using (var r = await e.ExecuteReaderAsync())
            while (await r.ReadAsync()) active.Add(r.GetGuid(0));
        if ((manager is Guid m && !active.Contains(m)) || list.Any(x => !active.Contains(x))) { await tx.RollbackAsync(); return CustomerStaffOutcome.Invalid; }
        await using (var clear = Cmd(Clear)) await clear.ExecuteNonQueryAsync();
        foreach (var (id, role) in (manager is Guid mm ? [(mm, CustomerStaffPolicy.Manager)] : Array.Empty<(Guid, string)>()).Concat(list.Select(x => (x, CustomerStaffPolicy.Executor))))
        {
            await using var ins = Cmd(Insert);
            ins.Parameters.AddWithValue("@e", id);
            ins.Parameters.AddWithValue("@r", role);
            ins.Parameters.AddWithValue("@by", actor);
            await ins.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return CustomerStaffOutcome.Saved;
    }
}
