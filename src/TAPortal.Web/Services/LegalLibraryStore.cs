using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TAPortal.Web.Services;

public sealed record LegalDocumentItem(Guid Id, string DocumentNo, string Title, string DocType, DateTime? IssuedDate, DateTime? EffectiveDate, DateTime? ExpiredDate,
    string? Note, int ArticleCount, DateTime CreatedAt)
{
    public bool IsInForce(DateTime today) => (EffectiveDate is null || EffectiveDate <= today) && (ExpiredDate is null || ExpiredDate > today);
}

public sealed record LegalArticleItem(long Id, string ArticleNo, string? Title, string Content, DateTime? UpdatedAt);

public sealed record LegalFileInfo(Guid Id, string FileName, string ContentType, int SizeBytes, int ArticleCount, bool IsCurrent, string? Note, DateTime CreatedAt, string? CreatedByName);

public sealed record LegalDocumentDetail(LegalDocumentItem Document, IReadOnlyList<LegalArticleItem> Articles, IReadOnlyList<LegalFileInfo> Files);

// Where a cited article lives in the law library (for the citation links on the review page).
public sealed record LegalArticleLink(Guid DocumentId, long ArticleId, string DocumentNo, string DocumentTitle, string ArticleNo, string? ArticleTitle, string? SourceFile, string? SourceUrl = null);

// One citation of a finding ("Điều 418, khoản 2 – 91/2015/QH13: note"), parsed back from ReviewFinding.LegalBasis.
public sealed record LegalCitation(string Text, string? DocumentNo, string? ArticleNo, string? Clause)
{
    // "khoản 2, điểm a" → ("2", "a"), used to highlight the cited passage on the law page.
    public string? Khoan => Clause is null ? null : Regex.Match(Clause, @"^(?:khoản\s+)?(\d+)|khoản\s+(\d+)", RegexOptions.IgnoreCase) is { Success: true } m ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
    public string? Diem => Clause is null ? null : Regex.Match(Clause, @"điểm\s+([a-zđ])\b", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value.ToLowerInvariant() : null;

    private static readonly Regex Line = new(@"^(?<art>Điều\s+\d+[a-zđ]?)(?:,\s*(?<clause>[^–]+?))?\s+–\s+(?<doc>\S+)(?::\s*.*)?$", RegexOptions.Compiled);

    public string? Key => DocumentNo is null || ArticleNo is null ? null : LegalTextParser.NormalizeDocumentNo(DocumentNo) + "|" + ArticleNo.ToLowerInvariant();

    public static IReadOnlyList<LegalCitation> Parse(string? legalBasis) =>
        (legalBasis ?? "").Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line =>
        {
            var m = Line.Match(line);
            return m.Success ? new LegalCitation(line, m.Groups["doc"].Value, LegalTextParser.NormalizeArticle(m.Groups["art"].Value), m.Groups["clause"].Success ? m.Groups["clause"].Value.Trim() : null)
                             : new LegalCitation(line, null, null, null);
        }).ToList();
}

// An uploaded law file with the text read from it.
public sealed record LegalUpload(string FileName, string ContentType, byte[] Data, string Text);

public sealed class LegalDocumentInput
{
    public string? DocumentNo { get; set; }
    public string? Title { get; set; }
    public string? DocType { get; set; } = "LAW";
    public DateTime? IssuedDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public DateTime? ExpiredDate { get; set; }
    public string? Note { get; set; }
    // Full text to split into articles (or an uploaded file); empty on update keeps the current articles.
    public string? FullText { get; set; }

    public static LegalDocumentInput From(LegalDocumentItem d) => new()
    {
        DocumentNo = d.DocumentNo, Title = d.Title, DocType = d.DocType, IssuedDate = d.IssuedDate, EffectiveDate = d.EffectiveDate, ExpiredDate = d.ExpiredDate, Note = d.Note
    };

    public IReadOnlyList<(string Key, string Message)> Validate(bool creating, bool hasFile = false)
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
        if (!hasFile)
        {
            if (creating && string.IsNullOrWhiteSpace(FullText)) errors.Add(("Input.FullText", "Tải file văn bản lên hoặc dán toàn văn để tách thành các điều."));
            if (!string.IsNullOrWhiteSpace(FullText) && LegalTextParser.Split(FullText).Count == 0)
                errors.Add(("Input.FullText", "Không tìm thấy điều nào. Mỗi điều phải bắt đầu bằng dòng \"Điều 1.\", \"Điều 2.\"..."));
        }
        return errors;
    }
}

public static class LegalLibraryPolicy
{
    public static readonly IReadOnlyList<ReviewOption> Types =
        [new("CODE", "Bộ luật"), new("LAW", "Luật"), new("DECREE", "Nghị định"), new("CIRCULAR", "Thông tư"), new("OTHER", "Văn bản khác")];

    // Suggested starting set for contract work; shown as a hint on the library page.
    public static readonly IReadOnlyList<string> Suggested =
    [
        "Bộ luật Dân sự 2015 (91/2015/QH13)", "Luật Thương mại 2005 (36/2005/QH11)", "Bộ luật Lao động 2019 (45/2019/QH14)",
        "Luật Doanh nghiệp 2020 (59/2020/QH14)", "Luật Kinh doanh bất động sản 2023 (29/2023/QH15)", "Luật Quản lý thuế 2025 (108/2025/QH15)",
        "Nghị định 123/2020/NĐ-CP về hóa đơn, chứng từ"
    ];

    // "tách được 689 điều" plus the article numbers missing between 1 and the last one (a sign of a damaged file).
    public static string Summary(IReadOnlyList<LegalArticleDraft> articles)
    {
        var missing = LegalTextParser.Missing(articles);
        var text = $"tách được {articles.Count} điều";
        if (missing.Count > 0) text += $"; thiếu {missing.Count} điều ({string.Join(", ", missing.Take(15))}{(missing.Count > 15 ? ", …" : "")}) – kiểm tra lại file";
        return text;
    }
}

// Text of an uploaded law file. Word .docx (paragraphs, without third-party code), PDF with a text layer (PdfPig) and plain
// text are read here on the server; no AI is involved. Scanned PDFs (images only) and old .doc files are refused.
public static class LegalFileText
{
    public const int MaxBytes = 20 * 1024 * 1024;   // CK_LegalDocumentFiles_Size
    public const string Accept = ".docx,.pdf,.txt,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/pdf,text/plain";

    public static (LegalUpload? Upload, string? Error) Read(string? fileName, byte[]? data)
    {
        if (data is null || data.Length == 0) return (null, "Chọn file văn bản.");
        if (data.Length > MaxBytes) return (null, "File vượt quá 20 MB.");
        var name = ContractPolicy.SafeFileName(fileName);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        string text, type;
        try
        {
            switch (ext)
            {
                case ".docx":
                    if (ContractPolicy.CheckFile(name, data).ContentType is null && data.Length <= ContractPolicy.MaxFileBytes) return (null, "Nội dung file không khớp định dạng DOCX.");
                    text = string.Join("\n", DocxParagraphs.Read(data).Select(p => p.Text));
                    type = DocxWriter.ContentType;
                    break;
                case ".pdf":
                    if (data.Length < 5 || Encoding.ASCII.GetString(data, 0, 5) != "%PDF-") return (null, "Nội dung file không khớp định dạng PDF.");
                    text = Pdf(data);
                    type = "application/pdf";
                    if (text.Count(char.IsLetter) < 200) return (null, "PDF không có lớp chữ (bản scan/chụp ảnh). Hãy tải bản Word hoặc PDF có chữ từ vbpl.vn.");
                    break;
                case ".txt":
                    text = Encoding.UTF8.GetString(data).TrimStart('﻿');
                    type = "text/plain";
                    break;
                case ".doc":
                    return (null, "File .doc (Word cũ) chưa đọc được: mở bằng Word rồi \"Lưu thành\" .docx.");
                default:
                    return (null, "Chỉ nhận file Word .docx, PDF có chữ hoặc .txt, tối đa 20 MB.");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return (null, "Không đọc được nội dung file. Hãy mở file và lưu lại, hoặc dùng bản Word.");
        }
        if (LegalTextParser.Split(text).Count == 0)
            return (null, "Đọc được file nhưng không tìm thấy dòng \"Điều 1.\", \"Điều 2.\"... Nếu là PDF, chữ có thể bị lỗi phông: hãy dùng bản Word.");
        return (new(name, type, data, text), null);
    }

    // Lines in reading order. Page numbers and gazette page headers ("CÔNG BÁO/Số 43 + 44/Ngày 08-01-2024 21") are dropped,
    // and lines wrapped in the middle of a sentence are joined back into one paragraph.
    private static string Pdf(byte[] data)
    {
        var lines = new List<string>();
        using (var pdf = PdfDocument.Open(data))
            foreach (var page in pdf.GetPages())
                foreach (var line in ContentOrderTextExtractor.GetText(page).Replace("\r\n", "\n").Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || Regex.IsMatch(t, @"^(Trang\s*)?\d{1,4}(\s*/\s*\d{1,4})?$") || Regex.IsMatch(t, @"^(\d+\s+)?CÔNG BÁO\s*/", RegexOptions.IgnoreCase)) continue;
                    lines.Add(t.Normalize(NormalizationForm.FormC));
                }
        return DocxParagraphs.Normalize(JoinWrapped(lines));
    }

    // A line continues the previous one unless the previous ends a sentence or clause, or the line starts a new item
    // (Điều/Chương/Mục heading, "1.", "a)", "-", "+") or is an upper-case heading.
    public static string JoinWrapped(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        string? prev = null;
        foreach (var t in lines)
        {
            var newItem = Regex.IsMatch(t, @"^(Điều|ĐIỀU|Chương|CHƯƠNG|Mục|MỤC|Phần|PHẦN)\s+\S+|^\d+[\.\)]\s|^\d+\.\d+|^[a-zđ][\)\.]\s|^[-+•–]\s")
                          || (t.Count(char.IsLetter) >= 4 && t.Where(char.IsLetter).All(char.IsUpper));
            var prevEnds = prev is null || Regex.IsMatch(prev, @"[\.:;!?…”""]$") || (prev.Count(char.IsLetter) >= 4 && prev.Where(char.IsLetter).All(char.IsUpper))
                           || Regex.IsMatch(prev, @"^(Điều|ĐIỀU)\s+\d+");
            if (prev is not null) sb.Append(newItem || prevEnds ? '\n' : ' ');
            sb.Append(t);
            prev = t;
        }
        return sb.ToString();
    }
}

public enum LegalOutcome { Saved, NotFound, Forbidden, Invalid, Duplicate, AuditUnavailable }

public interface ILegalLibraryStore
{
    Task<IReadOnlyList<LegalDocumentItem>> ListAsync();
    Task<LegalDocumentDetail?> GetAsync(Guid id);
    Task<IReadOnlyList<LegalIndexArticle>> IndexAsync(DateTime today);
    Task<(LegalOutcome Outcome, Guid? Id, string? Summary)> SaveAsync(Guid actor, Guid? id, LegalDocumentInput input, LegalUpload? upload, string? ip);
    Task<(LegalOutcome Outcome, string? Summary)> ReimportAsync(Guid actor, Guid id, Guid fileId, string? ip);
    Task<LegalOutcome> UpdateArticleAsync(Guid actor, Guid id, long articleId, string? title, string? content, string? ip);
    Task<LegalOutcome> DeleteArticleAsync(Guid actor, Guid id, long articleId, string? ip);
    Task<LegalOutcome> DeleteAsync(Guid actor, Guid id, string? ip);
    Task<ReviewFileContent?> DownloadAsync(Guid id, Guid fileId);
    // Library entries of the cited articles, keyed by LegalCitation.Key (citations not in the library are absent).
    // Default (no links) keeps other implementations of this interface (test fakes) compiling.
    Task<IReadOnlyDictionary<string, LegalArticleLink>> ResolveAsync(IEnumerable<LegalCitation> citations) =>
        Task.FromResult<IReadOnlyDictionary<string, LegalArticleLink>>(new Dictionary<string, LegalArticleLink>());
}

// Law library (dbo.LegalDocuments, dbo.LegalArticles of migration 044; dbo.LegalDocumentFiles of 046). Writes need
// LEGAL.LIBRARY.MANAGE (checked in SQL) and are audited; documents are soft-deleted. A new text or file replaces the
// document's articles; every uploaded file is kept and the one in use is marked current, so an older file can be re-imported.
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
        await using (var cmd = Cmd(cn, null, $"SELECT {Columns} FROM dbo.LegalDocuments d WHERE d.Id=@id AND d.IsDeleted=0;", ("@id", id)))
        await using (var r = await cmd.ExecuteReaderAsync())
            if (await r.ReadAsync()) doc = Item(r);
        if (doc is null) return null;
        var articles = new List<LegalArticleItem>();
        await using (var cmd = Cmd(cn, null, "SELECT Id,ArticleNo,Title,Content,UpdatedAt FROM dbo.LegalArticles WHERE DocumentId=@id ORDER BY Seq;", ("@id", id)))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) articles.Add(new(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetDateTime(4)));
        var files = new List<LegalFileInfo>();
        await using (var cmd = Cmd(cn, null, """
SELECT f.Id,f.FileName,f.ContentType,f.SizeBytes,f.ArticleCount,f.IsCurrent,f.Note,f.CreatedAt,COALESCE(u.DisplayName,u.Username)
FROM dbo.LegalDocumentFiles f LEFT JOIN dbo.Users u ON u.Id=f.CreatedBy WHERE f.DocumentId=@id ORDER BY f.CreatedAt DESC;
""", ("@id", id)))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                files.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetBoolean(5), r.IsDBNull(6) ? null : r.GetString(6),
                    r.GetDateTime(7), r.IsDBNull(8) ? null : r.GetString(8)));
        return new(doc, articles, files);
    }

    // Articles of documents in force today, for the AI prompt (titles only) and citation checks.
    public async Task<IReadOnlyList<LegalIndexArticle>> IndexAsync(DateTime today)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Cmd(cn, null, """
SELECT d.DocumentNo,d.Title,a.ArticleNo,a.Title FROM dbo.LegalArticles a JOIN dbo.LegalDocuments d ON d.Id=a.DocumentId
WHERE d.IsDeleted=0 AND (d.EffectiveDate IS NULL OR d.EffectiveDate<=@today) AND (d.ExpiredDate IS NULL OR d.ExpiredDate>@today)
ORDER BY d.DocType,d.DocumentNo,a.Seq;
""", ("@today", today.Date));
        var list = new List<LegalIndexArticle>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }

    public Task<(LegalOutcome Outcome, Guid? Id, string? Summary)> SaveAsync(Guid actor, Guid? id, LegalDocumentInput input, LegalUpload? upload, string? ip) =>
        Write<(LegalOutcome Outcome, Guid? Id, string? Summary)>(actor, r => r.Outcome, async (cn, tx) =>
        {
            if (input.Validate(id is null, upload is not null).Count > 0) return (LegalOutcome.Invalid, id, null);
            var target = id ?? Guid.NewGuid();
            if (Convert.ToInt32(await Scalar(cn, tx, "SELECT COUNT(1) FROM dbo.LegalDocuments WITH (UPDLOCK, HOLDLOCK) WHERE DocumentNo=@no AND IsDeleted=0 AND Id<>@id;",
                    ("@no", input.DocumentNo!), ("@id", target))) > 0) return (LegalOutcome.Duplicate, id, null);
            var values = new (string, object)[]
            {
                ("@id", target), ("@no", input.DocumentNo!), ("@title", input.Title!), ("@type", input.DocType!), ("@issued", Date(input.IssuedDate)),
                ("@effective", Date(input.EffectiveDate)), ("@expired", Date(input.ExpiredDate)), ("@note", (object?)input.Note ?? DBNull.Value), ("@by", actor)
            };
            if (id is null)
                await Exec(cn, tx, "INSERT dbo.LegalDocuments(Id,DocumentNo,Title,DocType,IssuedDate,EffectiveDate,ExpiredDate,Note,CreatedBy) VALUES(@id,@no,@title,@type,@issued,@effective,@expired,@note,@by);", values);
            else if (await Exec(cn, tx, "UPDATE dbo.LegalDocuments SET DocumentNo=@no,Title=@title,DocType=@type,IssuedDate=@issued,EffectiveDate=@effective,ExpiredDate=@expired,Note=@note,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;", values) != 1)
                return (LegalOutcome.NotFound, id, null);
            string? summary = null;
            var text = upload?.Text ?? input.FullText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var articles = LegalTextParser.Split(text);
                await ReplaceArticles(cn, tx, target, articles);
                summary = LegalLibraryPolicy.Summary(articles);
                if (upload is not null) await AddFile(cn, tx, target, upload, articles.Count, actor);
                else await Exec(cn, tx, "UPDATE dbo.LegalDocumentFiles SET IsCurrent=0 WHERE DocumentId=@id;", ("@id", target));   // pasted text replaces the file
            }
            await Audit(cn, tx, actor, id is null ? "LEGAL_DOCUMENT_CREATED" : "LEGAL_DOCUMENT_UPDATED", target,
                AuditSql.Json(("documentNo", input.DocumentNo), ("type", input.DocType), ("file", upload?.FileName), ("articlesReplaced", text is not null)), ip);
            return (LegalOutcome.Saved, target, summary);
        }, o => (o, id, null));

    public Task<(LegalOutcome Outcome, string? Summary)> ReimportAsync(Guid actor, Guid id, Guid fileId, string? ip) =>
        Write<(LegalOutcome Outcome, string? Summary)>(actor, r => r.Outcome, async (cn, tx) =>
        {
            string name; byte[] data;
            await using (var cmd = Cmd(cn, tx, """
SELECT f.FileName,f.Data FROM dbo.LegalDocumentFiles f JOIN dbo.LegalDocuments d WITH (UPDLOCK) ON d.Id=f.DocumentId
WHERE f.Id=@file AND f.DocumentId=@id AND d.IsDeleted=0;
""", ("@file", fileId), ("@id", id)))
            await using (var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
            {
                if (!await r.ReadAsync()) return (LegalOutcome.NotFound, null);
                name = r.GetString(0); data = (byte[])r.GetValue(1);
            }
            var (upload, _) = LegalFileText.Read(name, data);
            if (upload is null) return (LegalOutcome.Invalid, null);
            var articles = LegalTextParser.Split(upload.Text);
            await ReplaceArticles(cn, tx, id, articles);
            await Exec(cn, tx, "UPDATE dbo.LegalDocumentFiles SET IsCurrent=CASE WHEN Id=@file THEN 1 ELSE 0 END,ArticleCount=CASE WHEN Id=@file THEN @count ELSE ArticleCount END WHERE DocumentId=@id;",
                ("@file", fileId), ("@count", articles.Count), ("@id", id));
            await Exec(cn, tx, "UPDATE dbo.LegalDocuments SET UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id;", ("@by", actor), ("@id", id));
            await Audit(cn, tx, actor, "LEGAL_DOCUMENT_REIMPORTED", id, AuditSql.Json(("fileId", fileId), ("articles", articles.Count)), ip);
            return (LegalOutcome.Saved, LegalLibraryPolicy.Summary(articles));
        }, o => (o, null));

    public Task<LegalOutcome> UpdateArticleAsync(Guid actor, Guid id, long articleId, string? title, string? content, string? ip) =>
        Write(actor, o => o, async (cn, tx) =>
        {
            var text = DocxParagraphs.Normalize(content ?? "");
            var head = string.IsNullOrWhiteSpace(title) ? null : Regex.Replace(title.Trim(), @"\s+", " ");
            if (text.Length == 0 || head?.Length > 500) return LegalOutcome.Invalid;
            if (await Exec(cn, tx, """
UPDATE a SET Title=@title,Content=@content,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by FROM dbo.LegalArticles a JOIN dbo.LegalDocuments d ON d.Id=a.DocumentId
WHERE a.Id=@article AND a.DocumentId=@id AND d.IsDeleted=0;
""", ("@title", (object?)head ?? DBNull.Value), ("@content", text), ("@by", actor), ("@article", articleId), ("@id", id)) != 1) return LegalOutcome.NotFound;
            await Audit(cn, tx, actor, "LEGAL_ARTICLE_UPDATED", id, AuditSql.Json(("articleId", articleId), ("length", text.Length)), ip);
            return LegalOutcome.Saved;
        }, o => o);

    public Task<LegalOutcome> DeleteArticleAsync(Guid actor, Guid id, long articleId, string? ip) =>
        Write(actor, o => o, async (cn, tx) =>
        {
            var no = await Scalar(cn, tx, "SELECT a.ArticleNo FROM dbo.LegalArticles a JOIN dbo.LegalDocuments d ON d.Id=a.DocumentId WHERE a.Id=@article AND a.DocumentId=@id AND d.IsDeleted=0;",
                ("@article", articleId), ("@id", id)) as string;
            if (no is null || await Exec(cn, tx, "DELETE FROM dbo.LegalArticles WHERE Id=@article AND DocumentId=@id;", ("@article", articleId), ("@id", id)) != 1) return LegalOutcome.NotFound;
            await Audit(cn, tx, actor, "LEGAL_ARTICLE_DELETED", id, AuditSql.Json(("articleNo", no)), ip);
            return LegalOutcome.Saved;
        }, o => o);

    public Task<LegalOutcome> DeleteAsync(Guid actor, Guid id, string? ip) =>
        Write(actor, o => o, async (cn, tx) =>
        {
            if (await Exec(cn, tx, "UPDATE dbo.LegalDocuments SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@id AND IsDeleted=0;", ("@id", id), ("@by", actor)) != 1)
                return LegalOutcome.NotFound;
            await Audit(cn, tx, actor, "LEGAL_DOCUMENT_SOFT_DELETED", id, AuditSql.Json(("isDeleted", true)), ip);
            return LegalOutcome.Saved;
        }, o => o);

    public async Task<ReviewFileContent?> DownloadAsync(Guid id, Guid fileId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = Cmd(cn, null, """
SELECT f.FileName,f.ContentType,f.Data FROM dbo.LegalDocumentFiles f JOIN dbo.LegalDocuments d ON d.Id=f.DocumentId
WHERE f.Id=@file AND f.DocumentId=@id AND d.IsDeleted=0;
""", ("@file", fileId), ("@id", id));
        await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        if (!await r.ReadAsync()) return null;
        var name = r.GetString(0); var type = r.GetString(1); var data = (byte[])r.GetValue(2);
        return new(name, type, data);
    }

    public async Task<IReadOnlyDictionary<string, LegalArticleLink>> ResolveAsync(IEnumerable<LegalCitation> citations)
    {
        var docs = citations.Where(c => c.Key is not null).Select(c => LegalTextParser.NormalizeDocumentNo(c.DocumentNo)).Distinct().Take(50).ToList();
        var result = new Dictionary<string, LegalArticleLink>(StringComparer.Ordinal);
        if (docs.Count == 0) return result;
        await using var cn = Connection();
        await cn.OpenAsync();
        var names = docs.Select((_, i) => "@d" + i).ToList();
        await using var cmd = new SqlCommand($"""
SELECT d.Id,d.DocumentNo,d.Title,a.Id,a.ArticleNo,a.Title,
 (SELECT TOP 1 f.FileName FROM dbo.LegalDocumentFiles f WHERE f.DocumentId=d.Id AND f.IsCurrent=1),
 CASE WHEN COL_LENGTH(N'dbo.LegalDocuments', N'SourceUrl') IS NULL THEN NULL ELSE d.SourceUrl END
FROM dbo.LegalArticles a JOIN dbo.LegalDocuments d ON d.Id=a.DocumentId
WHERE d.IsDeleted=0 AND UPPER(REPLACE(d.DocumentNo,' ','')) IN ({string.Join(",", names)});
""", cn);
        for (var i = 0; i < docs.Count; i++) cmd.Parameters.Add(names[i], SqlDbType.NVarChar, 50).Value = docs[i];
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var link = new LegalArticleLink(r.GetGuid(0), r.GetInt64(3), r.GetString(1), r.GetString(2), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : Convert.ToString(r.GetValue(7)));
            result[LegalTextParser.NormalizeDocumentNo(link.DocumentNo) + "|" + link.ArticleNo.ToLowerInvariant()] = link;
        }
        return result;
    }

    // ---- helpers --------------------------------------------------------------------------------------------
    // Permission check (in SQL) + transaction; commits only when the body reports Saved.
    private async Task<T> Write<T>(Guid actor, Func<T, LegalOutcome> outcome, Func<SqlConnection, SqlTransaction, Task<T>> body, Func<LegalOutcome, T> failed)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.LegalLibraryManage)) return failed(LegalOutcome.Forbidden);
            var result = await body(cn, tx);
            if (outcome(result) == LegalOutcome.Saved) await tx.CommitAsync();
            return result;
        }
        catch (AuditUnavailableException) { return failed(LegalOutcome.AuditUnavailable); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return failed(LegalOutcome.Duplicate); }
    }

    private static async Task ReplaceArticles(SqlConnection cn, SqlTransaction tx, Guid id, IReadOnlyList<LegalArticleDraft> articles)
    {
        await Exec(cn, tx, "DELETE FROM dbo.LegalArticles WHERE DocumentId=@id;", ("@id", id));
        var seq = 0;
        foreach (var a in articles)
            await Exec(cn, tx, "INSERT dbo.LegalArticles(DocumentId,Seq,ArticleNo,Title,Content) VALUES(@id,@seq,@no,@title,@content);",
                ("@id", id), ("@seq", ++seq), ("@no", a.ArticleNo), ("@title", a.Title is null ? DBNull.Value : ReviewText.Short(a.Title, 500)), ("@content", a.Content));
    }

    private static async Task AddFile(SqlConnection cn, SqlTransaction tx, Guid id, LegalUpload upload, int articles, Guid actor)
    {
        await Exec(cn, tx, "UPDATE dbo.LegalDocumentFiles SET IsCurrent=0 WHERE DocumentId=@id;", ("@id", id));
        await Exec(cn, tx, "INSERT dbo.LegalDocumentFiles(Id,DocumentId,FileName,ContentType,SizeBytes,Data,ArticleCount,IsCurrent,CreatedBy) VALUES(NEWID(),@id,@name,@type,@size,@data,@count,1,@by);",
            ("@id", id), ("@name", upload.FileName), ("@type", upload.ContentType), ("@size", upload.Data.Length), ("@data", upload.Data), ("@count", articles), ("@by", actor));
    }

    private Task Audit(SqlConnection cn, SqlTransaction tx, Guid actor, string action, Guid id, string newValues, string? ip) =>
        AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, action, "dbo.LegalDocuments", id.ToString(), "{}", newValues, ip);

    private static LegalDocumentItem Item(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), D(r, 4), D(r, 5), D(r, 6),
        r.IsDBNull(7) ? null : r.GetString(7), r.GetInt32(8), r.GetDateTime(9));

    private static DateTime? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    private static object Date(DateTime? d) => d is DateTime x ? x.Date : DBNull.Value;

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Cmd(cn, tx, sql, args);
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    private static async Task<int> Exec(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Cmd(cn, tx, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static SqlCommand Cmd(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 120 };
        foreach (var (name, value) in args)
        {
            if (value is string s) cmd.Parameters.Add(name, SqlDbType.NVarChar, s.Length > 4000 ? -1 : 4000).Value = s;
            else if (value is byte[] b) cmd.Parameters.Add(name, SqlDbType.VarBinary, -1).Value = b;
            else if (name is "@issued" or "@effective" or "@expired" or "@today") cmd.Parameters.Add(name, SqlDbType.Date).Value = value;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }
}
