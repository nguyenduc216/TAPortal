using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-009
// The earlier create implementation did not gate customer scope or verify target
// organization eligibility within the write transaction. Keep these entry points
// fail-closed until an atomic, fully validated relation workflow is implemented.
public sealed partial class PortalDb
{
    // CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-013
    // The caller must supply its authenticated identity; this method never accepts a
    // VIEW result or creates grants on behalf of the target employee.
    public async Task<CrmRelationWriteResult> CreateCustomerEmployeeAssignmentAsync(CrmRelationWriteRequest request, Guid actorId)
    {
        if (request is null || actorId == Guid.Empty || request.CustomerId == Guid.Empty || request.TargetId == Guid.Empty
            || !CrmRelationRules.IsValidPeriod(request.ValidFrom, request.ValidTo)
            || request.RoleCode is not ("OWNER" or "PRIMARY" or "SECONDARY" or "VIEWER"))
            return CrmRelationWriteResult.Invalid;

        await using var cn = Open();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (!await AuthorizeCustomerMutationAsync(cn, tx, actorId, request.CustomerId, CustomerMutationAction.Assign))
            {
                await tx.RollbackAsync();
                return CrmRelationWriteResult.Forbidden;
            }

            // A customer assignment is an internal T.A employee relation, not a
            // grant. Require an eligible target and a single coherent HR posting.
            // Dates are inclusive; the whole requested interval must fit both the
            // employee's employment period and its current primary posting.
            const string eligibilitySql = """
DECLARE @today date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
SELECT CAST(CASE WHEN EXISTS (
 SELECT 1 FROM dbo.Customers c WITH (UPDLOCK,HOLDLOCK)
 JOIN dbo.Employees e WITH (UPDLOCK,HOLDLOCK) ON e.Id=@employeeId AND e.IsActive=1 AND e.IsDeleted=0
 JOIN dbo.Users targetUser ON targetUser.Id=e.UserId AND targetUser.IsActive=1 AND targetUser.IsDeleted=0 AND targetUser.Status='ACTIVE'
 JOIN dbo.EmployeeStatuses s ON s.Id=e.CurrentStatusId AND s.Code IN ('ACTIVE','PROBATION','MATERNITY')
 WHERE c.Id=@customerId AND c.IsActive=1 AND c.IsDeleted=0 AND c.Status='ACTIVE'
   AND @validFrom>=@today
   AND (e.StartDate IS NULL OR e.StartDate<=@today) AND (e.EndDate IS NULL OR e.EndDate>=@validFrom)
   AND (e.StartDate IS NULL OR e.StartDate<=@validFrom)
   AND (@validTo IS NULL OR e.EndDate IS NULL OR e.EndDate>=@validTo)
   AND (SELECT COUNT(*) FROM dbo.Employees other WITH (HOLDLOCK) WHERE other.UserId=e.UserId AND other.IsDeleted=0)=1
   AND (SELECT COUNT(*) FROM dbo.EmployeeAssignments a WITH (UPDLOCK,HOLDLOCK)
        WHERE a.EmployeeId=e.Id AND a.IsCurrent=1 AND a.IsPrimary=1 AND a.ValidFrom<=@today
          AND (a.ValidTo IS NULL OR a.ValidTo>=@today))=1
   AND EXISTS (SELECT 1 FROM dbo.EmployeeAssignments a WITH (UPDLOCK,HOLDLOCK)
     JOIN dbo.Branches b ON b.Id=a.BranchId AND b.IsActive=1 AND b.IsDeleted=0
     JOIN dbo.Companies co ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0
     LEFT JOIN dbo.Departments d ON d.Id=a.DepartmentId
     LEFT JOIN dbo.Teams t ON t.Id=a.TeamId
     WHERE a.EmployeeId=e.Id AND a.IsCurrent=1 AND a.IsPrimary=1
       AND (a.BranchId=c.OwnerBranchId OR EXISTS (SELECT 1 FROM dbo.CustomerBranchShares share
         WHERE share.CustomerId=c.Id AND share.BranchId=a.BranchId AND share.IsActive=1
           AND share.ValidFrom<=@validFrom AND (share.ValidTo IS NULL OR (@validTo IS NOT NULL AND share.ValidTo>=@validTo))))
       AND a.ValidFrom<=@today AND a.ValidFrom<=@validFrom
       AND (a.ValidTo IS NULL OR a.ValidTo>=@validFrom)
       AND (@validTo IS NULL OR a.ValidTo IS NULL OR a.ValidTo>=@validTo)
       AND (a.DepartmentId IS NULL OR (d.IsActive=1 AND d.IsDeleted=0 AND d.BranchId=a.BranchId))
       AND (a.TeamId IS NULL OR (t.IsActive=1 AND t.IsDeleted=0 AND t.BranchId=a.BranchId
          AND ((t.DepartmentId IS NULL AND a.DepartmentId IS NULL) OR t.DepartmentId=a.DepartmentId))))
) THEN 1 ELSE 0 END AS bit);
""";
            await using (var eligibility = new SqlCommand(eligibilitySql, cn, tx))
            {
                eligibility.Parameters.Add("@employeeId", SqlDbType.UniqueIdentifier).Value = request.TargetId;
                eligibility.Parameters.Add("@customerId", SqlDbType.UniqueIdentifier).Value = request.CustomerId;
                eligibility.Parameters.Add("@validFrom", SqlDbType.Date).Value = request.ValidFrom.ToDateTime(TimeOnly.MinValue);
                eligibility.Parameters.Add("@validTo", SqlDbType.Date).Value = request.ValidTo.HasValue
                    ? request.ValidTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
                if (!Convert.ToBoolean(await eligibility.ExecuteScalarAsync()))
                {
                    await tx.RollbackAsync();
                    return CrmRelationWriteResult.Invalid;
                }
            }

            // The customer row is held by authorization and eligibility until
            // commit; competing create calls for this customer serialize here.
            const string overlapSql = """
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.CustomerEmployeeAssignments ca WITH (UPDLOCK,HOLDLOCK)
 WHERE ca.CustomerId=@customerId AND ca.EmployeeId=@employeeId AND ca.IsActive=1
   AND ca.ValidFrom<=ISNULL(@validTo,CONVERT(date,'99991231',112))
   AND (ca.ValidTo IS NULL OR ca.ValidTo>=@validFrom)) THEN 1 ELSE 0 END AS bit);
""";
            await using (var overlap = new SqlCommand(overlapSql, cn, tx))
            {
                overlap.Parameters.Add("@customerId", SqlDbType.UniqueIdentifier).Value = request.CustomerId;
                overlap.Parameters.Add("@employeeId", SqlDbType.UniqueIdentifier).Value = request.TargetId;
                overlap.Parameters.Add("@validFrom", SqlDbType.Date).Value = request.ValidFrom.ToDateTime(TimeOnly.MinValue);
                overlap.Parameters.Add("@validTo", SqlDbType.Date).Value = request.ValidTo.HasValue
                    ? request.ValidTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
                if (Convert.ToBoolean(await overlap.ExecuteScalarAsync()))
                {
                    await tx.RollbackAsync();
                    return CrmRelationWriteResult.Overlap;
                }
            }

            var relationId = Guid.NewGuid();
            const string insertSql = """
INSERT dbo.CustomerEmployeeAssignments(Id,CustomerId,EmployeeId,RoleCode,ValidFrom,ValidTo,IsActive,CreatedBy)
VALUES(@id,@customerId,@employeeId,@roleCode,@validFrom,@validTo,1,@actorId);
""";
            await using (var insert = new SqlCommand(insertSql, cn, tx))
            {
                insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = relationId;
                insert.Parameters.Add("@customerId", SqlDbType.UniqueIdentifier).Value = request.CustomerId;
                insert.Parameters.Add("@employeeId", SqlDbType.UniqueIdentifier).Value = request.TargetId;
                insert.Parameters.Add("@roleCode", SqlDbType.VarChar, 50).Value = request.RoleCode;
                insert.Parameters.Add("@validFrom", SqlDbType.Date).Value = request.ValidFrom.ToDateTime(TimeOnly.MinValue);
                insert.Parameters.Add("@validTo", SqlDbType.Date).Value = request.ValidTo.HasValue
                    ? request.ValidTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
                insert.Parameters.Add("@actorId", SqlDbType.UniqueIdentifier).Value = actorId;
                if (await insert.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("Assignment insert did not affect one row.");
            }

            const string auditSql = """
INSERT dbo.AuditLogs(UserId,ModuleCode,FunctionCode,ActionCode,EntityType,EntityId,OldValues,NewValues,CreatedAt)
VALUES(@actorId,'CRM','Customers','ASSIGN','CustomerAssignment',@id,NULL,@newValues,SYSUTCDATETIME());
""";
            await using (var audit = new SqlCommand(auditSql, cn, tx))
            {
                audit.Parameters.Add("@actorId", SqlDbType.UniqueIdentifier).Value = actorId;
                audit.Parameters.Add("@id", SqlDbType.NVarChar, 200).Value = relationId.ToString();
                audit.Parameters.Add("@newValues", SqlDbType.NVarChar, -1).Value = CrmRelationRules.Serialize(new
                {
                    request.CustomerId, EmployeeId = request.TargetId, request.RoleCode, request.ValidFrom, request.ValidTo
                });
                if (await audit.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("Assignment audit did not affect one row.");
            }

            await tx.CommitAsync();
            return CrmRelationWriteResult.Success;
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch (Exception rollbackEx) { _logger.LogError(rollbackEx, "CRM assignment rollback failed"); }
            _logger.LogError(ex, "CRM customer assignment create failed");
            return CrmRelationWriteResult.Failed;
        }
    }

    // CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-014
    public Task<CrmRelationWriteResult> CreateCustomerBranchShareAsync(CrmRelationWriteRequest request, Guid actorId) =>
        CreateOrganizationRelationAsync(request, actorId, CustomerMutationAction.Share);

    public Task<CrmRelationWriteResult> CreateCustomerServiceTeamAsync(CrmRelationWriteRequest request, Guid actorId) =>
        CreateOrganizationRelationAsync(request, actorId, CustomerMutationAction.Team);

    private async Task<CrmRelationWriteResult> CreateOrganizationRelationAsync(
        CrmRelationWriteRequest request, Guid actorId, CustomerMutationAction action)
    {
        if (request is null || actorId == Guid.Empty || request.CustomerId == Guid.Empty || request.TargetId == Guid.Empty
            || request.RoleCode is not null || !CrmRelationRules.IsValidPeriod(request.ValidFrom, request.ValidTo))
            return CrmRelationWriteResult.Invalid;

        // These identifiers are selected exclusively from the internal enum, never user input.
        var isShare = action == CustomerMutationAction.Share;
        if (!isShare && action != CustomerMutationAction.Team) return CrmRelationWriteResult.Invalid;
        var table = isShare ? "CustomerBranchShares" : "CustomerServiceTeams";
        var targetColumn = isShare ? "BranchId" : "TeamId";
        var auditAction = isShare ? "SHARE" : "TEAM";
        var entityType = isShare ? "CustomerBranchShare" : "CustomerServiceTeam";

        await using var cn = Open();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (!await AuthorizeCustomerMutationAsync(cn, tx, actorId, request.CustomerId, action))
            {
                await tx.RollbackAsync();
                return CrmRelationWriteResult.Forbidden;
            }

            // Share to the owner is redundant. A team belongs to an active branch,
            // with an optional department that must belong to the same branch.
            // Share/Team changes customer visibility: only allow a target within
            // the customer's owner company, not an unrelated organization.
            var targetSql = isShare ? """
SELECT CAST(CASE WHEN EXISTS (
 SELECT 1 FROM dbo.Customers c WITH (UPDLOCK,HOLDLOCK)
 JOIN dbo.Branches ownerBranch WITH (HOLDLOCK) ON ownerBranch.Id=c.OwnerBranchId AND ownerBranch.IsActive=1 AND ownerBranch.IsDeleted=0
 JOIN dbo.Companies ownerCompany WITH (HOLDLOCK) ON ownerCompany.Id=ownerBranch.CompanyId AND ownerCompany.IsActive=1 AND ownerCompany.IsDeleted=0
 JOIN dbo.Branches b WITH (UPDLOCK,HOLDLOCK) ON b.Id=@targetId AND b.IsActive=1 AND b.IsDeleted=0
 JOIN dbo.Companies co WITH (HOLDLOCK) ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0
 WHERE c.Id=@customerId AND c.IsActive=1 AND c.IsDeleted=0 AND c.Status='ACTIVE'
 AND b.CompanyId=ownerBranch.CompanyId AND b.Id<>ownerBranch.Id
) THEN 1 ELSE 0 END AS bit);
""" : """
SELECT CAST(CASE WHEN EXISTS (
 SELECT 1 FROM dbo.Customers c WITH (UPDLOCK,HOLDLOCK)
 JOIN dbo.Branches ownerBranch WITH (HOLDLOCK) ON ownerBranch.Id=c.OwnerBranchId AND ownerBranch.IsActive=1 AND ownerBranch.IsDeleted=0
 JOIN dbo.Companies ownerCompany WITH (HOLDLOCK) ON ownerCompany.Id=ownerBranch.CompanyId AND ownerCompany.IsActive=1 AND ownerCompany.IsDeleted=0
 JOIN dbo.Teams t WITH (UPDLOCK,HOLDLOCK) ON t.Id=@targetId AND t.IsActive=1 AND t.IsDeleted=0
 JOIN dbo.Branches b WITH (HOLDLOCK) ON b.Id=t.BranchId AND b.IsActive=1 AND b.IsDeleted=0
 JOIN dbo.Companies co WITH (HOLDLOCK) ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0
 LEFT JOIN dbo.Departments d WITH (HOLDLOCK) ON d.Id=t.DepartmentId
 WHERE c.Id=@customerId AND c.IsActive=1 AND c.IsDeleted=0 AND c.Status='ACTIVE'
 AND b.CompanyId=ownerBranch.CompanyId
 AND (t.DepartmentId IS NULL OR (d.BranchId=t.BranchId AND d.IsActive=1 AND d.IsDeleted=0))
) THEN 1 ELSE 0 END AS bit);
""";
            await using (var target = new SqlCommand(targetSql, cn, tx))
            {
                target.Parameters.Add("@customerId", SqlDbType.UniqueIdentifier).Value = request.CustomerId;
                target.Parameters.Add("@targetId", SqlDbType.UniqueIdentifier).Value = request.TargetId;
                if (!Convert.ToBoolean(await target.ExecuteScalarAsync()))
                {
                    await tx.RollbackAsync();
                    return CrmRelationWriteResult.Invalid;
                }
            }

            // Authorization holds the customer row until commit, serializing all
            // competing relation creates for this customer. Inclusive date bounds.
            var overlapSql = $"""
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.{table} rel WITH (UPDLOCK,HOLDLOCK)
 WHERE rel.CustomerId=@customerId AND rel.{targetColumn}=@targetId AND rel.IsActive=1
 AND rel.ValidFrom<=ISNULL(@validTo,CONVERT(date,'99991231',112))
 AND (rel.ValidTo IS NULL OR rel.ValidTo>=@validFrom)) THEN 1 ELSE 0 END AS bit);
""";
            await using (var overlap = new SqlCommand(overlapSql, cn, tx))
            {
                overlap.Parameters.Add("@customerId", SqlDbType.UniqueIdentifier).Value = request.CustomerId;
                overlap.Parameters.Add("@targetId", SqlDbType.UniqueIdentifier).Value = request.TargetId;
                overlap.Parameters.Add("@validFrom", SqlDbType.Date).Value = request.ValidFrom.ToDateTime(TimeOnly.MinValue);
                overlap.Parameters.Add("@validTo", SqlDbType.Date).Value = request.ValidTo.HasValue
                    ? request.ValidTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
                if (Convert.ToBoolean(await overlap.ExecuteScalarAsync()))
                {
                    await tx.RollbackAsync();
                    return CrmRelationWriteResult.Overlap;
                }
            }

            var id = Guid.NewGuid();
            var insertSql = $"""
INSERT dbo.{table}(Id,CustomerId,{targetColumn},ValidFrom,ValidTo,IsActive,CreatedBy)
VALUES(@id,@customerId,@targetId,@validFrom,@validTo,1,@actorId);
""";
            await using (var insert = new SqlCommand(insertSql, cn, tx))
            {
                insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                insert.Parameters.Add("@customerId", SqlDbType.UniqueIdentifier).Value = request.CustomerId;
                insert.Parameters.Add("@targetId", SqlDbType.UniqueIdentifier).Value = request.TargetId;
                insert.Parameters.Add("@validFrom", SqlDbType.Date).Value = request.ValidFrom.ToDateTime(TimeOnly.MinValue);
                insert.Parameters.Add("@validTo", SqlDbType.Date).Value = request.ValidTo.HasValue
                    ? request.ValidTo.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
                insert.Parameters.Add("@actorId", SqlDbType.UniqueIdentifier).Value = actorId;
                if (await insert.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("CRM relation insert did not affect one row.");
            }

            const string auditSql = """
INSERT dbo.AuditLogs(UserId,ModuleCode,FunctionCode,ActionCode,EntityType,EntityId,OldValues,NewValues,CreatedAt)
VALUES(@actorId,'CRM','Customers',@action,@entityType,@id,NULL,@newValues,SYSUTCDATETIME());
""";
            await using (var audit = new SqlCommand(auditSql, cn, tx))
            {
                audit.Parameters.Add("@actorId", SqlDbType.UniqueIdentifier).Value = actorId;
                audit.Parameters.Add("@action", SqlDbType.VarChar, 30).Value = auditAction;
                audit.Parameters.Add("@entityType", SqlDbType.VarChar, 100).Value = entityType;
                audit.Parameters.Add("@id", SqlDbType.NVarChar, 200).Value = id.ToString();
                audit.Parameters.Add("@newValues", SqlDbType.NVarChar, -1).Value = CrmRelationRules.Serialize(new
                {
                    request.CustomerId, request.TargetId, request.ValidFrom, request.ValidTo
                });
                if (await audit.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("CRM relation audit did not affect one row.");
            }
            await tx.CommitAsync();
            return CrmRelationWriteResult.Success;
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch (Exception rollbackEx) { _logger.LogError(rollbackEx, "CRM relation rollback failed"); }
            _logger.LogError(ex, "CRM organization relation create failed");
            return CrmRelationWriteResult.Failed;
        }
    }
}