using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// Admin → Nhân sự → Nhân viên. Tables of the HR core bootstrap of TAPortalSys (created outside the repository migrations):
// dbo.Employees (UserId → dbo.Users, unique while not deleted), dbo.EmployeeAssignments (one primary current row per
// employee), dbo.EmployeeStatusHistories, dbo.NumberSequences (EMPLOYEE_CODE) and the organisation lookups.
// Every write re-checks the actor's permission in the same transaction, compares the employee RowVersion (concurrency) and
// writes a dbo.AuditLogs row through AuditSql (fail-closed when Security:Audit:RequireDurableAudit=true).
public interface IEmployeeStore
{
    Task<EmployeeLookups> LookupsAsync();
    Task<IReadOnlyList<EmployeeListItem>> ListAsync(EmployeeFilter filter);
    // includePrivate=false never reads the private columns.
    Task<EmployeeDetail?> GetAsync(Guid id, bool includePrivate);
    Task<string?> SuggestCodeAsync();
    Task<EmployeeResult> CreateAsync(Guid actorId, EmployeeInput input, string? ipAddress);
    Task<EmployeeResult> UpdateAsync(Guid actorId, Guid id, EmployeeInput input, string? ipAddress);
    Task<EmployeeResult> ChangeAssignmentAsync(Guid actorId, Guid id, EmployeeAssignmentInput input, string? ipAddress);
    Task<EmployeeResult> ChangeStatusAsync(Guid actorId, Guid id, EmployeeStatusInput input, string? ipAddress);
    Task<EmployeeResult> DeleteAsync(Guid actorId, Guid id, string? stamp, string? ipAddress);
    Task<EmployeeAccountOptions> AccountOptionsAsync();
    Task<EmployeeResult> CreateAccountAsync(Guid actorId, Guid id, EmployeeAccountInput input, string passwordHash, string? ipAddress);
    Task<EmployeeResult> LinkAccountAsync(Guid actorId, Guid id, Guid userId, string? stamp, string? ipAddress);
    Task<EmployeeResult> UnlinkAccountAsync(Guid actorId, Guid id, string? stamp, string? ipAddress);
    // UserId → linked (not deleted) employee, for the Users list.
    Task<IReadOnlyDictionary<Guid, LinkedEmployee>> LinkedEmployeesAsync();
}

public static class EmployeeSql
{
    public const string Lookups = """
SELECT b.Id,b.Code,b.Name,c.Name,b.IsActive FROM dbo.Branches b LEFT JOIN dbo.Companies c ON c.Id=b.CompanyId WHERE b.IsDeleted=0 ORDER BY b.Name;
SELECT Id,BranchId,Code,Name,IsActive FROM dbo.Departments WHERE IsDeleted=0 ORDER BY SortOrder,Name;
SELECT Id,BranchId,DepartmentId,Code,Name,IsActive FROM dbo.Teams WHERE IsDeleted=0 ORDER BY SortOrder,Name;
SELECT Id,Code,Name,IsActive FROM dbo.JobTitles WHERE IsDeleted=0 ORDER BY SortOrder,Name;
SELECT Id,Code,Name,IsActive FROM dbo.EmployeeStatuses ORDER BY SortOrder,Name;
SELECT Id,Code,Name,IsActive FROM dbo.EmploymentTypes ORDER BY SortOrder,Name;
""";

    // Current primary assignment only; LIKE patterns are escaped (EmployeeFilter.Pattern).
    public const string List = """
SELECT e.Id,e.EmployeeCode,e.FullName,a.BranchId,b.Name,a.DepartmentId,d.Name,t.Name,j.Name,e.MobilePhone,e.CompanyPhone,e.CompanyEmail,
 e.CurrentStatusId,s.Code,s.Name,e.UserId,u.Username,
 CAST(CASE WHEN u.Id IS NOT NULL AND u.IsDeleted=0 AND u.IsActive=1 AND u.Status='ACTIVE' THEN 1 ELSE 0 END AS bit)
FROM dbo.Employees e
LEFT JOIN dbo.EmployeeAssignments a ON a.EmployeeId=e.Id AND a.IsPrimary=1 AND a.IsCurrent=1
LEFT JOIN dbo.Branches b ON b.Id=a.BranchId
LEFT JOIN dbo.Departments d ON d.Id=a.DepartmentId
LEFT JOIN dbo.Teams t ON t.Id=a.TeamId
LEFT JOIN dbo.JobTitles j ON j.Id=a.JobTitleId
LEFT JOIN dbo.EmployeeStatuses s ON s.Id=e.CurrentStatusId
LEFT JOIN dbo.Users u ON u.Id=e.UserId
WHERE e.IsDeleted=0
 AND (@q IS NULL OR e.FullName LIKE @q ESCAPE '\' OR e.EmployeeCode LIKE @q ESCAPE '\' OR e.MobilePhone LIKE @q ESCAPE '\'
      OR e.CompanyPhone LIKE @q ESCAPE '\' OR e.CompanyEmail LIKE @q ESCAPE '\' OR e.PersonalEmail LIKE @q ESCAPE '\')
 AND (@branch IS NULL OR a.BranchId=@branch)
 AND (@dept IS NULL OR a.DepartmentId=@dept)
 AND (@status IS NULL OR e.CurrentStatusId=@status)
 AND (@account IS NULL OR (@account=1 AND e.UserId IS NOT NULL) OR (@account=0 AND e.UserId IS NULL))
ORDER BY e.FullName,e.EmployeeCode;
""";

    public const string Employee = """
SELECT e.Id,e.EmployeeCode,e.FullName,e.Gender,e.DateOfBirth,e.MobilePhone,e.CompanyPhone,e.PersonalEmail,e.CompanyEmail,e.StartDate,e.EndDate,
 e.EmploymentTypeId,e.CurrentStatusId,s.Code,s.Name,e.IsActive,e.Note,e.UserId,e.RowVersion
FROM dbo.Employees e LEFT JOIN dbo.EmployeeStatuses s ON s.Id=e.CurrentStatusId WHERE e.Id=@id AND e.IsDeleted=0;
""";

    // Same row under UPDLOCK/HOLDLOCK inside a write transaction.
    public const string EmployeeForUpdate = """
SELECT e.Id,e.EmployeeCode,e.FullName,e.Gender,e.DateOfBirth,e.MobilePhone,e.CompanyPhone,e.PersonalEmail,e.CompanyEmail,e.StartDate,e.EndDate,
 e.EmploymentTypeId,e.CurrentStatusId,s.Code,s.Name,e.IsActive,e.Note,e.UserId,e.RowVersion
FROM dbo.Employees e WITH (UPDLOCK, HOLDLOCK) LEFT JOIN dbo.EmployeeStatuses s ON s.Id=e.CurrentStatusId WHERE e.Id=@id AND e.IsDeleted=0;
""";

    public const string Private = "SELECT IdentityNumber,IdentityIssueDate,IdentityIssuePlace,PersonalTaxCode,SocialInsuranceNumber,PermanentAddress,CurrentAddress FROM dbo.Employees WHERE Id=@id AND IsDeleted=0;";

    public const string CurrentAssignment = "SELECT TOP 1 Id,BranchId,DepartmentId,TeamId,JobTitleId,ValidFrom,Note FROM dbo.EmployeeAssignments WITH (UPDLOCK, HOLDLOCK) WHERE EmployeeId=@id AND IsPrimary=1 AND IsCurrent=1;";

    public const string AssignmentHistory = """
SELECT TOP 50 a.ValidFrom,a.ValidTo,a.IsCurrent,b.Name,d.Name,t.Name,j.Name
FROM dbo.EmployeeAssignments a LEFT JOIN dbo.Branches b ON b.Id=a.BranchId LEFT JOIN dbo.Departments d ON d.Id=a.DepartmentId
LEFT JOIN dbo.Teams t ON t.Id=a.TeamId LEFT JOIN dbo.JobTitles j ON j.Id=a.JobTitleId
WHERE a.EmployeeId=@id AND a.IsPrimary=1 ORDER BY a.IsCurrent DESC,a.ValidFrom DESC,a.CreatedAt DESC;
""";

    public const string StatusHistory = """
SELECT TOP 50 h.EffectiveDate,os.Name,ns.Name,h.Reason,h.Note,u.DisplayName,h.ChangedAt
FROM dbo.EmployeeStatusHistories h LEFT JOIN dbo.EmployeeStatuses os ON os.Id=h.OldStatusId JOIN dbo.EmployeeStatuses ns ON ns.Id=h.NewStatusId
LEFT JOIN dbo.Users u ON u.Id=h.ChangedBy WHERE h.EmployeeId=@id ORDER BY h.EffectiveDate DESC,h.ChangedAt DESC;
""";

    public const string Account = "SELECT Id,Username,DisplayName,Email,IsActive,Status,IsDeleted FROM dbo.Users WHERE Id=@u;";
    public const string AccountRoles = "SELECT r.Name FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId WHERE ur.UserId=@u AND r.IsDeleted=0 ORDER BY r.Name;";

    public const string Sequence = "SELECT TOP 1 Prefix,Suffix,CurrentValue,Padding FROM dbo.NumberSequences WHERE Code=@seq AND ScopeType='SYSTEM' AND ScopeId IS NULL AND IsActive=1;";
    public const string SequenceForUpdate = "SELECT TOP 1 Prefix,Suffix,CurrentValue,Padding FROM dbo.NumberSequences WITH (UPDLOCK, HOLDLOCK) WHERE Code=@seq AND ScopeType='SYSTEM' AND ScopeId IS NULL AND IsActive=1;";
    public const string SequenceAdvance = "UPDATE dbo.NumberSequences SET CurrentValue=@n,UpdatedAt=SYSUTCDATETIME() WHERE Code=@seq AND ScopeType='SYSTEM' AND ScopeId IS NULL AND IsActive=1 AND CurrentValue<@n;";
    // Deleted employees count too, so a code is never handed out twice.
    public const string CodesWithPrefix = "SELECT EmployeeCode FROM dbo.Employees WHERE EmployeeCode LIKE @p ESCAPE '\\';";
    public const string CodeTaken = "SELECT COUNT(1) FROM dbo.Employees WITH (UPDLOCK, HOLDLOCK) WHERE EmployeeCode=@code AND IsDeleted=0;";

    public const string Insert = """
INSERT dbo.Employees(Id,EmployeeCode,EmploymentTypeId,CurrentStatusId,FullName,DateOfBirth,Gender,MobilePhone,CompanyPhone,PersonalEmail,CompanyEmail,StartDate,Note,IsActive,CreatedBy)
VALUES(@id,@code,@type,@status,@name,@dob,@gender,@mobile,@cphone,@pemail,@cemail,@start,@note,@active,@by);
""";

    public const string UpdateBasic = """
UPDATE dbo.Employees SET FullName=@name,Gender=@gender,DateOfBirth=@dob,MobilePhone=@mobile,CompanyPhone=@cphone,PersonalEmail=@pemail,CompanyEmail=@cemail,
 StartDate=@start,EmploymentTypeId=@type,Note=@note,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;
""";

    public const string UpdatePrivate = """
UPDATE dbo.Employees SET IdentityNumber=@idn,IdentityIssueDate=@idd,IdentityIssuePlace=@idp,PersonalTaxCode=@ptc,SocialInsuranceNumber=@sin,
 PermanentAddress=@padr,CurrentAddress=@cadr,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND IsDeleted=0;
""";

    // Bumps RowVersion: assignment changes are serialized with every other change of the employee page.
    public const string Touch = "UPDATE dbo.Employees SET UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;";

    public const string InsertAssignment = """
INSERT dbo.EmployeeAssignments(EmployeeId,BranchId,DepartmentId,TeamId,JobTitleId,ValidFrom,IsPrimary,IsCurrent,Note,CreatedBy)
VALUES(@id,@branch,@dept,@team,@job,@from,1,1,@note,@by);
""";
    public const string CorrectAssignment = "UPDATE dbo.EmployeeAssignments SET BranchId=@branch,DepartmentId=@dept,TeamId=@team,JobTitleId=@job,Note=@note,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@aid AND IsCurrent=1;";
    public const string CloseAssignment = "UPDATE dbo.EmployeeAssignments SET IsCurrent=0,ValidTo=@to,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@aid AND IsCurrent=1;";

    public const string InsertStatusHistory = """
INSERT dbo.EmployeeStatusHistories(EmployeeId,OldStatusId,NewStatusId,EffectiveDate,Reason,Note,ChangedBy)
VALUES(@id,@old,@new,@date,@reason,@note,@by);
""";

    // Leaving statuses (RESIGNED, TERMINATED) deactivate the record and set EndDate; any other status reactivates it.
    public const string UpdateStatus = """
UPDATE dbo.Employees SET CurrentStatusId=@new,IsActive=@active,EndDate=CASE WHEN @leaving=1 THEN ISNULL(EndDate,@date) ELSE NULL END,
 UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;
""";

    // Soft delete only: assignments, status history and audit rows stay.
    public const string SoftDelete = "UPDATE dbo.Employees SET IsDeleted=1,IsActive=0,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv AND UserId IS NULL;";

    public const string Link = "UPDATE dbo.Employees SET UserId=@u,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv AND UserId IS NULL;";
    public const string Unlink = "UPDATE dbo.Employees SET UserId=NULL,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv AND UserId=@u;";
    public const string UserLinked = "SELECT COUNT(1) FROM dbo.Employees WITH (UPDLOCK, HOLDLOCK) WHERE UserId=@u AND IsDeleted=0;";

    public const string Roles = "SELECT Id,Code,Name,Description,IsSystem,IsActive FROM dbo.Roles WHERE IsDeleted=0 ORDER BY IsSystem DESC,Name;";

    // Accounts that may be linked: not deleted, not linked to another employee, and not a Customer Portal-only account
    // (a user with an active customer membership and no TAPortal role belongs to a customer, not to T.A staff).
    public const string Candidates = """
IF OBJECT_ID(N'dbo.CustomerPortalMemberships',N'U') IS NOT NULL
 SELECT u.Id,u.Username,u.DisplayName,u.Email,CAST(CASE WHEN u.IsActive=1 AND u.Status='ACTIVE' THEN 1 ELSE 0 END AS bit) FROM dbo.Users u
 WHERE u.IsDeleted=0 AND (@u IS NULL OR u.Id=@u)
  AND NOT EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.UserId=u.Id AND e.IsDeleted=0)
  AND (EXISTS(SELECT 1 FROM dbo.UserRoles ur WHERE ur.UserId=u.Id) OR NOT EXISTS(SELECT 1 FROM dbo.CustomerPortalMemberships m WHERE m.UserId=u.Id AND m.IsActive=1))
 ORDER BY u.DisplayName;
ELSE
 SELECT u.Id,u.Username,u.DisplayName,u.Email,CAST(CASE WHEN u.IsActive=1 AND u.Status='ACTIVE' THEN 1 ELSE 0 END AS bit) FROM dbo.Users u
 WHERE u.IsDeleted=0 AND (@u IS NULL OR u.Id=@u)
  AND NOT EXISTS(SELECT 1 FROM dbo.Employees e WHERE e.UserId=u.Id AND e.IsDeleted=0)
 ORDER BY u.DisplayName;
""";

    public const string UsernameTaken = "SELECT COUNT(1) FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE NormalizedUsername=@nu AND IsDeleted=0;";
    public const string EmailTaken = "SELECT COUNT(1) FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE NormalizedEmail=@ne AND IsDeleted=0;";

    public const string LinkedEmployees = "SELECT UserId,Id,EmployeeCode,FullName FROM dbo.Employees WHERE UserId IS NOT NULL AND IsDeleted=0;";

    public const string Audit = AuditSql.Insert;
}

public sealed class SqlEmployeeStore(IConfiguration configuration, AdminAuditOptions audit) : IEmployeeStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<EmployeeLookups> LookupsAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await ReadLookups(cn, null);
    }

    public async Task<IReadOnlyList<EmployeeListItem>> ListAsync(EmployeeFilter filter)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(EmployeeSql.List, cn);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 210).Value = (object?)EmployeeFilter.Pattern(filter.Q) ?? DBNull.Value;
        cmd.Parameters.Add("@branch", SqlDbType.UniqueIdentifier).Value = (object?)filter.BranchId ?? DBNull.Value;
        cmd.Parameters.Add("@dept", SqlDbType.UniqueIdentifier).Value = (object?)filter.DepartmentId ?? DBNull.Value;
        cmd.Parameters.Add("@status", SqlDbType.UniqueIdentifier).Value = (object?)filter.StatusId ?? DBNull.Value;
        cmd.Parameters.Add("@account", SqlDbType.Bit).Value = filter.HasAccountFilter(out var linked) ? linked : DBNull.Value;
        var list = new List<EmployeeListItem>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), G(r, 3), S(r, 4), G(r, 5), S(r, 6), S(r, 7), S(r, 8), S(r, 9), S(r, 10), S(r, 11),
                G(r, 12), S(r, 13), S(r, 14), G(r, 15), S(r, 16), r.GetBoolean(17)));
        return list;
    }

    public async Task<EmployeeDetail?> GetAsync(Guid id, bool includePrivate)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var core = await ReadCore(cn, null, id, false);
        if (core is null) return null;
        var priv = includePrivate ? await ReadPrivate(cn, null, id) : null;
        var assignment = await ReadAssignment(cn, null, id);
        LinkedAccount? account = null;
        if (core.UserId is Guid userId)
        {
            await using (var cmd = new SqlCommand(EmployeeSql.Account, cn))
            {
                cmd.Parameters.AddWithValue("@u", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                    account = new(r.GetGuid(0), r.GetString(1), r.GetString(2), S(r, 3), AccountStatusPolicy.IsLoginEligible(r.GetBoolean(4), r.GetString(5), r.GetBoolean(6)), []);
            }
            if (account is not null)
            {
                var roles = new List<string>();
                await using var cmd = new SqlCommand(EmployeeSql.AccountRoles, cn);
                cmd.Parameters.AddWithValue("@u", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) roles.Add(r.GetString(0));
                account = account with { RoleNames = roles };
            }
        }
        var statuses = new List<EmployeeStatusHistoryRow>();
        await using (var cmd = new SqlCommand(EmployeeSql.StatusHistory, cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) statuses.Add(new(r.GetDateTime(0), S(r, 1), r.GetString(2), S(r, 3), S(r, 4), S(r, 5), r.GetDateTime(6)));
        }
        var assignments = new List<EmployeeAssignmentHistoryRow>();
        await using (var cmd = new SqlCommand(EmployeeSql.AssignmentHistory, cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) assignments.Add(new(r.GetDateTime(0), r.IsDBNull(1) ? null : r.GetDateTime(1), r.GetBoolean(2), S(r, 3), S(r, 4), S(r, 5), S(r, 6)));
        }
        return core.ToDetail(assignment, priv, account, statuses, assignments);
    }

    public async Task<string?> SuggestCodeAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await Suggest(cn, null, EmployeeSql.Sequence);
    }

    public Task<EmployeeResult> CreateAsync(Guid actorId, EmployeeInput input, string? ipAddress) =>
        Write(actorId, input.Private is null ? [AdminPermissions.EmployeesCreate] : [AdminPermissions.EmployeesCreate, AdminPermissions.EmployeesUpdatePrivate], async (cn, tx) =>
        {
            var code = EmployeePolicy.NormalizeCode(input.Code);
            if (!EmployeePolicy.IsValidCode(code)) return new(EmployeeOutcome.InvalidCode);
            var lookups = await ReadLookups(cn, tx);
            var status = lookups.Statuses.FirstOrDefault(s => s.Id == input.StatusId && s.IsActive);
            if (status is null || !EmployeePolicy.IsValidGender(input.Gender)
                || (input.EmploymentTypeId is Guid t && !lookups.EmploymentTypes.Any(x => x.Id == t && x.IsActive))
                || !EmployeePolicy.AssignmentIsValid(lookups, input.BranchId, input.DepartmentId, input.TeamId, input.JobTitleId))
                return new(EmployeeOutcome.InvalidReference);
            if (Convert.ToInt32(await Scalar(cn, tx, EmployeeSql.CodeTaken, ("@code", code))) > 0) return new(EmployeeOutcome.DuplicateCode);

            var id = Guid.NewGuid();
            var from = (input.StartDate ?? DateTime.Today).Date;
            await Exec(cn, tx, EmployeeSql.Insert, ("@id", id), ("@code", code), ("@type", Db(input.EmploymentTypeId)), ("@status", status.Id), ("@name", input.FullName.Trim()),
                ("@dob", Date(input.DateOfBirth)), ("@gender", Db(input.Gender)), ("@mobile", Db(input.MobilePhone)), ("@cphone", Db(input.CompanyPhone)),
                ("@pemail", Db(input.PersonalEmail)), ("@cemail", Db(input.CompanyEmail)), ("@start", Date(input.StartDate)), ("@note", Db(input.Note)),
                ("@active", !EmployeePolicy.IsLeaving(status.Code)), ("@by", actorId));
            if (input.Private is { } p) await WritePrivate(cn, tx, id, p.Normalized(), actorId);
            await Exec(cn, tx, EmployeeSql.InsertAssignment, ("@id", id), ("@branch", input.BranchId!.Value), ("@dept", Db(input.DepartmentId)), ("@team", Db(input.TeamId)),
                ("@job", Db(input.JobTitleId)), ("@from", Date(from)), ("@note", DBNull.Value), ("@by", actorId));
            await Exec(cn, tx, EmployeeSql.InsertStatusHistory, ("@id", id), ("@old", DBNull.Value), ("@new", status.Id), ("@date", Date(from)),
                ("@reason", "Tạo hồ sơ nhân viên"), ("@note", DBNull.Value), ("@by", actorId));
            await AdvanceSequence(cn, tx, code);
            var written = await Audit(cn, tx, actorId, "EMPLOYEE_CREATED", id, "{}", AuditSql.Json(
                ("code", code), ("fullName", input.FullName.Trim()), ("status", status.Code), ("employmentTypeId", input.EmploymentTypeId), ("branchId", input.BranchId),
                ("departmentId", input.DepartmentId), ("teamId", input.TeamId), ("jobTitleId", input.JobTitleId), ("validFrom", from.ToString("yyyy-MM-dd")),
                ("privateFields", input.Private is null ? "" : string.Join(",", input.Private.ChangedFields(null)))), ipAddress);
            return new(EmployeeOutcome.Saved, id, written);
        }, ex => ex.Number is 2601 or 2627 ? EmployeeOutcome.DuplicateCode : null);

    public Task<EmployeeResult> UpdateAsync(Guid actorId, Guid id, EmployeeInput input, string? ipAddress) =>
        Write(actorId, input.Private is null ? [AdminPermissions.EmployeesUpdate] : [AdminPermissions.EmployeesUpdate, AdminPermissions.EmployeesUpdatePrivate], async (cn, tx) =>
        {
            var core = await ReadCore(cn, tx, id, true);
            if (core is null) return new(EmployeeOutcome.NotFound);
            if (core.Stamp != input.Stamp) return new(EmployeeOutcome.Conflict, id);
            var lookups = await ReadLookups(cn, tx);
            if (!EmployeePolicy.IsValidGender(input.Gender) || (input.EmploymentTypeId is Guid t && t != core.EmploymentTypeId && !lookups.EmploymentTypes.Any(x => x.Id == t && x.IsActive)))
                return new(EmployeeOutcome.InvalidReference, id);
            var before = core.Basic();
            var after = EmployeeBasic.From(input);
            var privateBefore = input.Private is null ? null : await ReadPrivate(cn, tx, id);
            var privateChanged = input.Private?.ChangedFields(privateBefore) ?? [];
            if (before == after && privateChanged.Count == 0) return new(EmployeeOutcome.NoChange, id);
            if (await Exec(cn, tx, EmployeeSql.UpdateBasic, ("@id", id), ("@rv", core.RowVersion), ("@name", after.FullName), ("@gender", Db(after.Gender)), ("@dob", Date(after.DateOfBirth)),
                    ("@mobile", Db(after.MobilePhone)), ("@cphone", Db(after.CompanyPhone)), ("@pemail", Db(after.PersonalEmail)), ("@cemail", Db(after.CompanyEmail)),
                    ("@start", Date(after.StartDate)), ("@type", Db(after.EmploymentTypeId)), ("@note", Db(after.Note)), ("@by", actorId)) != 1)
                return new(EmployeeOutcome.Conflict, id);
            if (input.Private is { } p && privateChanged.Count > 0) await WritePrivate(cn, tx, id, p.Normalized(), actorId);
            var written = await Audit(cn, tx, actorId, "EMPLOYEE_UPDATED", id, before.Json(), after.Json(("privateFieldsChanged", string.Join(",", privateChanged))), ipAddress);
            return new(EmployeeOutcome.Saved, id, written);
        });

    public Task<EmployeeResult> ChangeAssignmentAsync(Guid actorId, Guid id, EmployeeAssignmentInput input, string? ipAddress) =>
        Write(actorId, [AdminPermissions.EmployeesChangeAssignment], async (cn, tx) =>
        {
            var core = await ReadCore(cn, tx, id, true);
            if (core is null) return new(EmployeeOutcome.NotFound);
            if (core.Stamp != input.Stamp) return new(EmployeeOutcome.Conflict, id);
            var current = await ReadAssignment(cn, tx, id);
            if (!EmployeePolicy.AssignmentIsValid(await ReadLookups(cn, tx), input.BranchId, input.DepartmentId, input.TeamId, input.JobTitleId, current))
                return new(EmployeeOutcome.InvalidReference, id);
            var plan = EmployeePolicy.PlanAssignment(current, input);
            if (plan == AssignmentPlanKind.NoChange) return new(EmployeeOutcome.NoChange, id);
            if (plan == AssignmentPlanKind.InvalidDate) return new(EmployeeOutcome.InvalidDate, id);
            var from = input.ValidFrom!.Value.Date;
            (string, object)[] values = [("@branch", input.BranchId!.Value), ("@dept", Db(input.DepartmentId)), ("@team", Db(input.TeamId)), ("@job", Db(input.JobTitleId)), ("@note", Db(input.Note)), ("@by", actorId)];
            if (plan == AssignmentPlanKind.UpdateInPlace)
                await Exec(cn, tx, EmployeeSql.CorrectAssignment, [("@aid", current!.Id), .. values]);
            else
            {
                if (plan == AssignmentPlanKind.CloseAndInsert)
                    await Exec(cn, tx, EmployeeSql.CloseAssignment, ("@aid", current!.Id), ("@to", Date(from.AddDays(-1))), ("@by", actorId));
                await Exec(cn, tx, EmployeeSql.InsertAssignment, [("@id", id), ("@from", Date(from)), .. values]);
            }
            if (await Exec(cn, tx, EmployeeSql.Touch, ("@id", id), ("@rv", core.RowVersion), ("@by", actorId)) != 1) return new(EmployeeOutcome.Conflict, id);
            var written = await Audit(cn, tx, actorId, "EMPLOYEE_ASSIGNMENT_CHANGED", id,
                current is null ? "{}" : AuditSql.Json(("branchId", current.BranchId), ("departmentId", current.DepartmentId), ("teamId", current.TeamId), ("jobTitleId", current.JobTitleId), ("validFrom", current.ValidFrom.ToString("yyyy-MM-dd"))),
                AuditSql.Json(("branchId", input.BranchId), ("departmentId", input.DepartmentId), ("teamId", input.TeamId), ("jobTitleId", input.JobTitleId), ("validFrom", from.ToString("yyyy-MM-dd")), ("plan", plan.ToString())), ipAddress);
            return new(EmployeeOutcome.Saved, id, written);
        });

    public Task<EmployeeResult> ChangeStatusAsync(Guid actorId, Guid id, EmployeeStatusInput input, string? ipAddress) =>
        Write(actorId, [AdminPermissions.EmployeesChangeStatus], async (cn, tx) =>
        {
            var core = await ReadCore(cn, tx, id, true);
            if (core is null) return new(EmployeeOutcome.NotFound);
            if (core.Stamp != input.Stamp) return new(EmployeeOutcome.Conflict, id);
            var status = (await ReadLookups(cn, tx)).Statuses.FirstOrDefault(s => s.Id == input.StatusId && s.IsActive);
            if (status is null) return new(EmployeeOutcome.InvalidReference, id);
            if (input.EffectiveDate is not DateTime date) return new(EmployeeOutcome.InvalidDate, id);
            if (status.Id == core.StatusId) return new(EmployeeOutcome.NoChange, id);
            var leaving = EmployeePolicy.IsLeaving(status.Code);
            if (await Exec(cn, tx, EmployeeSql.UpdateStatus, ("@id", id), ("@rv", core.RowVersion), ("@new", status.Id), ("@active", !leaving), ("@leaving", leaving),
                    ("@date", Date(date)), ("@by", actorId)) != 1)
                return new(EmployeeOutcome.Conflict, id);
            await Exec(cn, tx, EmployeeSql.InsertStatusHistory, ("@id", id), ("@old", Db(core.StatusId)), ("@new", status.Id), ("@date", Date(date)),
                ("@reason", Db(input.Reason)), ("@note", Db(input.Note)), ("@by", actorId));
            var written = await Audit(cn, tx, actorId, "EMPLOYEE_STATUS_CHANGED", id, AuditSql.Json(("status", core.StatusCode), ("isActive", core.IsActive)),
                AuditSql.Json(("status", status.Code), ("isActive", !leaving), ("effectiveDate", date.ToString("yyyy-MM-dd")), ("reason", EmployeePolicy.Clean(input.Reason))), ipAddress);
            return new(EmployeeOutcome.Saved, id, written);
        });

    public Task<EmployeeResult> DeleteAsync(Guid actorId, Guid id, string? stamp, string? ipAddress) =>
        Write(actorId, [AdminPermissions.EmployeesDelete], async (cn, tx) =>
        {
            var core = await ReadCore(cn, tx, id, true);
            if (core is null) return new(EmployeeOutcome.NotFound);
            if (core.Stamp != stamp) return new(EmployeeOutcome.Conflict, id);
            if (core.UserId is not null) return new(EmployeeOutcome.LinkedAccount, id);
            if (await Exec(cn, tx, EmployeeSql.SoftDelete, ("@id", id), ("@rv", core.RowVersion), ("@by", actorId)) != 1) return new(EmployeeOutcome.Conflict, id);
            var written = await Audit(cn, tx, actorId, "EMPLOYEE_SOFT_DELETED", id, AuditSql.Json(("code", core.Code), ("fullName", core.FullName), ("status", core.StatusCode)),
                AuditSql.Json(("isDeleted", true), ("isActive", false)), ipAddress);
            return new(EmployeeOutcome.Saved, id, written);
        });

    public async Task<EmployeeAccountOptions> AccountOptionsAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var roles = new List<PortalRole>();
        await using (var cmd = new SqlCommand(EmployeeSql.Roles, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) roles.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), S(r, 3), r.GetBoolean(4), r.GetBoolean(5)));
        return new(roles, await Candidates(cn, null, null));
    }

    public Task<EmployeeResult> CreateAccountAsync(Guid actorId, Guid id, EmployeeAccountInput input, string passwordHash, string? ipAddress) =>
        Write(actorId, [AdminPermissions.EmployeesAccountLink, AdminPermissions.UsersCreate], async (cn, tx) =>
        {
            var core = await ReadCore(cn, tx, id, true);
            if (core is null) return new(EmployeeOutcome.NotFound);
            if (core.Stamp != input.Stamp) return new(EmployeeOutcome.Conflict, id);
            if (core.UserId is not null) return new(EmployeeOutcome.AlreadyLinked, id);
            var username = (input.Username ?? "").Trim();
            if (Convert.ToInt32(await Scalar(cn, tx, EmployeeSql.UsernameTaken, ("@nu", username.ToUpperInvariant()))) > 0) return new(EmployeeOutcome.UsernameTaken, id);
            var email = EmployeePolicy.Clean(input.Email);
            if (email is not null && Convert.ToInt32(await Scalar(cn, tx, EmployeeSql.EmailTaken, ("@ne", email.ToUpperInvariant()))) > 0) return new(EmployeeOutcome.EmailTaken, id);
            var roles = new List<PortalRole>();
            await using (var cmd = new SqlCommand(EmployeeSql.Roles, cn, tx))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) roles.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), S(r, 3), r.GetBoolean(4), r.GetBoolean(5)));
            var roleIds = (input.RoleIds ?? []).Distinct().ToList();
            var roleOutcome = EmployeePolicy.EvaluateRoles(roles, roleIds, await ActorPermissionSql.IsSystemAdminAsync(cn, tx, actorId));
            if (roleOutcome != EmployeeOutcome.Saved) return new(roleOutcome, id);
            Guid userId;
            try
            {
                userId = await PortalDb.InsertUserAsync(cn, tx, audit.RequireDurableAudit,
                    new UserCreateVm { Username = username, DisplayName = core.FullName, Email = email, Phone = core.CompanyPhone ?? core.MobilePhone, RoleId = null },
                    passwordHash, actorId, roleIds, ipAddress);
            }
            catch (UnauthorizedAccessException) { return new(EmployeeOutcome.RoleForbidden, id); }
            if (await Exec(cn, tx, EmployeeSql.Link, ("@id", id), ("@rv", core.RowVersion), ("@u", userId), ("@by", actorId)) != 1) return new(EmployeeOutcome.Conflict, id);
            var written = await Audit(cn, tx, actorId, "EMPLOYEE_ACCOUNT_LINKED", id, AuditSql.Json(("userId", null)),
                AuditSql.Json(("userId", userId), ("username", username), ("created", true), ("roles", string.Join(",", roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Code)))), ipAddress);
            return new(EmployeeOutcome.Saved, id, written);
        }, ex => ex.Number is 2601 or 2627 ? EmployeeOutcome.AccountTaken : null);

    public Task<EmployeeResult> LinkAccountAsync(Guid actorId, Guid id, Guid userId, string? stamp, string? ipAddress) =>
        Write(actorId, [AdminPermissions.EmployeesAccountLink], async (cn, tx) =>
        {
            var core = await ReadCore(cn, tx, id, true);
            if (core is null) return new(EmployeeOutcome.NotFound);
            if (core.Stamp != stamp) return new(EmployeeOutcome.Conflict, id);
            if (core.UserId is not null) return new(EmployeeOutcome.AlreadyLinked, id);
            if (Convert.ToInt32(await Scalar(cn, tx, EmployeeSql.UserLinked, ("@u", userId))) > 0) return new(EmployeeOutcome.AccountTaken, id);
            var candidate = (await Candidates(cn, tx, userId)).FirstOrDefault();
            if (candidate is null) return new(EmployeeOutcome.InvalidReference, id);
            if (await Exec(cn, tx, EmployeeSql.Link, ("@id", id), ("@rv", core.RowVersion), ("@u", userId), ("@by", actorId)) != 1) return new(EmployeeOutcome.Conflict, id);
            var written = await Audit(cn, tx, actorId, "EMPLOYEE_ACCOUNT_LINKED", id, AuditSql.Json(("userId", null)),
                AuditSql.Json(("userId", userId), ("username", candidate.Username), ("created", false)), ipAddress);
            return new(EmployeeOutcome.Saved, id, written);
        }, ex => ex.Number is 2601 or 2627 ? EmployeeOutcome.AccountTaken : null);

    public Task<EmployeeResult> UnlinkAccountAsync(Guid actorId, Guid id, string? stamp, string? ipAddress) =>
        Write(actorId, [AdminPermissions.EmployeesAccountLink], async (cn, tx) =>
        {
            var core = await ReadCore(cn, tx, id, true);
            if (core is null) return new(EmployeeOutcome.NotFound);
            if (core.Stamp != stamp) return new(EmployeeOutcome.Conflict, id);
            if (core.UserId is not Guid userId) return new(EmployeeOutcome.NotLinked, id);
            if (await Exec(cn, tx, EmployeeSql.Unlink, ("@id", id), ("@rv", core.RowVersion), ("@u", userId), ("@by", actorId)) != 1) return new(EmployeeOutcome.Conflict, id);
            var written = await Audit(cn, tx, actorId, "EMPLOYEE_ACCOUNT_UNLINKED", id, AuditSql.Json(("userId", userId)), AuditSql.Json(("userId", null)), ipAddress);
            return new(EmployeeOutcome.Saved, id, written);
        });

    public async Task<IReadOnlyDictionary<Guid, LinkedEmployee>> LinkedEmployeesAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var map = new Dictionary<Guid, LinkedEmployee>();
        await using var cmd = new SqlCommand(EmployeeSql.LinkedEmployees, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.GetGuid(0)] = new(r.GetGuid(1), r.GetString(2), r.GetString(3));
        return map;
    }

    // ---- helpers ------------------------------------------------------------------------------------------------
    private async Task<EmployeeResult> Write(Guid actorId, string[] permissions, Func<SqlConnection, SqlTransaction, Task<EmployeeResult>> body, Func<SqlException, EmployeeOutcome?>? map = null)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            foreach (var permission in permissions)
                if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actorId, permission)) return new(EmployeeOutcome.Forbidden);
            var result = await body(cn, tx);
            if (result.Outcome == EmployeeOutcome.Saved) await tx.CommitAsync();
            return result; // anything else rolls back on dispose
        }
        catch (AuditUnavailableException) { return new(EmployeeOutcome.AuditUnavailable); }
        catch (SqlException ex) when (map?.Invoke(ex) is EmployeeOutcome mapped) { return new(mapped); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { return new(EmployeeOutcome.Conflict); }
        catch (SqlException ex) when (ex.Number is 547) { return new(EmployeeOutcome.InvalidReference); }
    }

    private async Task<bool> Audit(SqlConnection cn, SqlTransaction tx, Guid actorId, string action, Guid id, string oldValues, string newValues, string? ip) =>
        (await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actorId, action, "dbo.Employees", id.ToString(), oldValues, newValues, ip)).Written;

    private static async Task<EmployeeLookups> ReadLookups(SqlConnection cn, SqlTransaction? tx)
    {
        var branches = new List<BranchOption>(); var departments = new List<DepartmentOption>(); var teams = new List<TeamOption>();
        var jobs = new List<JobTitleOption>(); var statuses = new List<HrLookup>(); var types = new List<HrLookup>();
        await using var cmd = new SqlCommand(EmployeeSql.Lookups, cn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) branches.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), S(r, 3), r.GetBoolean(4)));
        await r.NextResultAsync();
        while (await r.ReadAsync()) departments.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        await r.NextResultAsync();
        while (await r.ReadAsync()) teams.Add(new(r.GetGuid(0), r.GetGuid(1), G(r, 2), r.GetString(3), r.GetString(4), r.GetBoolean(5)));
        await r.NextResultAsync();
        while (await r.ReadAsync()) jobs.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetBoolean(3)));
        await r.NextResultAsync();
        while (await r.ReadAsync()) statuses.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetBoolean(3)));
        await r.NextResultAsync();
        while (await r.ReadAsync()) types.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetBoolean(3)));
        return new(branches, departments, teams, jobs, statuses, types);
    }

    private static async Task<EmployeeCore?> ReadCore(SqlConnection cn, SqlTransaction? tx, Guid id, bool forUpdate)
    {
        await using var cmd = new SqlCommand(forUpdate ? EmployeeSql.EmployeeForUpdate : EmployeeSql.Employee, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new(r.GetGuid(0), r.GetString(1), r.GetString(2), S(r, 3), D(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8), D(r, 9), D(r, 10), G(r, 11), G(r, 12),
            S(r, 13), S(r, 14), r.GetBoolean(15), S(r, 16), G(r, 17), (byte[])r.GetValue(18));
    }

    private static async Task<EmployeePrivateInput?> ReadPrivate(SqlConnection cn, SqlTransaction? tx, Guid id)
    {
        await using var cmd = new SqlCommand(EmployeeSql.Private, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new() { IdentityNumber = S(r, 0), IdentityIssueDate = D(r, 1), IdentityIssuePlace = S(r, 2), PersonalTaxCode = S(r, 3), SocialInsuranceNumber = S(r, 4), PermanentAddress = S(r, 5), CurrentAddress = S(r, 6) };
    }

    private static async Task<EmployeeAssignmentView?> ReadAssignment(SqlConnection cn, SqlTransaction? tx, Guid id)
    {
        await using var cmd = new SqlCommand(EmployeeSql.CurrentAssignment, cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new(r.GetGuid(0), r.GetGuid(1), G(r, 2), G(r, 3), G(r, 4), r.GetDateTime(5), S(r, 6));
    }

    private static async Task WritePrivate(SqlConnection cn, SqlTransaction tx, Guid id, EmployeePrivateInput p, Guid actorId) =>
        await Exec(cn, tx, EmployeeSql.UpdatePrivate, ("@id", id), ("@idn", Db(p.IdentityNumber)), ("@idd", Date(p.IdentityIssueDate)), ("@idp", Db(p.IdentityIssuePlace)),
            ("@ptc", Db(p.PersonalTaxCode)), ("@sin", Db(p.SocialInsuranceNumber)), ("@padr", Db(p.PermanentAddress)), ("@cadr", Db(p.CurrentAddress)), ("@by", actorId));

    private static async Task<List<EmployeeAccountCandidate>> Candidates(SqlConnection cn, SqlTransaction? tx, Guid? userId)
    {
        var list = new List<EmployeeAccountCandidate>();
        await using var cmd = new SqlCommand(EmployeeSql.Candidates, cn, tx);
        cmd.Parameters.Add("@u", SqlDbType.UniqueIdentifier).Value = (object?)userId ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), S(r, 3), r.GetBoolean(4)));
        return list;
    }

    private static async Task<string?> Suggest(SqlConnection cn, SqlTransaction? tx, string sequenceSql)
    {
        string? prefix, suffix; long current; int padding;
        await using (var cmd = new SqlCommand(sequenceSql, cn, tx))
        {
            cmd.Parameters.AddWithValue("@seq", EmployeePolicy.EmployeeCodeSequence);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            (prefix, suffix, current, padding) = (S(r, 0), S(r, 1), r.GetInt64(2), r.GetInt32(3));
        }
        var codes = new List<string>();
        await using (var cmd = new SqlCommand(EmployeeSql.CodesWithPrefix, cn, tx))
        {
            cmd.Parameters.Add("@p", SqlDbType.VarChar, 120).Value = (prefix ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) codes.Add(r.GetString(0));
        }
        return EmployeePolicy.NextCode(prefix, suffix, padding, current, codes);
    }

    // A code following the sequence pattern moves CurrentValue forward (never back), so the next suggestion skips it.
    private static async Task AdvanceSequence(SqlConnection cn, SqlTransaction tx, string code)
    {
        string? prefix, suffix; int padding;
        await using (var cmd = new SqlCommand(EmployeeSql.SequenceForUpdate, cn, tx))
        {
            cmd.Parameters.AddWithValue("@seq", EmployeePolicy.EmployeeCodeSequence);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return;
            (prefix, suffix, padding) = (S(r, 0), S(r, 1), r.GetInt32(3));
        }
        if (EmployeePolicy.SequenceNumber(code, prefix, suffix) is not long n || code.Length != (prefix?.Length ?? 0) + Math.Max(padding, n.ToString().Length) + (suffix?.Length ?? 0)) return;
        await Exec(cn, tx, EmployeeSql.SequenceAdvance, ("@seq", EmployeePolicy.EmployeeCodeSequence), ("@n", n));
    }

    private static Guid? G(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetGuid(i);
    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    private static object Db(string? value) => EmployeePolicy.Clean(value) is { } v ? v : DBNull.Value;
    private static object Db(Guid? value) => value is Guid g ? g : DBNull.Value;
    private static object Date(DateTime? value) => value is DateTime d ? new DateParam(d.Date) : DBNull.Value;

    private sealed record DateParam(DateTime Value);

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

    private static SqlCommand Command(SqlConnection cn, SqlTransaction tx, string sql, (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args)
        {
            if (value is DateParam d) cmd.Parameters.Add(name, SqlDbType.Date).Value = d.Value;
            else if (value is byte[] b) cmd.Parameters.Add(name, SqlDbType.Timestamp).Value = b;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }

    // Employees row as read inside the transaction.
    private sealed record EmployeeCore(
        Guid Id, string Code, string FullName, string? Gender, DateTime? DateOfBirth, string? MobilePhone, string? CompanyPhone, string? PersonalEmail,
        string? CompanyEmail, DateTime? StartDate, DateTime? EndDate, Guid? EmploymentTypeId, Guid? StatusId, string? StatusCode, string? StatusName,
        bool IsActive, string? Note, Guid? UserId, byte[] RowVersion)
    {
        public string Stamp => Convert.ToHexString(RowVersion);

        public EmployeeBasic Basic() => new(FullName, Gender, DateOfBirth?.Date, MobilePhone, CompanyPhone, PersonalEmail, CompanyEmail, StartDate?.Date, EmploymentTypeId, Note);

        public EmployeeDetail ToDetail(EmployeeAssignmentView? assignment, EmployeePrivateInput? priv, LinkedAccount? account,
            IReadOnlyList<EmployeeStatusHistoryRow> statuses, IReadOnlyList<EmployeeAssignmentHistoryRow> assignments) =>
            new(Id, Code, FullName, Gender, DateOfBirth, MobilePhone, CompanyPhone, PersonalEmail, CompanyEmail, StartDate, EndDate, EmploymentTypeId, StatusId,
                StatusCode, StatusName, IsActive, Note, assignment, priv, account, Stamp, statuses, assignments);
    }
}

// The non-private editable fields, normalised the same way for comparison (NoChange) and audit.
public sealed record EmployeeBasic(string FullName, string? Gender, DateTime? DateOfBirth, string? MobilePhone, string? CompanyPhone, string? PersonalEmail,
    string? CompanyEmail, DateTime? StartDate, Guid? EmploymentTypeId, string? Note)
{
    public static EmployeeBasic From(EmployeeInput i) => new(i.FullName.Trim(), EmployeePolicy.Clean(i.Gender), i.DateOfBirth?.Date, EmployeePolicy.Clean(i.MobilePhone),
        EmployeePolicy.Clean(i.CompanyPhone), EmployeePolicy.Clean(i.PersonalEmail), EmployeePolicy.Clean(i.CompanyEmail), i.StartDate?.Date, i.EmploymentTypeId, EmployeePolicy.Clean(i.Note));

    public string Json(params (string Key, object? Value)[] extra) => AuditSql.Json([
        ("fullName", FullName), ("gender", Gender), ("dateOfBirth", DateOfBirth?.ToString("yyyy-MM-dd")), ("mobilePhone", MobilePhone), ("companyPhone", CompanyPhone),
        ("personalEmail", PersonalEmail), ("companyEmail", CompanyEmail), ("startDate", StartDate?.ToString("yyyy-MM-dd")), ("employmentTypeId", EmploymentTypeId), ("note", Note), .. extra]);
}
