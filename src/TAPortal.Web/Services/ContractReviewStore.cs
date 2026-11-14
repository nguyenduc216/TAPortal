using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// ---- Read models ------------------------------------------------------------------------------------------------------
public sealed record ReviewListItem(Guid Id, string Code, Guid CustomerId, string CustomerName, string? CustomerCode, string Title, string Role,
    string Status, int CurrentVersionNo, int OpenFindings, DateTime CreatedAt, string? CreatedByName);

public sealed record ReviewVersionInfo(int No, string Source, string Label, string? Note, DateTime CreatedAt, string? CreatedByName, int ParagraphCount);

public sealed record ReviewFileInfo(Guid Id, int VersionNo, string Kind, string FileName, int SizeBytes, DateTime CreatedAt);

public sealed record ReviewRunInfo(Guid Id, int VersionNo, string Status, string? Model, int? InputTokens, int? OutputTokens, decimal? CostUsd,
    int? FindingCount, string? Summary, string? Error, DateTime RequestedAt, DateTime? FinishedAt, string? RequestedByName, DateTime? StartedAt = null);

public sealed record ReviewFinding(Guid Id, int VersionNo, string ParagraphKey, string Action, string? Quote, string Issue, string Severity, string Party,
    string? LegalBasis, bool LegalVerified, string? Suggestion, string? RevisedText, string Status, string? StaffText, string? StaffNote, string Source,
    int? AppliedInVersionNo, DateTime CreatedAt, DateTime? DecidedAt, string? DecidedByName)
{
    // Text that goes into the next version: the staff wording when edited, else the AI proposal.
    public string? Replacement => Status == ContractReviewPolicy.Edited ? StaffText : RevisedText;
    public bool IsDecided => Status is ContractReviewPolicy.Accepted or ContractReviewPolicy.Edited;
}

public sealed record ReviewChange(Guid Id, int FromVersionNo, int ToVersionNo, string ParagraphKey, Guid? FindingId, string? OldText, string? NewText,
    string? Note, string CustomerDecision, string? CustomerText, string? CustomerNote, DateTime? DecidedAt);

public sealed record ReviewDetail(Guid Id, string Code, Guid CustomerId, string CustomerName, string? CustomerCode, Guid? ContractId, string? ContractNo,
    string Title, string Role, string Status, int CurrentVersionNo, int? FinalVersionNo, string? Note, DateTime CreatedAt, string? CreatedByName, string Stamp,
    IReadOnlyList<ReviewVersionInfo> Versions, IReadOnlyList<ReviewFileInfo> Files, IReadOnlyList<ReviewRunInfo> Runs);

public sealed record ReviewContractOption(Guid Id, Guid CustomerId, string ContractNo, string Title);

public sealed record ReviewLookups(IReadOnlyList<ContractCustomerOption> Customers, IReadOnlyList<ReviewContractOption> Contracts)
{
    public static readonly ReviewLookups Empty = new([], []);
}

public sealed class ReviewFilter
{
    public Guid? CustomerId { get; set; }
    public string? Status { get; set; }
    public string? Q { get; set; }
    public string? StatusOrNull => ContractReviewPolicy.Is(ContractReviewPolicy.Statuses, Status?.Trim().ToUpperInvariant()) ? Status!.Trim().ToUpperInvariant() : null;
}

public sealed class ReviewInput
{
    public Guid? CustomerId { get; set; }
    public Guid? ContractId { get; set; }
    public string? Title { get; set; }
    public string? Role { get; set; } = "BOTH";
    public string? Note { get; set; }

    public IReadOnlyList<(string Key, string Message)> Validate()
    {
        Title = string.IsNullOrWhiteSpace(Title) ? null : Regex.Replace(Title.Trim(), @"\s+", " ");
        Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        Role = (Role ?? "").Trim().ToUpperInvariant();
        var errors = new List<(string, string)>();
        if (CustomerId is null || CustomerId == Guid.Empty) errors.Add(("Input.CustomerId", "Vui lòng chọn khách hàng."));
        if (Title is null) errors.Add(("Input.Title", "Vui lòng nhập tên hợp đồng cần chỉnh lý."));
        else if (Title.Length > 250) errors.Add(("Input.Title", "Tên tối đa 250 ký tự."));
        if (!ContractReviewPolicy.Is(ContractReviewPolicy.Roles, Role)) errors.Add(("Input.Role", "Chọn khách hàng là Bên A, Bên B hay rà soát cho cả hai bên."));
        if (Note?.Length > 2000) errors.Add(("Input.Note", "Ghi chú tối đa 2000 ký tự."));
        return errors;
    }
}

public sealed class ReviewFindingInput
{
    public string? ParagraphKey { get; set; }
    public string? Action { get; set; } = ContractReviewPolicy.Replace;
    public string? Quote { get; set; }
    public string? Issue { get; set; }
    public string? Severity { get; set; } = "MEDIUM";
    public string? Party { get; set; } = "BOTH";
    public string? LegalBasis { get; set; }
    public string? RevisedText { get; set; }
}

public sealed record ReviewUpload(string FileName, byte[] Data, IReadOnlyList<ReviewParagraph> Paragraphs);

public sealed record ReviewFileContent(string FileName, string ContentType, byte[] Data);

public enum ReviewOutcome { Saved, NotFound, Forbidden, Conflict, Invalid, Locked, NotConfigured, AlreadyQueued, AuditUnavailable }

public sealed record ReviewResult(ReviewOutcome Outcome, Guid? Id = null, int? VersionNo = null, IReadOnlyList<string>? Errors = null, bool AuditPersisted = false);

// Work item of the AI worker.
public sealed record ReviewRunJob(Guid RunId, Guid ReviewId, int VersionNo, bool Mask, string Title, string Role, IReadOnlyList<ReviewParagraph> Paragraphs, string? Note = null);

public interface IContractReviewStore
{
    Task<IReadOnlyList<ReviewListItem>> ListAsync(ReviewFilter filter);
    Task<ReviewLookups> LookupsAsync();
    Task<ReviewDetail?> GetAsync(Guid id);
    Task<IReadOnlyList<ReviewParagraph>> ParagraphsAsync(Guid id, int versionNo);
    Task<IReadOnlyList<ReviewFinding>> FindingsAsync(Guid id);
    Task<IReadOnlyList<ReviewChange>> ChangesAsync(Guid id);
    Task<ReviewFileContent?> DownloadAsync(Guid id, Guid fileId);

    Task<ReviewResult> CreateAsync(Guid actor, ReviewInput input, ReviewUpload upload, string? ip);
    Task<ReviewResult> QueueRunAsync(Guid actor, Guid id, bool mask, string? ip);
    Task<ReviewResult> AddFindingAsync(Guid actor, Guid id, ReviewFindingInput input, string? ip);
    Task<ReviewResult> DecideFindingAsync(Guid actor, Guid id, Guid findingId, string status, string? staffText, string? staffNote, string? ip);
    Task<ReviewResult> CreateVersionAsync(Guid actor, Guid id, string? note, string? ip);
    Task<ReviewResult> MarkSentAsync(Guid actor, Guid id, string? ip);
    Task<ReviewResult> RecordCustomerAsync(Guid actor, Guid id, Guid changeId, string decision, string? customerText, string? customerNote, string? ip);
    Task<ReviewResult> CreateCustomerVersionAsync(Guid actor, Guid id, string? note, string? ip);
    Task<ReviewResult> FinalizeAsync(Guid actor, Guid id, int versionNo, string? stamp, string? ip);
    Task<ReviewResult> CancelAsync(Guid actor, Guid id, string? stamp, string? ip);

    // AI worker (no actor; the run row records who asked for it).
    Task<ReviewRunJob?> ClaimRunAsync();
    Task CompleteRunAsync(ReviewRunJob job, ReviewAiResult result);
    Task FailRunAsync(Guid runId, Guid reviewId, string error);
    Task<int> RecoverStaleRunsAsync(TimeSpan olderThan);
    // Live token counts of a running review (shown on the page while the AI works).
    Task ProgressAsync(Guid runId, long inputTokens, long outputTokens);
}

public static class ContractReviewSql
{
    public const string Entity = "dbo.ContractReviews";

    public const string List = """
SELECT r.Id,r.Code,r.CustomerId,cu.Name,cu.Code,r.Title,r.CustomerRole,r.Status,r.CurrentVersionNo,
 (SELECT COUNT(1) FROM dbo.ContractReviewFindings f WHERE f.ReviewId=r.Id AND f.Status='OPEN' AND f.VersionNo=r.CurrentVersionNo),
 r.CreatedAt,COALESCE(u.DisplayName,u.Username)
FROM dbo.ContractReviews r JOIN dbo.Customers cu ON cu.Id=r.CustomerId LEFT JOIN dbo.Users u ON u.Id=r.CreatedBy
WHERE r.IsDeleted=0 AND (@customer IS NULL OR r.CustomerId=@customer) AND (@status IS NULL OR r.Status=@status)
 AND (@q IS NULL OR r.Code LIKE @q ESCAPE '\' OR r.Title LIKE @q ESCAPE '\' OR cu.Name LIKE @q ESCAPE '\')
ORDER BY r.CreatedAt DESC;
""";

    public const string Review = """
SELECT r.Id,r.Code,r.CustomerId,cu.Name,cu.Code,r.ContractId,c.ContractNo,r.Title,r.CustomerRole,r.Status,r.CurrentVersionNo,r.FinalVersionNo,r.Note,
 r.CreatedAt,COALESCE(u.DisplayName,u.Username),r.RowVersion
FROM dbo.ContractReviews r JOIN dbo.Customers cu ON cu.Id=r.CustomerId LEFT JOIN dbo.CustomerContracts c ON c.Id=r.ContractId LEFT JOIN dbo.Users u ON u.Id=r.CreatedBy
WHERE r.Id=@id AND r.IsDeleted=0;
""";

    public const string Versions = """
SELECT v.VersionNo,v.Source,v.Label,v.Note,v.CreatedAt,COALESCE(u.DisplayName,u.Username),(SELECT COUNT(1) FROM dbo.ContractReviewParagraphs p WHERE p.VersionId=v.Id)
FROM dbo.ContractReviewVersions v LEFT JOIN dbo.Users u ON u.Id=v.CreatedBy WHERE v.ReviewId=@id ORDER BY v.VersionNo;
""";

    public const string Files = "SELECT Id,VersionNo,Kind,FileName,SizeBytes,CreatedAt FROM dbo.ContractReviewFiles WHERE ReviewId=@id ORDER BY CreatedAt DESC;";

    public const string Runs = """
SELECT x.Id,x.VersionNo,x.Status,x.Model,x.InputTokens,x.OutputTokens,x.CostUsd,x.FindingCount,x.Summary,x.Error,x.RequestedAt,x.FinishedAt,COALESCE(u.DisplayName,u.Username),x.StartedAt
FROM dbo.ContractReviewRuns x LEFT JOIN dbo.Users u ON u.Id=x.RequestedBy WHERE x.ReviewId=@id ORDER BY x.RequestedAt DESC;
""";

    public const string Paragraphs = """
SELECT p.ParagraphKey,p.Kind,p.Text FROM dbo.ContractReviewParagraphs p JOIN dbo.ContractReviewVersions v ON v.Id=p.VersionId
WHERE v.ReviewId=@id AND v.VersionNo=@no ORDER BY p.Seq;
""";

    public const string AllKeys = """
SELECT DISTINCT p.ParagraphKey FROM dbo.ContractReviewParagraphs p JOIN dbo.ContractReviewVersions v ON v.Id=p.VersionId WHERE v.ReviewId=@id;
""";

    public const string Findings = """
SELECT f.Id,f.VersionNo,f.ParagraphKey,f.Action,f.Quote,f.Issue,f.Severity,f.Party,f.LegalBasis,f.LegalVerified,f.Suggestion,f.RevisedText,f.Status,
 f.StaffText,f.StaffNote,f.Source,f.AppliedInVersionNo,f.CreatedAt,f.DecidedAt,COALESCE(u.DisplayName,u.Username)
FROM dbo.ContractReviewFindings f LEFT JOIN dbo.Users u ON u.Id=f.DecidedBy WHERE f.ReviewId=@id
ORDER BY f.VersionNo, CASE f.Severity WHEN 'HIGH' THEN 0 WHEN 'MEDIUM' THEN 1 ELSE 2 END, f.CreatedAt;
""";

    public const string Changes = """
SELECT Id,FromVersionNo,ToVersionNo,ParagraphKey,FindingId,OldText,NewText,Note,CustomerDecision,CustomerText,CustomerNote,DecidedAt
FROM dbo.ContractReviewChanges WHERE ReviewId=@id ORDER BY ToVersionNo, ParagraphKey;
""";

    public const string Download = """
SELECT f.FileName,f.ContentType,f.Data FROM dbo.ContractReviewFiles f JOIN dbo.ContractReviews r ON r.Id=f.ReviewId
WHERE f.Id=@file AND f.ReviewId=@id AND r.IsDeleted=0;
""";

    public const string Customers = "SELECT Id,Code,Name FROM dbo.Customers WHERE IsDeleted=0 ORDER BY Name;";
    public const string Contracts = "SELECT Id,CustomerId,ContractNo,Title FROM dbo.CustomerContracts WHERE IsDeleted=0 ORDER BY ContractNo;";
    public const string CustomerExists = "SELECT COUNT(1) FROM dbo.Customers WHERE Id=@customer AND IsDeleted=0;";
    public const string ContractOfCustomer = "SELECT COUNT(1) FROM dbo.CustomerContracts WHERE Id=@contract AND CustomerId=@customer AND IsDeleted=0;";

    public const string Lock = "SELECT Status,CurrentVersionNo,RowVersion,Title,CustomerRole FROM dbo.ContractReviews WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id AND IsDeleted=0;";

    public const string SequenceForUpdate = "SELECT TOP 1 Prefix,CurrentValue,Padding FROM dbo.NumberSequences WITH (UPDLOCK, HOLDLOCK) WHERE Code='CONTRACT_REVIEW_CODE' AND IsActive=1;";
    public const string SequenceAdvance = "UPDATE dbo.NumberSequences SET CurrentValue=@n,UpdatedAt=SYSUTCDATETIME() WHERE Code='CONTRACT_REVIEW_CODE' AND IsActive=1 AND CurrentValue<@n;";
    public const string CodeTaken = "SELECT COUNT(1) FROM dbo.ContractReviews WHERE Code=@code;";

    public const string Insert = """
INSERT dbo.ContractReviews(Id,Code,CustomerId,ContractId,Title,CustomerRole,Status,CurrentVersionNo,Note,CreatedBy)
VALUES(@id,@code,@customer,@contract,@title,@role,'NEW',0,@note,@by);
""";

    public const string InsertFile = "INSERT dbo.ContractReviewFiles(Id,ReviewId,VersionNo,Kind,FileName,ContentType,SizeBytes,Data,CreatedBy) VALUES(@file,@id,@no,@kind,@name,@type,@size,@data,@by);";
    public const string InsertVersion = "INSERT dbo.ContractReviewVersions(Id,ReviewId,VersionNo,Source,Label,Note,CreatedBy) VALUES(@version,@id,@no,@source,@label,@note,@by);";
    public const string InsertParagraph = "INSERT dbo.ContractReviewParagraphs(VersionId,Seq,ParagraphKey,Kind,Text) VALUES(@version,@seq,@key,@kind,@text);";

    public const string SetStatus = "UPDATE dbo.ContractReviews SET Status=@status,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
    public const string SetVersion = "UPDATE dbo.ContractReviews SET Status=@status,CurrentVersionNo=@no,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
    public const string SetFinal = "UPDATE dbo.ContractReviews SET Status='FINALIZED',FinalVersionNo=@no,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;";
    public const string SetCancelled = "UPDATE dbo.ContractReviews SET Status='CANCELLED',UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;";

    public const string ActiveRun = "SELECT COUNT(1) FROM dbo.ContractReviewRuns WHERE ReviewId=@id AND Status IN ('QUEUED','RUNNING');";
    public const string InsertRun = "INSERT dbo.ContractReviewRuns(Id,ReviewId,VersionNo,Status,MaskPersonalData,RequestedBy) VALUES(@run,@id,@no,'QUEUED',@mask,@by);";

    public const string InsertFinding = """
INSERT dbo.ContractReviewFindings(Id,ReviewId,RunId,VersionNo,ParagraphKey,Action,Quote,Issue,Severity,Party,LegalBasis,LegalVerified,Suggestion,RevisedText,Status,Source,CreatedBy)
VALUES(@finding,@id,@run,@no,@key,@action,@quote,@issue,@severity,@party,@legal,@verified,@suggestion,@revised,'OPEN',@source,@by);
""";

    public const string FindingForUpdate = "SELECT VersionNo,AppliedInVersionNo,Action FROM dbo.ContractReviewFindings WITH (UPDLOCK) WHERE Id=@finding AND ReviewId=@id;";
    public const string DecideFinding = """
UPDATE dbo.ContractReviewFindings SET Status=@status,StaffText=@staffText,StaffNote=@staffNote,DecidedAt=SYSUTCDATETIME(),DecidedBy=@by
WHERE Id=@finding AND ReviewId=@id AND AppliedInVersionNo IS NULL;
""";
    public const string MarkApplied = "UPDATE dbo.ContractReviewFindings SET AppliedInVersionNo=@no WHERE ReviewId=@id AND VersionNo=@from AND Status IN ('ACCEPTED','EDITED') AND AppliedInVersionNo IS NULL;";

    public const string InsertChange = """
INSERT dbo.ContractReviewChanges(Id,ReviewId,FromVersionNo,ToVersionNo,ParagraphKey,FindingId,OldText,NewText,Note,CustomerDecision,DecidedAt,DecidedBy)
VALUES(NEWID(),@id,@from,@no,@key,@findingRef,@old,@new,@note,@decision,@decidedAt,@decidedBy);
""";

    public const string ChangeForUpdate = "SELECT ToVersionNo FROM dbo.ContractReviewChanges WITH (UPDLOCK) WHERE Id=@change AND ReviewId=@id;";
    public const string DecideChange = """
UPDATE dbo.ContractReviewChanges SET CustomerDecision=@decision,CustomerText=@customerText,CustomerNote=@customerNote,DecidedAt=SYSUTCDATETIME(),DecidedBy=@by
WHERE Id=@change AND ReviewId=@id;
""";

    // Worker: take the oldest queued run; READPAST lets several app instances share the queue without blocking.
    public const string Claim = """
UPDATE TOP (1) x SET Status='RUNNING',StartedAt=SYSUTCDATETIME()
OUTPUT inserted.Id,inserted.ReviewId,inserted.VersionNo,inserted.MaskPersonalData
FROM dbo.ContractReviewRuns x WITH (UPDLOCK, READPAST, ROWLOCK)
WHERE x.Status='QUEUED' AND x.RequestedAt=(SELECT MIN(q.RequestedAt) FROM dbo.ContractReviewRuns q WITH (READPAST) WHERE q.Status='QUEUED');
""";
    public const string RunReview = "SELECT Title,CustomerRole,Note FROM dbo.ContractReviews WHERE Id=@id;";
    public const string CompleteRun = """
UPDATE dbo.ContractReviewRuns SET Status='DONE',Model=@model,InputTokens=@input,OutputTokens=@output,CostUsd=@cost,FindingCount=@count,Summary=@summary,
 Error=@error,ResponseJson=@json,FinishedAt=SYSUTCDATETIME() WHERE Id=@run AND Status='RUNNING';
""";
    public const string FailRun = "UPDATE dbo.ContractReviewRuns SET Status='FAILED',Error=@error,FinishedAt=SYSUTCDATETIME() WHERE Id=@run AND Status IN ('QUEUED','RUNNING');";
    public const string AfterRun = "UPDATE dbo.ContractReviews SET Status='IN_REVIEW',UpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND Status='ANALYZING';";
    public const string Stale = """
UPDATE dbo.ContractReviewRuns SET Status='FAILED',Error=N'Lượt rà soát bị gián đoạn (ứng dụng khởi động lại). Hãy chạy lại.',FinishedAt=SYSUTCDATETIME()
OUTPUT inserted.ReviewId
WHERE Status='RUNNING' AND StartedAt<@before;
""";
}

// SQL store of "Hợp đồng dịch vụ". Every staff write runs in a transaction that re-checks the actor's CONTRACTS.REVIEW.*
// permission in SQL, locks the review row (UPDLOCK) and writes one dbo.AuditLogs row. A version is never edited after it is
// created; FINALIZED and CANCELLED reviews refuse every change.
public sealed class SqlContractReviewStore(IConfiguration configuration, AdminAuditOptions audit) : IContractReviewStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    // ---- reads -----------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<ReviewListItem>> ListAsync(ReviewFilter filter)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(ContractReviewSql.List, cn);
        cmd.Parameters.Add("@customer", SqlDbType.UniqueIdentifier).Value = (object?)filter.CustomerId ?? DBNull.Value;
        cmd.Parameters.Add("@status", SqlDbType.VarChar, 20).Value = (object?)filter.StatusOrNull ?? DBNull.Value;
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 210).Value = (object?)ContractPolicy.Pattern(filter.Q) ?? DBNull.Value;
        var list = new List<ReviewListItem>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), r.GetGuid(2), r.GetString(3), S(r, 4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetInt32(8),
                r.GetInt32(9), r.GetDateTime(10), S(r, 11)));
        return list;
    }

    public async Task<ReviewLookups> LookupsAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var customers = new List<ContractCustomerOption>();
        await using (var cmd = new SqlCommand(ContractReviewSql.Customers, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) customers.Add(new(r.GetGuid(0), S(r, 1), r.GetString(2)));
        var contracts = new List<ReviewContractOption>();
        await using (var cmd = new SqlCommand(ContractReviewSql.Contracts, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) contracts.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3)));
        return new(customers, contracts);
    }

    public async Task<ReviewDetail?> GetAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        ReviewDetail? d = null;
        await using (var cmd = Cmd(cn, null, ContractReviewSql.Review, ("@id", id)))
        await using (var r = await cmd.ExecuteReaderAsync())
            if (await r.ReadAsync())
                d = new(r.GetGuid(0), r.GetString(1), r.GetGuid(2), r.GetString(3), S(r, 4), r.IsDBNull(5) ? null : r.GetGuid(5), S(r, 6), r.GetString(7),
                    r.GetString(8), r.GetString(9), r.GetInt32(10), r.IsDBNull(11) ? null : r.GetInt32(11), S(r, 12), r.GetDateTime(13), S(r, 14),
                    Convert.ToHexString((byte[])r.GetValue(15)), [], [], []);
        if (d is null) return null;
        var versions = new List<ReviewVersionInfo>();
        await using (var cmd = Cmd(cn, null, ContractReviewSql.Versions, ("@id", id)))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) versions.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), S(r, 3), r.GetDateTime(4), S(r, 5), r.GetInt32(6)));
        var files = new List<ReviewFileInfo>();
        await using (var cmd = Cmd(cn, null, ContractReviewSql.Files, ("@id", id)))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) files.Add(new(r.GetGuid(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetDateTime(5)));
        var runs = new List<ReviewRunInfo>();
        await using (var cmd = Cmd(cn, null, ContractReviewSql.Runs, ("@id", id)))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                runs.Add(new(r.GetGuid(0), r.GetInt32(1), r.GetString(2), S(r, 3), I(r, 4), I(r, 5), r.IsDBNull(6) ? null : r.GetDecimal(6), I(r, 7), S(r, 8), S(r, 9),
                    r.GetDateTime(10), r.IsDBNull(11) ? null : r.GetDateTime(11), S(r, 12), r.IsDBNull(13) ? null : r.GetDateTime(13)));
        return d with { Versions = versions, Files = files, Runs = runs };
    }

    public async Task<IReadOnlyList<ReviewParagraph>> ParagraphsAsync(Guid id, int versionNo)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await Paragraphs(cn, null, id, versionNo);
    }

    public async Task<IReadOnlyList<ReviewFinding>> FindingsAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await Findings(cn, null, id);
    }

    public async Task<IReadOnlyList<ReviewChange>> ChangesAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await Changes(cn, null, id);
    }

    public async Task<ReviewFileContent?> DownloadAsync(Guid id, Guid fileId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Cmd(cn, null, ContractReviewSql.Download, ("@file", fileId), ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        if (!await r.ReadAsync()) return null;
        var name = r.GetString(0); var type = r.GetString(1); var data = (byte[])r.GetValue(2);
        return new(name, type, data);
    }

    // ---- writes ----------------------------------------------------------------------------------------------
    public Task<ReviewResult> CreateAsync(Guid actor, ReviewInput input, ReviewUpload upload, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewCreate, async (cn, tx) =>
        {
            if (input.Validate().Count > 0 || upload.Paragraphs.Count == 0) return new(ReviewOutcome.Invalid);
            if (Convert.ToInt32(await Scalar(cn, tx, ContractReviewSql.CustomerExists, ("@customer", input.CustomerId!.Value))) != 1) return new(ReviewOutcome.Invalid, Errors: ["Khách hàng không hợp lệ."]);
            if (input.ContractId is Guid contract
                && Convert.ToInt32(await Scalar(cn, tx, ContractReviewSql.ContractOfCustomer, ("@contract", contract), ("@customer", input.CustomerId!.Value))) != 1)
                return new(ReviewOutcome.Invalid, Errors: ["Hợp đồng đã chọn không thuộc khách hàng này."]);
            var code = await NextCode(cn, tx);
            var id = Guid.NewGuid();
            await Exec(cn, tx, ContractReviewSql.Insert, ("@id", id), ("@code", code), ("@customer", input.CustomerId!.Value), ("@contract", Db(input.ContractId)),
                ("@title", input.Title!), ("@role", input.Role!), ("@note", Db(input.Note)), ("@by", actor));
            var name = ContractPolicy.SafeFileName(upload.FileName);
            await Exec(cn, tx, ContractReviewSql.InsertFile, ("@file", Guid.NewGuid()), ("@id", id), ("@no", 0), ("@kind", "ORIGINAL"), ("@name", name),
                ("@type", DocxWriter.ContentType), ("@size", upload.Data.Length), ("@data", upload.Data), ("@by", actor));
            await InsertVersion(cn, tx, id, 0, "ORIGINAL", "Bản gốc của khách hàng", null, upload.Paragraphs, actor);
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_CREATED", id, "{}",
                AuditSql.Json(("code", code), ("customerId", input.CustomerId), ("role", input.Role), ("fileName", name), ("paragraphs", upload.Paragraphs.Count)), ip);
            return new(ReviewOutcome.Saved, id, 0, AuditPersisted: written);
        });

    public Task<ReviewResult> QueueRunAsync(Guid actor, Guid id, bool mask, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewRunAi, async (cn, tx) =>
        {
            var state = await Lock(cn, tx, id);
            if (state is null) return new(ReviewOutcome.NotFound);
            if (!ContractReviewPolicy.CanRunAi(state.Value.Status)) return new(ReviewOutcome.Locked, id);
            if (Convert.ToInt32(await Scalar(cn, tx, ContractReviewSql.ActiveRun, ("@id", id))) > 0) return new(ReviewOutcome.AlreadyQueued, id);
            var run = Guid.NewGuid();
            await Exec(cn, tx, ContractReviewSql.InsertRun, ("@run", run), ("@id", id), ("@no", state.Value.Version), ("@mask", mask), ("@by", actor));
            await Exec(cn, tx, ContractReviewSql.SetStatus, ("@id", id), ("@status", ContractReviewPolicy.Analyzing), ("@by", actor));
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_AI_QUEUED", id, AuditSql.Json(("status", state.Value.Status)),
                AuditSql.Json(("runId", run), ("versionNo", state.Value.Version), ("maskPersonalData", mask)), ip);
            return new(ReviewOutcome.Saved, run, state.Value.Version, AuditPersisted: written);
        });

    public Task<ReviewResult> AddFindingAsync(Guid actor, Guid id, ReviewFindingInput input, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewEdit, async (cn, tx) =>
        {
            var state = await Lock(cn, tx, id);
            if (state is null) return new(ReviewOutcome.NotFound);
            if (ContractReviewPolicy.IsLocked(state.Value.Status) || state.Value.Status == ContractReviewPolicy.Analyzing) return new(ReviewOutcome.Locked, id);
            var paragraphs = await Paragraphs(cn, tx, id, state.Value.Version);
            var key = (input.ParagraphKey ?? "").Trim().ToUpperInvariant();
            var paragraph = paragraphs.FirstOrDefault(p => p.Key == key);
            var action = (input.Action ?? "").Trim().ToUpperInvariant();
            var issue = Clean(input.Issue);
            var quote = Clean(input.Quote);
            var revised = Clean(input.RevisedText);
            var errors = new List<string>();
            if (paragraph is null) errors.Add("Chọn đoạn cần góp ý.");
            if (!ContractReviewPolicy.Is(ContractReviewPolicy.Actions, action)) errors.Add("Loại góp ý không hợp lệ.");
            if (issue is null) errors.Add("Nhập nội dung góp ý.");
            if (quote is not null && paragraph is not null && !paragraph.Text.Contains(quote, StringComparison.Ordinal)) errors.Add("Câu trích phải chép nguyên văn từ đoạn đã chọn.");
            if (action is ContractReviewPolicy.Replace or ContractReviewPolicy.Insert && revised is null) errors.Add("Nhập câu chữ đề xuất.");
            var severity = ContractReviewPolicy.Is(ContractReviewPolicy.Severities, input.Severity) ? input.Severity! : "MEDIUM";
            var party = ContractReviewPolicy.Is(ContractReviewPolicy.Roles, input.Party) ? input.Party! : "BOTH";
            if (errors.Count > 0) return new(ReviewOutcome.Invalid, id, Errors: errors);
            var finding = Guid.NewGuid();
            await Exec(cn, tx, ContractReviewSql.InsertFinding, ("@finding", finding), ("@id", id), ("@run", DBNull.Value), ("@no", state.Value.Version), ("@key", key),
                ("@action", action), ("@quote", Db(quote)), ("@issue", issue!), ("@severity", severity), ("@party", party), ("@legal", Db(Clean(input.LegalBasis))),
                ("@verified", false), ("@suggestion", DBNull.Value), ("@revised", Db(action is ContractReviewPolicy.Replace or ContractReviewPolicy.Insert ? revised : null)),
                ("@source", "STAFF"), ("@by", actor));
            if (state.Value.Status == ContractReviewPolicy.New) await Exec(cn, tx, ContractReviewSql.SetStatus, ("@id", id), ("@status", ContractReviewPolicy.InReview), ("@by", actor));
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_FINDING_ADDED", id, "{}", AuditSql.Json(("findingId", finding), ("paragraph", key), ("action", action)), ip);
            return new(ReviewOutcome.Saved, finding, state.Value.Version, AuditPersisted: written);
        });

    public Task<ReviewResult> DecideFindingAsync(Guid actor, Guid id, Guid findingId, string status, string? staffText, string? staffNote, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewEdit, async (cn, tx) =>
        {
            var state = await Lock(cn, tx, id);
            if (state is null) return new(ReviewOutcome.NotFound);
            if (ContractReviewPolicy.IsLocked(state.Value.Status) || state.Value.Status == ContractReviewPolicy.Analyzing) return new(ReviewOutcome.Locked, id);
            status = (status ?? "").Trim().ToUpperInvariant();
            if (!ContractReviewPolicy.Is(ContractReviewPolicy.FindingStatuses, status)) return new(ReviewOutcome.Invalid, id);
            int versionNo; int? applied; string action;
            await using (var cmd = Cmd(cn, tx, ContractReviewSql.FindingForUpdate, ("@finding", findingId), ("@id", id)))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return new(ReviewOutcome.NotFound);
                versionNo = r.GetInt32(0); applied = r.IsDBNull(1) ? null : r.GetInt32(1); action = r.GetString(2);
            }
            if (applied is not null || versionNo != state.Value.Version) return new(ReviewOutcome.Locked, id, Errors: ["Góp ý này thuộc phiên bản cũ hoặc đã được áp dụng."]);
            var text = Clean(staffText);
            if (status == ContractReviewPolicy.Edited && text is null && action is ContractReviewPolicy.Replace or ContractReviewPolicy.Insert)
                return new(ReviewOutcome.Invalid, id, Errors: ["Nhập câu chữ đã chỉnh sửa."]);
            await Exec(cn, tx, ContractReviewSql.DecideFinding, ("@status", status), ("@staffText", Db(status == ContractReviewPolicy.Edited ? text : null)),
                ("@staffNote", Db(Clean(staffNote))), ("@by", actor), ("@finding", findingId), ("@id", id));
            if (state.Value.Status is ContractReviewPolicy.New) await Exec(cn, tx, ContractReviewSql.SetStatus, ("@id", id), ("@status", ContractReviewPolicy.InReview), ("@by", actor));
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_FINDING_DECIDED", id, "{}", AuditSql.Json(("findingId", findingId), ("status", status)), ip);
            return new(ReviewOutcome.Saved, findingId, versionNo, AuditPersisted: written);
        });

    public Task<ReviewResult> CreateVersionAsync(Guid actor, Guid id, string? note, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewEdit, async (cn, tx) =>
        {
            var state = await Lock(cn, tx, id);
            if (state is null) return new(ReviewOutcome.NotFound);
            if (ContractReviewPolicy.IsLocked(state.Value.Status) || state.Value.Status == ContractReviewPolicy.Analyzing) return new(ReviewOutcome.Locked, id);
            var from = state.Value.Version;
            var current = await Paragraphs(cn, tx, id, from);
            var findings = (await Findings(cn, tx, id)).Where(f => f.VersionNo == from && f.IsDecided && f.AppliedInVersionNo is null).ToList();
            if (findings.Count == 0) return new(ReviewOutcome.Invalid, id, Errors: ["Chưa có góp ý nào được chấp nhận ở phiên bản hiện tại."]);
            var edits = findings.Select(f => new ReviewEdit(f.Id, f.ParagraphKey, f.Action, f.Quote, f.Replacement,
                f.StaffNote is null ? f.Issue : f.Issue + "\nGhi chú: " + f.StaffNote)).ToList();
            var keys = new List<string>();
            await using (var cmd = Cmd(cn, tx, ContractReviewSql.AllKeys, ("@id", id)))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) keys.Add(r.GetString(0));
            var result = ContractReviewPolicy.Apply(current, edits, ContractReviewPolicy.NextInsertNo(keys));
            if (result.Errors.Count > 0) return new(ReviewOutcome.Invalid, id, Errors: result.Errors);
            if (result.Changes.Count == 0) return new(ReviewOutcome.Invalid, id, Errors: ["Các góp ý được chấp nhận không làm thay đổi câu chữ (chỉ là ghi chú)."]);
            var no = from + 1;
            await InsertVersion(cn, tx, id, no, "STAFF", $"Bản sửa lần {no} (T.A đề xuất)", Clean(note), result.Paragraphs, actor);
            foreach (var c in result.Changes)
                await Exec(cn, tx, ContractReviewSql.InsertChange, ("@id", id), ("@from", from), ("@no", no), ("@key", c.Key),
                    ("@findingRef", c.FindingIds.Count == 1 ? c.FindingIds[0] : DBNull.Value), ("@old", Db(c.OldText)), ("@new", Db(c.NewText)), ("@note", Db(c.Note)),
                    ("@decision", ContractReviewPolicy.Pending), ("@decidedAt", DBNull.Value), ("@decidedBy", DBNull.Value));
            await Exec(cn, tx, ContractReviewSql.MarkApplied, ("@no", no), ("@id", id), ("@from", from));
            await Exec(cn, tx, ContractReviewSql.SetVersion, ("@status", ContractReviewPolicy.DraftReady), ("@no", no), ("@by", actor), ("@id", id));
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_VERSION_CREATED", id, AuditSql.Json(("versionNo", from)),
                AuditSql.Json(("versionNo", no), ("changes", result.Changes.Count), ("findings", findings.Count)), ip);
            return new(ReviewOutcome.Saved, id, no, AuditPersisted: written);
        });

    public Task<ReviewResult> MarkSentAsync(Guid actor, Guid id, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewEdit, async (cn, tx) =>
        {
            var state = await Lock(cn, tx, id);
            if (state is null) return new(ReviewOutcome.NotFound);
            if (state.Value.Status != ContractReviewPolicy.DraftReady) return new(ReviewOutcome.Locked, id);
            await Exec(cn, tx, ContractReviewSql.SetStatus, ("@id", id), ("@status", ContractReviewPolicy.Sent), ("@by", actor));
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_SENT", id, AuditSql.Json(("status", state.Value.Status)), AuditSql.Json(("versionNo", state.Value.Version)), ip);
            return new(ReviewOutcome.Saved, id, state.Value.Version, AuditPersisted: written);
        });

    public Task<ReviewResult> RecordCustomerAsync(Guid actor, Guid id, Guid changeId, string decision, string? customerText, string? customerNote, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewEdit, async (cn, tx) =>
        {
            var state = await Lock(cn, tx, id);
            if (state is null) return new(ReviewOutcome.NotFound);
            if (state.Value.Status is not (ContractReviewPolicy.DraftReady or ContractReviewPolicy.Sent or ContractReviewPolicy.CustomerReplied)) return new(ReviewOutcome.Locked, id);
            decision = (decision ?? "").Trim().ToUpperInvariant();
            if (!ContractReviewPolicy.Is(ContractReviewPolicy.CustomerDecisions, decision)) return new(ReviewOutcome.Invalid, id);
            var to = await Scalar(cn, tx, ContractReviewSql.ChangeForUpdate, ("@change", changeId), ("@id", id));
            if (to is null) return new(ReviewOutcome.NotFound);
            if (Convert.ToInt32(to) != state.Value.Version) return new(ReviewOutcome.Locked, id, Errors: ["Thay đổi này thuộc phiên bản cũ."]);
            var text = Clean(customerText);
            if (decision == ContractReviewPolicy.Counter && text is null) return new(ReviewOutcome.Invalid, id, Errors: ["Nhập câu chữ khách hàng đề xuất."]);
            await Exec(cn, tx, ContractReviewSql.DecideChange, ("@decision", decision), ("@customerText", Db(decision == ContractReviewPolicy.Counter ? text : null)),
                ("@customerNote", Db(Clean(customerNote))), ("@by", actor), ("@change", changeId), ("@id", id));
            if (decision != ContractReviewPolicy.Pending && state.Value.Status != ContractReviewPolicy.CustomerReplied)
                await Exec(cn, tx, ContractReviewSql.SetStatus, ("@id", id), ("@status", ContractReviewPolicy.CustomerReplied), ("@by", actor));
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_CUSTOMER_ANSWER", id, "{}", AuditSql.Json(("changeId", changeId), ("decision", decision)), ip);
            return new(ReviewOutcome.Saved, changeId, state.Value.Version, AuditPersisted: written);
        });

    public Task<ReviewResult> CreateCustomerVersionAsync(Guid actor, Guid id, string? note, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewEdit, async (cn, tx) =>
        {
            var state = await Lock(cn, tx, id);
            if (state is null) return new(ReviewOutcome.NotFound);
            if (state.Value.Status != ContractReviewPolicy.CustomerReplied) return new(ReviewOutcome.Locked, id);
            var from = state.Value.Version;
            var changes = (await Changes(cn, tx, id)).Where(c => c.ToVersionNo == from).ToList();
            if (changes.Count == 0) return new(ReviewOutcome.Invalid, id, Errors: ["Phiên bản hiện tại không có thay đổi để khách hàng phản hồi."]);
            var current = await Paragraphs(cn, tx, id, from);
            var before = await Paragraphs(cn, tx, id, changes.Min(c => c.FromVersionNo));
            var result = ContractReviewPolicy.ApplyCustomer(current, before,
                changes.Select(c => new ReviewCustomerAnswer(c.ParagraphKey, c.OldText, c.NewText, c.CustomerDecision, c.CustomerText)).ToList());
            if (result.Errors.Count > 0) return new(ReviewOutcome.Invalid, id, Errors: result.Errors);
            var no = from + 1;
            await InsertVersion(cn, tx, id, no, "CUSTOMER", $"Bản {no} theo phản hồi của khách hàng", Clean(note), result.Paragraphs, actor);
            foreach (var c in result.Changes)
                await Exec(cn, tx, ContractReviewSql.InsertChange, ("@id", id), ("@from", from), ("@no", no), ("@key", c.Key), ("@findingRef", DBNull.Value),
                    ("@old", Db(c.OldText)), ("@new", Db(c.NewText)), ("@note", Db(c.Note)), ("@decision", ContractReviewPolicy.Agreed),
                    ("@decidedAt", DateTime.UtcNow), ("@decidedBy", actor));
            await Exec(cn, tx, ContractReviewSql.SetVersion, ("@status", ContractReviewPolicy.InReview), ("@no", no), ("@by", actor), ("@id", id));
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_CUSTOMER_VERSION", id, AuditSql.Json(("versionNo", from)),
                AuditSql.Json(("versionNo", no), ("changes", result.Changes.Count)), ip);
            return new(ReviewOutcome.Saved, id, no, AuditPersisted: written);
        });

    public Task<ReviewResult> FinalizeAsync(Guid actor, Guid id, int versionNo, string? stamp, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewFinalize, async (cn, tx) =>
        {
            var state = await Lock(cn, tx, id);
            if (state is null) return new(ReviewOutcome.NotFound);
            if (ContractReviewPolicy.IsLocked(state.Value.Status) || state.Value.Status == ContractReviewPolicy.Analyzing) return new(ReviewOutcome.Locked, id);
            if (!string.Equals(Convert.ToHexString(state.Value.RowVersion), stamp, StringComparison.Ordinal)) return new(ReviewOutcome.Conflict, id);
            if (versionNo < 0 || versionNo > state.Value.Version) return new(ReviewOutcome.Invalid, id);
            var paragraphs = await Paragraphs(cn, tx, id, versionNo);
            if (paragraphs.Count == 0) return new(ReviewOutcome.Invalid, id);
            var data = DocxWriter.Write(state.Value.Title, paragraphs);
            var name = ContractPolicy.SafeFileName($"{state.Value.Title} - ban cuoi v{versionNo}.docx");
            await Exec(cn, tx, ContractReviewSql.InsertFile, ("@file", Guid.NewGuid()), ("@id", id), ("@no", versionNo), ("@kind", "EXPORT"), ("@name", name),
                ("@type", DocxWriter.ContentType), ("@size", data.Length), ("@data", data), ("@by", actor));
            if (await Exec(cn, tx, ContractReviewSql.SetFinal, ("@no", versionNo), ("@by", actor), ("@id", id), ("@rv", state.Value.RowVersion)) != 1) return new(ReviewOutcome.Conflict, id);
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_FINALIZED", id, AuditSql.Json(("status", state.Value.Status)), AuditSql.Json(("finalVersionNo", versionNo)), ip);
            return new(ReviewOutcome.Saved, id, versionNo, AuditPersisted: written);
        });

    public Task<ReviewResult> CancelAsync(Guid actor, Guid id, string? stamp, string? ip) =>
        Write(actor, AdminPermissions.ContractReviewFinalize, async (cn, tx) =>
        {
            var state = await Lock(cn, tx, id);
            if (state is null) return new(ReviewOutcome.NotFound);
            if (ContractReviewPolicy.IsLocked(state.Value.Status)) return new(ReviewOutcome.Locked, id);
            if (!string.Equals(Convert.ToHexString(state.Value.RowVersion), stamp, StringComparison.Ordinal)) return new(ReviewOutcome.Conflict, id);
            if (await Exec(cn, tx, ContractReviewSql.SetCancelled, ("@by", actor), ("@id", id), ("@rv", state.Value.RowVersion)) != 1) return new(ReviewOutcome.Conflict, id);
            await Exec(cn, tx, ContractReviewSql.FailRun.Replace("WHERE Id=@run", "WHERE ReviewId=@id"), ("@error", "Yêu cầu đã bị hủy."), ("@id", id));
            var written = await Audit(cn, tx, actor, "CONTRACT_REVIEW_CANCELLED", id, AuditSql.Json(("status", state.Value.Status)), AuditSql.Json(("status", ContractReviewPolicy.Cancelled)), ip);
            return new(ReviewOutcome.Saved, id, AuditPersisted: written);
        });

    // ---- AI worker -------------------------------------------------------------------------------------------
    public async Task<ReviewRunJob?> ClaimRunAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        Guid run, review; int no; bool mask;
        await using (var cmd = new SqlCommand(ContractReviewSql.Claim, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return null;
            run = r.GetGuid(0); review = r.GetGuid(1); no = r.GetInt32(2); mask = r.GetBoolean(3);
        }
        string title, role; string? note;
        await using (var cmd = Cmd(cn, null, ContractReviewSql.RunReview, ("@id", review)))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) { await FailRunAsync(run, review, "Không tìm thấy yêu cầu chỉnh lý."); return null; }
            title = r.GetString(0); role = r.GetString(1); note = r.IsDBNull(2) ? null : r.GetString(2);
        }
        return new(run, review, no, mask, title, role, await Paragraphs(cn, null, review, no), note);
    }

    public async Task CompleteRunAsync(ReviewRunJob job, ReviewAiResult result)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var requestedBy = await Scalar(cn, tx, "SELECT RequestedBy FROM dbo.ContractReviewRuns WHERE Id=@run;", ("@run", job.RunId)) as Guid? ?? Guid.Empty;
        var status = await Scalar(cn, tx, "SELECT Status FROM dbo.ContractReviews WITH (UPDLOCK) WHERE Id=@id;", ("@id", job.ReviewId)) as string;
        var error = result.Dropped.Count == 0 ? null : ReviewText.Short("Bỏ qua " + result.Dropped.Count + " góp ý không hợp lệ: " + string.Join(" ", result.Dropped), 1000);
        if (await Exec(cn, tx, ContractReviewSql.CompleteRun, ("@model", result.Model), ("@input", (int)Math.Min(result.InputTokens, int.MaxValue)),
                ("@output", (int)Math.Min(result.OutputTokens, int.MaxValue)), ("@cost", result.CostUsd), ("@count", result.Findings.Count), ("@summary", Db(result.Summary)),
                ("@error", Db(error)), ("@json", result.RawJson), ("@run", job.RunId)) != 1 || status == ContractReviewPolicy.Cancelled)
        {
            await tx.RollbackAsync();
            return;
        }
        foreach (var f in result.Findings)
            await Exec(cn, tx, ContractReviewSql.InsertFinding, ("@finding", Guid.NewGuid()), ("@id", job.ReviewId), ("@run", job.RunId), ("@no", job.VersionNo),
                ("@key", f.ParagraphKey), ("@action", f.Action), ("@quote", Db(f.Quote)), ("@issue", f.Issue), ("@severity", f.Severity), ("@party", f.Party),
                ("@legal", Db(ContractReviewPrompt.LegalText(f.LegalRefs))), ("@verified", f.LegalVerified), ("@suggestion", Db(f.Suggestion)),
                ("@revised", Db(f.RevisedText)), ("@source", "AI"), ("@by", requestedBy));
        await Exec(cn, tx, ContractReviewSql.AfterRun, ("@id", job.ReviewId));
        await tx.CommitAsync();
    }

    public async Task FailRunAsync(Guid runId, Guid reviewId, string error)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await Exec(cn, tx, ContractReviewSql.FailRun, ("@error", ReviewText.Short(error, 1000)), ("@run", runId));
        await Exec(cn, tx, ContractReviewSql.AfterRun, ("@id", reviewId));
        await tx.CommitAsync();
    }

    public async Task ProgressAsync(Guid runId, long inputTokens, long outputTokens)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Cmd(cn, null, "UPDATE dbo.ContractReviewRuns SET InputTokens=@input,OutputTokens=@output WHERE Id=@run AND Status='RUNNING';",
            ("@input", (int)Math.Min(inputTokens, int.MaxValue)), ("@output", (int)Math.Min(outputTokens, int.MaxValue)), ("@run", runId));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> RecoverStaleRunsAsync(TimeSpan olderThan)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var reviews = new List<Guid>();
        await using (var cmd = Cmd(cn, null, ContractReviewSql.Stale, ("@before", DateTime.UtcNow - olderThan)))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) reviews.Add(r.GetGuid(0));
        foreach (var id in reviews.Distinct())
        {
            await using var cmd = Cmd(cn, null, ContractReviewSql.AfterRun, ("@id", id));
            await cmd.ExecuteNonQueryAsync();
        }
        return reviews.Count;
    }

    // ---- helpers --------------------------------------------------------------------------------------------
    private async Task<ReviewResult> Write(Guid actor, string permission, Func<SqlConnection, SqlTransaction, Task<ReviewResult>> body)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, permission)) return new(ReviewOutcome.Forbidden);
            var result = await body(cn, tx);
            if (result.Outcome == ReviewOutcome.Saved) await tx.CommitAsync();
            return result;
        }
        catch (AuditUnavailableException) { return new(ReviewOutcome.AuditUnavailable); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627) { return new(ReviewOutcome.Conflict); }
        catch (SqlException ex) when (ex.Number is 547) { return new(ReviewOutcome.Invalid); }
    }

    private static async Task<(string Status, int Version, byte[] RowVersion, string Title, string Role)?> Lock(SqlConnection cn, SqlTransaction tx, Guid id)
    {
        await using var cmd = Cmd(cn, tx, ContractReviewSql.Lock, ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.GetString(0), r.GetInt32(1), (byte[])r.GetValue(2), r.GetString(3), r.GetString(4));
    }

    private static async Task<string> NextCode(SqlConnection cn, SqlTransaction tx)
    {
        string prefix; long current; int padding;
        await using (var cmd = Cmd(cn, tx, ContractReviewSql.SequenceForUpdate))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) throw new InvalidOperationException("NumberSequences CONTRACT_REVIEW_CODE missing (migration 044).");
            prefix = r.IsDBNull(0) ? "CL" : r.GetString(0); current = Convert.ToInt64(r.GetValue(1)); padding = r.IsDBNull(2) ? 6 : Convert.ToInt32(r.GetValue(2));
        }
        for (var n = current + 1; ; n++)
        {
            var code = prefix + n.ToString().PadLeft(padding, '0');
            if (Convert.ToInt32(await Scalar(cn, tx, ContractReviewSql.CodeTaken, ("@code", code))) > 0) continue;
            await Exec(cn, tx, ContractReviewSql.SequenceAdvance, ("@n", n));
            return code;
        }
    }

    private static async Task InsertVersion(SqlConnection cn, SqlTransaction tx, Guid id, int no, string source, string label, string? note, IReadOnlyList<ReviewParagraph> paragraphs, Guid actor)
    {
        var version = Guid.NewGuid();
        await Exec(cn, tx, ContractReviewSql.InsertVersion, ("@version", version), ("@id", id), ("@no", no), ("@source", source), ("@label", label), ("@note", Db(note)), ("@by", actor));
        var seq = 0;
        foreach (var p in paragraphs)
            await Exec(cn, tx, ContractReviewSql.InsertParagraph, ("@version", version), ("@seq", ++seq), ("@key", p.Key), ("@kind", p.Kind), ("@text", p.Text));
    }

    private static async Task<IReadOnlyList<ReviewParagraph>> Paragraphs(SqlConnection cn, SqlTransaction? tx, Guid id, int no)
    {
        var list = new List<ReviewParagraph>();
        await using var cmd = Cmd(cn, tx, ContractReviewSql.Paragraphs, ("@id", id), ("@no", no));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetString(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    private static async Task<IReadOnlyList<ReviewFinding>> Findings(SqlConnection cn, SqlTransaction? tx, Guid id)
    {
        var list = new List<ReviewFinding>();
        await using var cmd = Cmd(cn, tx, ContractReviewSql.Findings, ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetInt32(1), r.GetString(2), r.GetString(3), S(r, 4), r.GetString(5), r.GetString(6), r.GetString(7), S(r, 8), r.GetBoolean(9),
                S(r, 10), S(r, 11), r.GetString(12), S(r, 13), S(r, 14), r.GetString(15), I(r, 16), r.GetDateTime(17), r.IsDBNull(18) ? null : r.GetDateTime(18), S(r, 19)));
        return list;
    }

    private static async Task<IReadOnlyList<ReviewChange>> Changes(SqlConnection cn, SqlTransaction? tx, Guid id)
    {
        var list = new List<ReviewChange>();
        await using var cmd = Cmd(cn, tx, ContractReviewSql.Changes, ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetGuid(4), S(r, 5), S(r, 6), S(r, 7), r.GetString(8),
                S(r, 9), S(r, 10), r.IsDBNull(11) ? null : r.GetDateTime(11)));
        return list;
    }

    private async Task<bool> Audit(SqlConnection cn, SqlTransaction tx, Guid actor, string action, Guid id, string oldValues, string newValues, string? ip) =>
        (await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, action, ContractReviewSql.Entity, id.ToString(), oldValues, newValues, ip)).Written;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : DocxParagraphs.Normalize(value);
    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static int? I(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    private static object Db(Guid? value) => value is Guid g ? g : DBNull.Value;

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Cmd(cn, tx, sql, args);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<int> Exec(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Cmd(cn, tx, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    // Only parameters the statement uses are added; @rv is a rowversion, byte[] a file, long text nvarchar(max).
    private static SqlCommand Cmd(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 60 };
        foreach (var (name, value) in args)
        {
            if (!Regex.IsMatch(sql, Regex.Escape(name) + @"\b")) continue;
            if (name == "@rv") cmd.Parameters.Add(name, SqlDbType.Timestamp).Value = value;
            else if (value is byte[] b) cmd.Parameters.Add(name, SqlDbType.VarBinary, -1).Value = b;
            else if (value is string s) cmd.Parameters.Add(name, SqlDbType.NVarChar, s.Length > 4000 ? -1 : 4000).Value = s;
            else if (value is decimal d) { var p = cmd.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 10; p.Scale = 4; p.Value = d; }
            else if (value is DateTime dt) cmd.Parameters.Add(name, SqlDbType.DateTime2).Value = dt;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }
}
