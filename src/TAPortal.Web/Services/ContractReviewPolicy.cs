using System.Text.RegularExpressions;

namespace TAPortal.Web.Services;

public sealed record ReviewOption(string Code, string Label);

// A finding as the editing rules see it (staff text wins over the AI proposal).
public sealed record ReviewEdit(Guid Id, string ParagraphKey, string Action, string? Quote, string? Replacement, string Note);

public sealed record ReviewChangeDraft(string Key, string? OldText, string? NewText, string Note, IReadOnlyList<Guid> FindingIds);

public sealed record ReviewApplyResult(IReadOnlyList<ReviewParagraph> Paragraphs, IReadOnlyList<ReviewChangeDraft> Changes, IReadOnlyList<string> Errors);

// Customer answer to one change when building the next version from the customer's reply.
public sealed record ReviewCustomerAnswer(string Key, string? OldText, string? NewText, string Decision, string? CustomerText);

// Rules of "Hợp đồng dịch vụ": statuses, labels, how accepted findings become the next version, and how a customer reply
// becomes the version after that. Pure functions (no SQL) so the Safe tests cover them directly.
public static class ContractReviewPolicy
{
    public const string New = "NEW", Analyzing = "ANALYZING", InReview = "IN_REVIEW", DraftReady = "DRAFT_READY", Sent = "SENT",
        CustomerReplied = "CUSTOMER_REPLIED", Finalized = "FINALIZED", Cancelled = "CANCELLED";

    public const string Open = "OPEN", Accepted = "ACCEPTED", Edited = "EDITED", Rejected = "REJECTED";
    public const string Pending = "PENDING", Agreed = "AGREED", Declined = "DECLINED", Counter = "COUNTER";
    public const string Replace = "REPLACE", Insert = "INSERT", Delete = "DELETE", NoteOnly = "NOTE";
    public const int MaxFileBytes = 10 * 1024 * 1024;   // CK_ContractReviewFiles_Size

    public static readonly IReadOnlyList<ReviewOption> Statuses =
    [
        new(New, "Mới tạo"), new(Analyzing, "AI đang rà soát"), new(InReview, "Đang xử lý góp ý"), new(DraftReady, "Đã có bản sửa"),
        new(Sent, "Đã gửi khách hàng"), new(CustomerReplied, "Khách hàng đã phản hồi"), new(Finalized, "Đã chốt bản cuối"), new(Cancelled, "Đã hủy")
    ];

    public static readonly IReadOnlyList<ReviewOption> Roles =
        [new("A", "Bên A"), new("B", "Bên B"), new("BOTH", "Cả hai bên (trung lập)")];

    public static readonly IReadOnlyList<ReviewOption> Severities = [new("HIGH", "Nghiêm trọng"), new("MEDIUM", "Cần lưu ý"), new("LOW", "Góp ý nhỏ")];

    public static readonly IReadOnlyList<ReviewOption> Actions =
        [new(Replace, "Sửa câu chữ"), new(Insert, "Thêm đoạn mới sau đoạn này"), new(Delete, "Bỏ đoạn này"), new(NoteOnly, "Chỉ ghi chú")];

    public static readonly IReadOnlyList<ReviewOption> FindingStatuses =
        [new(Open, "Chưa xử lý"), new(Accepted, "Chấp nhận"), new(Edited, "Chấp nhận có sửa"), new(Rejected, "Bỏ qua")];

    public static readonly IReadOnlyList<ReviewOption> CustomerDecisions =
        [new(Pending, "Chờ khách hàng"), new(Agreed, "Khách đồng ý"), new(Declined, "Khách không đồng ý"), new(Counter, "Khách đề xuất câu khác")];

    public static string Label(IReadOnlyList<ReviewOption> options, string? code) => options.FirstOrDefault(o => o.Code == code)?.Label ?? code ?? "—";
    public static bool Is(IReadOnlyList<ReviewOption> options, string? code) => code is not null && options.Any(o => o.Code == code);

    public static string StatusBadge(string? status) => status switch
    {
        Finalized => "ta-badge--success",
        Cancelled => "ta-badge--danger",
        Analyzing or CustomerReplied => "ta-badge--warning",
        DraftReady or Sent => "ta-badge--info",
        _ => "ta-badge--neutral"
    };

    public static string SeverityBadge(string? severity) => severity switch
    {
        "HIGH" => "ta-badge--danger",
        "MEDIUM" => "ta-badge--warning",
        _ => "ta-badge--neutral"
    };

    public static bool IsLocked(string status) => status is Finalized or Cancelled;
    public static bool CanRunAi(string status) => status is New or InReview or CustomerReplied or DraftReady;

    public static string RoleInstruction(string role) => role switch
    {
        "A" => "Rà soát để bảo vệ quyền lợi của BÊN A (khách hàng của T.A là Bên A).",
        "B" => "Rà soát để bảo vệ quyền lợi của BÊN B (khách hàng của T.A là Bên B).",
        _ => "Rà soát trung lập, cân bằng quyền lợi của CẢ HAI BÊN."
    };

    // ---- Next version from decided findings --------------------------------------------------------------------
    // REPLACE swaps the quoted text (the whole paragraph when the quote is empty) for the replacement; several replacements
    // in one paragraph are applied when their quotes do not overlap. INSERT adds a new paragraph after the target, DELETE
    // removes it, NOTE changes nothing. nextInsertNo numbers inserted paragraphs N<n> so keys stay unique in the review.
    public static ReviewApplyResult Apply(IReadOnlyList<ReviewParagraph> current, IReadOnlyList<ReviewEdit> edits, int nextInsertNo)
    {
        var errors = new List<string>();
        var byKey = current.ToDictionary(p => p.Key);
        var text = current.ToDictionary(p => p.Key, p => p.Text);
        var notes = new Dictionary<string, List<string>>();
        var ids = new Dictionary<string, List<Guid>>();
        var deleted = new HashSet<string>();
        var inserts = new Dictionary<string, List<ReviewParagraph>>();

        void Track(string key, ReviewEdit e)
        {
            (notes.TryGetValue(key, out var n) ? n : notes[key] = []).Add(e.Note);
            (ids.TryGetValue(key, out var l) ? l : ids[key] = []).Add(e.Id);
        }

        foreach (var group in edits.Where(e => e.Action != NoteOnly).GroupBy(e => e.ParagraphKey))
        {
            if (!byKey.ContainsKey(group.Key)) { errors.Add($"Góp ý trỏ tới đoạn {group.Key} không còn trong phiên bản hiện tại."); continue; }
            var original = text[group.Key];
            var spans = new List<(int Start, int Length, string With, ReviewEdit Edit)>();
            foreach (var e in group)
            {
                switch (e.Action)
                {
                    case Delete:
                        deleted.Add(group.Key); Track(group.Key, e); break;
                    case Insert:
                        var added = Clean(e.Replacement);
                        if (added.Length == 0) { errors.Add($"Góp ý thêm đoạn sau {group.Key} chưa có nội dung."); break; }
                        var p = new ReviewParagraph("N" + nextInsertNo++, ReviewText.LooksLikeHeading(added) ? "HEADING" : "TEXT", added);
                        (inserts.TryGetValue(group.Key, out var list) ? list : inserts[group.Key] = []).Add(p);
                        Track(p.Key, e);
                        break;
                    case Replace:
                        var quote = e.Quote ?? "";
                        var start = quote.Length == 0 ? 0 : original.IndexOf(quote, StringComparison.Ordinal);
                        if (start < 0) { errors.Add($"Không tìm thấy câu được trích trong đoạn {group.Key}: \"{ReviewText.Short(quote, 80)}\"."); break; }
                        spans.Add((start, quote.Length == 0 ? original.Length : quote.Length, e.Replacement ?? "", e));
                        break;
                }
            }
            if (deleted.Contains(group.Key) && spans.Count > 0) { errors.Add($"Đoạn {group.Key} vừa bị bỏ vừa được sửa; chỉ giữ một trong hai."); continue; }
            spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (var i = 1; i < spans.Count; i++)
                if (spans[i].Start < spans[i - 1].Start + spans[i - 1].Length) { errors.Add($"Đoạn {group.Key} có hai góp ý sửa chồng lên cùng một câu; hãy gộp lại thành một."); spans.Clear(); break; }
            if (spans.Count == 0) continue;
            var result = original;
            foreach (var s in Enumerable.Reverse(spans)) result = result[..s.Start] + s.With + result[(s.Start + s.Length)..];
            text[group.Key] = Clean(result);
            foreach (var s in spans) Track(group.Key, s.Edit);
            if (text[group.Key].Length == 0) deleted.Add(group.Key);
        }

        var paragraphs = new List<ReviewParagraph>();
        var changes = new List<ReviewChangeDraft>();
        foreach (var p in current)
        {
            if (deleted.Contains(p.Key)) changes.Add(new(p.Key, p.Text, null, Join(notes, p.Key), ids.GetValueOrDefault(p.Key) ?? []));
            else
            {
                var t = text[p.Key];
                paragraphs.Add(p with { Text = t });
                if (t != p.Text) changes.Add(new(p.Key, p.Text, t, Join(notes, p.Key), ids.GetValueOrDefault(p.Key) ?? []));
            }
            foreach (var added in inserts.GetValueOrDefault(p.Key) ?? [])
            {
                paragraphs.Add(added);
                changes.Add(new(added.Key, null, added.Text, Join(notes, added.Key), ids.GetValueOrDefault(added.Key) ?? []));
            }
        }
        return new(paragraphs, changes, errors);
    }

    // ---- Version from the customer's reply ----------------------------------------------------------------------
    // AGREED keeps the change, DECLINED restores the text before it (an inserted paragraph disappears, a deleted one comes
    // back after its former predecessor), COUNTER takes the customer's wording. PENDING answers are not allowed here.
    public static ReviewApplyResult ApplyCustomer(IReadOnlyList<ReviewParagraph> current, IReadOnlyList<ReviewParagraph> before, IReadOnlyList<ReviewCustomerAnswer> answers)
    {
        var errors = answers.Where(a => a.Decision == Pending).Select(a => $"Đoạn {a.Key} chưa ghi nhận ý kiến của khách hàng.").ToList();
        if (errors.Count > 0) return new(current, [], errors);
        var list = current.ToList();
        var changes = new List<ReviewChangeDraft>();
        foreach (var a in answers.Where(a => a.Decision is Declined or Counter))
        {
            var idx = list.FindIndex(p => p.Key == a.Key);
            var now = idx >= 0 ? list[idx].Text : null;
            string? target = a.Decision == Counter ? Clean(a.CustomerText) : a.OldText;
            if (a.Decision == Counter && string.IsNullOrEmpty(target)) { errors.Add($"Đoạn {a.Key}: chưa nhập câu chữ khách hàng đề xuất."); continue; }
            if (string.IsNullOrEmpty(target))
            {
                if (idx >= 0) list.RemoveAt(idx);
            }
            else if (idx >= 0) list[idx] = list[idx] with { Text = target };
            else
            {
                // Paragraph deleted in the current version: put it back after the nearest paragraph that preceded it.
                var oldIdx = before.ToList().FindIndex(p => p.Key == a.Key);
                var kind = oldIdx >= 0 ? before[oldIdx].Kind : "TEXT";
                var at = 0;
                for (var i = oldIdx - 1; i >= 0; i--)
                {
                    var prev = list.FindIndex(p => p.Key == before[i].Key);
                    if (prev >= 0) { at = prev + 1; break; }
                }
                list.Insert(at, new ReviewParagraph(a.Key, kind, target));
            }
            if (now != target)
                changes.Add(new(a.Key, now, string.IsNullOrEmpty(target) ? null : target,
                    a.Decision == Counter ? "Khách hàng đề xuất câu chữ khác." : "Khách hàng không đồng ý, giữ nguyên câu chữ trước khi sửa.", []));
        }
        return new(list, changes, errors);
    }

    private static string Join(Dictionary<string, List<string>> notes, string key) =>
        notes.TryGetValue(key, out var n) ? string.Join("\n", n.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()) : "";

    public static string Clean(string? value) => DocxParagraphs.Normalize(value ?? "");

    public static int NextInsertNo(IEnumerable<string> keys) =>
        keys.Select(k => Regex.Match(k, @"^N(\d+)$")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max() + 1;

    // ---- Files ------------------------------------------------------------------------------------------------
    // Only DOCX: paragraphs must be read reliably to keep stable keys and to export real Word track changes.
    public static (byte[]? Data, string? Error, IReadOnlyList<ReviewParagraph> Paragraphs) CheckDocx(string? fileName, byte[]? data)
    {
        if (data is null || data.Length == 0) return (null, "Chọn file hợp đồng (DOCX).", []);
        if (data.Length > MaxFileBytes) return (null, "File vượt quá 10 MB.", []);
        if (!string.Equals(Path.GetExtension(fileName ?? ""), ".docx", StringComparison.OrdinalIgnoreCase))
            return (null, "Chỉ nhận file Word .docx. File PDF hoặc .doc: mở bằng Word rồi lưu lại dạng .docx.", []);
        var (type, error) = ContractPolicy.CheckFile(fileName, data);
        if (type is null) return (null, error ?? "File không hợp lệ.", []);
        try
        {
            var paragraphs = DocxParagraphs.Read(data);
            return paragraphs.Count == 0 ? (null, "Không đọc được nội dung chữ trong file (file trống hoặc chỉ có hình ảnh).", []) : (data, null, paragraphs);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            return (null, "Không đọc được nội dung file Word. Hãy mở file bằng Word, lưu lại rồi tải lên lần nữa.", []);
        }
    }
}

// Masks personal data before a contract is sent to the AI: e-mail addresses, phone numbers, ID-card numbers and bank
// account numbers become placeholders ([EMAIL_1], [SDT_1], [SO_GIAY_TO_1], [SO_TAI_KHOAN_1]). The same value always gets
// the same placeholder; Unmask restores the AI's quotes and proposals. Names and addresses are not masked.
public sealed class PersonalDataMask
{
    private static readonly (string Tag, Regex Pattern)[] Rules =
    [
        ("EMAIL", new Regex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled)),
        ("SDT", new Regex(@"(?<!\d|\d[.,])(\+84|0)(\d[ .-]?){8,9}\d(?!\d|[.,]\d)", RegexOptions.Compiled)),
        ("SO_GIAY_TO", new Regex(@"(?<!\d|\d[.,])(\d{12}|\d{9})(?!\d|[.,]\d)", RegexOptions.Compiled)),
        ("SO_TAI_KHOAN", new Regex(@"(?<!\d|\d[.,])\d{8,19}(?!\d|[.,]\d)", RegexOptions.Compiled))
    ];

    private readonly Dictionary<string, string> toPlaceholder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> toValue = new(StringComparer.Ordinal);

    public int Count => toValue.Count;

    public string Mask(string text)
    {
        foreach (var (tag, pattern) in Rules)
            text = pattern.Replace(text, m =>
            {
                if (m.Value.StartsWith('[')) return m.Value;
                if (!toPlaceholder.TryGetValue(m.Value, out var ph))
                {
                    ph = $"[{tag}_{toPlaceholder.Count(kv => kv.Value.StartsWith("[" + tag + "_", StringComparison.Ordinal)) + 1}]";
                    toPlaceholder[m.Value] = ph;
                    toValue[ph] = m.Value;
                }
                return ph;
            });
        return text;
    }

    public string? Unmask(string? text)
    {
        if (string.IsNullOrEmpty(text) || toValue.Count == 0) return text;
        return Regex.Replace(text, @"\[(EMAIL|SDT|SO_GIAY_TO|SO_TAI_KHOAN)_\d+\]", m => toValue.GetValueOrDefault(m.Value) ?? m.Value);
    }
}

public sealed record LegalArticleDraft(string ArticleNo, string? Title, string Content);

// Splits the full text of a law (copied from the official gazette or vbpl.vn) into articles: each "Điều <n>." line starts
// an article; text before the first article is ignored. Article numbers are kept as "Điều <n>".
public static class LegalTextParser
{
    private static readonly Regex Head = new(@"^\s*(Điều|ĐIỀU)\s+(\d+[a-zđ]?)\s*[\.:]?\s*(.*)$", RegexOptions.Compiled);

    public static IReadOnlyList<LegalArticleDraft> Split(string? text)
    {
        var result = new List<LegalArticleDraft>();
        string? no = null, title = null;
        var body = new List<string>();
        void Flush()
        {
            if (no is null) return;
            var content = string.Join("\n", body).Trim();
            if (result.All(a => a.ArticleNo != no)) result.Add(new(no, string.IsNullOrWhiteSpace(title) ? null : title.Trim().TrimEnd('.'), content.Length == 0 ? (title ?? no) : content));
        }
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            var m = Head.Match(line);
            if (m.Success)
            {
                Flush();
                no = "Điều " + m.Groups[2].Value;
                title = m.Groups[3].Value;
                body = [];
            }
            else if (no is not null && line.Length > 0) body.Add(line);
        }
        Flush();
        return result;
    }

    // "Điều 385", "điều 385", "Đ.385", "385" all become "Điều 385" so citations can be matched to the library.
    public static string? NormalizeArticle(string? value)
    {
        var m = Regex.Match(value ?? "", @"(\d+[a-zđ]?)", RegexOptions.IgnoreCase);
        return m.Success ? "Điều " + m.Groups[1].Value.ToLowerInvariant() : null;
    }

    // "91/2015/QH13" and "91/2015/qh13 " match; spaces and case are ignored.
    public static string NormalizeDocumentNo(string? value) => Regex.Replace(value ?? "", @"\s+", "").ToUpperInvariant();
}
