using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Anthropic;
using Anthropic.Models.Messages;
using TAWebsite.Core.Data;

namespace TAPortal.Web.Services;

// "Soạn bài bằng AI" (Bài viết): staff give a topic, or the link of an article on another site to summarise; the AI researches
// and writes one article in T.A's format, saved as a DRAFT for review (never published by the AI).
// Provider order: Claude API first (Anthropic, web search + web fetch, structured JSON); when that fails (no key, no credit,
// overload, cut off, bad JSON) the same prompt goes to 79AI (api.gommo.net, model claude-opus-5.5) as fallback.
// Every provider call is written to <content root>/logs/website-ai-usage.log (JSON lines: provider, model, tokens, cost);
// the provider and token figures are never shown in the UI.
//
// Settings "WebsiteAi:*" (secrets are placed on the server by the operator, never committed):
//   WebsiteAi:Anthropic:ApiKey      Claude API key; when empty, ContractReview:Anthropic:ApiKey is used
//   WebsiteAi:Model                 default claude-opus-5-5
//   WebsiteAi:Effort                low | medium | high (default high)
//   WebsiteAi:MaxOutputTokens       default 32000
//   WebsiteAi:Fallback:GommoToken   79AI access token (Gommo-Token header); empty = no fallback
//   WebsiteAi:Fallback:Model        default claude-opus-5.5
//   WebsiteAi:Fallback:Endpoint     default https://api.gommo.net/api/v2/chat
//   WebsiteAi:UsageLog              default <content root>/logs/website-ai-usage.log
public sealed class WebsiteAiOptions
{
    public string? AnthropicApiKey { get; init; }
    public string Model { get; init; } = "claude-opus-5-5";
    public string Effort { get; init; } = "high";
    public int MaxOutputTokens { get; init; } = 32000;
    public string? GommoToken { get; init; }
    public string FallbackModel { get; init; } = "claude-opus-5.5";
    public string FallbackEndpoint { get; init; } = "https://api.gommo.net/api/v2/chat";
    public string? UsageLogPath { get; init; }
    // USD per million tokens (Claude Opus 5.5 list price, same on 79AI) and per web search.
    public decimal PriceInputPerMTok { get; init; } = 4m;
    public decimal PriceOutputPerMTok { get; init; } = 20m;
    public decimal PriceCacheReadPerMTok { get; init; } = 0.2m;
    public decimal PricePerSearch { get; init; } = 0.01m;

    public bool HasAnthropic => !string.IsNullOrWhiteSpace(AnthropicApiKey);
    public bool HasFallback => !string.IsNullOrWhiteSpace(GommoToken);
    public bool IsConfigured => HasAnthropic || HasFallback;

    public static WebsiteAiOptions From(IConfiguration c, string contentRoot)
    {
        static string? Value(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        decimal Price(string key, decimal fallback) =>
            decimal.TryParse(c[key], NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;
        return new()
        {
            AnthropicApiKey = Value(c["WebsiteAi:Anthropic:ApiKey"]) ?? Value(c["ContractReview:Anthropic:ApiKey"]),
            Model = Value(c["WebsiteAi:Model"]) ?? "claude-opus-5-5",
            Effort = (Value(c["WebsiteAi:Effort"]) ?? "high").ToLowerInvariant(),
            MaxOutputTokens = int.TryParse(c["WebsiteAi:MaxOutputTokens"], out var m) && m is >= 8000 and <= 64000 ? m : 32000,
            GommoToken = Value(c["WebsiteAi:Fallback:GommoToken"]),
            FallbackModel = Value(c["WebsiteAi:Fallback:Model"]) ?? "claude-opus-5.5",
            FallbackEndpoint = Value(c["WebsiteAi:Fallback:Endpoint"]) ?? "https://api.gommo.net/api/v2/chat",
            UsageLogPath = Value(c["WebsiteAi:UsageLog"]) ?? Path.Combine(contentRoot, "logs", "website-ai-usage.log"),
            PriceInputPerMTok = Price("WebsiteAi:PriceInputPerMTok", 4m),
            PriceOutputPerMTok = Price("WebsiteAi:PriceOutputPerMTok", 20m)
        };
    }

    public decimal Cost(long input, long output, long cacheRead = 0, long searches = 0) =>
        Math.Round((input * PriceInputPerMTok + output * PriceOutputPerMTok + cacheRead * PriceCacheReadPerMTok) / 1_000_000m + searches * PricePerSearch, 4);
}

/// <summary>What staff asked for. Kind = PostSource.Own (topic) or PostSource.Summary (summarise SourceUrl).</summary>
public sealed record WebsiteAiRequest(byte Kind, string? Topic, string? SourceUrl, string? Notes, string? Category);

/// <summary>The article the AI wrote (before it becomes a WebsitePostInput).</summary>
public sealed record WebsiteAiArticle(string Title, string? Slug, string Summary, string Category, string Tags, string SeoTitle, string SeoDescription,
    string BodyHtml, string? SourceName, string? SourceAuthor, DateTime? SourcePublishedOn, IReadOnlyList<string> Sources, IReadOnlyList<string> Uncertain);

/// <summary>One provider call, for the usage log. Estimated = the provider did not report tokens (counted from characters).</summary>
public sealed record WebsiteAiUsage(string Provider, string Model, bool Ok, long InputTokens, long OutputTokens, long CacheReadTokens, long WebSearches,
    long WebFetches, decimal CostUsd, bool Estimated, long DurationMs, string? Error);

public sealed class WebsiteAiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One AI provider. Returns the raw JSON text of the article plus the usage of the call (also on failure).</summary>
public interface IWebsiteAiProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<(string Json, WebsiteAiUsage Usage)> WriteAsync(string system, string user, CancellationToken ct);
}

// Prompt, JSON schema and answer validation shared by every provider.
public static partial class WebsiteAiPrompt
{
    public static readonly string[] Categories = PostLibrary.Categories.Select(c => c.Slug).ToArray();

    public static string System(DateTime todayVn) => $$"""
Bạn là chuyên viên kế toán – thuế cao cấp, biên soạn bài cho website của Công ty TNHH Tư vấn Giải pháp T.A (Nha Trang, Khánh Hòa;
dịch vụ kế toán – thuế, thành lập doanh nghiệp, hộ kinh doanh, hóa đơn điện tử). Người đọc: chủ doanh nghiệp nhỏ và vừa, hộ kinh doanh, kế toán.
Hôm nay là {{todayVn:dd/MM/yyyy}}. Bài phải đúng quy định ĐANG CÓ HIỆU LỰC tại thời điểm này.

Nguyên tắc:
1. Tra cứu trước khi viết (công cụ tìm kiếm/đọc web nếu có), ưu tiên nguồn nhà nước và tạp chí ngành: chinhphu.vn, baochinhphu.vn, mof.gov.vn,
   gdt.gov.vn, thuvienphapluat.vn, luatvietnam.vn. Mỗi con số, thời hạn, thuế suất, mức phạt phải có văn bản gốc; ghi số hiệu văn bản ngay trong bài.
2. Điểm nào chưa chắc, chưa đọc được văn bản gốc, hoặc văn bản đang chờ hướng dẫn: KHÔNG đoán; viết thận trọng và ghi vào "uncertain".
   Nếu không có công cụ tra cứu, ghi rõ trong "uncertain" rằng số liệu chưa được kiểm chứng trên web.
3. Không bịa số liệu, không bịa kinh nghiệm hay khách hàng của T.A, không hứa hẹn kết quả. Tiếng Việt chuẩn, câu ngắn, giọng chuyên gia gần gũi.
4. Bài loại "tóm tắt có trích nguồn": viết lại HOÀN TOÀN bằng lời của T.A dựa trên bài gốc được cung cấp; không chép đoạn văn;
   được trích nguyên văn tối đa 1 câu ngắn (dưới 15 từ) trong ngoặc kép, ghi rõ nơi đăng; đoạn mở đầu nói rõ tổng hợp từ đâu và ngày đăng.
   Có mục <h2>T.A lưu ý gì cho doanh nghiệp?</h2> với 3–5 ý thực tế. Điền sourceName (tên nơi đăng), sourceAuthor, sourcePublishedOn (YYYY-MM-DD) theo bài gốc.
5. Bài tự biên soạn: 900–1.400 từ; bài tóm tắt: 600–900 từ.

Định dạng bodyHtml:
- Chỉ dùng thẻ p, h2, h3, ul, ol, li, strong, em, a, table, thead, tbody, tr, th, td, blockquote, div. Không ảnh, không style, không link ra trang ngoài.
- Mở đầu <p class="lead">…</p> (2–3 câu). 3–6 mục <h2>. Đúng 1–2 hộp <div class="ar-callout warn"><p><strong>Lưu ý:</strong> …</p></div>
  (class được phép: ar-callout + info|warn|ok). Có bảng hoặc danh sách kiểm tra nếu phù hợp.
- Mục cuối <h2>T.A có thể hỗ trợ gì?</h2>: 2–3 câu, liên kết <a href="/lien-he">liên hệ T.A</a> và một trong /dich-vu/ke-toan-thue, /dich-vu/thanh-lap-doanh-nghiep, /dich-vu.
- Câu cuối: bài tự biên soạn <p><em>Bài viết mang tính tham khảo, cập nhật theo quy định hiện hành đến tháng {{todayVn:MM/yyyy}}. Trường hợp cụ thể cần đối chiếu hồ sơ thực tế.</em></p>;
  bài tóm tắt <p><em>Bài viết tóm tắt, diễn giải từ nguồn đã nêu để doanh nghiệp tham khảo; trường hợp cụ thể cần đối chiếu văn bản gốc và hồ sơ thực tế.</em></p>

Trả lời DUY NHẤT một đối tượng JSON (không markdown, không giải thích) với các trường:
title (≤ 90 ký tự), slug (a-z, 0-9, gạch nối, ≤ 80), summary (≤ 260 ký tự), category (một trong: {{string.Join(", ", Categories)}}),
tags (3–5 từ khóa cách nhau dấu phẩy), seoTitle (≤ 65), seoDescription (≤ 155), bodyHtml,
sourceName, sourceAuthor, sourcePublishedOn (chuỗi rỗng nếu là bài tự biên soạn hoặc không có),
sources (mảng "Tên văn bản/bài – URL" đã đọc), uncertain (mảng điểm cần T.A rà lại).
""";

    public static string User(WebsiteAiRequest request, string? sourceText)
    {
        var sb = new StringBuilder();
        if (request.Kind == PostSource.Summary)
        {
            sb.AppendLine("LOẠI BÀI: Tóm tắt có trích nguồn.");
            sb.AppendLine("Đường dẫn bài gốc: " + request.SourceUrl);
            if (!string.IsNullOrWhiteSpace(request.Topic)) sb.AppendLine("Góc nhìn mong muốn: " + request.Topic.Trim());
        }
        else
        {
            sb.AppendLine("LOẠI BÀI: Tự biên soạn.");
            sb.AppendLine("Chủ đề: " + request.Topic?.Trim());
        }
        if (!string.IsNullOrWhiteSpace(request.Category)) sb.AppendLine("Chuyên mục đề nghị: " + request.Category);
        if (!string.IsNullOrWhiteSpace(request.Notes)) sb.AppendLine("Yêu cầu thêm của T.A: " + request.Notes.Trim());
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            sb.AppendLine();
            sb.AppendLine("NỘI DUNG BÀI GỐC (Portal đã tải về, chỉ dùng để tóm tắt; không chép nguyên văn):");
            sb.AppendLine("<<<");
            sb.AppendLine(sourceText);
            sb.AppendLine(">>>");
        }
        return sb.ToString();
    }

    public static Dictionary<string, JsonElement> Schema()
    {
        object Str() => new { type = "string" };
        object List() => new { type = "array", items = new { type = "string" } };
        var properties = new
        {
            title = Str(), slug = Str(), summary = Str(), category = new { type = "string", @enum = Categories }, tags = Str(), seoTitle = Str(),
            seoDescription = Str(), bodyHtml = Str(), sourceName = Str(), sourceAuthor = Str(), sourcePublishedOn = Str(), sources = List(), uncertain = List()
        };
        return new()
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            ["properties"] = JsonSerializer.SerializeToElement(properties),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "title", "slug", "summary", "category", "tags", "seoTitle", "seoDescription", "bodyHtml",
                "sourceName", "sourceAuthor", "sourcePublishedOn", "sources", "uncertain" })
        };
    }

    /// <summary>Parses the answer (a JSON object, possibly wrapped in prose or a code fence by a provider without structured output).</summary>
    public static WebsiteAiArticle Parse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new WebsiteAiException("AI không trả về bài viết theo định dạng JSON.");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text[start..(end + 1)]); }
        catch (JsonException ex) { throw new WebsiteAiException("AI trả về dữ liệu không đúng định dạng JSON.", ex); }
        using (doc)
        {
            var r = doc.RootElement;
            string S(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
            IReadOnlyList<string> L(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).ToList() : [];
            var title = S("title");
            var body = S("bodyHtml");
            if (title.Length == 0 || body.Length == 0) throw new WebsiteAiException("AI trả về bài viết thiếu tiêu đề hoặc nội dung.");
            var category = Categories.Contains(S("category")) ? S("category") : Categories[0];
            DateTime? published = DateTime.TryParseExact(S("sourcePublishedOn"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            return new(title, S("slug"), S("summary"), category, S("tags"), S("seoTitle"), S("seoDescription"), body,
                Blank(S("sourceName")), Blank(S("sourceAuthor")), published, L("sources"), L("uncertain"));
        }
    }

    private static string? Blank(string s) => s.Length == 0 ? null : s;

    /// <summary>Readable text of an HTML page (scripts, styles and tags removed), at most maxChars characters.</summary>
    public static string PageText(string html, int maxChars = 60000)
    {
        var s = ScriptOrStyle().Replace(html, " ");
        s = BlockTag().Replace(s, "\n");
        s = Tag().Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = Spaces().Replace(s, " ");
        s = Lines().Replace(s, "\n");
        s = s.Trim();
        return s.Length > maxChars ? s[..maxChars] : s;
    }

    [GeneratedRegex(@"<(script|style|noscript|svg|nav|footer|header)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex ScriptOrStyle();
    [GeneratedRegex(@"<(br|/p|/div|/h[1-6]|/li|/tr)\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex BlockTag();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tag();
    [GeneratedRegex(@"[ \t ]+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\s*\n\s*")] private static partial Regex Lines();
}

// Claude API through the official Anthropic C# SDK: server-side web search + web fetch, structured JSON output, streaming.
public sealed class AnthropicWebsiteAiProvider(WebsiteAiOptions options) : IWebsiteAiProvider
{
    public string Name => "claude";
    public bool IsConfigured => options.HasAnthropic;

    public async Task<(string Json, WebsiteAiUsage Usage)> WriteAsync(string system, string user, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var client = new AnthropicClient { ApiKey = options.AnthropicApiKey, Timeout = TimeSpan.FromMinutes(15) };
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxOutputTokens,
            System = new List<TextBlockParam> { new() { Text = system, CacheControl = new CacheControlEphemeral() } },
            Tools =
            [
                new ToolUnion(new WebSearchTool20260209 { MaxUses = 8 }),
                new ToolUnion(new WebFetchTool20260209 { MaxUses = 6 })
            ],
            OutputConfig = new OutputConfig
            {
                Effort = options.Effort switch { "low" => Effort.Low, "medium" => Effort.Medium, _ => Effort.High },
                Format = new JsonOutputFormat { Schema = WebsiteAiPrompt.Schema() }
            },
            Messages = [new UserMessageParam { Content = user }]
        };
        // The answer is the text written after the last tool call; text before a tool call is the model's working notes.
        var answer = new StringBuilder();
        long input = 0, output = 0, cacheRead = 0, searches = 0, fetches = 0;
        string? stop = null;
        try
        {
            await foreach (var e in client.Messages.CreateStreaming(parameters, ct))
            {
                if (e.TryPickStart(out var start))
                {
                    input = start.Message.Usage.InputTokens + (start.Message.Usage.CacheCreationInputTokens ?? 0);
                    cacheRead = start.Message.Usage.CacheReadInputTokens ?? 0;
                }
                else if (e.TryPickContentBlockStart(out var block))
                {
                    if (!block.ContentBlock.TryPickText(out _) && !block.ContentBlock.TryPickThinking(out _)) answer.Clear();
                }
                else if (e.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var t)) answer.Append(t.Text);
                else if (e.TryPickDelta(out var md))
                {
                    output = md.Usage.OutputTokens;
                    if (md.Usage.InputTokens is long i) input = i + (md.Usage.CacheCreationInputTokens ?? 0);
                    if (md.Usage.CacheReadInputTokens is long cr) cacheRead = cr;
                    if (md.Usage.ServerToolUse is { } tools) { searches = tools.WebSearchRequests; fetches = tools.WebFetchRequests; }
                    stop = md.Delta.StopReason?.ToString();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new WebsiteAiProviderException(Usage(false, ex.Message), "Gọi Claude API không thành công.", ex);
        }
        if (stop is not null && stop.Contains("max_tokens", StringComparison.OrdinalIgnoreCase))
            throw new WebsiteAiProviderException(Usage(false, "max_tokens"), "Bài viết bị cắt do vượt giới hạn độ dài.");
        if (stop is not null && stop.Contains("pause_turn", StringComparison.OrdinalIgnoreCase))
            throw new WebsiteAiProviderException(Usage(false, "pause_turn"), "AI tra cứu quá nhiều bước, chưa viết xong.");
        if (stop is not null && stop.Contains("refusal", StringComparison.OrdinalIgnoreCase))
            throw new WebsiteAiProviderException(Usage(false, "refusal"), "AI từ chối viết nội dung này.");
        return (answer.ToString(), Usage(true, null));

        WebsiteAiUsage Usage(bool ok, string? error) => new(Name, options.Model, ok, input, output, cacheRead, searches, fetches,
            options.Cost(input, output, cacheRead, searches), false, watch.ElapsedMilliseconds, error);
    }
}

/// <summary>A provider call that failed; carries the usage so far (tokens may already be billed).</summary>
public sealed class WebsiteAiProviderException(WebsiteAiUsage usage, string message, Exception? inner = null) : Exception(message, inner)
{
    public WebsiteAiUsage Usage { get; } = usage;
}

// 79AI fallback: POST api.gommo.net/api/v2/chat {action: chat, model, messages:[{role, text}]} with the Gommo-Token header.
// Its public contract (api-docs v2.16.0) returns data.text; token usage is read when present, otherwise estimated from characters.
public sealed class GommoWebsiteAiProvider(WebsiteAiOptions options, IHttpClientFactory http) : IWebsiteAiProvider
{
    public string Name => "79ai";
    public bool IsConfigured => options.HasFallback;

    public async Task<(string Json, WebsiteAiUsage Usage)> WriteAsync(string system, string user, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var prompt = system + "\n\n" + user;
        var body = new JsonObject
        {
            ["action"] = "chat",
            ["model"] = options.FallbackModel,
            ["platform"] = "unknown",
            ["user_message_id"] = Guid.NewGuid().ToString(),
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["text"] = prompt })
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, options.FallbackEndpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Gommo-Token", options.GommoToken);
        request.Headers.Add("Accept", "application/json");
        string text;
        JsonElement root;
        try
        {
            var client = http.CreateClient(nameof(GommoWebsiteAiProvider));
            using var response = await client.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            root = doc.RootElement.Clone();
            if (!response.IsSuccessStatusCode || (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False))
            {
                var code = root.TryGetProperty("error_code", out var c) ? c.ToString() : $"HTTP {(int)response.StatusCode}";
                throw new WebsiteAiProviderException(Usage(prompt.Length, 0, null, false, code), "79AI không xử lý được yêu cầu (" + code + ").");
            }
            text = root.TryGetProperty("data", out var data) && data.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        }
        catch (WebsiteAiProviderException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new WebsiteAiProviderException(Usage(prompt.Length, 0, null, false, ex.Message), "Gọi 79AI không thành công.", ex);
        }
        return (text, Usage(prompt.Length, text.Length, root, true, null));

        WebsiteAiUsage Usage(int inChars, int outChars, JsonElement? reply, bool ok, string? error)
        {
            var (input, output, reported) = ReportedTokens(reply);
            if (!reported) { input = inChars / 3; output = outChars / 3; }
            return new(Name, options.FallbackModel, ok, input, output, 0, 0, 0, options.Cost(input, output), !reported, watch.ElapsedMilliseconds, error);
        }
    }

    // Looks for a usage object anywhere in the reply: input_tokens/prompt_tokens and output_tokens/completion_tokens.
    public static (long Input, long Output, bool Reported) ReportedTokens(JsonElement? reply)
    {
        long input = 0, output = 0;
        var found = false;
        void Walk(JsonElement e, int depth)
        {
            if (depth > 6) return;
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var n))
                    {
                        var key = p.Name.ToLowerInvariant();
                        if (key is "input_tokens" or "prompt_tokens" or "inputtokens" or "prompttokens") { input = n; found = true; }
                        else if (key is "output_tokens" or "completion_tokens" or "outputtokens" or "completiontokens") { output = n; found = true; }
                    }
                    else Walk(p.Value, depth + 1);
                }
        }
        if (reply is JsonElement r) Walk(r, 0);
        return (input, output, found);
    }
}

/// <summary>Appends one JSON line per provider call to the usage log (never shown in the UI).</summary>
public sealed class WebsiteAiUsageLog(WebsiteAiOptions options, ILogger<WebsiteAiUsageLog> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task WriteAsync(Guid jobId, Guid actor, WebsiteAiUsage usage, Guid? postId)
    {
        logger.LogInformation("WEBSITE_AI_USAGE job {Job} provider {Provider} model {Model} ok {Ok} in {Input} out {Output} cacheRead {CacheRead} search {Search} fetch {Fetch} usd {Cost} estimated {Estimated} ms {Ms}",
            jobId, usage.Provider, usage.Model, usage.Ok, usage.InputTokens, usage.OutputTokens, usage.CacheReadTokens, usage.WebSearches, usage.WebFetches,
            usage.CostUsd, usage.Estimated, usage.DurationMs);
        if (string.IsNullOrWhiteSpace(options.UsageLogPath)) return;
        var line = JsonSerializer.Serialize(new
        {
            at = DateTime.UtcNow.ToString("O"), job = jobId, actor, provider = usage.Provider, model = usage.Model, ok = usage.Ok,
            input_tokens = usage.InputTokens, output_tokens = usage.OutputTokens, cache_read_tokens = usage.CacheReadTokens,
            web_searches = usage.WebSearches, web_fetches = usage.WebFetches, cost_usd = usage.CostUsd, estimated = usage.Estimated,
            duration_ms = usage.DurationMs, error = usage.Error is null ? null : (usage.Error.Length > 300 ? usage.Error[..300] : usage.Error), post = postId
        });
        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.UsageLogPath)!);
            await File.AppendAllTextAsync(options.UsageLogPath, line + Environment.NewLine);
        }
        catch (Exception ex) { logger.LogWarning(ex, "WEBSITE_AI_USAGE_LOG_FAILED"); }
        finally { Gate.Release(); }
    }
}

public enum WebsiteAiJobState { Queued, Fetching, Writing, Saving, Done, Failed }

public sealed class WebsiteAiJob
{
    public Guid Id { get; } = Guid.NewGuid();
    public required Guid Actor { get; init; }
    public required string? ActorIp { get; init; }
    public required WebsiteAiRequest Request { get; init; }
    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
    public volatile WebsiteAiJobState State = WebsiteAiJobState.Queued;
    public string? Error { get; set; }
    public Guid? PostId { get; set; }
    public string? Title { get; set; }
    public IReadOnlyList<string> Sources { get; set; } = [];
    public IReadOnlyList<string> Uncertain { get; set; } = [];
}

/// <summary>Jobs in memory (one Portal instance): queued by the controller, run one at a time by WebsiteAiWorker.</summary>
public sealed class WebsiteAiJobs
{
    private readonly ConcurrentDictionary<Guid, WebsiteAiJob> jobs = new();
    private readonly Channel<WebsiteAiJob> queue = Channel.CreateUnbounded<WebsiteAiJob>();

    public ChannelReader<WebsiteAiJob> Reader => queue.Reader;

    public WebsiteAiJob Enqueue(Guid actor, string? ip, WebsiteAiRequest request)
    {
        foreach (var old in jobs.Values.Where(j => j.CreatedAtUtc < DateTime.UtcNow.AddDays(-2)).ToList()) jobs.TryRemove(old.Id, out _);
        var job = new WebsiteAiJob { Actor = actor, ActorIp = ip, Request = request };
        jobs[job.Id] = job;
        queue.Writer.TryWrite(job);
        return job;
    }

    public WebsiteAiJob? Find(Guid id) => jobs.TryGetValue(id, out var job) ? job : null;
}

/// <summary>Runs the queued jobs: fetch the original article (summaries), ask the providers in order, save a draft.</summary>
public sealed class WebsiteAiWriter(IEnumerable<IWebsiteAiProvider> providers, WebsiteAiUsageLog usageLog, IHttpClientFactory http, TimeProvider clock,
    ILogger<WebsiteAiWriter> logger)
{
    public async Task RunAsync(WebsiteAiJob job, IWebsiteAdminStore store, CancellationToken ct)
    {
        try
        {
            string? sourceText = null;
            if (job.Request.Kind == PostSource.Summary)
            {
                job.State = WebsiteAiJobState.Fetching;
                sourceText = await FetchSourceAsync(job.Request.SourceUrl!, ct);
            }
            job.State = WebsiteAiJobState.Writing;
            var today = clock.GetUtcNow().UtcDateTime.AddHours(7);
            var system = WebsiteAiPrompt.System(today);
            var user = WebsiteAiPrompt.User(job.Request, sourceText);
            WebsiteAiArticle? article = null;
            string? lastError = null;
            foreach (var provider in providers.Where(p => p.IsConfigured))
            {
                try
                {
                    var (json, usage) = await provider.WriteAsync(system, user, ct);
                    try { article = WebsiteAiPrompt.Parse(json); }
                    catch (WebsiteAiException ex) { usage = usage with { Ok = false, Error = ex.Message }; lastError = ex.Message; }
                    await usageLog.WriteAsync(job.Id, job.Actor, usage, null);
                    if (article is not null) break;
                }
                catch (WebsiteAiProviderException ex)
                {
                    lastError = ex.Message;
                    logger.LogWarning(ex.InnerException ?? ex, "WEBSITE_AI_PROVIDER_FAILED {Provider}", provider.Name);
                    await usageLog.WriteAsync(job.Id, job.Actor, ex.Usage, null);
                }
            }
            if (article is null) throw new WebsiteAiException(lastError ?? "Chưa cấu hình AI để soạn bài.");
            job.State = WebsiteAiJobState.Saving;
            job.PostId = await SaveDraftAsync(job, article, store);
            job.Title = article.Title;
            job.Sources = article.Sources;
            job.Uncertain = article.Uncertain;
            job.State = WebsiteAiJobState.Done;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { job.Error = "Portal khởi động lại khi đang soạn, hãy thử lại."; job.State = WebsiteAiJobState.Failed; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WEBSITE_AI_JOB_FAILED {Job}", job.Id);
            job.Error = ex is WebsiteAiException ? ex.Message : "Không soạn được bài, hãy thử lại sau.";
            job.State = WebsiteAiJobState.Failed;
        }
    }

    private async Task<Guid> SaveDraftAsync(WebsiteAiJob job, WebsiteAiArticle a, IWebsiteAdminStore store)
    {
        var existing = (await store.PostsAsync(null, null, null)).Select(p => p.Slug).ToHashSet(StringComparer.Ordinal);
        var baseSlug = TAWebsite.Core.Content.Slug.From(string.IsNullOrWhiteSpace(a.Slug) ? a.Title : a.Slug);
        if (baseSlug.Length > 70) baseSlug = baseSlug[..70].TrimEnd('-');
        var slug = baseSlug;
        for (var n = 2; existing.Contains(slug); n++) slug = $"{baseSlug}-{n}";
        var categories = (await store.CategoriesAsync()).ToDictionary(c => c.Slug, c => c.Id, StringComparer.Ordinal);
        if (!categories.TryGetValue(a.Category, out var categoryId))
        {
            var order = Array.IndexOf(WebsiteAiPrompt.Categories, a.Category) + 1;
            var created = await store.SaveCategoryAsync(job.Actor, job.ActorIp, null, PostLibrary.CategoryName(a.Category), a.Category, order);
            if (created is { Outcome: WebsiteOutcome.Saved, Id: Guid id }) categoryId = id;
        }
        var input = new WebsitePostInput
        {
            Title = a.Title.Length > 300 ? a.Title[..300] : a.Title, Slug = slug, Summary = Cut(a.Summary, 1000), BodyHtml = a.BodyHtml, Tags = Cut(a.Tags, 500),
            AuthorName = PostLibrary.Author, SeoTitle = Cut(a.SeoTitle, 200), SeoDescription = Cut(a.SeoDescription, 400),
            CategoryId = categoryId == Guid.Empty ? null : categoryId, Publish = false
        };
        if (job.Request.Kind == PostSource.Summary)
            input.SetSource(new PostSource(PostSource.Summary, Cut(a.SourceName ?? new Uri(job.Request.SourceUrl!).Host, 200), job.Request.SourceUrl,
                a.SourceAuthor is null ? null : Cut(a.SourceAuthor, 200), a.SourcePublishedOn));
        var errors = input.Normalize();
        if (errors.Count > 0) throw new WebsiteAiException("Bài AI soạn chưa hợp lệ: " + string.Join(" ", errors.Select(e => e.Message)));
        var result = await store.SavePostAsync(job.Actor, job.ActorIp, null, input, clock.GetUtcNow().UtcDateTime);
        if (result is not { Outcome: WebsiteOutcome.Saved, Id: Guid postId }) throw new WebsiteAiException(WebsiteUiMessage(result.Outcome));
        return postId;
    }

    private static string WebsiteUiMessage(WebsiteOutcome outcome) => outcome == WebsiteOutcome.Duplicate ? "Đường dẫn bài đã có, hãy thử lại." : "Không lưu được bản nháp, hãy thử lại.";
    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;

    // Downloads the original article (public http/https addresses only) and keeps its readable text.
    private async Task<string> FetchSourceAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !PostSource.IsHttpUrl(url)) throw new WebsiteAiException("Đường dẫn bài gốc không hợp lệ.");
        var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
        if (addresses.Length == 0 || addresses.Any(IsPrivate)) throw new WebsiteAiException("Đường dẫn bài gốc phải là một website công khai.");
        try
        {
            var client = http.CreateClient(nameof(WebsiteAiWriter));
            using var response = await client.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode) throw new WebsiteAiException($"Không mở được bài gốc (HTTP {(int)response.StatusCode}).");
            var html = await response.Content.ReadAsStringAsync(ct);
            var text = WebsiteAiPrompt.PageText(html);
            if (text.Length < 400) throw new WebsiteAiException("Không đọc được nội dung bài gốc (trang trống hoặc cần đăng nhập).");
            return text;
        }
        catch (WebsiteAiException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new WebsiteAiException("Không tải được bài gốc, kiểm tra lại đường dẫn.", ex); }
    }

    public static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
        var b = ip.GetAddressBytes();
        return b[0] is 10 or 127 or 0 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }
}

public sealed class WebsiteAiWorker(WebsiteAiJobs jobs, WebsiteAiWriter writer, IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in jobs.Reader.ReadAllAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope();
            await writer.RunAsync(job, scope.ServiceProvider.GetRequiredService<IWebsiteAdminStore>(), stoppingToken);
        }
    }
}
