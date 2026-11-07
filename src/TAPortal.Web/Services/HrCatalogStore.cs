using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// Admin → Nhân sự → Phòng ban / Nhóm / Chức danh (dbo.Departments per branch, dbo.Teams per branch and optionally per
// department, dbo.JobTitles). Code (and the branch of a department or team) is fixed after creation; deletion is soft and
// refused while an active employee is currently assigned (or, for a department, while an active team belongs to it).
public enum HrCatalogKind { Department, Team, JobTitle }

public sealed record HrCatalogInfo(HrCatalogKind Kind, string Controller, string Title, string Singular, string Icon, string Entity,
    string ViewPermission, string CreatePermission, string UpdatePermission, string DeletePermission, int CodeLength);

public static class HrCatalogs
{
    public static readonly HrCatalogInfo Department = new(HrCatalogKind.Department, "Departments", "Phòng ban", "phòng ban", "building", "dbo.Departments",
        AdminPermissions.DepartmentsView, AdminPermissions.DepartmentsCreate, AdminPermissions.DepartmentsUpdate, AdminPermissions.DepartmentsDelete, 50);
    public static readonly HrCatalogInfo Team = new(HrCatalogKind.Team, "Teams", "Nhóm", "nhóm", "users-group", "dbo.Teams",
        AdminPermissions.TeamsView, AdminPermissions.TeamsCreate, AdminPermissions.TeamsUpdate, AdminPermissions.TeamsDelete, 50);
    public static readonly HrCatalogInfo JobTitle = new(HrCatalogKind.JobTitle, "JobTitles", "Chức danh", "chức danh", "user-cog", "dbo.JobTitles",
        AdminPermissions.JobTitlesView, AdminPermissions.JobTitlesCreate, AdminPermissions.JobTitlesUpdate, AdminPermissions.JobTitlesDelete, 100);

    public static HrCatalogInfo Of(HrCatalogKind kind) => kind switch
    {
        HrCatalogKind.Department => Department,
        HrCatalogKind.Team => Team,
        _ => JobTitle
    };
}

// ActiveEmployees: not-deleted, active employees whose current assignment uses the row. ActiveTeams: departments only.
public sealed record HrCatalogItem(Guid Id, HrCatalogKind Kind, string Code, string Name, string? Description, int SortOrder, bool IsActive,
    Guid? BranchId, string? BranchName, Guid? DepartmentId, string? DepartmentName, int? Level, bool IsManagement, int ActiveEmployees, int ActiveTeams, string Stamp);

public sealed class HrCatalogInput
{
    [StringLength(100, ErrorMessage = "Mã tối đa 100 ký tự.")] public string? Code { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập tên."), StringLength(200, ErrorMessage = "Tên tối đa 200 ký tự.")] public string Name { get; set; } = "";
    [StringLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")] public string? Description { get; set; }
    [Range(0, 100000, ErrorMessage = "Thứ tự hiển thị từ 0 đến 100000.")] public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? BranchId { get; set; }
    public Guid? DepartmentId { get; set; }
    [Range(0, 1000, ErrorMessage = "Cấp bậc từ 0 đến 1000.")] public int? Level { get; set; }
    public bool IsManagement { get; set; }
    [ValidateNever] public string? Stamp { get; set; }

    public static HrCatalogInput From(HrCatalogItem item) => new()
    {
        Code = item.Code, Name = item.Name, Description = item.Description, SortOrder = item.SortOrder, IsActive = item.IsActive, BranchId = item.BranchId,
        DepartmentId = item.DepartmentId, Level = item.Level, IsManagement = item.IsManagement, Stamp = item.Stamp
    };
}

public enum HrCatalogOutcome { Saved, NoChange, NotFound, Forbidden, Conflict, InvalidCode, DuplicateCode, InvalidReference, InUse, AuditUnavailable }

public sealed record HrCatalogResult(HrCatalogOutcome Outcome, Guid? Id = null, bool AuditPersisted = false);

public static class HrCatalogPolicy
{
    private static readonly Regex CodePattern = new("^[A-Z0-9_.-]+$", RegexOptions.CultureInvariant);

    public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

    public static bool IsValidCode(HrCatalogKind kind, string code) => code.Length >= 1 && code.Length <= HrCatalogs.Of(kind).CodeLength && CodePattern.IsMatch(code);

    // Department: an existing branch. Team: an existing branch, and a department of that same branch when one is chosen.
    public static bool ReferencesAreValid(HrCatalogKind kind, HrCatalogInput input, IReadOnlyList<BranchOption> branches, IReadOnlyList<DepartmentOption> departments)
    {
        if (kind == HrCatalogKind.JobTitle) return true;
        if (input.BranchId is not Guid b || !branches.Any(x => x.Id == b)) return false;
        if (kind == HrCatalogKind.Team && input.DepartmentId is Guid d && !departments.Any(x => x.Id == d && x.BranchId == b)) return false;
        return true;
    }

    public static bool Unchanged(HrCatalogItem current, HrCatalogInput input) =>
        current.Name == input.Name.Trim() && (current.Description ?? "") == (input.Description?.Trim() ?? "") && current.SortOrder == input.SortOrder
        && current.IsActive == input.IsActive && (current.Kind != HrCatalogKind.Team || current.DepartmentId == input.DepartmentId)
        && (current.Kind != HrCatalogKind.JobTitle || (current.Level == input.Level && current.IsManagement == input.IsManagement));

    public static HrCatalogOutcome EvaluateDelete(HrCatalogItem current, string? stamp)
    {
        if (!string.Equals(stamp, current.Stamp, StringComparison.Ordinal)) return HrCatalogOutcome.Conflict;
        return current.ActiveEmployees > 0 || current.ActiveTeams > 0 ? HrCatalogOutcome.InUse : HrCatalogOutcome.Saved;
    }

    public static string Json(HrCatalogKind kind, string code, HrCatalogInput i) => kind switch
    {
        HrCatalogKind.Department => AuditSql.Json(("code", code), ("branchId", i.BranchId), ("name", i.Name.Trim()), ("description", i.Description?.Trim()), ("sortOrder", i.SortOrder), ("isActive", i.IsActive)),
        HrCatalogKind.Team => AuditSql.Json(("code", code), ("branchId", i.BranchId), ("departmentId", i.DepartmentId), ("name", i.Name.Trim()), ("sortOrder", i.SortOrder), ("isActive", i.IsActive)),
        _ => AuditSql.Json(("code", code), ("name", i.Name.Trim()), ("level", i.Level), ("description", i.Description?.Trim()), ("isManagement", i.IsManagement), ("sortOrder", i.SortOrder), ("isActive", i.IsActive))
    };
}

public interface IHrCatalogStore
{
    Task<IReadOnlyList<HrCatalogItem>> ListAsync(HrCatalogKind kind);
    Task<HrCatalogItem?> GetAsync(HrCatalogKind kind, Guid id);
    Task<IReadOnlyList<BranchOption>> BranchesAsync();
    Task<IReadOnlyList<DepartmentOption>> DepartmentsAsync();
    Task<HrCatalogResult> CreateAsync(HrCatalogKind kind, Guid actorId, HrCatalogInput input, string? ipAddress);
    Task<HrCatalogResult> UpdateAsync(HrCatalogKind kind, Guid actorId, Guid id, HrCatalogInput input, string? ipAddress);
    Task<HrCatalogResult> DeleteAsync(HrCatalogKind kind, Guid actorId, Guid id, string? stamp, string? ipAddress);
}

public static class HrCatalogSql
{
    // "Active employee": Employees.IsDeleted=0 AND IsActive=1, counted through its current assignment (IsCurrent=1).
    public const string Departments = """
SELECT x.Id,x.Code,x.Name,x.Description,x.SortOrder,x.IsActive,x.BranchId,b.Name,
 (SELECT COUNT(1) FROM dbo.EmployeeAssignments a JOIN dbo.Employees e ON e.Id=a.EmployeeId WHERE a.IsCurrent=1 AND e.IsDeleted=0 AND e.IsActive=1 AND a.DepartmentId=x.Id),
 (SELECT COUNT(1) FROM dbo.Teams t WHERE t.DepartmentId=x.Id AND t.IsDeleted=0 AND t.IsActive=1),
 x.RowVersion
FROM dbo.Departments x LEFT JOIN dbo.Branches b ON b.Id=x.BranchId
WHERE x.IsDeleted=0 AND (@id IS NULL OR x.Id=@id) ORDER BY b.Name,x.SortOrder,x.Name;
""";

    public const string Teams = """
SELECT x.Id,x.Code,x.Name,x.SortOrder,x.IsActive,x.BranchId,b.Name,x.DepartmentId,d.Name,
 (SELECT COUNT(1) FROM dbo.EmployeeAssignments a JOIN dbo.Employees e ON e.Id=a.EmployeeId WHERE a.IsCurrent=1 AND e.IsDeleted=0 AND e.IsActive=1 AND a.TeamId=x.Id),
 x.RowVersion
FROM dbo.Teams x LEFT JOIN dbo.Branches b ON b.Id=x.BranchId LEFT JOIN dbo.Departments d ON d.Id=x.DepartmentId
WHERE x.IsDeleted=0 AND (@id IS NULL OR x.Id=@id) ORDER BY b.Name,d.Name,x.SortOrder,x.Name;
""";

    public const string JobTitles = """
SELECT x.Id,x.Code,x.Name,x.Description,x.SortOrder,x.IsActive,x.[Level],x.IsManagement,
 (SELECT COUNT(1) FROM dbo.EmployeeAssignments a JOIN dbo.Employees e ON e.Id=a.EmployeeId WHERE a.IsCurrent=1 AND e.IsDeleted=0 AND e.IsActive=1 AND a.JobTitleId=x.Id),
 x.RowVersion
FROM dbo.JobTitles x WHERE x.IsDeleted=0 AND (@id IS NULL OR x.Id=@id) ORDER BY x.SortOrder,x.Name;
""";

    public const string Branches = "SELECT b.Id,b.Code,b.Name,c.Name,b.IsActive FROM dbo.Branches b LEFT JOIN dbo.Companies c ON c.Id=b.CompanyId WHERE b.IsDeleted=0 ORDER BY b.Name;";
    public const string DepartmentOptions = "SELECT Id,BranchId,Code,Name,IsActive FROM dbo.Departments WHERE IsDeleted=0 ORDER BY SortOrder,Name;";

    public const string Lock = "SELECT RowVersion FROM {0} WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id AND IsDeleted=0;";

    // Production also has UX_Departments_Code_NotDeleted: a department code is unique company-wide, not only per branch.
    public const string DepartmentCodeTaken = "SELECT COUNT(1) FROM dbo.Departments WITH (UPDLOCK, HOLDLOCK) WHERE Code=@code AND IsDeleted=0;";
    public const string TeamCodeTaken = "SELECT COUNT(1) FROM dbo.Teams WITH (UPDLOCK, HOLDLOCK) WHERE BranchId=@branch AND Code=@code AND IsDeleted=0;";
    public const string JobTitleCodeTaken = "SELECT COUNT(1) FROM dbo.JobTitles WITH (UPDLOCK, HOLDLOCK) WHERE Code=@code AND IsDeleted=0;";

    public const string InsertDepartment = "INSERT dbo.Departments(Id,BranchId,Code,Name,Description,SortOrder,IsActive,CreatedBy) VALUES(@id,@branch,@code,@name,@desc,@sort,@active,@by);";
    public const string InsertTeam = "INSERT dbo.Teams(Id,BranchId,DepartmentId,Code,Name,SortOrder,IsActive,CreatedBy) VALUES(@id,@branch,@dept,@code,@name,@sort,@active,@by);";
    public const string InsertJobTitle = "INSERT dbo.JobTitles(Id,Code,Name,[Level],Description,IsManagement,SortOrder,IsActive,CreatedBy) VALUES(@id,@code,@name,@level,@desc,@mgmt,@sort,@active,@by);";

    public const string UpdateDepartment = "UPDATE dbo.Departments SET Name=@name,Description=@desc,SortOrder=@sort,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;";
    public const string UpdateTeam = "UPDATE dbo.Teams SET DepartmentId=@dept,Name=@name,SortOrder=@sort,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;";
    public const string UpdateJobTitle = "UPDATE dbo.JobTitles SET Name=@name,[Level]=@level,Description=@desc,IsManagement=@mgmt,SortOrder=@sort,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;";

    // Soft delete only; IsActive=0 as a second barrier.
    public const string SoftDelete = "UPDATE {0} SET IsDeleted=1,IsActive=0,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;";

    public const string Audit = AuditSql.Insert;

    public static string Table(HrCatalogKind kind) => HrCatalogs.Of(kind).Entity; // constant table names only, never user input
}

public sealed class SqlHrCatalogStore(IConfiguration configuration, AdminAuditOptions audit) : IHrCatalogStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<IReadOnlyList<HrCatalogItem>> ListAsync(HrCatalogKind kind)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await Read(cn, null, kind, null);
    }

    public async Task<HrCatalogItem?> GetAsync(HrCatalogKind kind, Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return (await Read(cn, null, kind, id)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<BranchOption>> BranchesAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await Branches(cn, null);
    }

    public async Task<IReadOnlyList<DepartmentOption>> DepartmentsAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await Departments(cn, null);
    }

    public Task<HrCatalogResult> CreateAsync(HrCatalogKind kind, Guid actorId, HrCatalogInput input, string? ipAddress) =>
        Write(actorId, HrCatalogs.Of(kind).CreatePermission, async (cn, tx) =>
        {
            var code = HrCatalogPolicy.NormalizeCode(input.Code);
            if (!HrCatalogPolicy.IsValidCode(kind, code)) return new(HrCatalogOutcome.InvalidCode);
            var branches = (await Branches(cn, tx)).Where(b => b.IsActive).ToList();
            var departments = (await Departments(cn, tx)).Where(d => d.IsActive).ToList();
            if (!HrCatalogPolicy.ReferencesAreValid(kind, input, branches, departments)) return new(HrCatalogOutcome.InvalidReference);
            var takenSql = kind switch { HrCatalogKind.Department => HrCatalogSql.DepartmentCodeTaken, HrCatalogKind.Team => HrCatalogSql.TeamCodeTaken, _ => HrCatalogSql.JobTitleCodeTaken };
            if (Convert.ToInt32(await Scalar(cn, tx, takenSql, ("@code", code), ("@branch", Db(input.BranchId)))) > 0) return new(HrCatalogOutcome.DuplicateCode);
            var id = Guid.NewGuid();
            var sql = kind switch { HrCatalogKind.Department => HrCatalogSql.InsertDepartment, HrCatalogKind.Team => HrCatalogSql.InsertTeam, _ => HrCatalogSql.InsertJobTitle };
            await Exec(cn, tx, sql, [("@id", id), ("@code", code), .. Values(input, actorId)]);
            var written = await Audit(cn, tx, kind, actorId, "CREATED", id, "{}", HrCatalogPolicy.Json(kind, code, input), ipAddress);
            return new(HrCatalogOutcome.Saved, id, written);
        }, ex => ex.Number is 2601 or 2627 ? HrCatalogOutcome.DuplicateCode : null);

    public Task<HrCatalogResult> UpdateAsync(HrCatalogKind kind, Guid actorId, Guid id, HrCatalogInput input, string? ipAddress) =>
        Write(actorId, HrCatalogs.Of(kind).UpdatePermission, async (cn, tx) =>
        {
            var rv = await Lock(cn, tx, kind, id);
            if (rv is null) return new(HrCatalogOutcome.NotFound);
            var current = (await Read(cn, tx, kind, id)).First();
            if (current.Stamp != input.Stamp) return new(HrCatalogOutcome.Conflict, id);
            input.BranchId = current.BranchId; // branch and code are fixed after creation
            if (kind == HrCatalogKind.Team && input.DepartmentId != current.DepartmentId
                && !HrCatalogPolicy.ReferencesAreValid(kind, input, await Branches(cn, tx), (await Departments(cn, tx)).Where(d => d.IsActive).ToList()))
                return new(HrCatalogOutcome.InvalidReference, id);
            if (HrCatalogPolicy.Unchanged(current, input)) return new(HrCatalogOutcome.NoChange, id);
            var sql = kind switch { HrCatalogKind.Department => HrCatalogSql.UpdateDepartment, HrCatalogKind.Team => HrCatalogSql.UpdateTeam, _ => HrCatalogSql.UpdateJobTitle };
            if (await Exec(cn, tx, sql, [("@id", id), ("@rv", rv), .. Values(input, actorId)]) != 1) return new(HrCatalogOutcome.Conflict, id);
            var written = await Audit(cn, tx, kind, actorId, "UPDATED", id, HrCatalogPolicy.Json(kind, current.Code, HrCatalogInput.From(current)), HrCatalogPolicy.Json(kind, current.Code, input), ipAddress);
            return new(HrCatalogOutcome.Saved, id, written);
        });

    public Task<HrCatalogResult> DeleteAsync(HrCatalogKind kind, Guid actorId, Guid id, string? stamp, string? ipAddress) =>
        Write(actorId, HrCatalogs.Of(kind).DeletePermission, async (cn, tx) =>
        {
            var rv = await Lock(cn, tx, kind, id);
            if (rv is null) return new(HrCatalogOutcome.NotFound);
            var current = (await Read(cn, tx, kind, id)).First();
            var outcome = HrCatalogPolicy.EvaluateDelete(current, stamp);
            if (outcome != HrCatalogOutcome.Saved) return new(outcome, id);
            if (await Exec(cn, tx, string.Format(HrCatalogSql.SoftDelete, HrCatalogSql.Table(kind)), ("@id", id), ("@rv", rv), ("@by", actorId)) != 1) return new(HrCatalogOutcome.Conflict, id);
            var written = await Audit(cn, tx, kind, actorId, "SOFT_DELETED", id, HrCatalogPolicy.Json(kind, current.Code, HrCatalogInput.From(current)), AuditSql.Json(("isDeleted", true), ("isActive", false)), ipAddress);
            return new(HrCatalogOutcome.Saved, id, written);
        });

    // ---- helpers --------------------------------------------------------------------------------------------
    private async Task<HrCatalogResult> Write(Guid actorId, string permission, Func<SqlConnection, SqlTransaction, Task<HrCatalogResult>> body, Func<SqlException, HrCatalogOutcome?>? map = null)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actorId, permission)) return new(HrCatalogOutcome.Forbidden);
            var result = await body(cn, tx);
            if (result.Outcome == HrCatalogOutcome.Saved) await tx.CommitAsync();
            return result;
        }
        catch (AuditUnavailableException) { return new(HrCatalogOutcome.AuditUnavailable); }
        catch (SqlException ex) when (map?.Invoke(ex) is HrCatalogOutcome mapped) { return new(mapped); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { return new(HrCatalogOutcome.Conflict); }
        catch (SqlException ex) when (ex.Number is 547) { return new(HrCatalogOutcome.InvalidReference); }
    }

    private async Task<bool> Audit(SqlConnection cn, SqlTransaction tx, HrCatalogKind kind, Guid actorId, string verb, Guid id, string oldValues, string newValues, string? ip)
    {
        var action = kind switch { HrCatalogKind.Department => "DEPARTMENT_", HrCatalogKind.Team => "TEAM_", _ => "JOB_TITLE_" } + verb;
        return (await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actorId, action, HrCatalogSql.Table(kind), id.ToString(), oldValues, newValues, ip)).Written;
    }

    private static (string, object)[] Values(HrCatalogInput i, Guid actorId) =>
    [
        ("@branch", Db(i.BranchId)), ("@dept", Db(i.DepartmentId)), ("@name", i.Name.Trim()), ("@desc", Db(i.Description)), ("@sort", i.SortOrder),
        ("@active", i.IsActive), ("@level", (object?)i.Level ?? DBNull.Value), ("@mgmt", i.IsManagement), ("@by", actorId)
    ];

    private static async Task<byte[]?> Lock(SqlConnection cn, SqlTransaction tx, HrCatalogKind kind, Guid id)
    {
        await using var cmd = new SqlCommand(string.Format(HrCatalogSql.Lock, HrCatalogSql.Table(kind)), cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteScalarAsync() as byte[];
    }

    private static async Task<List<HrCatalogItem>> Read(SqlConnection cn, SqlTransaction? tx, HrCatalogKind kind, Guid? id)
    {
        var list = new List<HrCatalogItem>();
        var sql = kind switch { HrCatalogKind.Department => HrCatalogSql.Departments, HrCatalogKind.Team => HrCatalogSql.Teams, _ => HrCatalogSql.JobTitles };
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = (object?)id ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(kind switch
            {
                HrCatalogKind.Department => new(r.GetGuid(0), kind, r.GetString(1), r.GetString(2), S(r, 3), r.GetInt32(4), r.GetBoolean(5), r.GetGuid(6), S(r, 7), null, null, null, false,
                    r.GetInt32(8), r.GetInt32(9), Convert.ToHexString((byte[])r.GetValue(10))),
                HrCatalogKind.Team => new(r.GetGuid(0), kind, r.GetString(1), r.GetString(2), null, r.GetInt32(3), r.GetBoolean(4), r.GetGuid(5), S(r, 6),
                    r.IsDBNull(7) ? null : r.GetGuid(7), S(r, 8), null, false, r.GetInt32(9), 0, Convert.ToHexString((byte[])r.GetValue(10))),
                _ => new(r.GetGuid(0), kind, r.GetString(1), r.GetString(2), S(r, 3), r.GetInt32(4), r.GetBoolean(5), null, null, null, null,
                    r.IsDBNull(6) ? null : r.GetInt32(6), r.GetBoolean(7), r.GetInt32(8), 0, Convert.ToHexString((byte[])r.GetValue(9)))
            });
        return list;
    }

    private static async Task<List<BranchOption>> Branches(SqlConnection cn, SqlTransaction? tx)
    {
        var list = new List<BranchOption>();
        await using var cmd = new SqlCommand(HrCatalogSql.Branches, cn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), S(r, 3), r.GetBoolean(4)));
        return list;
    }

    private static async Task<List<DepartmentOption>> Departments(SqlConnection cn, SqlTransaction? tx)
    {
        var list = new List<DepartmentOption>();
        await using var cmd = new SqlCommand(HrCatalogSql.DepartmentOptions, cn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        return list;
    }

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static object Db(Guid? value) => value is Guid g ? g : DBNull.Value;

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

    // Only the parameters the statement uses are added (one parameter list serves the three tables).
    private static SqlCommand Command(SqlConnection cn, SqlTransaction tx, string sql, (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args)
        {
            if (!Regex.IsMatch(sql, Regex.Escape(name) + @"\b")) continue;
            if (value is byte[] b) cmd.Parameters.Add(name, SqlDbType.Timestamp).Value = b;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }
}
