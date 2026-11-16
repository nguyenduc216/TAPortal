using System.Globalization;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace TAPortal.Web.Services;

// Settings "ContractReview:*" (operator places the API key on the server; it is never committed):
//   ContractReview:Anthropic:ApiKey   Claude API key of the T.A organisation (platform.claude.com)
//   ContractReview:Model              default claude-opus-5-5
//   ContractReview:Effort             low | medium | high (default high)
//   ContractReview:MaxOutputTokens    default 32000
//   ContractReview:PriceInputPerMTok / PriceOutputPerMTok   USD per million tokens, for the cost column (default 4 / 20)
public sealed class ContractReviewAiOptions
{
    public string? ApiKey { get; init; }
    public string Model { get; init; } = "claude-opus-5-5";
    public string Effort { get; init; } = "high";
    public int MaxOutputTokens { get; init; } = 32000;
    public decimal PriceInputPerMTok { get; init; } = 4m;
    public decimal PriceOutputPerMTok { get; init; } = 20m;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public static ContractReviewAiOptions From(IConfiguration c) => new()
    {
        ApiKey = c["ContractReview:Anthropic:ApiKey"],
        Model = string.IsNullOrWhiteSpace(c["ContractReview:Model"]) ? "claude-opus-5-5" : c["ContractReview:Model"]!.Trim(),
        Effort = (c["ContractReview:Effort"] ?? "high").Trim().ToLowerInvariant(),
        MaxOutputTokens = int.TryParse(c["ContractReview:MaxOutputTokens"], out var m) && m is >= 4000 and <= 128000 ? m : 32000,
        PriceInputPerMTok = decimal.TryParse(c["ContractReview:PriceInputPerMTok"], NumberStyles.Number, CultureInfo.InvariantCulture, out var pi) ? pi : 4m,
        PriceOutputPerMTok = decimal.TryParse(c["ContractReview:PriceOutputPerMTok"], NumberStyles.Number, CultureInfo.InvariantCulture, out var po) ? po : 20m
    };

    public decimal Cost(long input, long output) => Math.Round((input * PriceInputPerMTok + output * PriceOutputPerMTok) / 1_000_000m, 4);
}

public sealed record LegalIndexArticle(string DocumentNo, string DocumentTitle, string ArticleNo, string? Title);

// CustomerNote: "Yêu cầu của khách hàng / ghi chú" of the request (optional); the reviewer gives it priority.
public sealed record ReviewAiRequest(string ReviewTitle, string CustomerRole, IReadOnlyList<ReviewParagraph> Paragraphs, IReadOnlyList<LegalIndexArticle> Library, bool MaskPersonalData, string? CustomerNote = null);

public sealed record ReviewAiLegalRef(string DocumentNo, string Article, string? Clause, string? Note);

public sealed record ReviewAiFinding(string ParagraphKey, string Action, string? Quote, string Issue, string Severity, string Party,
    IReadOnlyList<ReviewAiLegalRef> LegalRefs, bool LegalVerified, string? Suggestion, string? RevisedText);

public sealed record ReviewAiResult(string Model, long InputTokens, long OutputTokens, decimal CostUsd, string Summary,
    IReadOnlyList<ReviewAiFinding> Findings, IReadOnlyList<string> Dropped, string RawJson);

public sealed class ReviewAiException(string message, Exception? inner = null) : Exception(message, inner);

public interface IContractReviewAi
{
    bool IsConfigured { get; }
    string Model { get; }
    // progress: tokens read (exact, once the answer starts) and tokens written so far (estimated from the streamed text).
    Task<ReviewAiResult> ReviewAsync(ReviewAiRequest request, CancellationToken cancellationToken, IProgress<(long Input, long Output)>? progress = null);
}

// Prompt, JSON schema and answer validation for the AI contract review. The answer is never trusted as-is: every finding
// must point at an existing paragraph, its quote must occur in that paragraph, enums must be valid, and each legal reference
// is checked against the law library (LegalVerified = every reference was found there).
public static class ContractReviewPrompt
{
    public const string System = """
Bạn là luật sư Việt Nam giàu kinh nghiệm về hợp đồng dân sự và thương mại, làm việc cho Công ty Tư vấn giải pháp T.A.
Nhiệm vụ: rà soát hợp đồng khách hàng gửi, chỉ ra điều khoản sai luật, bất lợi, mơ hồ, thiếu sót hoặc mâu thuẫn, và đề xuất câu chữ sửa.

Quy tắc bắt buộc:
1. Hợp đồng được đánh số đoạn dạng [P1], [P2], [N1]... Mỗi góp ý phải ghi đúng paragraphKey của đoạn đó.
2. "quote" phải là một đoạn chữ CHÉP NGUYÊN VĂN, liên tục, có trong đoạn đó (không sửa dấu, không rút gọn). Để trống nếu góp ý cho cả đoạn.
3. action:
   - REPLACE: revisedText là câu chữ mới THAY CHO đúng phần "quote" (hoặc thay cả đoạn nếu quote trống).
   - INSERT: revisedText là một đoạn mới hoàn chỉnh, chèn SAU đoạn paragraphKey (dùng khi hợp đồng thiếu điều khoản).
   - DELETE: bỏ cả đoạn; revisedText để trống.
   - NOTE: chỉ lưu ý, không đề xuất sửa chữ; revisedText để trống.
4. Căn cứ pháp lý: ưu tiên văn bản trong "KHO VĂN BẢN PHÁP LUẬT" bên dưới, ghi documentNo đúng như trong kho, article dạng "Điều 385",
   clause dạng "khoản 2" hoặc "khoản 2, điểm a" (để trống nếu dẫn cả điều).
   Chỉ trích dẫn văn bản còn hiệu lực mà bạn chắc chắn. Không bịa số điều. Nếu không có căn cứ cụ thể, để legalRefs rỗng và nói rõ đây là khuyến nghị thực tiễn.
5. party: bên được lợi khi sửa theo góp ý (A, B hoặc BOTH).
6. severity: HIGH (trái luật, vô hiệu, rủi ro lớn về tiền/trách nhiệm), MEDIUM (bất lợi hoặc mơ hồ đáng kể), LOW (câu chữ, hình thức).
7. Viết tiếng Việt, ngắn gọn, chuyên nghiệp. Giữ nguyên các ký hiệu ẩn danh như [SDT_1], [SO_GIAY_TO_1] nếu cần trích.
8. Không lặp lại cùng một vấn đề ở nhiều góp ý. Tập trung vào điều quan trọng; tối đa 60 góp ý.
""";

    public static string User(ReviewAiRequest r, Func<string, string> mask)
    {
        var sb = new StringBuilder();
        sb.AppendLine("YÊU CẦU: " + ContractReviewPolicy.RoleInstruction(r.CustomerRole));
        sb.AppendLine("Tên hợp đồng: " + mask(r.ReviewTitle));
        if (!string.IsNullOrWhiteSpace(r.CustomerNote))
            sb.AppendLine("YÊU CẦU RIÊNG CỦA KHÁCH HÀNG (ưu tiên rà soát kỹ các điểm này, vẫn góp ý các vấn đề quan trọng khác): " + mask(r.CustomerNote.Trim()));
        sb.AppendLine();
        sb.AppendLine("KHO VĂN BẢN PHÁP LUẬT (chỉ danh mục điều):");
        if (r.Library.Count == 0) sb.AppendLine("(Kho đang trống: nếu trích dẫn luật, chỉ dùng văn bản còn hiệu lực mà bạn chắc chắn.)");
        foreach (var doc in r.Library.GroupBy(a => (a.DocumentNo, a.DocumentTitle)))
        {
            sb.Append("- ").Append(doc.Key.DocumentNo).Append(" – ").AppendLine(doc.Key.DocumentTitle);
            foreach (var a in doc) sb.Append("  ").Append(a.ArticleNo).AppendLine(string.IsNullOrWhiteSpace(a.Title) ? "" : ". " + a.Title);
        }
        sb.AppendLine();
        sb.AppendLine("NỘI DUNG HỢP ĐỒNG:");
        foreach (var p in r.Paragraphs) sb.Append('[').Append(p.Key).Append("] ").AppendLine(mask(p.Text));
        return sb.ToString();
    }

    public static Dictionary<string, JsonElement> Schema()
    {
        object Str() => new { type = "string" };
        var legal = new
        {
            type = "object",
            additionalProperties = false,
            properties = new { documentNo = Str(), article = Str(), clause = Str(), note = Str() },
            required = new[] { "documentNo", "article", "clause", "note" }
        };
        var finding = new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                paragraphKey = Str(),
                action = new { type = "string", @enum = new[] { "REPLACE", "INSERT", "DELETE", "NOTE" } },
                quote = Str(),
                issue = Str(),
                severity = new { type = "string", @enum = new[] { "HIGH", "MEDIUM", "LOW" } },
                party = new { type = "string", @enum = new[] { "A", "B", "BOTH" } },
                legalRefs = new { type = "array", items = legal },
                suggestion = Str(),
                revisedText = Str()
            },
            required = new[] { "paragraphKey", "action", "quote", "issue", "severity", "party", "legalRefs", "suggestion", "revisedText" }
        };
        return new()
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            ["properties"] = JsonSerializer.SerializeToElement(new { summary = Str(), findings = new { type = "array", items = finding } }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "summary", "findings" })
        };
    }

    // Parses and validates the JSON answer. Findings that cannot be applied safely are dropped with a reason.
    public static (string Summary, List<ReviewAiFinding> Findings, List<string> Dropped) Parse(string json, IReadOnlyList<ReviewParagraph> paragraphs,
        IReadOnlyList<LegalIndexArticle> library, Func<string?, string?> unmask)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var summary = unmask(Str(root, "summary")) ?? "";
        var byKey = paragraphs.ToDictionary(p => p.Key);
        var known = library.Select(a => (LegalTextParser.NormalizeDocumentNo(a.DocumentNo), a.ArticleNo.ToLowerInvariant())).ToHashSet();
        var findings = new List<ReviewAiFinding>();
        var dropped = new List<string>();
        if (!root.TryGetProperty("findings", out var list) || list.ValueKind != JsonValueKind.Array) return (summary, findings, dropped);
        foreach (var f in list.EnumerateArray())
        {
            var key = (Str(f, "paragraphKey") ?? "").Trim().Trim('[', ']');
            var action = (Str(f, "action") ?? "").Trim().ToUpperInvariant();
            var issue = unmask(Str(f, "issue"))?.Trim();
            var quote = unmask(Str(f, "quote"))?.Trim();
            var revised = unmask(Str(f, "revisedText"))?.Trim();
            var severity = (Str(f, "severity") ?? "MEDIUM").Trim().ToUpperInvariant();
            var party = (Str(f, "party") ?? "BOTH").Trim().ToUpperInvariant();
            if (!byKey.TryGetValue(key, out var paragraph)) { dropped.Add($"Đoạn {key} không tồn tại."); continue; }
            if (string.IsNullOrWhiteSpace(issue)) { dropped.Add($"{key}: góp ý không có nội dung."); continue; }
            if (!ContractReviewPolicy.Is(ContractReviewPolicy.Actions, action)) action = ContractReviewPolicy.NoteOnly;
            if (!ContractReviewPolicy.Is(ContractReviewPolicy.Severities, severity)) severity = "MEDIUM";
            if (!ContractReviewPolicy.Is(ContractReviewPolicy.Roles, party)) party = "BOTH";
            if (!string.IsNullOrEmpty(quote) && !paragraph.Text.Contains(quote, StringComparison.Ordinal))
            {
                // A quote that is not verbatim cannot be highlighted or replaced: keep the finding as a note on the paragraph.
                if (action == ContractReviewPolicy.Replace) { action = ContractReviewPolicy.NoteOnly; issue += " (Đoạn trích của AI không khớp nguyên văn; cần sửa tay.)"; }
                quote = null;
            }
            if (action is ContractReviewPolicy.Replace or ContractReviewPolicy.Insert && string.IsNullOrWhiteSpace(revised)) action = ContractReviewPolicy.NoteOnly;
            if (action is ContractReviewPolicy.Delete or ContractReviewPolicy.NoteOnly) revised = null;
            var refs = new List<ReviewAiLegalRef>();
            if (f.TryGetProperty("legalRefs", out var lr) && lr.ValueKind == JsonValueKind.Array)
                foreach (var r in lr.EnumerateArray())
                {
                    var no = Str(r, "documentNo")?.Trim();
                    var art = LegalTextParser.NormalizeArticle(Str(r, "article"));
                    if (string.IsNullOrWhiteSpace(no) || art is null) continue;
                    refs.Add(new(no, art, Blank(Str(r, "clause")), Blank(unmask(Str(r, "note")))));
                }
            var verified = refs.Count > 0 && refs.All(r => known.Contains((LegalTextParser.NormalizeDocumentNo(r.DocumentNo), r.Article.ToLowerInvariant())));
            findings.Add(new(key, action, string.IsNullOrEmpty(quote) ? null : quote, issue!, severity, party, refs, verified, Blank(unmask(Str(f, "suggestion"))), Blank(revised)));
        }
        return (summary, findings, dropped);
    }

    public static string LegalText(IReadOnlyList<ReviewAiLegalRef> refs) =>
        string.Join("\n", refs.Select(r => $"{r.Article}{(r.Clause is null ? "" : ", " + r.Clause)} – {r.DocumentNo}{(r.Note is null ? "" : ": " + r.Note)}"));

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// Claude API through the official Anthropic C# SDK: structured JSON output, streaming (long answers), adaptive thinking.
public sealed class AnthropicContractReviewAi(ContractReviewAiOptions options, ILogger<AnthropicContractReviewAi> logger) : IContractReviewAi
{
    public bool IsConfigured => options.IsConfigured;
    public string Model => options.Model;

    public async Task<ReviewAiResult> ReviewAsync(ReviewAiRequest request, CancellationToken cancellationToken, IProgress<(long Input, long Output)>? progress = null)
    {
        if (!options.IsConfigured) throw new ReviewAiException("Chưa cấu hình khóa Claude API (ContractReview:Anthropic:ApiKey).");
        var mask = new PersonalDataMask();
        string Mask(string s) => request.MaskPersonalData ? mask.Mask(s) : s;
        var user = ContractReviewPrompt.User(request, Mask);
        var client = new AnthropicClient { ApiKey = options.ApiKey };
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxOutputTokens,
            System = new List<TextBlockParam> { new() { Text = ContractReviewPrompt.System, CacheControl = new CacheControlEphemeral() } },
            OutputConfig = new OutputConfig
            {
                Effort = options.Effort switch { "low" => Effort.Low, "medium" => Effort.Medium, _ => Effort.High },
                Format = new JsonOutputFormat { Schema = ContractReviewPrompt.Schema() }
            },
            Messages = [new UserMessageParam { Content = user }]
        };
        var text = new StringBuilder();
        long input = 0, output = 0, chars = 0;
        string? stop = null;
        var lastReport = DateTime.MinValue;
        try
        {
            await foreach (var e in client.Messages.CreateStreaming(parameters, cancellationToken))
            {
                if (e.TryPickStart(out var start))
                {
                    input = start.Message.Usage.InputTokens + (start.Message.Usage.CacheReadInputTokens ?? 0) + (start.Message.Usage.CacheCreationInputTokens ?? 0);
                    progress?.Report((input, 0));
                }
                else if (e.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var t))
                {
                    text.Append(t.Text);
                    chars += t.Text.Length;
                    if (DateTime.UtcNow - lastReport > TimeSpan.FromSeconds(2)) { lastReport = DateTime.UtcNow; progress?.Report((input, chars / 3)); }
                }
                else if (e.TryPickDelta(out var md))
                {
                    output = md.Usage.OutputTokens;
                    stop = md.Delta.StopReason?.ToString();
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CONTRACT_REVIEW_AI_FAILED model {Model}", options.Model);
            throw new ReviewAiException("Gọi Claude API không thành công: " + ReviewText.Short(ex.Message, 300), ex);
        }
        var json = text.ToString();
        if (stop is not null && stop.Contains("max_tokens", StringComparison.OrdinalIgnoreCase))
            throw new ReviewAiException($"Câu trả lời của AI bị cắt do vượt giới hạn {options.MaxOutputTokens} token; tăng ContractReview:MaxOutputTokens hoặc chia hợp đồng.");
        if (stop is not null && stop.Contains("refusal", StringComparison.OrdinalIgnoreCase))
            throw new ReviewAiException("AI từ chối rà soát nội dung này.");
        try
        {
            var (summary, findings, dropped) = ContractReviewPrompt.Parse(json, request.Paragraphs, request.Library, mask.Unmask);
            return new(options.Model, input, output, options.Cost(input, output), summary, findings, dropped, json);
        }
        catch (JsonException ex) { throw new ReviewAiException("AI trả về dữ liệu không đúng định dạng JSON.", ex); }
    }
}
