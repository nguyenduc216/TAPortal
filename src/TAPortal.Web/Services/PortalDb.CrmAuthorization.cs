using System.Data;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-011C
internal enum CustomerMutationAction { Assign, Share, Team }

public sealed partial class PortalDb
{
    // Caller owns a SERIALIZABLE transaction and must recheck immediately before writing.
    // UPDLOCK/HOLDLOCK on the customer serializes competing customer mutations; SERIALIZABLE
    // also protects matching relationship ranges. Changes to RBAC/HR outside this lock
    // protocol require database-backed concurrency verification before enabling writes.
    internal static async Task<bool> AuthorizeCustomerMutationAsync(SqlConnection connection,
        SqlTransaction transaction, Guid actorUserId, Guid customerId, CustomerMutationAction action)
    {
        if (connection.State != ConnectionState.Open || transaction.Connection != connection
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || actorUserId == Guid.Empty || customerId == Guid.Empty) return false;
        var actionKey = action switch
        {
            CustomerMutationAction.Assign => "Customers.Assign",
            CustomerMutationAction.Share => "Customers.Share",
            CustomerMutationAction.Team => "Customers.Team",
            _ => null
        };
        if (actionKey is null) return false;

        // Same customer function scope convention as GetCustomersAsync. An Index-only
        // grant is NOT promoted into management; Customers is the common function grant.
        const string sql = """
DECLARE @today date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
SELECT CASE WHEN EXISTS (
 SELECT 1 FROM dbo.Customers c WITH (UPDLOCK,HOLDLOCK)
 JOIN dbo.Users u WITH (HOLDLOCK) ON u.Id=@userId AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE'
 JOIN dbo.Employees e WITH (HOLDLOCK) ON e.UserId=u.Id AND e.IsActive=1 AND e.IsDeleted=0
 JOIN dbo.EmployeeStatuses es ON es.Id=e.CurrentStatusId AND es.Code IN ('ACTIVE','PROBATION','MATERNITY')
 WHERE c.Id=@customerId AND c.IsDeleted=0 AND c.IsActive=1 AND c.Status='ACTIVE'
 AND (e.StartDate IS NULL OR e.StartDate<=@today) AND (e.EndDate IS NULL OR e.EndDate>=@today)
 AND (SELECT COUNT(*) FROM dbo.Employees el WHERE el.UserId=u.Id AND el.IsDeleted=0)=1
  AND (SELECT COUNT(*) FROM dbo.EmployeeAssignments ea WITH (HOLDLOCK)
      WHERE ea.EmployeeId=e.Id AND ea.IsPrimary=1 AND ea.IsCurrent=1
        AND ea.ValidFrom<=@today AND (ea.ValidTo IS NULL OR ea.ValidTo>=@today))=1
  AND (SELECT COUNT(*) FROM dbo.EmployeeAssignments ea WITH (HOLDLOCK)
      JOIN dbo.Branches eb ON eb.Id=ea.BranchId AND eb.IsActive=1 AND eb.IsDeleted=0
      JOIN dbo.Companies ec ON ec.Id=eb.CompanyId AND ec.IsActive=1 AND ec.IsDeleted=0
      LEFT JOIN dbo.Departments ed ON ed.Id=ea.DepartmentId
      LEFT JOIN dbo.Teams et ON et.Id=ea.TeamId
      WHERE ea.EmployeeId=e.Id AND ea.IsPrimary=1 AND ea.IsCurrent=1
        AND ea.ValidFrom<=@today AND (ea.ValidTo IS NULL OR ea.ValidTo>=@today)
        AND (ea.DepartmentId IS NULL OR (ed.IsActive=1 AND ed.IsDeleted=0 AND ed.BranchId=ea.BranchId))
        AND (ea.TeamId IS NULL OR (et.IsActive=1 AND et.IsDeleted=0 AND et.BranchId=ea.BranchId
             AND ((et.DepartmentId IS NULL AND ea.DepartmentId IS NULL) OR et.DepartmentId=ea.DepartmentId))))=1
 AND EXISTS (
  SELECT 1 FROM dbo.FunctionActions fa
  JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id
  JOIN dbo.Permissions p ON p.Id=fap.PermissionId AND p.IsActive=1 AND p.IsDeleted=0
  WHERE fa.IsActive=1 AND fa.IsDeleted=0 AND (fa.UiKey=@actionKey OR fa.EndpointKey=@actionKey)
   AND NOT EXISTS (SELECT 1 FROM dbo.UserPermissions up WHERE up.UserId=u.Id AND up.PermissionId=p.Id AND up.Effect='DENY')
   AND (EXISTS (SELECT 1 FROM dbo.UserPermissions up WHERE up.UserId=u.Id AND up.PermissionId=p.Id AND up.Effect='ALLOW')
     OR EXISTS (SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.IsActive=1 AND r.IsDeleted=0
                JOIN dbo.RolePermissions rp ON rp.RoleId=r.Id AND rp.PermissionId=p.Id WHERE ur.UserId=u.Id)
     OR EXISTS (SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.Code='SYS_ADMIN'
                AND r.IsSystem=1 AND r.IsActive=1 AND r.IsDeleted=0 WHERE ur.UserId=u.Id))
 )
 AND (
  EXISTS (SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.Code='SYS_ADMIN'
          AND r.IsSystem=1 AND r.IsActive=1 AND r.IsDeleted=0 WHERE ur.UserId=u.Id)
  OR EXISTS (SELECT 1 FROM dbo.UserDataScopes uds JOIN dbo.DataScopes ds ON ds.Id=uds.DataScopeId AND ds.IsActive=1
    WHERE uds.UserId=u.Id AND uds.Effect='ALLOW' AND uds.FunctionCode='Customers' AND uds.ScopeReferenceId IS NOT NULL
      AND (
        (ds.ScopeType='ASSIGNED' AND uds.ScopeReferenceId=e.Id AND EXISTS
          (SELECT 1 FROM dbo.CustomerEmployeeAssignments ca WHERE ca.CustomerId=c.Id AND ca.EmployeeId=e.Id AND ca.IsActive=1 AND ca.ValidFrom<=@today AND (ca.ValidTo IS NULL OR ca.ValidTo>=@today)))
        OR (ds.ScopeType='TEAM' AND EXISTS
          (SELECT 1 FROM dbo.CustomerServiceTeams st JOIN dbo.EmployeeAssignments ea ON ea.TeamId=st.TeamId AND ea.EmployeeId=e.Id
           WHERE st.CustomerId=c.Id AND st.TeamId=uds.ScopeReferenceId AND st.IsActive=1 AND st.ValidFrom<=@today AND (st.ValidTo IS NULL OR st.ValidTo>=@today)
             AND ea.IsCurrent=1 AND ea.IsPrimary=1 AND ea.ValidFrom<=@today AND (ea.ValidTo IS NULL OR ea.ValidTo>=@today)
             AND EXISTS (SELECT 1 FROM dbo.Branches eb JOIN dbo.Companies ec ON ec.Id=eb.CompanyId AND ec.IsActive=1 AND ec.IsDeleted=0 WHERE eb.Id=ea.BranchId AND eb.IsActive=1 AND eb.IsDeleted=0)
             AND (ea.DepartmentId IS NULL OR EXISTS (SELECT 1 FROM dbo.Departments ed WHERE ed.Id=ea.DepartmentId AND ed.BranchId=ea.BranchId AND ed.IsActive=1 AND ed.IsDeleted=0))
             AND EXISTS (SELECT 1 FROM dbo.Teams et WHERE et.Id=ea.TeamId AND et.BranchId=ea.BranchId AND et.IsActive=1 AND et.IsDeleted=0
               AND ((et.DepartmentId IS NULL AND ea.DepartmentId IS NULL) OR et.DepartmentId=ea.DepartmentId))))
        OR (ds.ScopeType='BRANCH' AND (c.OwnerBranchId=uds.ScopeReferenceId OR EXISTS
          (SELECT 1 FROM dbo.CustomerBranchShares bs WHERE bs.CustomerId=c.Id AND bs.BranchId=uds.ScopeReferenceId AND bs.IsActive=1 AND bs.ValidFrom<=@today AND (bs.ValidTo IS NULL OR bs.ValidTo>=@today))))
        OR (ds.ScopeType='COMPANY' AND EXISTS (SELECT 1 FROM dbo.Branches b JOIN dbo.Companies co ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0
             WHERE b.Id=c.OwnerBranchId AND b.CompanyId=uds.ScopeReferenceId AND b.IsActive=1 AND b.IsDeleted=0))
      ))
  OR EXISTS (SELECT 1 FROM dbo.RoleDataScopes rds JOIN dbo.DataScopes ds ON ds.Id=rds.DataScopeId AND ds.IsActive=1
    JOIN dbo.UserRoles ur ON ur.RoleId=rds.RoleId AND ur.UserId=u.Id
    JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.IsActive=1 AND r.IsDeleted=0
    WHERE rds.FunctionCode='Customers' AND rds.ScopeReferenceId IS NOT NULL
      AND (
        (ds.ScopeType='ASSIGNED' AND rds.ScopeReferenceId=e.Id AND EXISTS
          (SELECT 1 FROM dbo.CustomerEmployeeAssignments ca WHERE ca.CustomerId=c.Id AND ca.EmployeeId=e.Id AND ca.IsActive=1 AND ca.ValidFrom<=@today AND (ca.ValidTo IS NULL OR ca.ValidTo>=@today)))
        OR (ds.ScopeType='TEAM' AND EXISTS
          (SELECT 1 FROM dbo.CustomerServiceTeams st JOIN dbo.EmployeeAssignments ea ON ea.TeamId=st.TeamId AND ea.EmployeeId=e.Id
           WHERE st.CustomerId=c.Id AND st.TeamId=rds.ScopeReferenceId AND st.IsActive=1 AND st.ValidFrom<=@today AND (st.ValidTo IS NULL OR st.ValidTo>=@today)
             AND ea.IsCurrent=1 AND ea.IsPrimary=1 AND ea.ValidFrom<=@today AND (ea.ValidTo IS NULL OR ea.ValidTo>=@today)
             AND EXISTS (SELECT 1 FROM dbo.Branches eb JOIN dbo.Companies ec ON ec.Id=eb.CompanyId AND ec.IsActive=1 AND ec.IsDeleted=0 WHERE eb.Id=ea.BranchId AND eb.IsActive=1 AND eb.IsDeleted=0)
             AND (ea.DepartmentId IS NULL OR EXISTS (SELECT 1 FROM dbo.Departments ed WHERE ed.Id=ea.DepartmentId AND ed.BranchId=ea.BranchId AND ed.IsActive=1 AND ed.IsDeleted=0))
             AND EXISTS (SELECT 1 FROM dbo.Teams et WHERE et.Id=ea.TeamId AND et.BranchId=ea.BranchId AND et.IsActive=1 AND et.IsDeleted=0
               AND ((et.DepartmentId IS NULL AND ea.DepartmentId IS NULL) OR et.DepartmentId=ea.DepartmentId))))
        OR (ds.ScopeType='BRANCH' AND (c.OwnerBranchId=rds.ScopeReferenceId OR EXISTS
          (SELECT 1 FROM dbo.CustomerBranchShares bs WHERE bs.CustomerId=c.Id AND bs.BranchId=rds.ScopeReferenceId AND bs.IsActive=1 AND bs.ValidFrom<=@today AND (bs.ValidTo IS NULL OR bs.ValidTo>=@today))))
        OR (ds.ScopeType='COMPANY' AND EXISTS (SELECT 1 FROM dbo.Branches b JOIN dbo.Companies co ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0
             WHERE b.Id=c.OwnerBranchId AND b.CompanyId=rds.ScopeReferenceId AND b.IsActive=1 AND b.IsDeleted=0))
      ))
 )
 AND NOT EXISTS (SELECT 1 FROM dbo.UserDataScopes [deny] JOIN dbo.DataScopes ds ON ds.Id=[deny].DataScopeId AND ds.IsActive=1
   WHERE [deny].UserId=u.Id AND [deny].Effect='DENY' AND [deny].FunctionCode='Customers' AND [deny].ScopeReferenceId IS NOT NULL
    AND (
      (ds.ScopeType='ASSIGNED' AND [deny].ScopeReferenceId /* deny.ScopeReferenceId */=e.Id AND EXISTS
        (SELECT 1 FROM dbo.CustomerEmployeeAssignments ca WHERE ca.CustomerId=c.Id AND ca.EmployeeId=e.Id AND ca.IsActive=1 AND ca.ValidFrom<=@today AND (ca.ValidTo IS NULL OR ca.ValidTo>=@today)))
      OR (ds.ScopeType='TEAM' AND EXISTS
        (SELECT 1 FROM dbo.CustomerServiceTeams st JOIN dbo.EmployeeAssignments ea ON ea.TeamId=st.TeamId AND ea.EmployeeId=e.Id
         WHERE st.CustomerId=c.Id AND st.TeamId=[deny].ScopeReferenceId /* deny.ScopeReferenceId */ AND st.IsActive=1 AND st.ValidFrom<=@today AND (st.ValidTo IS NULL OR st.ValidTo>=@today)
           AND ea.IsCurrent=1 AND ea.IsPrimary=1 AND ea.ValidFrom<=@today AND (ea.ValidTo IS NULL OR ea.ValidTo>=@today)
           AND EXISTS (SELECT 1 FROM dbo.Branches eb JOIN dbo.Companies ec ON ec.Id=eb.CompanyId AND ec.IsActive=1 AND ec.IsDeleted=0 WHERE eb.Id=ea.BranchId AND eb.IsActive=1 AND eb.IsDeleted=0)
           AND (ea.DepartmentId IS NULL OR EXISTS (SELECT 1 FROM dbo.Departments ed WHERE ed.Id=ea.DepartmentId AND ed.BranchId=ea.BranchId AND ed.IsActive=1 AND ed.IsDeleted=0))
           AND EXISTS (SELECT 1 FROM dbo.Teams et WHERE et.Id=ea.TeamId AND et.BranchId=ea.BranchId AND et.IsActive=1 AND et.IsDeleted=0
             AND ((et.DepartmentId IS NULL AND ea.DepartmentId IS NULL) OR et.DepartmentId=ea.DepartmentId))))
      OR (ds.ScopeType='BRANCH' AND (c.OwnerBranchId=[deny].ScopeReferenceId /* deny.ScopeReferenceId */ OR EXISTS
        (SELECT 1 FROM dbo.CustomerBranchShares bs WHERE bs.CustomerId=c.Id AND bs.BranchId=[deny].ScopeReferenceId /* deny.ScopeReferenceId */ AND bs.IsActive=1 AND bs.ValidFrom<=@today AND (bs.ValidTo IS NULL OR bs.ValidTo>=@today))))
      OR (ds.ScopeType='COMPANY' AND EXISTS (SELECT 1 FROM dbo.Branches b JOIN dbo.Companies co ON co.Id=b.CompanyId AND co.IsActive=1 AND co.IsDeleted=0
           WHERE b.Id=c.OwnerBranchId AND b.CompanyId=[deny].ScopeReferenceId /* deny.ScopeReferenceId */ AND b.IsActive=1 AND b.IsDeleted=0))
    ))
) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
""";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@userId", SqlDbType.UniqueIdentifier).Value = actorUserId;
        command.Parameters.Add("@customerId", SqlDbType.UniqueIdentifier).Value = customerId;
        command.Parameters.Add("@actionKey", SqlDbType.VarChar, 100).Value = actionKey;
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }
}