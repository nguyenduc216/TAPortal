using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// CHANGE_ID: TAPORTAL-20261018-USERS-001
public sealed partial class PortalDb
{
    public async Task<bool> CreateManagedUserAsync(Guid actorId, UserCreateVm input, string passwordHash)
    {
        if (!await HasFunctionActionAsync(actorId, "Users.Create")) return false;
        if (string.IsNullOrWhiteSpace(input.Username) || input.Username.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 200 ||
            !Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true) ||
            !string.IsNullOrEmpty(input.Email) && input.Email.Trim().Length > 256 ||
            !string.IsNullOrEmpty(input.Phone) && input.Phone.Trim().Length > 50 ||
            string.IsNullOrWhiteSpace(passwordHash) || input.RoleId.HasValue) return false;

        await using var cn = Open(); await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var id = Guid.NewGuid();
            await using var cmd = new SqlCommand("""
INSERT dbo.Users(Id,Username,NormalizedUsername,Email,NormalizedEmail,Phone,DisplayName,PasswordHash,Status,IsActive,CreatedBy)
VALUES(@id,@username,@normalizedUsername,@email,@normalizedEmail,@phone,@name,@hash,'ACTIVE',1,@actor)
""", cn, tx);
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            cmd.Parameters.Add("@username", SqlDbType.NVarChar, 100).Value = input.Username.Trim();
            cmd.Parameters.Add("@normalizedUsername", SqlDbType.NVarChar, 100).Value = input.Username.Trim().ToUpperInvariant();
            cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = string.IsNullOrWhiteSpace(input.Email) ? DBNull.Value : input.Email.Trim();
            cmd.Parameters.Add("@normalizedEmail", SqlDbType.NVarChar, 256).Value = string.IsNullOrWhiteSpace(input.Email) ? DBNull.Value : input.Email.Trim().ToUpperInvariant();
            cmd.Parameters.Add("@phone", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(input.Phone) ? DBNull.Value : input.Phone.Trim();
            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = input.DisplayName.Trim();
            cmd.Parameters.Add("@hash", SqlDbType.NVarChar, 1000).Value = passwordHash;
            cmd.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actorId;
            if (await cmd.ExecuteNonQueryAsync() != 1) return false;
            await using var audit = new SqlCommand("INSERT dbo.AuditLogs(UserId,ModuleCode,FunctionCode,ActionCode,EntityType,EntityId,NewValues,CreatedAt) VALUES(@actor,'AUTH','Users','CREATE','User',@id,@new,SYSUTCDATETIME())", cn, tx);
            audit.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actorId;
            audit.Parameters.Add("@id", SqlDbType.NVarChar, 200).Value = id.ToString();
            audit.Parameters.Add("@new", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(new { input.Username, input.DisplayName, input.Email, input.Phone });
            if (await audit.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("User creation audit failed.");
            await tx.CommitAsync();
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await tx.RollbackAsync();
            return false;
        }
    }

    public async Task<IReadOnlyList<Guid>> GetUserRoleIdsAsync(Guid userId)
    {
        var ids = new List<Guid>();
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT RoleId FROM dbo.UserRoles WHERE UserId=@user", cn);
        cmd.Parameters.Add("@user", SqlDbType.UniqueIdentifier).Value = userId;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    public async Task<bool> UpdateManagedUserAsync(Guid actorId, UserProfileUpdate input)
    {
        if (!Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true)
            || string.IsNullOrWhiteSpace(input.DisplayName)) return false;
        return await MutateUserAsync(actorId, input.Id, "UPDATE", async (cn, tx) =>
        {
            await using var cmd = new SqlCommand("UPDATE dbo.Users SET DisplayName=@name,Email=@email,NormalizedEmail=@normalized,Phone=@phone,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE Id=@id AND IsDeleted=0", cn, tx);
            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = input.DisplayName.Trim();
            var email = input.Email?.Trim();
            cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = string.IsNullOrEmpty(email) ? DBNull.Value : email;
            cmd.Parameters.Add("@normalized", SqlDbType.NVarChar, 256).Value = string.IsNullOrEmpty(email) ? DBNull.Value : email.ToUpperInvariant();
            cmd.Parameters.Add("@phone", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(input.Phone) ? DBNull.Value : input.Phone.Trim();
            cmd.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actorId;
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = input.Id;
            return await cmd.ExecuteNonQueryAsync() == 1;
        });
    }

    public Task<bool> SetManagedUserActiveAsync(Guid actorId, Guid userId, bool active) =>
        MutateUserAsync(actorId, userId, active ? "ENABLE" : "DISABLE", async (cn, tx) =>
        {
            if (!active && actorId == userId) return false;
            await using var cmd = new SqlCommand("UPDATE dbo.Users SET IsActive=@active,Status=@status,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE Id=@id AND IsDeleted=0 AND IsActive<>@active", cn, tx);
            cmd.Parameters.Add("@active", SqlDbType.Bit).Value = active;
            cmd.Parameters.Add("@status", SqlDbType.VarChar, 30).Value = active ? "ACTIVE" : "INACTIVE";
            cmd.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actorId;
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = userId;
            return await cmd.ExecuteNonQueryAsync() == 1;
        });

    public Task<bool> SetManagedUserRoleAsync(Guid actorId, Guid userId, Guid roleId, bool assign) =>
        MutateUserAsync(actorId, userId, assign ? "ASSIGN_ROLE" : "REVOKE_ROLE", async (cn, tx) =>
        {
            await using var role = new SqlCommand("SELECT Code FROM dbo.Roles WHERE Id=@role AND IsDeleted=0 AND IsActive=1", cn, tx);
            role.Parameters.Add("@role", SqlDbType.UniqueIdentifier).Value = roleId;
            var code = await role.ExecuteScalarAsync() as string;
            if (code is null) return false;
            if (code.Equals("SYS_ADMIN", StringComparison.OrdinalIgnoreCase) && !await IsActiveSysAdminAsync(cn, tx, actorId)) return false;
            if (!assign && code.Equals("SYS_ADMIN", StringComparison.OrdinalIgnoreCase))
            {
                if (actorId == userId) return false;
                await using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.Code='SYS_ADMIN' AND r.IsSystem=1 AND r.IsActive=1 AND r.IsDeleted=0 JOIN dbo.Users u ON u.Id=ur.UserId AND u.IsActive=1 AND u.Status='ACTIVE' AND u.IsDeleted=0", cn, tx);
                if (Convert.ToInt32(await count.ExecuteScalarAsync()) <= 1) return false;
            }
            await using var cmd = new SqlCommand(assign
                ? "INSERT dbo.UserRoles(UserId,RoleId,CreatedBy) SELECT @user,@role,@actor WHERE NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@user AND RoleId=@role)"
                : "DELETE dbo.UserRoles WHERE UserId=@user AND RoleId=@role", cn, tx);
            cmd.Parameters.Add("@user", SqlDbType.UniqueIdentifier).Value = userId;
            cmd.Parameters.Add("@role", SqlDbType.UniqueIdentifier).Value = roleId;
            cmd.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actorId;
            return await cmd.ExecuteNonQueryAsync() == 1;
        });

    private async Task<bool> MutateUserAsync(Guid actorId, Guid userId, string action, Func<SqlConnection, SqlTransaction, Task<bool>> write)
    {
        // The effective-permission projection honors explicit DENY even for SYS_ADMIN.
        if (!await HasPermissionAsync(actorId, "AUTH.USERS.UPDATE")) return false;
        await using var cn = Open(); await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await using var target = new SqlCommand("SELECT DisplayName,Email,Phone,IsActive,Status FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id AND IsDeleted=0", cn, tx);
            target.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = userId;
            await using var rd = await target.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return false;
            var before = new { DisplayName = rd.GetString(0), Email = rd.IsDBNull(1) ? null : rd.GetString(1), Phone = rd.IsDBNull(2) ? null : rd.GetString(2), IsActive = rd.GetBoolean(3), Status = rd.GetString(4) };
            await rd.CloseAsync();
            if (action == "DISABLE" && await IsActiveSysAdminAsync(cn, tx, userId))
            {
                await using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.Code='SYS_ADMIN' AND r.IsSystem=1 AND r.IsActive=1 AND r.IsDeleted=0 JOIN dbo.Users u ON u.Id=ur.UserId AND u.IsActive=1 AND u.Status='ACTIVE' AND u.IsDeleted=0", cn, tx);
                if (Convert.ToInt32(await count.ExecuteScalarAsync()) <= 1) return false;
            }
            if (!await write(cn, tx)) return false;
            await using var audit = new SqlCommand("INSERT dbo.AuditLogs(UserId,ModuleCode,FunctionCode,ActionCode,EntityType,EntityId,OldValues,NewValues,CreatedAt) VALUES(@actor,'AUTH','Users',@action,'User',@id,@old,@new,SYSUTCDATETIME())", cn, tx);
            audit.Parameters.Add("@actor", SqlDbType.UniqueIdentifier).Value = actorId;
            audit.Parameters.Add("@action", SqlDbType.VarChar, 30).Value = action;
            audit.Parameters.Add("@id", SqlDbType.NVarChar, 200).Value = userId.ToString();
            audit.Parameters.Add("@old", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(before);
            audit.Parameters.Add("@new", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(new { Action = action, Target = userId });
            if (await audit.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("User mutation audit failed.");
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static async Task<bool> IsActiveSysAdminAsync(SqlConnection cn, SqlTransaction tx, Guid userId)
    {
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.Code='SYS_ADMIN' AND r.IsSystem=1 AND r.IsActive=1 AND r.IsDeleted=0 JOIN dbo.Users u ON u.Id=ur.UserId AND u.IsActive=1 AND u.Status='ACTIVE' AND u.IsDeleted=0 WHERE ur.UserId=@user", cn, tx);
        cmd.Parameters.Add("@user", SqlDbType.UniqueIdentifier).Value = userId;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }
}

public sealed class UserProfileUpdate
{
    public Guid Id { get; set; }
    [Required, StringLength(200)] public string DisplayName { get; set; } = string.Empty;
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
}