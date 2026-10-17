using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261013-CRM-PHASE0-001
public sealed partial class PortalDb
{
    private static async Task<HrSchemaOptions> ReadHrSchemaAsync(SqlConnection connection, SqlTransaction? transaction = null)
    {
        const string sql = """
SELECT CAST(CASE WHEN COL_LENGTH('dbo.Departments','BranchId') IS NOT NULL THEN 1 ELSE 0 END AS bit),
       CAST(CASE WHEN COL_LENGTH('dbo.Teams','BranchId') IS NOT NULL THEN 1 ELSE 0 END AS bit),
       CAST(CASE WHEN COL_LENGTH('dbo.Teams','Description') IS NOT NULL THEN 1 ELSE 0 END AS bit);
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var options = new HrSchemaOptions(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2), []);
        if (options.TeamHasBranch && !options.DepartmentHasBranch)
            throw new InvalidOperationException("Incompatible HR branch schema.");
        return options;
    }

    public async Task<HrSchemaOptions> GetHrSchemaOptionsAsync()
    {
        await using var connection = Open();
        await connection.OpenAsync();
        var schema = await ReadHrSchemaAsync(connection);
        if (!schema.DepartmentHasBranch) return schema;
        var branches = new List<HrBranchOption>();
        await using var command = new SqlCommand("""
SELECT b.Id,b.Code,b.Name FROM dbo.Branches b
JOIN dbo.Companies c ON c.Id=b.CompanyId AND c.IsActive=1 AND c.IsDeleted=0
WHERE b.IsActive=1 AND b.IsDeleted=0 ORDER BY b.Code,b.Name;
""", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) branches.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return schema with { Branches = branches };
    }

    internal static string AdaptTeamWriteSql(string sql, HrSchemaOptions schema, bool update)
    {
        if (!schema.TeamHasDescription)
            sql = update ? sql.Replace("Description=@description,", "")
                : sql.Replace("Name,Description,SortOrder", "Name,SortOrder").Replace("@name,@description,@sortOrder", "@name,@sortOrder");
        if (schema.TeamHasBranch)
        {
            const string branch = "(SELECT BranchId FROM dbo.Departments WHERE Id=@departmentId)";
            sql = update ? sql.Replace("SET DepartmentId=", $"SET BranchId={branch},DepartmentId=")
                : sql.Replace("Id,DepartmentId", "Id,BranchId,DepartmentId").Replace("NEWID(),@departmentId", $"NEWID(),{branch},@departmentId");
        }
        return sql;
    }

    private static async Task<bool> TeamCodeExistsInTransactionAsync(SqlConnection connection, SqlTransaction transaction, string code, Guid? id)
    {
        // Preserve the existing global Code contract even when the live unique index is branch-scoped.
        await using var command = new SqlCommand("SELECT COUNT(1) FROM dbo.Teams WHERE Code=@code AND IsDeleted=0 AND (@id IS NULL OR Id<>@id)", connection, transaction);
        command.Parameters.Add("@code", System.Data.SqlDbType.VarChar, 50).Value = TeamRules.NormalizeCode(code);
        command.Parameters.Add("@id", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)id ?? DBNull.Value;
        return Convert.ToInt32(await command.ExecuteScalarAsync()) != 0;
    }

    // No cached cookie/UserTeams scope: every call resolves effective HR dates in Asia/Bangkok.
    // This is the organization source for Phase 1; it does not implement Customer row scope yet.
    public async Task<EmployeeAccessContext> GetEmployeeAccessContextAsync(Guid userId)
    {
        var date = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        await using var connection = Open();
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Users WHERE Id=@userId AND IsActive=1 AND IsDeleted=0 AND Status='ACTIVE') THEN 1 ELSE 0 END AS bit);
SELECT e.Id,e.IsActive,e.IsDeleted,s.Code,e.StartDate,e.EndDate
FROM dbo.Employees e LEFT JOIN dbo.EmployeeStatuses s ON s.Id=e.CurrentStatusId
WHERE e.UserId=@userId;
SELECT a.Id,a.EmployeeId,a.BranchId,a.DepartmentId,a.TeamId,a.ValidFrom,a.ValidTo,a.IsPrimary,a.IsCurrent,
 CAST(CASE WHEN b.IsActive=1 AND b.IsDeleted=0 AND c.IsActive=1 AND c.IsDeleted=0
   AND (a.DepartmentId IS NULL OR (d.IsActive=1 AND d.IsDeleted=0 AND d.BranchId=a.BranchId))
   AND (a.TeamId IS NULL OR (t.IsActive=1 AND t.IsDeleted=0 AND t.BranchId=a.BranchId
     AND ((t.DepartmentId IS NULL AND a.DepartmentId IS NULL) OR t.DepartmentId=a.DepartmentId)))
 THEN 1 ELSE 0 END AS bit) OrganizationIsValid
FROM dbo.EmployeeAssignments a JOIN dbo.Employees e ON e.Id=a.EmployeeId AND e.UserId=@userId
JOIN dbo.Branches b ON b.Id=a.BranchId JOIN dbo.Companies c ON c.Id=b.CompanyId
LEFT JOIN dbo.Departments d ON d.Id=a.DepartmentId LEFT JOIN dbo.Teams t ON t.Id=a.TeamId
WHERE a.IsCurrent=1 AND a.IsPrimary=1 AND a.ValidFrom<=@date AND (a.ValidTo IS NULL OR a.ValidTo>=@date);
""", connection);
        command.Parameters.Add("@userId", System.Data.SqlDbType.UniqueIdentifier).Value = userId;
        command.Parameters.Add("@date", System.Data.SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        var employees = new List<EmployeeAccessState>();
        var assignments = new List<EmployeeOrganizationAssignment>();
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        if (!reader.GetBoolean(0)) return new(false, false, null);
        await reader.NextResultAsync();
        while (await reader.ReadAsync()) employees.Add(new(reader.GetGuid(0), reader.GetBoolean(1), reader.GetBoolean(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
            reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5))));
        await reader.NextResultAsync();
        while (await reader.ReadAsync()) assignments.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
            DateOnly.FromDateTime(reader.GetDateTime(5)), reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
            reader.GetBoolean(7), reader.GetBoolean(8), reader.GetBoolean(9)));
        return EmployeeAssignmentRules.Resolve(employees, assignments, date);
    }
}
