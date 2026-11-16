using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Security;

namespace TAPortal.Web.Services;

public sealed record WorkCatalogItem(Guid Id, string Name, string? Description, int SortOrder, bool IsActive, int TaskCount, string? ChecklistText = null)
{
    public int ChecklistCount => WorkTaskPolicy.ChecklistLines(ChecklistText).Count;
}

public sealed class WorkCatalogInput
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ChecklistText { get; set; }   // checklist mẫu: one item per line (operator 2026-11-16)
}

public enum WorkCatalogOutcome { Saved, Invalid, Duplicate, NotFound, Forbidden, NoChange }

public sealed record WorkCatalogResult(WorkCatalogOutcome Outcome, string? Message = null);

// Danh mục công việc (operator 2026-11-14): a task (and a recurring task) is named from this list so the same work always
// carries the same name; details go to the description. While the list has no active entry, names stay free (start-up).
public static class WorkCatalogPolicy
{
    public const int NameMax = 200, DescriptionMax = 500, ChecklistTextMax = 4000;

    // Checklist mẫu (operator 2026-11-16): a task created with this name starts with these items. Stored one per line,
    // cleaned like a task's checklist (bullets trimmed, blanks dropped, same item once), NULL when empty.
    public static string? NormalizeChecklist(string? text) => Join(WorkTaskPolicy.ChecklistLines(text));

    // Items of a running task added to the template: only the ones it does not have yet (case-insensitive), in order.
    public static (string? Text, int Added) Append(string? current, IEnumerable<string> items)
    {
        var lines = WorkTaskPolicy.ChecklistLines(current).ToList();
        var added = 0;
        foreach (var item in items.Select(i => Clean(i)).Where(i => i.Length > 0))
        {
            if (lines.Count >= WorkTaskPolicy.ChecklistItemsMax) break;
            var line = item.Length > WorkTaskPolicy.ChecklistMax ? item[..WorkTaskPolicy.ChecklistMax] : item;
            if (lines.Any(l => string.Equals(l, line, StringComparison.OrdinalIgnoreCase))) continue;
            lines.Add(line);
            added++;
        }
        return (Join(lines), added);
    }

    private static string? Join(IReadOnlyList<string> lines)
    {
        var distinct = lines.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0) return null;
        var text = string.Join('\n', distinct);
        return text.Length > ChecklistTextMax ? text[..ChecklistTextMax] : text;
    }

    public static string? Validate(WorkCatalogInput input)
    {
        var name = Clean(input.Name);
        if (name.Length == 0) return "Nhập tên đầu việc.";
        if (name.Length > NameMax) return $"Tên đầu việc tối đa {NameMax} ký tự.";
        if ((input.Description?.Trim().Length ?? 0) > DescriptionMax) return $"Mô tả tối đa {DescriptionMax} ký tự.";
        if ((input.ChecklistText?.Length ?? 0) > ChecklistTextMax) return $"Checklist mẫu tối đa {ChecklistTextMax} ký tự.";
        return null;
    }

    // One space between words, no leading/trailing spaces: "Kéo  hóa đơn " and "Kéo hóa đơn" are the same entry.
    public static string Clean(string? text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public const string NotInCatalog = "Tên công việc phải chọn trong danh mục công việc: gõ vài chữ rồi chọn đầu việc được gợi ý, chi tiết ghi ở phần Mô tả. "
        + "Cần đầu việc mới thì nhờ người quản lý danh mục thêm (Công việc > Danh mục công việc).";
}

public static class WorkCatalogSql
{
    // The catalogue name matching @title (canonical spelling), '' while the catalogue is empty or not installed (any name
    // allowed), NULL when the name is not in the catalogue.
    public const string Resolve = """
IF OBJECT_ID(N'dbo.WorkTaskCatalog', N'U') IS NULL SELECT CAST(N'' AS nvarchar(200));
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.WorkTaskCatalog WHERE IsDeleted=0 AND IsActive=1) SELECT CAST(N'' AS nvarchar(200));
ELSE SELECT (SELECT TOP 1 Name FROM dbo.WorkTaskCatalog WHERE IsDeleted=0 AND IsActive=1 AND Name=@title);
""";

    public const string Active = """
IF OBJECT_ID(N'dbo.WorkTaskCatalog', N'U') IS NOT NULL
 SELECT Name,Description,ChecklistText FROM dbo.WorkTaskCatalog WHERE IsDeleted=0 AND IsActive=1 ORDER BY SortOrder,Name;
ELSE SELECT TOP 0 CAST(NULL AS nvarchar(200)),CAST(NULL AS nvarchar(500)),CAST(NULL AS nvarchar(4000));
""";

    public const string List = """
SELECT c.Id,c.Name,c.Description,c.SortOrder,c.IsActive,
 (SELECT COUNT(1) FROM dbo.WorkTasks t WHERE t.IsDeleted=0 AND t.Title=c.Name),c.ChecklistText
FROM dbo.WorkTaskCatalog c WHERE c.IsDeleted=0 ORDER BY c.IsActive DESC,c.SortOrder,c.Name;
""";

    public const string Taken = "SELECT COUNT(1) FROM dbo.WorkTaskCatalog WITH (UPDLOCK, HOLDLOCK) WHERE IsDeleted=0 AND Name=@name AND (@id IS NULL OR Id<>@id);";
    public const string Insert = "INSERT dbo.WorkTaskCatalog(Id,Name,Description,SortOrder,IsActive,ChecklistText,CreatedBy) VALUES(@id,@name,@desc,@sort,@active,@check,@by);";
    public const string OldName = "SELECT Name FROM dbo.WorkTaskCatalog WITH (UPDLOCK) WHERE Id=@id AND IsDeleted=0;";
    public const string Update = "UPDATE dbo.WorkTaskCatalog SET Name=@name,Description=@desc,SortOrder=@sort,IsActive=@active,ChecklistText=@check,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
    public const string ChecklistOf = "SELECT Id,ChecklistText FROM dbo.WorkTaskCatalog WITH (UPDLOCK) WHERE IsDeleted=0 AND Name=@name;";
    public const string SetChecklist = "UPDATE dbo.WorkTaskCatalog SET ChecklistText=@check,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
    // Renaming an entry renames the tasks and recurring tasks that carry the old name, so they stay grouped.
    public const string Rename = """
UPDATE dbo.WorkTasks SET Title=@name,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE IsDeleted=0 AND Title=@old;
UPDATE dbo.WorkTaskTemplates SET Title=@name,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE IsDeleted=0 AND Title=@old;
""";
    public const string Delete = "UPDATE dbo.WorkTaskCatalog SET IsDeleted=1,IsActive=0,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
}

public interface IWorkCatalogStore
{
    Task<IReadOnlyList<WorkCatalogItem>> ListAsync();
    Task<WorkCatalogResult> SaveAsync(Guid actor, Guid? id, WorkCatalogInput input);
    Task<WorkCatalogResult> DeleteAsync(Guid actor, Guid id);
    // Adds the items it lacks to the checklist template of the entry named @name (from a task being worked on).
    Task<WorkCatalogResult> AppendChecklistAsync(Guid actor, string name, IReadOnlyList<string> items);
}

public sealed class SqlWorkCatalogStore(IConfiguration configuration, AdminAuditOptions audit) : IWorkCatalogStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<IReadOnlyList<WorkCatalogItem>> ListAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(WorkCatalogSql.List, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<WorkCatalogItem>();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3), r.GetBoolean(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    public async Task<WorkCatalogResult> SaveAsync(Guid actor, Guid? id, WorkCatalogInput input)
    {
        if (WorkCatalogPolicy.Validate(input) is string error) return new(WorkCatalogOutcome.Invalid, error);
        var name = WorkCatalogPolicy.Clean(input.Name);
        var desc = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        var check = WorkCatalogPolicy.NormalizeChecklist(input.ChecklistText);
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TaskCatalogManage)) return new(WorkCatalogOutcome.Forbidden);
        if (Convert.ToInt32(await Scalar(cn, tx, WorkCatalogSql.Taken, ("@name", name), ("@id", (object?)id ?? DBNull.Value))) > 0)
            return new(WorkCatalogOutcome.Duplicate, $"Đầu việc \"{name}\" đã có trong danh mục.");
        string? old = null;
        if (id is Guid existing)
        {
            old = await Scalar(cn, tx, WorkCatalogSql.OldName, ("@id", existing)) as string;
            if (old is null) return new(WorkCatalogOutcome.NotFound);
            await Exec(cn, tx, WorkCatalogSql.Update, ("@id", existing), ("@name", name), ("@desc", (object?)desc ?? DBNull.Value), ("@sort", input.SortOrder), ("@active", input.IsActive), ("@check", (object?)check ?? DBNull.Value), ("@by", actor));
            if (!string.Equals(old, name, StringComparison.Ordinal))
                await Exec(cn, tx, WorkCatalogSql.Rename, ("@name", name), ("@old", old), ("@by", actor));
        }
        else
        {
            id = Guid.NewGuid();
            await Exec(cn, tx, WorkCatalogSql.Insert, ("@id", id.Value), ("@name", name), ("@desc", (object?)desc ?? DBNull.Value), ("@sort", input.SortOrder), ("@active", input.IsActive), ("@check", (object?)check ?? DBNull.Value), ("@by", actor));
        }
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, old is null ? "WORK_CATALOG_CREATED" : "WORK_CATALOG_UPDATED", "dbo.WorkTaskCatalog", id.Value.ToString(),
            old is null ? "{}" : AuditSql.Json(("name", old)), AuditSql.Json(("name", name), ("active", input.IsActive), ("sortOrder", input.SortOrder), ("checklistItems", WorkTaskPolicy.ChecklistLines(check).Count)), null);
        await tx.CommitAsync();
        return new(WorkCatalogOutcome.Saved);
    }

    public async Task<WorkCatalogResult> DeleteAsync(Guid actor, Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TaskCatalogManage)) return new(WorkCatalogOutcome.Forbidden);
        if (await Exec(cn, tx, WorkCatalogSql.Delete, ("@id", id), ("@by", actor)) != 1) return new(WorkCatalogOutcome.NotFound);
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, "WORK_CATALOG_DELETED", "dbo.WorkTaskCatalog", id.ToString(), "{}", "{}", null);
        await tx.CommitAsync();
        return new(WorkCatalogOutcome.Saved);
    }

    public async Task<WorkCatalogResult> AppendChecklistAsync(Guid actor, string name, IReadOnlyList<string> items)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TaskCatalogManage)) return new(WorkCatalogOutcome.Forbidden);
        Guid id; string? current;
        await using (var cmd = Command(cn, tx, WorkCatalogSql.ChecklistOf, [("@name", WorkCatalogPolicy.Clean(name))]))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return new(WorkCatalogOutcome.NotFound, $"Đầu việc \"{name}\" không có trong danh mục công việc.");
            id = r.GetGuid(0); current = r.IsDBNull(1) ? null : r.GetString(1);
        }
        var (text, added) = WorkCatalogPolicy.Append(current, items);
        if (added == 0) return new(WorkCatalogOutcome.NoChange, "Checklist mẫu của đầu việc đã có đủ các mục này.");
        await Exec(cn, tx, WorkCatalogSql.SetChecklist, ("@id", id), ("@check", (object?)text ?? DBNull.Value), ("@by", actor));
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, "WORK_CATALOG_CHECKLIST_ADDED", "dbo.WorkTaskCatalog", id.ToString(), "{}",
            AuditSql.Json(("added", added)), null);
        await tx.CommitAsync();
        return new(WorkCatalogOutcome.Saved, $"Đã thêm {added} mục vào checklist mẫu của \"{name}\".");
    }

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Command(cn, tx, sql, args);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
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
            if (value is string s) cmd.Parameters.Add(name, SqlDbType.NVarChar, Math.Max(s.Length, 1)).Value = s;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }
}
