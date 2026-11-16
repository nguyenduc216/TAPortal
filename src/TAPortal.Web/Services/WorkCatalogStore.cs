using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using TAPortal.Shared;
using TAPortal.Web.Security;

namespace TAPortal.Web.Services;

public sealed record WorkCatalogItem(Guid Id, string Name, string? Description, int SortOrder, bool IsActive, int TaskCount, string? ChecklistText = null, string? ChecklistJson = null)
{
    public IReadOnlyList<WorkChecklistStep> Steps => WorkCatalogPolicy.Steps(ChecklistJson, ChecklistText);
    public int ChecklistCount => Steps.Count;

    // Checklist items the tasks of this name already use (most used first), for adding to the template in one click.
    public IReadOnlyList<WorkCatalogUsedItem> Used { get; init; } = [];
    public IEnumerable<WorkCatalogUsedItem> UsedNotInTemplate =>
        Used.Where(u => !Steps.Any(s => string.Equals(s.Title, WorkCatalogPolicy.Clean(u.Title), StringComparison.OrdinalIgnoreCase)));
}

public sealed record WorkCatalogUsedItem(string Title, int Tasks);

// One item of an entry's checklist template; Guide explains how to do it (procedure, formula, example) — operator 2026-11-16.
public sealed record WorkChecklistStep(string Title, string? Guide);

public sealed class WorkCatalogStepInput
{
    public string? Title { get; set; }
    public string? Guide { get; set; }
}

// Explanations by catalogue entry and item name, for showing next to a task's checklist items.
public sealed class WorkGuideIndex(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byEntry)
{
    public static readonly WorkGuideIndex Empty = new(new Dictionary<string, IReadOnlyDictionary<string, string>>());

    // A recurring task is named "tên việc — kỳ T10/2026": its entry is the part before " — ".
    public string? For(string taskTitle, string itemTitle)
    {
        var item = WorkCatalogPolicy.Clean(itemTitle);
        foreach (var name in Names(taskTitle))
            if (byEntry.TryGetValue(name, out var guides) && guides.TryGetValue(item, out var guide)) return guide;
        return null;
    }

    private static IEnumerable<string> Names(string taskTitle)
    {
        var clean = WorkCatalogPolicy.Clean(taskTitle);
        yield return clean;
        var cut = clean.IndexOf(" — ", StringComparison.Ordinal);
        if (cut > 0) yield return clean[..cut];
    }
}

public sealed class WorkCatalogInput
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ChecklistText { get; set; }   // checklist mẫu: one item per line (operator 2026-11-16) — the add form
    public List<WorkCatalogStepInput>? Items { get; set; }   // checklist mẫu item by item with explanations — the entry editor
}

public enum WorkCatalogOutcome { Saved, Invalid, Duplicate, NotFound, Forbidden, NoChange }

public sealed record WorkCatalogResult(WorkCatalogOutcome Outcome, string? Message = null);

// Danh mục công việc (operator 2026-11-14): a task (and a recurring task) is named from this list so the same work always
// carries the same name; details go to the description. While the list has no active entry, names stay free (start-up).
public static class WorkCatalogPolicy
{
    public const int NameMax = 200, DescriptionMax = 500, ChecklistTextMax = 4000, GuideMax = 4000;

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private sealed record StepJson(string? title, string? guide);

    // The template item by item: ChecklistJson (057) when present, else the item names of ChecklistText (056).
    public static IReadOnlyList<WorkChecklistStep> Steps(string? json, string? text)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<StepJson>>(json, Json);
                if (parsed is not null) return CleanSteps(parsed.Select(s => (s.title, s.guide)));
            }
            catch (JsonException) { }
        }
        return WorkTaskPolicy.ChecklistLines(text).Select(t => new WorkChecklistStep(t, null)).ToList();
    }

    // Names cleaned (one space, at most ChecklistMax), blanks and repeated names dropped, explanation kept as typed
    // (line breaks matter for a procedure) but trimmed and capped; at most ChecklistItemsMax items.
    public static IReadOnlyList<WorkChecklistStep> CleanSteps(IEnumerable<(string? Title, string? Guide)> items)
    {
        var steps = new List<WorkChecklistStep>();
        foreach (var (rawTitle, rawGuide) in items)
        {
            var title = Clean((rawTitle ?? "").TrimStart().TrimStart('-', '*', '•'));
            if (title.Length == 0 || steps.Count >= WorkTaskPolicy.ChecklistItemsMax) continue;
            if (title.Length > WorkTaskPolicy.ChecklistMax) title = title[..WorkTaskPolicy.ChecklistMax];
            if (steps.Any(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase))) continue;
            var guide = string.IsNullOrWhiteSpace(rawGuide) ? null : rawGuide.Replace("\r\n", "\n").Trim();
            if (guide is { Length: > GuideMax }) guide = guide[..GuideMax];
            steps.Add(new(title, guide));
        }
        return steps;
    }

    public static string? ToJson(IReadOnlyList<WorkChecklistStep> steps) =>
        steps.Count == 0 ? null : JsonSerializer.Serialize(steps.Select(s => new StepJson(s.Title, s.Guide)), Json);

    public static string? ToText(IReadOnlyList<WorkChecklistStep> steps) => Join(steps.Select(s => s.Title).ToList());

    // Checklist mẫu (operator 2026-11-16): a task created with this name starts with these items. Stored one per line,
    // cleaned like a task's checklist (bullets trimmed, blanks dropped, same item once), NULL when empty.
    public static string? NormalizeChecklist(string? text) => Join(WorkTaskPolicy.ChecklistLines(text));

    // Items of a running task added to the template: only the ones it does not have yet (case-insensitive), in order,
    // without explanation (written later on the catalogue page).
    public static (IReadOnlyList<WorkChecklistStep> Steps, int Added) Append(IReadOnlyList<WorkChecklistStep> current, IEnumerable<string> items)
    {
        var merged = CleanSteps(current.Select(s => ((string?)s.Title, s.Guide)).Concat(items.Select(i => ((string?)i, (string?)null))));
        return (merged, merged.Count - current.Count);
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
        if (input.Items?.Any(i => (i.Guide?.Trim().Length ?? 0) > GuideMax) == true) return $"Diễn giải mỗi mục tối đa {GuideMax} ký tự.";
        if (input.Items?.Any(i => string.IsNullOrWhiteSpace(i.Title) && !string.IsNullOrWhiteSpace(i.Guide)) == true) return "Mục checklist có diễn giải thì phải có tên mục.";
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
 (SELECT COUNT(1) FROM dbo.WorkTasks t WHERE t.IsDeleted=0 AND (t.Title=c.Name OR LEFT(t.Title,LEN(c.Name)+3)=c.Name+N' — ')),c.ChecklistText,c.ChecklistJson
FROM dbo.WorkTaskCatalog c WHERE c.IsDeleted=0 ORDER BY c.IsActive DESC,c.SortOrder,c.Name;
SELECT c.Id,i.Title,COUNT(DISTINCT t.Id)
FROM dbo.WorkTaskCatalog c
JOIN dbo.WorkTasks t ON t.IsDeleted=0 AND (t.Title=c.Name OR LEFT(t.Title,LEN(c.Name)+3)=c.Name+N' — ')
JOIN dbo.WorkTaskChecklistItems i ON i.TaskId=t.Id AND i.IsDeleted=0
WHERE c.IsDeleted=0
GROUP BY c.Id,i.Title ORDER BY c.Id,COUNT(DISTINCT t.Id) DESC,i.Title;
""";

    public const string Taken = "SELECT COUNT(1) FROM dbo.WorkTaskCatalog WITH (UPDLOCK, HOLDLOCK) WHERE IsDeleted=0 AND Name=@name AND (@id IS NULL OR Id<>@id);";
    public const string Insert = "INSERT dbo.WorkTaskCatalog(Id,Name,Description,SortOrder,IsActive,ChecklistText,ChecklistJson,CreatedBy) VALUES(@id,@name,@desc,@sort,@active,@check,@json,@by);";
    public const string OldName = "SELECT Name FROM dbo.WorkTaskCatalog WITH (UPDLOCK) WHERE Id=@id AND IsDeleted=0;";
    public const string Update = "UPDATE dbo.WorkTaskCatalog SET Name=@name,Description=@desc,SortOrder=@sort,IsActive=@active,ChecklistText=@check,ChecklistJson=@json,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
    public const string ChecklistOf = "SELECT Id,ChecklistText,ChecklistJson FROM dbo.WorkTaskCatalog WITH (UPDLOCK) WHERE IsDeleted=0 AND Name=@name;";
    public const string Guides = "SELECT Name,ChecklistText,ChecklistJson FROM dbo.WorkTaskCatalog WHERE IsDeleted=0 AND ChecklistJson IS NOT NULL;";
    public const string SetChecklist = "UPDATE dbo.WorkTaskCatalog SET ChecklistText=@check,ChecklistJson=@json,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
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
    // Explanations of the template items, for task cards and pages (cached; empty when unavailable).
    Task<WorkGuideIndex> GuidesAsync();
}

public sealed class SqlWorkCatalogStore(IConfiguration configuration, AdminAuditOptions audit, IMemoryCache cache, ILogger<SqlWorkCatalogStore> logger) : IWorkCatalogStore
{
    private const string GuideCacheKey = "work-catalog-guides";
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<WorkGuideIndex> GuidesAsync()
    {
        if (cache.TryGetValue(GuideCacheKey, out WorkGuideIndex? hit) && hit is not null) return hit;
        WorkGuideIndex index;
        try
        {
            var byEntry = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            await using var cn = Connection();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(WorkCatalogSql.Guides, cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var guides = WorkCatalogPolicy.Steps(r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(1) ? null : r.GetString(1))
                    .Where(s => s.Guide is not null).ToDictionary(s => s.Title, s => s.Guide!, StringComparer.OrdinalIgnoreCase);
                if (guides.Count > 0) byEntry[r.GetString(0)] = guides;
            }
            index = new(byEntry);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            // Explanations are help text: a task page still opens without them.
            logger.LogWarning(ex, "Work checklist guides unavailable.");
            index = WorkGuideIndex.Empty;
        }
        cache.Set(GuideCacheKey, index, TimeSpan.FromMinutes(5));
        return index;
    }

    public async Task<IReadOnlyList<WorkCatalogItem>> ListAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(WorkCatalogSql.List, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<WorkCatalogItem>();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3), r.GetBoolean(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7)));
        var used = new Dictionary<Guid, List<WorkCatalogUsedItem>>();
        if (await r.NextResultAsync())
            while (await r.ReadAsync())
            {
                if (!used.TryGetValue(r.GetGuid(0), out var items)) used[r.GetGuid(0)] = items = [];
                if (items.Count < WorkTaskPolicy.ChecklistItemsMax) items.Add(new(r.GetString(1), r.GetInt32(2)));
            }
        return list.Select(x => used.TryGetValue(x.Id, out var items) ? x with { Used = items } : x).ToList();
    }

    public async Task<WorkCatalogResult> SaveAsync(Guid actor, Guid? id, WorkCatalogInput input)
    {
        if (WorkCatalogPolicy.Validate(input) is string error) return new(WorkCatalogOutcome.Invalid, error);
        var name = WorkCatalogPolicy.Clean(input.Name);
        var desc = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        var steps = input.Items is not null
            ? WorkCatalogPolicy.CleanSteps(input.Items.Select(i => (i.Title, i.Guide)))
            : WorkCatalogPolicy.Steps(null, WorkCatalogPolicy.NormalizeChecklist(input.ChecklistText));
        var check = WorkCatalogPolicy.ToText(steps);
        var json = WorkCatalogPolicy.ToJson(steps);
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
            await Exec(cn, tx, WorkCatalogSql.Update, ("@id", existing), ("@name", name), ("@desc", (object?)desc ?? DBNull.Value), ("@sort", input.SortOrder), ("@active", input.IsActive), ("@check", (object?)check ?? DBNull.Value), ("@json", (object?)json ?? DBNull.Value), ("@by", actor));
            if (!string.Equals(old, name, StringComparison.Ordinal))
                await Exec(cn, tx, WorkCatalogSql.Rename, ("@name", name), ("@old", old), ("@by", actor));
        }
        else
        {
            id = Guid.NewGuid();
            await Exec(cn, tx, WorkCatalogSql.Insert, ("@id", id.Value), ("@name", name), ("@desc", (object?)desc ?? DBNull.Value), ("@sort", input.SortOrder), ("@active", input.IsActive), ("@check", (object?)check ?? DBNull.Value), ("@json", (object?)json ?? DBNull.Value), ("@by", actor));
        }
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, old is null ? "WORK_CATALOG_CREATED" : "WORK_CATALOG_UPDATED", "dbo.WorkTaskCatalog", id.Value.ToString(),
            old is null ? "{}" : AuditSql.Json(("name", old)), AuditSql.Json(("name", name), ("active", input.IsActive), ("sortOrder", input.SortOrder), ("checklistItems", steps.Count)), null);
        await tx.CommitAsync();
        cache.Remove(GuideCacheKey);
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
        cache.Remove(GuideCacheKey);
        return new(WorkCatalogOutcome.Saved);
    }

    public async Task<WorkCatalogResult> AppendChecklistAsync(Guid actor, string name, IReadOnlyList<string> items)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TaskCatalogManage)) return new(WorkCatalogOutcome.Forbidden);
        Guid id; IReadOnlyList<WorkChecklistStep> current;
        await using (var cmd = Command(cn, tx, WorkCatalogSql.ChecklistOf, [("@name", WorkCatalogPolicy.Clean(name))]))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return new(WorkCatalogOutcome.NotFound, $"Đầu việc \"{name}\" không có trong danh mục công việc.");
            id = r.GetGuid(0); current = WorkCatalogPolicy.Steps(r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(1) ? null : r.GetString(1));
        }
        var (steps, added) = WorkCatalogPolicy.Append(current, items);
        if (added == 0) return new(WorkCatalogOutcome.NoChange, "Checklist mẫu của đầu việc đã có đủ các mục này.");
        await Exec(cn, tx, WorkCatalogSql.SetChecklist, ("@id", id), ("@check", (object?)WorkCatalogPolicy.ToText(steps) ?? DBNull.Value),
            ("@json", (object?)WorkCatalogPolicy.ToJson(steps) ?? DBNull.Value), ("@by", actor));
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, "WORK_CATALOG_CHECKLIST_ADDED", "dbo.WorkTaskCatalog", id.ToString(), "{}",
            AuditSql.Json(("added", added)), null);
        await tx.CommitAsync();
        cache.Remove(GuideCacheKey);
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
