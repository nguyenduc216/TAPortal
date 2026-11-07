using Microsoft.Data.SqlClient;

namespace TAPortal.Shared;

// Re-checks inside the caller's transaction (never from the cookie or request data) whether the acting Admin user may use a
// permission: the user must be login-eligible (IsDeleted=0, IsActive=1, Status='ACTIVE') and either hold the active
// SYS_ADMIN role, or get the permission from an active role or a direct ALLOW without a direct DENY.
// Same effective-permission rule as the Admin login (PortalDb.LoadGrantsAsync).
public static class ActorPermissionSql
{
    public const string IsSystemAdmin = """
SELECT CASE WHEN EXISTS(
 SELECT 1 FROM dbo.Users u JOIN dbo.UserRoles ur ON ur.UserId=u.Id JOIN dbo.Roles r ON r.Id=ur.RoleId
 WHERE u.Id=@actor AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE' AND r.Code='SYS_ADMIN' AND r.IsActive=1 AND r.IsDeleted=0
) THEN 1 ELSE 0 END;
""";

    public const string HasPermission = """
SELECT CASE WHEN EXISTS(
 SELECT 1 FROM dbo.Users u
 WHERE u.Id=@actor AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE' AND (
  EXISTS(SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId
         WHERE ur.UserId=u.Id AND r.Code='SYS_ADMIN' AND r.IsActive=1 AND r.IsDeleted=0)
  OR ((EXISTS(SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.IsActive=1 AND r.IsDeleted=0
              JOIN dbo.RolePermissions rp ON rp.RoleId=r.Id JOIN dbo.Permissions p ON p.Id=rp.PermissionId
              WHERE ur.UserId=u.Id AND p.Code=@permission AND p.IsActive=1 AND p.IsDeleted=0)
       OR EXISTS(SELECT 1 FROM dbo.UserPermissions up JOIN dbo.Permissions p ON p.Id=up.PermissionId
              WHERE up.UserId=u.Id AND up.Effect='ALLOW' AND p.Code=@permission AND p.IsActive=1 AND p.IsDeleted=0))
      AND NOT EXISTS(SELECT 1 FROM dbo.UserPermissions up JOIN dbo.Permissions p ON p.Id=up.PermissionId
              WHERE up.UserId=u.Id AND up.Effect='DENY' AND p.Code=@permission AND p.IsDeleted=0)))
) THEN 1 ELSE 0 END;
""";

    public static async Task<bool> IsSystemAdminAsync(SqlConnection cn, SqlTransaction? tx, Guid actor)
    {
        await using var cmd = new SqlCommand(IsSystemAdmin, cn, tx);
        cmd.Parameters.AddWithValue("@actor", actor);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }

    /// <summary>Operator 2026-11-07: only a SYS_ADMIN creates, edits or assigns roles; a grant of this code never counts.</summary>
    public const string SystemAdminOnlyPermission = "AUTH.ROLES.MANAGE";

    public static async Task<bool> HasPermissionAsync(SqlConnection cn, SqlTransaction? tx, Guid actor, string permission)
    {
        if (string.Equals(permission, SystemAdminOnlyPermission, StringComparison.OrdinalIgnoreCase)) return await IsSystemAdminAsync(cn, tx, actor);
        await using var cmd = new SqlCommand(HasPermission, cn, tx);
        cmd.Parameters.AddWithValue("@actor", actor);
        cmd.Parameters.Add("@permission", System.Data.SqlDbType.VarChar, 200).Value = permission;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }
}
