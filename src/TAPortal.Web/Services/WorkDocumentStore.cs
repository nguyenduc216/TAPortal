using System.Data;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// Công việc > Tài liệu (operator 2026-11-16, migration 058): files attached to a task (images, Word, PDF, Excel) with a note
// on their content. Who may see the task may add documents and read them; the uploader or a task editor may change the note
// or remove one (WorkController checks this through IWorkTaskStore.GetAsync). Each change is logged on the task's history.
public sealed record WorkDocument(Guid Id, string FileName, string ContentType, int SizeBytes, string? Note, DateTime CreatedAt, Guid? CreatedBy, string? CreatedByName)
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.Ordinal);
    public string Kind => WorkDocumentPolicy.KindOf(FileName);
}

public sealed record WorkDocumentContent(string FileName, string ContentType, byte[] Data);

public static class WorkDocumentPolicy
{
    public const int MaxBytes = ContractPolicy.MaxFileBytes;   // CK_WorkTaskDocuments_Size (10 MB)
    public const int NoteMax = 1000, MaxPerTask = 50;
    public const string AllowedText = "ảnh (JPG, PNG, GIF, WEBP), Word (DOC, DOCX), PDF, Excel (XLS, XLSX), tối đa 10 MB";

    // Beyond the contract types (PDF, PNG, JPG, DOCX, XLSX, checked by ContractPolicy): older Office files and more images.
    private static readonly IReadOnlyDictionary<string, string> Extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".doc"] = "application/msword",
        [".xls"] = "application/vnd.ms-excel",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp"
    };

    public static readonly string Accept = ".jpg,.jpeg,.png,.gif,.webp,.pdf,.doc,.docx,.xls,.xlsx";

    public static string KindOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "PDF",
        ".doc" or ".docx" => "Word",
        ".xls" or ".xlsx" => "Excel",
        _ => "Ảnh"
    };

    public static string? Note(string? note)
    {
        var text = (note ?? "").Replace("\r\n", "\n").Trim();
        return text.Length == 0 ? null : text.Length > NoteMax ? text[..NoteMax] : text;
    }

    // Extension, size and content signature; returns the stored content type or a message in Vietnamese.
    public static (string? ContentType, string? Error) CheckFile(string? fileName, byte[]? data)
    {
        var ext = Path.GetExtension(fileName ?? "");
        if (!Extra.TryGetValue(ext, out var type))
        {
            var (contractType, error) = ContractPolicy.CheckFile(fileName, data);
            return contractType is not null ? (contractType, null)
                : (null, error is not null && error.StartsWith("Chỉ nhận file", StringComparison.Ordinal) ? $"Chỉ nhận {AllowedText}." : error);
        }
        if (data is null || data.Length == 0) return (null, "Chọn file cần tải lên.");
        if (data.Length > MaxBytes) return (null, "File vượt quá 10 MB.");
        var ok = ext.ToLowerInvariant() switch
        {
            ".doc" or ".xls" => StartsWith(data, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            ".gif" => StartsWith(data, "GIF87a"u8) || StartsWith(data, "GIF89a"u8),
            ".webp" => data.Length >= 12 && StartsWith(data, "RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        return ok ? (type, null) : (null, "Nội dung file không khớp với định dạng " + ext.TrimStart('.').ToUpperInvariant() + ".");
    }

    private static bool StartsWith(byte[] data, ReadOnlySpan<byte> magic) => data.Length >= magic.Length && data.AsSpan(0, magic.Length).SequenceEqual(magic);
}

public static class WorkDocumentSql
{
    public const string List = """
SELECT d.Id,d.FileName,d.ContentType,d.SizeBytes,d.Note,d.CreatedAt,d.CreatedBy,u.DisplayName
FROM dbo.WorkTaskDocuments d LEFT JOIN dbo.Users u ON u.Id=d.CreatedBy
WHERE d.TaskId=@task AND d.IsDeleted=0 ORDER BY d.CreatedAt DESC;
""";
    public const string Count = "SELECT COUNT(1) FROM dbo.WorkTaskDocuments WHERE TaskId=@task AND IsDeleted=0;";
    public const string Insert = """
INSERT dbo.WorkTaskDocuments(Id,TaskId,FileName,ContentType,SizeBytes,Data,Note,CreatedBy) VALUES(@doc,@task,@name,@type,@size,@data,@note,@by);
INSERT dbo.WorkTaskActivities(TaskId,Kind,Body,CreatedBy) VALUES(@task,'EDIT',N'Tải lên tài liệu: '+@name,@by);
UPDATE dbo.WorkTasks SET LastActivityAt=SYSUTCDATETIME() WHERE Id=@task;
""";
    public const string SetNote = """
UPDATE dbo.WorkTaskDocuments SET Note=@note,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@doc AND TaskId=@task AND IsDeleted=0;
IF @@ROWCOUNT=1 INSERT dbo.WorkTaskActivities(TaskId,Kind,Body,CreatedBy)
 SELECT @task,'EDIT',N'Sửa ghi chú tài liệu: '+FileName,@by FROM dbo.WorkTaskDocuments WHERE Id=@doc;
""";
    public const string Delete = """
UPDATE dbo.WorkTaskDocuments SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@doc AND TaskId=@task AND IsDeleted=0;
IF @@ROWCOUNT=1 INSERT dbo.WorkTaskActivities(TaskId,Kind,Body,CreatedBy)
 SELECT @task,'EDIT',N'Xóa tài liệu: '+FileName,@by FROM dbo.WorkTaskDocuments WHERE Id=@doc;
""";
    public const string Content = "SELECT FileName,ContentType,Data FROM dbo.WorkTaskDocuments WHERE Id=@doc AND TaskId=@task AND IsDeleted=0;";
}

public interface IWorkDocumentStore
{
    // null when the documents table is not available (migration 058 not run).
    Task<IReadOnlyList<WorkDocument>?> ListAsync(Guid taskId);
    Task<string?> AddAsync(Guid actor, Guid taskId, string fileName, string contentType, byte[] data, string? note);
    Task<bool> SetNoteAsync(Guid actor, Guid taskId, Guid docId, string? note);
    Task<bool> DeleteAsync(Guid actor, Guid taskId, Guid docId);
    Task<WorkDocumentContent?> ContentAsync(Guid taskId, Guid docId);
}

public sealed class SqlWorkDocumentStore(IConfiguration configuration, ILogger<SqlWorkDocumentStore> logger) : IWorkDocumentStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<IReadOnlyList<WorkDocument>?> ListAsync(Guid taskId)
    {
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync();
            await using var cmd = Command(cn, null, WorkDocumentSql.List, ("@task", taskId));
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<WorkDocument>();
            while (await r.ReadAsync())
                list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetDateTime(5),
                    r.IsDBNull(6) ? null : r.GetGuid(6), r.IsDBNull(7) ? null : r.GetString(7)));
            return list;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Work task documents unavailable.");
            return null;
        }
    }

    // Returns null when saved, otherwise the reason in Vietnamese.
    public async Task<string?> AddAsync(Guid actor, Guid taskId, string fileName, string contentType, byte[] data, string? note)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        await using (var count = Command(cn, tx, WorkDocumentSql.Count, ("@task", taskId)))
            if (Convert.ToInt32(await count.ExecuteScalarAsync()) >= WorkDocumentPolicy.MaxPerTask)
                return $"Mỗi công việc tối đa {WorkDocumentPolicy.MaxPerTask} tài liệu.";
        await using (var cmd = Command(cn, tx, WorkDocumentSql.Insert, ("@doc", Guid.NewGuid()), ("@task", taskId), ("@name", fileName), ("@type", contentType),
            ("@size", data.Length), ("@data", data), ("@note", (object?)note ?? DBNull.Value), ("@by", actor)))
            await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return null;
    }

    public Task<bool> SetNoteAsync(Guid actor, Guid taskId, Guid docId, string? note) =>
        Change(WorkDocumentSql.SetNote, ("@task", taskId), ("@doc", docId), ("@note", (object?)note ?? DBNull.Value), ("@by", actor));

    public Task<bool> DeleteAsync(Guid actor, Guid taskId, Guid docId) =>
        Change(WorkDocumentSql.Delete, ("@task", taskId), ("@doc", docId), ("@by", actor));

    public async Task<WorkDocumentContent?> ContentAsync(Guid taskId, Guid docId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Command(cn, null, WorkDocumentSql.Content, ("@task", taskId), ("@doc", docId));
        await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        if (!await r.ReadAsync()) return null;
        var name = r.GetString(0);
        var type = r.GetString(1);
        var data = (byte[])r.GetValue(2);
        return new(name, type, data);
    }

    private async Task<bool> Change(string sql, params (string Name, object Value)[] args)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        int rows;
        await using (var cmd = Command(cn, tx, sql, args)) rows = await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return rows > 0;
    }

    private static SqlCommand Command(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args)
        {
            if (value is string s) cmd.Parameters.Add(name, SqlDbType.NVarChar, Math.Max(s.Length, 1)).Value = s;
            else if (value is byte[] b) cmd.Parameters.Add(name, SqlDbType.VarBinary, -1).Value = b;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }
}
