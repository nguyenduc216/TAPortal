using System.Data;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// One queue entry (dbo.LegalWatchItems of migration 048). LibraryId/LibraryTitle: the library document with the same number
// (a NEW entry already entered by hand) or the document a STATUS entry is about; LibraryStatus/LibraryExpired: its values now.
public sealed record LegalWatchItem(Guid Id, string Kind, string Source, string SourceUrl, string? FileUrl, string DocumentNo, string DocType, string? TypeName,
    string Summary, string? Agency, DateTime? IssuedDate, DateTime? EffectiveDate, DateTime? ExpiredDate, string? EffectStatus, string? Relations, string? Keywords,
    string Status, DateTime CreatedAt, DateTime? DecidedAt, string? DecidedByName, string? DecisionNote,
    Guid? LibraryId, string? LibraryTitle, string? LibraryStatus, DateTime? LibraryExpired)
{
    public bool IsPending => Status == "PENDING";
    public IReadOnlyList<LegalRelation> RelationList => LegalWatchPolicy.Split(Relations);

    public LegalWatchCandidate Candidate() =>
        new(Source, SourceUrl, FileUrl, DocumentNo, DocType, TypeName, Summary, Agency, IssuedDate, EffectiveDate, Relations, Keywords ?? "");

    // Library form values for a NEW entry (also used to pre-fill the form when the file cannot be read).
    public LegalDocumentInput Input() => new()
    {
        DocumentNo = DocumentNo, Title = Candidate().LibraryTitle(), DocType = DocType, IssuedDate = IssuedDate, EffectiveDate = EffectiveDate,
        Note = LegalWatchPolicy.Note(SourceUrl, RelationList)
    };
}

public sealed record LegalWatchRun(Guid Id, string Kind, string Period, string Status, DateTime StartedAt, DateTime? FinishedAt, int? Scanned, int? NewItems,
    int? StatusItems, string? Error, DateTime? NotifiedAt, string? RequestedByName);

// A library document as the status check needs it.
public sealed record LegalWatchDocument(Guid Id, string DocumentNo, string DocType, string Title, string? SourceUrl, string? EffectStatus, DateTime? ExpiredDate);

public sealed record LegalWatchRecipient(string Email, string Name);

public sealed record LegalWatchOverview(IReadOnlyList<LegalWatchItem> Items, IReadOnlyList<LegalWatchRun> Runs, int Pending);

public interface ILegalWatchStore
{
    // ---- collection (scheduler; no user) ----
    Task<bool> RunExistsAsync(string kind, string period);
    Task<Guid?> ClaimRunAsync(string kind, string period, Guid? requestedBy);
    Task FinishRunAsync(Guid runId, int scanned, int newItems, int statusItems, string? error);
    Task<bool> IsKnownAsync(string documentNo);
    Task<bool> AddNewAsync(Guid runId, LegalWatchCandidate candidate);
    Task<IReadOnlyList<LegalWatchDocument>> DocumentsAsync();
    Task MarkCheckedAsync(Guid documentId, string sourceUrl, string? baselineStatus);
    Task<bool> AddStatusAsync(Guid runId, LegalWatchDocument document, string sourceUrl, VbplStatus status);
    Task<(int New, int Status)> PendingSinceAsync(DateTime sinceUtc);
    Task<IReadOnlyList<LegalWatchRecipient>> RecipientsAsync();
    Task MarkNotifiedAsync(Guid runId);
    // ---- queue (pages) ----
    Task<LegalWatchOverview> OverviewAsync(bool pending);
    Task<LegalWatchItem?> GetAsync(Guid id);
    Task<int> PendingCountAsync();
    Task<LegalOutcome> DecideAsync(Guid actor, Guid id, string status, Guid? libraryId, string? note, string? ip);
    Task<LegalOutcome> ApplyStatusAsync(Guid actor, Guid id, string? ip);
}

// Writes made by a person need LEGAL.LIBRARY.MANAGE (checked in SQL) and are audited; collection writes come from the
// scheduler. Entries are never deleted: a decision only moves them out of PENDING.
public sealed class SqlLegalWatchStore(IConfiguration configuration, AdminAuditOptions audit) : ILegalWatchStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    private const string ItemColumns = """
i.Id,i.Kind,i.Source,i.SourceUrl,i.FileUrl,i.DocumentNo,i.DocType,i.TypeName,i.Summary,i.Agency,i.IssuedDate,i.EffectiveDate,i.ExpiredDate,i.EffectStatus,
i.Relations,i.Keywords,i.Status,i.CreatedAt,i.DecidedAt,COALESCE(u.DisplayName,u.Username),i.DecisionNote,d.Id,d.Title,d.EffectStatus,d.ExpiredDate
""";

    private const string ItemFrom = """
FROM dbo.LegalWatchItems i
LEFT JOIN dbo.Users u ON u.Id=i.DecidedBy
OUTER APPLY (SELECT TOP 1 x.Id,x.Title,x.EffectStatus,x.ExpiredDate FROM dbo.LegalDocuments x
             WHERE x.IsDeleted=0 AND ((i.Kind='STATUS' AND x.Id=i.LegalDocumentId) OR (i.Kind='NEW' AND (x.Id=i.LegalDocumentId OR x.DocumentNo=i.DocumentNo)))
             ORDER BY CASE WHEN x.Id=i.LegalDocumentId THEN 0 ELSE 1 END) d
""";

    public async Task<bool> RunExistsAsync(string kind, string period)
    {
        await using var cn = await Open();
        return Convert.ToInt32(await Scalar(cn, null, "SELECT COUNT(1) FROM dbo.LegalWatchRuns WHERE Kind=@kind AND Period=@period AND Status<>'FAILED';",
            ("@kind", kind), ("@period", period))) > 0;
    }

    // Inserts the run of a period; a failed run (or one left RUNNING for 3 hours by a recycled process) is taken over.
    // Null when another instance already has it.
    public async Task<Guid?> ClaimRunAsync(string kind, string period, Guid? requestedBy)
    {
        await using var cn = await Open();
        var id = Guid.NewGuid();
        try
        {
            await Exec(cn, null, "INSERT dbo.LegalWatchRuns(Id,Kind,Period,RequestedBy) VALUES(@id,@kind,@period,@by);",
                ("@id", id), ("@kind", kind), ("@period", period), ("@by", (object?)requestedBy ?? DBNull.Value));
            return id;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            var taken = await Scalar(cn, null, """
UPDATE dbo.LegalWatchRuns SET Status='RUNNING',StartedAt=SYSUTCDATETIME(),FinishedAt=NULL,Error=NULL
OUTPUT inserted.Id
WHERE Kind=@kind AND Period=@period AND (Status='FAILED' OR (Status='RUNNING' AND StartedAt<DATEADD(HOUR,-3,SYSUTCDATETIME())));
""", ("@kind", kind), ("@period", period));
            return taken is Guid g ? g : null;
        }
    }

    public async Task FinishRunAsync(Guid runId, int scanned, int newItems, int statusItems, string? error)
    {
        await using var cn = await Open();
        await Exec(cn, null, "UPDATE dbo.LegalWatchRuns SET Status=@status,FinishedAt=SYSUTCDATETIME(),Scanned=@scanned,NewItems=@new,StatusItems=@st,Error=@error WHERE Id=@id;",
            ("@status", error is null ? "DONE" : "FAILED"), ("@scanned", scanned), ("@new", newItems), ("@st", statusItems),
            ("@error", error is null ? DBNull.Value : LegalWatchPolicy.Clip(error, 1000)), ("@id", runId));
    }

    // Already in the queue or in the library: the document page is not opened again.
    public async Task<bool> IsKnownAsync(string documentNo)
    {
        await using var cn = await Open();
        return Convert.ToInt32(await Scalar(cn, null, """
SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.LegalWatchItems WHERE Kind='NEW' AND DocumentNo=@no)
              OR EXISTS(SELECT 1 FROM dbo.LegalDocuments WHERE IsDeleted=0 AND DocumentNo=@no) THEN 1 ELSE 0 END;
""", ("@no", documentNo))) == 1;
    }

    public async Task<bool> AddNewAsync(Guid runId, LegalWatchCandidate c)
    {
        await using var cn = await Open();
        try
        {
            return await Exec(cn, null, """
INSERT dbo.LegalWatchItems(Id,Kind,Source,SourceUrl,FileUrl,DocumentNo,DocType,TypeName,Summary,Agency,IssuedDate,EffectiveDate,Relations,Keywords,RunId)
SELECT NEWID(),'NEW',@source,@url,@file,@no,@type,@typeName,@summary,@agency,@issued,@effective,@relations,@keywords,@run
WHERE NOT EXISTS(SELECT 1 FROM dbo.LegalWatchItems WHERE Kind='NEW' AND DocumentNo=@no)
  AND NOT EXISTS(SELECT 1 FROM dbo.LegalDocuments WHERE IsDeleted=0 AND DocumentNo=@no);
""", ("@source", c.Source), ("@url", LegalWatchPolicy.Clip(c.SourceUrl, 500)), ("@file", N(c.FileUrl is null ? null : LegalWatchPolicy.Clip(c.FileUrl, 1000))),
                ("@no", c.DocumentNo), ("@type", c.DocType), ("@typeName", N(c.TypeName)), ("@summary", c.Summary), ("@agency", N(c.Agency)),
                ("@issued", Date(c.IssuedDate)), ("@effective", Date(c.EffectiveDate)), ("@relations", N(c.Relations)),
                ("@keywords", N(c.Keywords.Length == 0 ? null : LegalWatchPolicy.Clip(c.Keywords, 200))), ("@run", runId)) == 1;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return false; }
    }

    public async Task<IReadOnlyList<LegalWatchDocument>> DocumentsAsync()
    {
        await using var cn = await Open();
        await using var cmd = Cmd(cn, null, "SELECT Id,DocumentNo,DocType,Title,SourceUrl,EffectStatus,ExpiredDate FROM dbo.LegalDocuments WHERE IsDeleted=0 ORDER BY DocumentNo;");
        var list = new List<LegalWatchDocument>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), S(r, 4), S(r, 5), D(r, 6)));
        return list;
    }

    // Records where the document was found and when; the status itself is written only the first time (baseline), later
    // changes go through the queue.
    public async Task MarkCheckedAsync(Guid documentId, string sourceUrl, string? baselineStatus)
    {
        await using var cn = await Open();
        await Exec(cn, null, """
UPDATE dbo.LegalDocuments SET SourceUrl=@url,StatusCheckedAt=SYSUTCDATETIME(),EffectStatus=CASE WHEN EffectStatus IS NULL THEN @status ELSE EffectStatus END
WHERE Id=@id AND IsDeleted=0;
""", ("@url", LegalWatchPolicy.Clip(sourceUrl, 500)), ("@status", N(baselineStatus is null ? null : LegalWatchPolicy.Clip(baselineStatus, 100))), ("@id", documentId));
    }

    public async Task<bool> AddStatusAsync(Guid runId, LegalWatchDocument doc, string sourceUrl, VbplStatus status)
    {
        await using var cn = await Open();
        var summary = $"{doc.Title}: vbpl.vn ghi \"{status.EffectStatus ?? "không rõ"}\"" + (status.ExpiredDate is DateTime x ? $", hết hiệu lực từ {x:dd/MM/yyyy}" : "");
        try
        {
            return await Exec(cn, null, """
INSERT dbo.LegalWatchItems(Id,Kind,Source,SourceUrl,DocumentNo,DocType,Summary,EffectiveDate,ExpiredDate,EffectStatus,LegalDocumentId,RunId)
SELECT NEWID(),'STATUS','VBPL',@url,@no,@type,@summary,@effective,@expired,@status,@doc,@run
WHERE NOT EXISTS(SELECT 1 FROM dbo.LegalWatchItems WHERE Kind='STATUS' AND LegalDocumentId=@doc AND Status='PENDING')
  AND NOT EXISTS(SELECT 1 FROM dbo.LegalWatchItems WHERE Kind='STATUS' AND LegalDocumentId=@doc
                 AND ISNULL(EffectStatus,N'')=ISNULL(@status,N'') AND ISNULL(ExpiredDate,'19000101')=ISNULL(@expired,'19000101'));
""", ("@url", LegalWatchPolicy.Clip(sourceUrl, 500)), ("@no", doc.DocumentNo), ("@type", doc.DocType), ("@summary", LegalWatchPolicy.Clip(summary, 1000)),
                ("@effective", Date(status.EffectiveFrom)), ("@expired", Date(status.ExpiredDate)),
                ("@status", N(status.EffectStatus is null ? null : LegalWatchPolicy.Clip(status.EffectStatus, 100))), ("@doc", doc.Id), ("@run", runId)) == 1;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return false; }
    }

    public async Task<(int New, int Status)> PendingSinceAsync(DateTime sinceUtc)
    {
        await using var cn = await Open();
        await using var cmd = Cmd(cn, null, """
SELECT SUM(CASE WHEN Kind='NEW' THEN 1 ELSE 0 END),SUM(CASE WHEN Kind='STATUS' THEN 1 ELSE 0 END)
FROM dbo.LegalWatchItems WHERE Status='PENDING' AND CreatedAt>=@since;
""", ("@since", sinceUtc));
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? (r.IsDBNull(0) ? 0 : r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1)) : (0, 0);
    }

    // Active staff with an email who may decide the queue (LEGAL.LIBRARY.MANAGE, SYS_ADMIN included).
    public async Task<IReadOnlyList<LegalWatchRecipient>> RecipientsAsync()
    {
        await using var cn = await Open();
        var users = new List<(Guid Id, string Email, string Name)>();
        await using (var cmd = Cmd(cn, null, "SELECT u.Id,u.Email,COALESCE(u.DisplayName,u.Username) FROM dbo.Users u WHERE u.IsDeleted=0 AND u.IsActive=1 AND u.Status='ACTIVE' AND u.Email LIKE '%_@_%';"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) users.Add((r.GetGuid(0), r.GetString(1).Trim(), r.GetString(2)));
        var list = new List<LegalWatchRecipient>();
        foreach (var u in users)
            if (await ActorPermissionSql.HasPermissionAsync(cn, null, u.Id, AdminPermissions.LegalLibraryManage)) list.Add(new(u.Email, u.Name));
        return list;
    }

    public async Task MarkNotifiedAsync(Guid runId)
    {
        await using var cn = await Open();
        await Exec(cn, null, "UPDATE dbo.LegalWatchRuns SET NotifiedAt=SYSUTCDATETIME() WHERE Id=@id;", ("@id", runId));
    }

    public async Task<LegalWatchOverview> OverviewAsync(bool pending)
    {
        await using var cn = await Open();
        var items = new List<LegalWatchItem>();
        await using (var cmd = Cmd(cn, null, $"SELECT TOP 300 {ItemColumns} {ItemFrom} WHERE {(pending ? "i.Status='PENDING'" : "i.Status<>'PENDING'")} ORDER BY i.CreatedAt DESC,i.DocumentNo;"))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) items.Add(Item(r));
        var runs = new List<LegalWatchRun>();
        await using (var cmd = Cmd(cn, null, """
SELECT TOP 12 w.Id,w.Kind,w.Period,w.Status,w.StartedAt,w.FinishedAt,w.Scanned,w.NewItems,w.StatusItems,w.Error,w.NotifiedAt,COALESCE(u.DisplayName,u.Username)
FROM dbo.LegalWatchRuns w LEFT JOIN dbo.Users u ON u.Id=w.RequestedBy ORDER BY w.StartedAt DESC;
"""))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                runs.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDateTime(4), D(r, 5), I(r, 6), I(r, 7), I(r, 8), S(r, 9), D(r, 10), S(r, 11)));
        var count = Convert.ToInt32(await Scalar(cn, null, "SELECT COUNT(1) FROM dbo.LegalWatchItems WHERE Status='PENDING';"));
        return new(items, runs, count);
    }

    public async Task<LegalWatchItem?> GetAsync(Guid id)
    {
        await using var cn = await Open();
        await using var cmd = Cmd(cn, null, $"SELECT {ItemColumns} {ItemFrom} WHERE i.Id=@id;", ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Item(r) : null;
    }

    public async Task<int> PendingCountAsync()
    {
        await using var cn = await Open();
        return Convert.ToInt32(await Scalar(cn, null, "SELECT COUNT(1) FROM dbo.LegalWatchItems WHERE Status='PENDING';"));
    }

    // IMPORTED (a NEW entry now in the library, libraryId = that document) or DISMISSED (with an optional note).
    public Task<LegalOutcome> DecideAsync(Guid actor, Guid id, string status, Guid? libraryId, string? note, string? ip) =>
        Write(actor, async (cn, tx) =>
        {
            if (status is not ("IMPORTED" or "DISMISSED") || (status == "IMPORTED" && libraryId is null)) return LegalOutcome.Invalid;
            var kind = (string?)await Scalar(cn, tx, "SELECT Kind FROM dbo.LegalWatchItems WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id AND Status='PENDING';", ("@id", id));
            if (kind is null) return LegalOutcome.NotFound;
            if (status == "IMPORTED" && kind != "NEW") return LegalOutcome.Invalid;
            if (libraryId is Guid lib && Convert.ToInt32(await Scalar(cn, tx, "SELECT COUNT(1) FROM dbo.LegalDocuments WHERE Id=@doc AND IsDeleted=0;", ("@doc", lib))) == 0)
                return LegalOutcome.NotFound;
            var text = string.IsNullOrWhiteSpace(note) ? null : LegalWatchPolicy.Clip(note.Trim(), 500);
            await Exec(cn, tx, """
UPDATE dbo.LegalWatchItems SET Status=@status,DecidedAt=SYSUTCDATETIME(),DecidedBy=@by,DecisionNote=@note,
       LegalDocumentId=CASE WHEN Kind='NEW' THEN @doc ELSE LegalDocumentId END
WHERE Id=@id AND Status='PENDING';
""", ("@status", status), ("@by", actor), ("@note", N(text)), ("@doc", (object?)libraryId ?? DBNull.Value), ("@id", id));
            await Audit(cn, tx, actor, status == "IMPORTED" ? "LEGAL_WATCH_IMPORTED" : "LEGAL_WATCH_DISMISSED", "dbo.LegalWatchItems", id,
                AuditSql.Json(("status", status), ("libraryId", libraryId), ("note", text is not null)), ip);
            return LegalOutcome.Saved;
        });

    // A STATUS entry accepted: the library document takes the status and, when vbpl.vn gives one, the expiry date.
    public Task<LegalOutcome> ApplyStatusAsync(Guid actor, Guid id, string? ip) =>
        Write(actor, async (cn, tx) =>
        {
            Guid doc; string? status; DateTime? expired; DateTime? oldExpired;
            await using (var cmd = Cmd(cn, tx, """
SELECT i.LegalDocumentId,i.EffectStatus,i.ExpiredDate,d.ExpiredDate,d.EffectiveDate FROM dbo.LegalWatchItems i WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.LegalDocuments d ON d.Id=i.LegalDocumentId AND d.IsDeleted=0
WHERE i.Id=@id AND i.Kind='STATUS' AND i.Status='PENDING';
""", ("@id", id)))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return LegalOutcome.NotFound;
                doc = r.GetGuid(0); status = S(r, 1); expired = D(r, 2); oldExpired = D(r, 3);
                if (expired is DateTime x && D(r, 4) is DateTime e && x <= e) expired = null;   // keeps CK on the form rule
            }
            await Exec(cn, tx, """
UPDATE dbo.LegalDocuments SET EffectStatus=@status,ExpiredDate=CASE WHEN @expired IS NULL THEN ExpiredDate ELSE @expired END,
       StatusCheckedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@doc AND IsDeleted=0;
""", ("@status", N(status)), ("@expired", Date(expired)), ("@by", actor), ("@doc", doc));
            await Exec(cn, tx, "UPDATE dbo.LegalWatchItems SET Status='APPLIED',DecidedAt=SYSUTCDATETIME(),DecidedBy=@by WHERE Id=@id;", ("@by", actor), ("@id", id));
            await Audit(cn, tx, actor, "LEGAL_DOCUMENT_STATUS_APPLIED", "dbo.LegalDocuments", doc,
                AuditSql.Json(("watchItem", id), ("effectStatus", status), ("expiredDate", expired?.ToString("yyyy-MM-dd")), ("previousExpiredDate", oldExpired?.ToString("yyyy-MM-dd"))), ip);
            return LegalOutcome.Saved;
        });

    // ---- helpers --------------------------------------------------------------------------------------------
    private async Task<SqlConnection> Open()
    {
        var cn = Connection();
        await cn.OpenAsync();
        return cn;
    }

    private async Task<LegalOutcome> Write(Guid actor, Func<SqlConnection, SqlTransaction, Task<LegalOutcome>> body)
    {
        await using var cn = await Open();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.LegalLibraryManage)) return LegalOutcome.Forbidden;
            var result = await body(cn, tx);
            if (result == LegalOutcome.Saved) await tx.CommitAsync();
            return result;
        }
        catch (AuditUnavailableException) { return LegalOutcome.AuditUnavailable; }
    }

    private Task Audit(SqlConnection cn, SqlTransaction tx, Guid actor, string action, string table, Guid id, string newValues, string? ip) =>
        AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, action, table, id.ToString(), "{}", newValues, ip);

    private static LegalWatchItem Item(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), S(r, 4), r.GetString(5), r.GetString(6), S(r, 7),
        r.GetString(8), S(r, 9), D(r, 10), D(r, 11), D(r, 12), S(r, 13), S(r, 14), S(r, 15), r.GetString(16), r.GetDateTime(17), D(r, 18), S(r, 19), S(r, 20),
        r.IsDBNull(21) ? null : r.GetGuid(21), S(r, 22), S(r, 23), D(r, 24));

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    private static int? I(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
    private static object N(string? s) => (object?)s ?? DBNull.Value;
    private static object Date(DateTime? d) => d is DateTime x ? x.Date : DBNull.Value;

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Cmd(cn, tx, sql, args);
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    private static async Task<int> Exec(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Cmd(cn, tx, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static SqlCommand Cmd(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 60 };
        foreach (var (name, value) in args)
        {
            if (value is string s) cmd.Parameters.Add(name, SqlDbType.NVarChar, s.Length > 4000 ? -1 : 4000).Value = s;
            else if (name is "@issued" or "@effective" or "@expired") cmd.Parameters.Add(name, SqlDbType.Date).Value = value;
            else if (name is "@since") cmd.Parameters.Add(name, SqlDbType.DateTime2).Value = value;
            else if (value is DBNull) cmd.Parameters.Add(name, SqlDbType.NVarChar, 4000).Value = value;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }
}
