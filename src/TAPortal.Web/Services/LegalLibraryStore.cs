using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

public sealed record LegalDocumentItem(Guid Id, string DocumentNo, string Title, string DocType, DateTime? IssuedDate, DateTime? EffectiveDate, DateTime? ExpiredDate,
    string? Note, int ArticleCount, DateTime CreatedAt)
{
    public bool IsInForce(DateTime today) => (EffectiveDate is null || EffectiveDate <= today) && (ExpiredDate is null || ExpiredDate > today);
}

public sealed record LegalArticleItem(long Id, string ArticleNo, string? Title, string Content);

public sealed record LegalDocumentDetail(LegalDocumentItem Document, IReadOnlyList<LegalArticleItem> Articles);

public sealed class LegalDocumentInput
{
    public string? DocumentNo { get; set; }
    public string? Title { get; set; }
    public string? DocType { get; set; } = "LAW";
    public DateTime? IssuedDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public DateTime? ExpiredDate { get; set; }
    public string? Note { get; set; }
    // Full text to split into articles; empty on update keeps the current articles.
    public string? FullText { get; set; }

    public static LegalDocumentInput From(LegalDocumentItem d) => new()
    {
        DocumentNo = d.DocumentNo, Title = d.Title, DocType = d.DocType, IssuedDate = d.IssuedDate, EffectiveDate = d.EffectiveDate, ExpiredDate = d.ExpiredDate, Note = d.Note
    };

    public IReadOnlyList<(string Key, string Message)> Validate(bool creating)
    {
        DocumentNo = string.IsNullOrWhiteSpace(DocumentNo) ? null : Regex.Replace(DocumentNo.Trim(), @"\s+", "");
        Title = string.IsNullOrWhiteSpace(Title) ? null : Regex.Replace(Title.Trim(), @"\s+", " ");
        Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        DocType = (DocType ?? "").Trim().ToUpperInvariant();
        var errors = new List<(string, string)>();
        if (DocumentNo is null) errors.Add(("Input.DocumentNo", "Nhập số hiệu văn bản (ví dụ 91/2015/QH13)."));
        else if (DocumentNo.Length > 50) errors.Add(("Input.DocumentNo", "Số hiệu tối đa 50 ký tự."));
        if (Title is null) errors.Add(("Input.Title", "Nhập tên văn bản."));
        else if (Title.Length > 300) errors.Add(("Input.Title", "Tên văn bản tối đa 300 ký tự."));
        if (!LegalLibraryPolicy.Types.Any(t => t.Code == DocType)) errors.Add(("Input.DocType", "Loại văn bản không hợp lệ."));
        if (EffectiveDate is DateTime e && ExpiredDate is DateTime x && x <= e) errors.Add(("Input.ExpiredDate", "Ngày hết hiệu lực phải sau ngày có hiệu lực."));
        if (Note?.Length > 1000) errors.Add(("Input.Note", "Ghi chú tối đa 1000 ký tự."));
        if (creating && string.IsNullOrWhiteSpace(FullText)) errors.Add(("Input.FullText", "Dán toàn văn văn bản để tách thành các điều."));
        if (!string.IsNullOrWhiteSpace(FullText) && LegalTextParser.Split(FullText).Count == 0)
            errors.Add(("Input.FullText", "Không tìm thấy điều nào. Mỗi điều phải bắt đầu bằng dòng \"Điều 1.\", \"Điều 2.\"..."));
        return errors;
    }
}

public static class LegalLibraryPolicy
{
    public static readonly IReadOnlyList<ReviewOption> Types =
        [new("CODE", "Bộ luật"), new("LAW", "Luật"), new("DECREE", "Nghị định"), new("CIRCULAR", "Thông tư"), new("OTHER", "Văn bản khác")];

    // Suggested starting set for contract work (operator to confirm); shown as a hint on the library page.
    public static readonly IReadOnlyList<string> Suggested =
    [
        "Bộ luật Dân sự 2015 (91/2015/QH13)", "Luật Thương mại 2005 (36/2005/QH11)", "Bộ luật Lao động 2019 (45/2019/QH14)",
        "Luật Doanh nghiệp 2020 (59/2020/QH14)", "Luật Kinh doanh bất động sản 2023 (29/2023/QH15)", "Luật Quản lý thuế 2019 (38/2019/QH14)",
        "Nghị định 123/2020/NĐ-CP về hóa đơn, chứng từ"
    ];
}

public enum LegalOutcome { Saved, NotFound, Forbidden, Invalid, Duplicate, AuditUnavailable }

public interface ILegalLibraryStore
{
    Task<IReadOnlyList<LegalDocumentItem>> ListAsync();
    Task<LegalDocumentDetail?> GetAsync(Guid id);
    Task<IReadOnlyList<LegalIndexArticle>> IndexAsync(DateTime today);
    Task<(LegalOutcome Outcome, Guid? Id, int Articles)> SaveAsync(Guid actor, Guid? id, LegalDocumentInput input, string? ip);
    Task<LegalOutcome> DeleteAsync(Guid actor, Guid id, string? ip);
}

// Law library (dbo.LegalDocuments, dbo.LegalArticles of migration 044). Writes need LEGAL.LIBRARY.MANAGE (checked in SQL)
// and are audited; documents are soft-deleted. Saving a full text replaces the document's articles.
public sealed class SqlLegalLibraryStore(IConfiguration configuration, AdminAuditOptions audit) : ILegalLibraryStore
{
    private const string Columns = "d.Id,d.DocumentNo,d.Title,d.DocType,d.IssuedDate,d.EffectiveDate,d.ExpiredDate,d.Note,(SELECT COUNT(1) FROM dbo.LegalArticles a WHERE a.DocumentId=d.Id),d.CreatedAt";
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<IReadOnlyList<LegalDocumentItem>> ListAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT {Columns} FROM dbo.LegalDocuments d WHERE d.IsDeleted=0 ORDER BY d.DocType,d.Title;", cn);
        var list = new List<LegalDocumentItem>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(Item(r));
        return list;
    }

    public async Task<LegalDocumentDetail?> GetAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        LegalDocumentItem? doc = null;
        await using (var cmd = new SqlCommand($"SELECT {Columns} FROM dbo.LegalDocuments d WHERE d.Id=@id AND d.IsDeleted=0;", cn))
        {
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) doc = Item(r);
        }
        if (doc is null) return null;
        var articles = new List<LegalArticleItem>();
        await using (var cmd = new SqlCommand("SELECT Id,ArticleNo,Title,Content FROM dbo.LegalArticles WHERE DocumentId=@id ORDER BY Seq;", cn))
        {
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) articles.Add(new(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3)));
        }
        return new(doc, articles);
    }

    // Articles of documents in force today, for the AI prompt (titles only) and citation checks.
    public async Task<IReadOnlyList<LegalIndexArticle>> IndexAsync(DateTime today)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
SELECT d.DocumentNo,d.Title,a.ArticleNo,a.Title FROM dbo.LegalArticles a JOIN dbo.LegalDocuments d ON d.Id=a.DocumentId
WHERE d.IsDeleted=0 AND (d.EffectiveDate IS NULL OR d.EffectiveDate<=@today) AND (d.ExpiredDate IS NULL OR d.ExpiredDate>@today)
ORDER BY d.DocType,d.DocumentNo,a.Seq;
""", cn);
        cmd.Parameters.Add("@today", SqlDbType.Date).Value = today.Date;
        var list = new List<LegalIndexArticle>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }

    public async Task<(LegalOutcome Outcome, Guid? Id, int Articles)> SaveAsync(Guid actor, Guid? id, LegalDocumentInput input, string? ip)
    {
        if (input.Validate(id is null).Count > 0) return (LegalOutcome.Invalid, id, 0);
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.LegalLibraryManage)) return (LegalOutcome.Forbidden, id, 0);
            var target = id ?? Guid.NewGuid();
            await using (var dup = Cmd(cn, tx, "SELECT COUNT(1) FROM dbo.LegalDocuments WITH (UPDLOCK, HOLDLOCK) WHERE DocumentNo=@no AND IsDeleted=0 AND Id<>@id;", ("@no", input.DocumentNo!), ("@id", target)))
                if (Convert.ToInt32(await dup.ExecuteScalarAsync()) > 0) return (LegalOutcome.Duplicate, id, 0);
            var values = new (string, object)[]
            {
                ("@id", target), ("@no", input.DocumentNo!), ("@title", input.Title!), ("@type", input.DocType!), ("@issued", Date(input.IssuedDate)),
                ("@effective", Date(input.EffectiveDate)), ("@expired", Date(input.ExpiredDate)), ("@note", (object?)input.Note ?? DBNull.Value), ("@by", actor)
            };
            if (id is null)
                await Exec(cn, tx, "INSERT dbo.LegalDocuments(Id,DocumentNo,Title,DocType,IssuedDate,EffectiveDate,ExpiredDate,Note,CreatedBy) VALUES(@id,@no,@title,@type,@issued,@effective,@expired,@note,@by);", values);
            else if (await Exec(cn, tx, "UPDATE dbo.LegalDocuments SET DocumentNo=@no,Title=@title,DocType=@type,IssuedDate=@issued,EffectiveDate=@effective,ExpiredDate=@expired,Note=@note,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;", values) != 1)
                return (LegalOutcome.NotFound, id, 0);
            var count = -1;
            if (!string.IsNullOrWhiteSpace(input.FullText))
            {
                var articles = LegalTextParser.Split(input.FullText);
                await Exec(cn, tx, "DELETE FROM dbo.LegalArticles WHERE DocumentId=@id;", ("@id", target));
                var seq = 0;
                foreach (var a in articles)
                    await Exec(cn, tx, "INSERT dbo.LegalArticles(DocumentId,Seq,ArticleNo,Title,Content) VALUES(@id,@seq,@no,@title,@content);",
                        ("@id", target), ("@seq", ++seq), ("@no", a.ArticleNo), ("@title", (object?)ReviewText.Short(a.Title, 500) ?? DBNull.Value), ("@content", a.Content));
                count = articles.Count;
            }
            await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, id is null ? "LEGAL_DOCUMENT_CREATED" : "LEGAL_DOCUMENT_UPDATED", "dbo.LegalDocuments",
                target.ToString(), "{}", AuditSql.Json(("documentNo", input.DocumentNo), ("type", input.DocType), ("articles", count)), ip);
            await tx.CommitAsync();
            return (LegalOutcome.Saved, target, count);
        }
        catch (AuditUnavailableException) { return (LegalOutcome.AuditUnavailable, id, 0); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return (LegalOutcome.Duplicate, id, 0); }
    }

    public async Task<LegalOutcome> DeleteAsync(Guid actor, Guid id, string? ip)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.LegalLibraryManage)) return LegalOutcome.Forbidden;
            if (await Exec(cn, tx, "UPDATE dbo.LegalDocuments SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@id AND IsDeleted=0;", ("@id", id), ("@by", actor)) != 1)
                return LegalOutcome.NotFound;
            await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, "LEGAL_DOCUMENT_SOFT_DELETED", "dbo.LegalDocuments", id.ToString(), "{}", AuditSql.Json(("isDeleted", true)), ip);
            await tx.CommitAsync();
            return LegalOutcome.Saved;
        }
        catch (AuditUnavailableException) { return LegalOutcome.AuditUnavailable; }
    }

    private static LegalDocumentItem Item(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), D(r, 4), D(r, 5), D(r, 6),
        r.IsDBNull(7) ? null : r.GetString(7), r.GetInt32(8), r.GetDateTime(9));

    private static DateTime? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    private static object Date(DateTime? d) => d is DateTime x ? x.Date : DBNull.Value;

    private static async Task<int> Exec(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Cmd(cn, tx, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static SqlCommand Cmd(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args)
        {
            if (value is string s) cmd.Parameters.Add(name, SqlDbType.NVarChar, s.Length > 4000 ? -1 : 4000).Value = s;
            else if (name is "@issued" or "@effective" or "@expired") cmd.Parameters.Add(name, SqlDbType.Date).Value = value;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }
}

// Runs queued AI reviews one at a time (every 10 seconds when idle). A run left RUNNING by a restart is marked FAILED after
// 30 minutes so the staff can run it again. Registered only when a database is configured; without an API key it idles.
public sealed class ContractReviewWorker(IServiceScopeFactory scopes, IContractReviewAi ai, ILogger<ContractReviewWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan Idle = TimeSpan.FromSeconds(10), Stale = TimeSpan.FromMinutes(30), RunTimeout = TimeSpan.FromMinutes(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }
        var lastRecovery = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IContractReviewStore>();
                if (DateTime.UtcNow - lastRecovery > TimeSpan.FromMinutes(5))
                {
                    lastRecovery = DateTime.UtcNow;
                    var stale = await store.RecoverStaleRunsAsync(Stale);
                    if (stale > 0) logger.LogWarning("Contract review runs marked failed after interruption: {Count}.", stale);
                }
                if (ai.IsConfigured && await store.ClaimRunAsync() is { } job)
                {
                    worked = true;
                    await Process(scope.ServiceProvider, store, job, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable or migration 044 not applied yet: try again later.
                logger.LogWarning(ex, "CONTRACT_REVIEW_WORKER_FAILED");
            }
            if (worked) continue;
            try { await Task.Delay(Idle, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task Process(IServiceProvider services, IContractReviewStore store, ReviewRunJob job, CancellationToken stoppingToken)
    {
        try
        {
            var library = await services.GetRequiredService<ILegalLibraryStore>().IndexAsync(DateTime.Today);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(RunTimeout);
            var result = await ai.ReviewAsync(new(job.Title, job.Role, job.Paragraphs, library, job.Mask), timeout.Token);
            await store.CompleteRunAsync(job, result);
            logger.LogInformation("Contract review run {RunId} done: {Findings} findings, {Input}+{Output} tokens, {Cost} USD.",
                job.RunId, result.Findings.Count, result.InputTokens, result.OutputTokens, result.CostUsd);
        }
        catch (ReviewAiException ex) { await store.FailRunAsync(job.RunId, job.ReviewId, ex.Message); }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { await store.FailRunAsync(job.RunId, job.ReviewId, "AI rà soát quá 20 phút nên đã dừng. Hãy chạy lại."); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "CONTRACT_REVIEW_RUN_FAILED {RunId}", job.RunId);
            await store.FailRunAsync(job.RunId, job.ReviewId, "Lỗi khi xử lý kết quả rà soát. Chi tiết đã ghi vào nhật ký ứng dụng.");
        }
    }
}
