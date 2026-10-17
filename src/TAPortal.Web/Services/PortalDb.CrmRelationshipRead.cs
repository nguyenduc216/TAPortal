using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-016
public sealed partial class PortalDb
{
    public async Task<CrmRelationshipDetail?> GetCrmRelationshipDetailAsync(Guid customerId, Guid actorId)
    {
        if (customerId == Guid.Empty || actorId == Guid.Empty) return null;
        // The existing scoped customer query is the disclosure boundary. Action-specific
        // authorization is checked again by every writer in its own transaction.
        var customer = (await GetCustomersAsync(actorId)).FirstOrDefault(c => c.Id == customerId);
        if (customer is null) return null;
        await using var cn = Open();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var assign = await AuthorizeCustomerMutationAsync(cn, tx, actorId, customerId, CustomerMutationAction.Assign);
            var share = await AuthorizeCustomerMutationAsync(cn, tx, actorId, customerId, CustomerMutationAction.Share);
            var team = await AuthorizeCustomerMutationAsync(cn, tx, actorId, customerId, CustomerMutationAction.Team);
            if (!assign && !share && !team) { await tx.RollbackAsync(); return null; }

            async Task<List<CrmRelationshipRow>> Relations(string sql)
            {
                var rows = new List<CrmRelationshipRow>();
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@customerId", SqlDbType.UniqueIdentifier).Value = customerId;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), DateOnly.FromDateTime(reader.GetDateTime(4)),
                    reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)), reader.GetBoolean(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), (byte[])reader[8]));
                return rows;
            }
            async Task<List<CrmTargetOption>> Options(string sql)
            {
                var rows = new List<CrmTargetOption>();
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@customerId", SqlDbType.UniqueIdentifier).Value = customerId;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                return rows;
            }
            var employees = await Relations("SELECT r.Id,r.EmployeeId,u.DisplayName,CAST(NULL AS nvarchar(100)),r.ValidFrom,r.ValidTo,r.IsActive,r.RoleCode,r.RowVersion FROM dbo.CustomerEmployeeAssignments r JOIN dbo.Employees e ON e.Id=r.EmployeeId JOIN dbo.Users u ON u.Id=e.UserId WHERE r.CustomerId=@customerId ORDER BY r.IsActive DESC,r.ValidFrom DESC");
            var branches = await Relations("SELECT r.Id,r.BranchId,b.Name,CONVERT(nvarchar(100),co.Name),r.ValidFrom,r.ValidTo,r.IsActive,CAST(NULL AS varchar(50)),r.RowVersion FROM dbo.CustomerBranchShares r JOIN dbo.Branches b ON b.Id=r.BranchId JOIN dbo.Companies co ON co.Id=b.CompanyId WHERE r.CustomerId=@customerId ORDER BY r.IsActive DESC,r.ValidFrom DESC");
            var teams = await Relations("SELECT r.Id,r.TeamId,t.Name,CONVERT(nvarchar(100),b.Name),r.ValidFrom,r.ValidTo,r.IsActive,CAST(NULL AS varchar(50)),r.RowVersion FROM dbo.CustomerServiceTeams r JOIN dbo.Teams t ON t.Id=r.TeamId JOIN dbo.Branches b ON b.Id=t.BranchId WHERE r.CustomerId=@customerId ORDER BY r.IsActive DESC,r.ValidFrom DESC");
            var employeeOptions = assign ? await Options("SELECT e.Id,u.DisplayName,CONVERT(nvarchar(100),b.Name) FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE' JOIN dbo.EmployeeStatuses s ON s.Id=e.CurrentStatusId AND s.Code IN ('ACTIVE','PROBATION','MATERNITY') JOIN dbo.EmployeeAssignments a ON a.EmployeeId=e.Id AND a.IsCurrent=1 AND a.IsPrimary=1 JOIN dbo.Branches b ON b.Id=a.BranchId AND b.IsActive=1 AND b.IsDeleted=0 JOIN dbo.Companies co ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0 JOIN dbo.Customers c ON c.Id=@customerId WHERE e.IsActive=1 AND e.IsDeleted=0 AND (e.StartDate IS NULL OR e.StartDate<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))) AND (e.EndDate IS NULL OR e.EndDate>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))) AND a.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (a.ValidTo IS NULL OR a.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))) AND (b.Id=c.OwnerBranchId OR EXISTS (SELECT 1 FROM dbo.CustomerBranchShares share WHERE share.CustomerId=c.Id AND share.BranchId=b.Id AND share.IsActive=1 AND share.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (share.ValidTo IS NULL OR share.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))) AND (SELECT COUNT(*) FROM dbo.Employees other WHERE other.UserId=u.Id AND other.IsDeleted=0)=1 AND (SELECT COUNT(*) FROM dbo.EmployeeAssignments posting WHERE posting.EmployeeId=e.Id AND posting.IsCurrent=1 AND posting.IsPrimary=1 AND posting.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (posting.ValidTo IS NULL OR posting.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))=1 AND (a.DepartmentId IS NULL OR EXISTS (SELECT 1 FROM dbo.Departments d WHERE d.Id=a.DepartmentId AND d.BranchId=b.Id AND d.IsActive=1 AND d.IsDeleted=0)) AND (a.TeamId IS NULL OR EXISTS (SELECT 1 FROM dbo.Teams t WHERE t.Id=a.TeamId AND t.BranchId=b.Id AND t.IsActive=1 AND t.IsDeleted=0 AND ((t.DepartmentId IS NULL AND a.DepartmentId IS NULL) OR t.DepartmentId=a.DepartmentId))) GROUP BY e.Id,u.DisplayName,b.Name ORDER BY u.DisplayName") : new();
            var branchOptions = share ? await Options("SELECT b.Id,b.Name,CONVERT(nvarchar(100),co.Name) FROM dbo.Branches b JOIN dbo.Companies co ON co.Id=b.CompanyId JOIN dbo.Customers c ON c.Id=@customerId JOIN dbo.Branches owner ON owner.Id=c.OwnerBranchId WHERE b.IsActive=1 AND b.IsDeleted=0 AND co.IsActive=1 AND co.IsDeleted=0 AND b.Id<>c.OwnerBranchId AND b.CompanyId=owner.CompanyId ORDER BY b.Name") : new();
            var teamOptions = team ? await Options("SELECT t.Id,t.Name,CONVERT(nvarchar(100),b.Name) FROM dbo.Teams t JOIN dbo.Branches b ON b.Id=t.BranchId JOIN dbo.Companies co ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0 JOIN dbo.Customers c ON c.Id=@customerId JOIN dbo.Branches owner ON owner.Id=c.OwnerBranchId LEFT JOIN dbo.Departments d ON d.Id=t.DepartmentId WHERE t.IsActive=1 AND t.IsDeleted=0 AND b.IsActive=1 AND b.IsDeleted=0 AND b.CompanyId=owner.CompanyId AND (t.DepartmentId IS NULL OR (d.IsActive=1 AND d.IsDeleted=0 AND d.BranchId=b.Id)) ORDER BY t.Name") : new();
            await tx.CommitAsync();
            return new(customer, employees, branches, teams, employeeOptions, branchOptions, teamOptions, assign, share, team);
        }
        catch { await tx.RollbackAsync(); throw; }
    }
}