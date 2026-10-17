using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-015
public sealed partial class PortalDb
{
    public Task<CrmRelationWriteResult> ChangeCustomerEmployeeAssignmentAsync(CrmRelationChangeRequest request, Guid actorId) =>
        MutateRelationAsync(request, null, actorId, CustomerMutationAction.Assign);
    public Task<CrmRelationWriteResult> RevokeCustomerEmployeeAssignmentAsync(CrmRelationRevokeRequest request, Guid actorId) =>
        MutateRelationAsync(null, request, actorId, CustomerMutationAction.Assign);
    public Task<CrmRelationWriteResult> ChangeCustomerBranchShareAsync(CrmRelationChangeRequest request, Guid actorId) =>
        MutateRelationAsync(request, null, actorId, CustomerMutationAction.Share);
    public Task<CrmRelationWriteResult> RevokeCustomerBranchShareAsync(CrmRelationRevokeRequest request, Guid actorId) =>
        MutateRelationAsync(null, request, actorId, CustomerMutationAction.Share);
    public Task<CrmRelationWriteResult> ChangeCustomerServiceTeamAsync(CrmRelationChangeRequest request, Guid actorId) =>
        MutateRelationAsync(request, null, actorId, CustomerMutationAction.Team);
    public Task<CrmRelationWriteResult> RevokeCustomerServiceTeamAsync(CrmRelationRevokeRequest request, Guid actorId) =>
        MutateRelationAsync(null, request, actorId, CustomerMutationAction.Team);

    private async Task<CrmRelationWriteResult> MutateRelationAsync(CrmRelationChangeRequest? change,
        CrmRelationRevokeRequest? revoke, Guid actorId, CustomerMutationAction action)
    {
        var id = change?.RelationId ?? revoke?.RelationId ?? Guid.Empty;
        var version = change?.RowVersion ?? revoke?.RowVersion;
        if (actorId == Guid.Empty || id == Guid.Empty || version is not { Length: 8 }
            || (change is null && revoke is null) || (change is not null && revoke is not null)
            || (change is not null && (!CrmRelationRules.IsValidPeriod(change.ValidFrom, change.ValidTo)
                || (action == CustomerMutationAction.Assign
                    ? change.RoleCode is not ("OWNER" or "PRIMARY" or "SECONDARY" or "VIEWER")
                    : change.RoleCode is not null)))) return CrmRelationWriteResult.Invalid;

        var table = action switch
        {
            CustomerMutationAction.Assign => "CustomerEmployeeAssignments",
            CustomerMutationAction.Share => "CustomerBranchShares",
            CustomerMutationAction.Team => "CustomerServiceTeams",
            _ => null
        };
        if (table is null) return CrmRelationWriteResult.Invalid;
        var targetColumn = action switch
        {
            CustomerMutationAction.Assign => "EmployeeId",
            CustomerMutationAction.Share => "BranchId",
            _ => "TeamId"
        };
        var entityType = action switch
        {
            CustomerMutationAction.Assign => "CustomerAssignment",
            CustomerMutationAction.Share => "CustomerBranchShare",
            _ => "CustomerServiceTeam"
        };

        await using var cn = Open();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Discover the persisted customer without trusting a client-supplied ID.
            // Do not acquire an update lock on the relation before authorization;
            // the guarded update checks RowVersion again after the customer lock.
            var lookupSql = $"SELECT CustomerId FROM dbo.{table} WHERE Id=@id;";
            Guid customerId;
            await using (var lookup = new SqlCommand(lookupSql, cn, tx))
            {
                lookup.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                var value = await lookup.ExecuteScalarAsync();
                if (value is not Guid storedCustomer)
                {
                    await tx.RollbackAsync();
                    return CrmRelationWriteResult.NotFound;
                }
                customerId = storedCustomer;
            }
            if (!await AuthorizeCustomerMutationAsync(cn, tx, actorId, customerId, action))
            {
                await tx.RollbackAsync();
                return CrmRelationWriteResult.Forbidden;
            }

            var loadSql = $"SELECT CustomerId,{targetColumn},ValidFrom,ValidTo,IsActive,RowVersion"
                + (action == CustomerMutationAction.Assign ? ",RoleCode" : "")
                + $" FROM dbo.{table} WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id;";
            Guid targetId;
            DateOnly oldFrom;
            DateOnly? oldTo;
            string? oldRole;
            await using (var load = new SqlCommand(loadSql, cn, tx))
            {
                load.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                await using var reader = await load.ExecuteReaderAsync();
                if (!await reader.ReadAsync() || reader.GetGuid(0) != customerId)
                {
                    await reader.CloseAsync();
                    await tx.RollbackAsync();
                    return CrmRelationWriteResult.NotFound;
                }
                targetId = reader.GetGuid(1);
                oldFrom = DateOnly.FromDateTime(reader.GetDateTime(2));
                oldTo = reader.IsDBNull(3) ? null : DateOnly.FromDateTime(reader.GetDateTime(3));
                var active = reader.GetBoolean(4);
                var currentVersion = (byte[])reader[5];
                oldRole = action == CustomerMutationAction.Assign ? reader.GetString(6) : null;
                await reader.CloseAsync();
                if (!currentVersion.AsSpan().SequenceEqual(version))
                {
                    await tx.RollbackAsync();
                    return CrmRelationWriteResult.ConcurrencyConflict;
                }
                // Repeated revoke is a conflict, never a second audit entry.
                if (!active)
                {
                    await tx.RollbackAsync();
                    return CrmRelationWriteResult.ConcurrencyConflict;
                }
            }

            if (change is not null)
            {
                // Reuse Create's validation by executing the same target predicates
                // against persisted identifiers (never an untrusted customer/target).
                var validationSql = action switch
                {
                    CustomerMutationAction.Assign => """
DECLARE @today date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Customers c
 JOIN dbo.Employees e ON e.Id=@target AND e.IsActive=1 AND e.IsDeleted=0
 JOIN dbo.Users u ON u.Id=e.UserId AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE'
 JOIN dbo.EmployeeStatuses s ON s.Id=e.CurrentStatusId AND s.Code IN ('ACTIVE','PROBATION','MATERNITY')
 WHERE c.Id=@customer AND c.IsActive=1 AND c.IsDeleted=0 AND c.Status='ACTIVE'
 AND @from>=@today AND (e.StartDate IS NULL OR e.StartDate<=@today)
 AND (e.StartDate IS NULL OR e.StartDate<=@from) AND (e.EndDate IS NULL OR e.EndDate>=@from)
 AND (@to IS NULL OR e.EndDate IS NULL OR e.EndDate>=@to)
 AND (SELECT COUNT(*) FROM dbo.Employees x WHERE x.UserId=e.UserId AND x.IsDeleted=0)=1
 AND (SELECT COUNT(*) FROM dbo.EmployeeAssignments a WITH (HOLDLOCK)
     WHERE a.EmployeeId=e.Id AND a.IsCurrent=1 AND a.IsPrimary=1 AND a.ValidFrom<=@today
       AND (a.ValidTo IS NULL OR a.ValidTo>=@today))=1
 AND EXISTS (SELECT 1 FROM dbo.EmployeeAssignments a WITH (HOLDLOCK)
     JOIN dbo.Branches b ON b.Id=a.BranchId AND b.IsActive=1 AND b.IsDeleted=0
     JOIN dbo.Companies co ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0
     LEFT JOIN dbo.Departments d ON d.Id=a.DepartmentId
     LEFT JOIN dbo.Teams t ON t.Id=a.TeamId
     WHERE a.EmployeeId=e.Id AND a.IsCurrent=1 AND a.IsPrimary=1
       AND a.ValidFrom<=@today AND a.ValidFrom<=@from AND (a.ValidTo IS NULL OR a.ValidTo>=@from)
       AND (@to IS NULL OR a.ValidTo IS NULL OR a.ValidTo>=@to)
       AND (a.BranchId=c.OwnerBranchId OR EXISTS (SELECT 1 FROM dbo.CustomerBranchShares sh
          WHERE sh.CustomerId=c.Id AND sh.BranchId=a.BranchId AND sh.IsActive=1
          AND sh.ValidFrom<=@from AND (sh.ValidTo IS NULL OR (@to IS NOT NULL AND sh.ValidTo>=@to))))
       AND (a.DepartmentId IS NULL OR (d.IsActive=1 AND d.IsDeleted=0 AND d.BranchId=a.BranchId))
       AND (a.TeamId IS NULL OR (t.IsActive=1 AND t.IsDeleted=0 AND t.BranchId=a.BranchId
          AND ((t.DepartmentId IS NULL AND a.DepartmentId IS NULL) OR t.DepartmentId=a.DepartmentId))))
) THEN 1 ELSE 0 END AS bit);
""",
                    CustomerMutationAction.Share => """
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Customers c
 JOIN dbo.Branches ownerBranch ON ownerBranch.Id=c.OwnerBranchId AND ownerBranch.IsActive=1 AND ownerBranch.IsDeleted=0
 JOIN dbo.Companies ownerCompany ON ownerCompany.Id=ownerBranch.CompanyId AND ownerCompany.IsActive=1 AND ownerCompany.IsDeleted=0
 JOIN dbo.Branches b ON b.Id=@target AND b.IsActive=1 AND b.IsDeleted=0
 JOIN dbo.Companies co ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0
 WHERE c.Id=@customer AND c.IsActive=1 AND c.IsDeleted=0 AND c.Status='ACTIVE'
 AND b.CompanyId=ownerBranch.CompanyId AND b.Id<>ownerBranch.Id) THEN 1 ELSE 0 END AS bit);
""",
                    _ => """
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Customers c
 JOIN dbo.Branches ownerBranch ON ownerBranch.Id=c.OwnerBranchId AND ownerBranch.IsActive=1 AND ownerBranch.IsDeleted=0
 JOIN dbo.Companies ownerCompany ON ownerCompany.Id=ownerBranch.CompanyId AND ownerCompany.IsActive=1 AND ownerCompany.IsDeleted=0
 JOIN dbo.Teams t ON t.Id=@target AND t.IsActive=1 AND t.IsDeleted=0
 JOIN dbo.Branches b ON b.Id=t.BranchId AND b.IsActive=1 AND b.IsDeleted=0
 JOIN dbo.Companies co ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0
 LEFT JOIN dbo.Departments d ON d.Id=t.DepartmentId
 WHERE c.Id=@customer AND c.IsActive=1 AND c.IsDeleted=0 AND c.Status='ACTIVE'
 AND b.CompanyId=ownerBranch.CompanyId
 AND (t.DepartmentId IS NULL OR (d.BranchId=t.BranchId AND d.IsActive=1 AND d.IsDeleted=0))) THEN 1 ELSE 0 END AS bit);
"""
                };
                await using (var validate = new SqlCommand(validationSql, cn, tx))
                {
                    validate.Parameters.Add("@customer", SqlDbType.UniqueIdentifier).Value = customerId;
                    validate.Parameters.Add("@target", SqlDbType.UniqueIdentifier).Value = targetId;
                    if (action == CustomerMutationAction.Assign)
                    {
                        validate.Parameters.Add("@from", SqlDbType.Date).Value = change.ValidFrom.ToDateTime(TimeOnly.MinValue);
                        validate.Parameters.Add("@to", SqlDbType.Date).Value = change.ValidTo.HasValue
                            ? change.ValidTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
                    }
                    if (!Convert.ToBoolean(await validate.ExecuteScalarAsync()))
                    {
                        await tx.RollbackAsync();
                        return CrmRelationWriteResult.Invalid;
                    }
                }
                var overlapSql = $"""
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.{table} rel WITH (UPDLOCK,HOLDLOCK)
 WHERE rel.Id<>@id AND rel.CustomerId=@customer AND rel.{targetColumn}=@target AND rel.IsActive=1
 AND rel.ValidFrom<=ISNULL(@to,CONVERT(date,'99991231',112))
 AND (rel.ValidTo IS NULL OR rel.ValidTo>=@from)) THEN 1 ELSE 0 END AS bit);
""";
                await using (var overlap = new SqlCommand(overlapSql, cn, tx))
                {
                    overlap.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                    overlap.Parameters.Add("@customer", SqlDbType.UniqueIdentifier).Value = customerId;
                    overlap.Parameters.Add("@target", SqlDbType.UniqueIdentifier).Value = targetId;
                    overlap.Parameters.Add("@from", SqlDbType.Date).Value = change.ValidFrom.ToDateTime(TimeOnly.MinValue);
                    overlap.Parameters.Add("@to", SqlDbType.Date).Value = change.ValidTo.HasValue
                        ? change.ValidTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
                    if (Convert.ToBoolean(await overlap.ExecuteScalarAsync()))
                    {
                        await tx.RollbackAsync();
                        return CrmRelationWriteResult.Overlap;
                    }
                }
            }

            var updateSql = change is null
                ? $"UPDATE dbo.{table} SET IsActive=0 WHERE Id=@id AND CustomerId=@customer AND IsActive=1 AND RowVersion=@version;"
                : $"UPDATE dbo.{table} SET ValidFrom=@from,ValidTo=@to"
                    + (action == CustomerMutationAction.Assign ? ",RoleCode=@role" : "")
                    + " WHERE Id=@id AND CustomerId=@customer AND IsActive=1 AND RowVersion=@version;";
            await using (var update = new SqlCommand(updateSql, cn, tx))
            {
                update.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                update.Parameters.Add("@customer", SqlDbType.UniqueIdentifier).Value = customerId;
                update.Parameters.Add("@version", SqlDbType.Timestamp, 8).Value = version!;
                if (change is not null)
                {
                    update.Parameters.Add("@from", SqlDbType.Date).Value = change.ValidFrom.ToDateTime(TimeOnly.MinValue);
                    update.Parameters.Add("@to", SqlDbType.Date).Value = change.ValidTo.HasValue
                        ? change.ValidTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
                    if (action == CustomerMutationAction.Assign)
                        update.Parameters.Add("@role", SqlDbType.VarChar, 50).Value = change.RoleCode!;
                }
                if (await update.ExecuteNonQueryAsync() != 1)
                {
                    await tx.RollbackAsync();
                    return CrmRelationWriteResult.ConcurrencyConflict;
                }
            }

            const string auditSql = """
INSERT dbo.AuditLogs(UserId,ModuleCode,FunctionCode,ActionCode,EntityType,EntityId,OldValues,NewValues,CreatedAt)
VALUES(@actor,'CRM','Customers',@action,@entity,@id,@old,@new,SYSUTCDATETIME());
""";
            await using (var audit = new SqlCommand(auditSql, cn, tx))
            {
                audit.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actorId;
                audit.Parameters.Add("@action", SqlDbType.VarChar, 30).Value = change is null ? "REVOKE" : "CHANGE";
                audit.Parameters.Add("@entity", SqlDbType.VarChar, 100).Value = entityType;
                audit.Parameters.Add("@id", SqlDbType.NVarChar, 200).Value = id.ToString();
                audit.Parameters.Add("@old", SqlDbType.NVarChar, -1).Value = CrmRelationRules.Serialize(new
                {
                    CustomerId = customerId, TargetId = targetId, ValidFrom = oldFrom, ValidTo = oldTo,
                    RoleCode = oldRole, IsActive = true
                });
                audit.Parameters.Add("@new", SqlDbType.NVarChar, -1).Value = CrmRelationRules.Serialize(new
                {
                    CustomerId = customerId, TargetId = targetId,
                    ValidFrom = change?.ValidFrom ?? oldFrom, ValidTo = change is null ? oldTo : change.ValidTo,
                    RoleCode = change?.RoleCode ?? oldRole, IsActive = change is not null
                });
                if (await audit.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("CRM mutation audit did not affect one row.");
            }
            await tx.CommitAsync();
            return CrmRelationWriteResult.Success;
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch (Exception rollbackEx) { _logger.LogError(rollbackEx, "CRM mutation rollback failed"); }
            _logger.LogError(ex, "CRM relation mutation failed");
            return CrmRelationWriteResult.Failed;
        }
    }
}