using System.Net;
using System.Text;
using System.Threading.Channels;

namespace TAPortal.Web.Services;

// HTTP access of the watch: HTTPS only, to LegalWatchPolicy.Hosts only (redirects are followed by hand and checked), with
// a size limit. Replaced by an in-memory fake in the Safe tests, which never use the network.
public interface ILegalWatchWeb
{
    Task<string?> GetTextAsync(string url, CancellationToken ct);
    Task<byte[]?> GetBytesAsync(string url, int maxBytes, CancellationToken ct);
}

public sealed class HttpLegalWatchWeb(IHttpClientFactory clients, ILogger<HttpLegalWatchWeb> logger) : ILegalWatchWeb
{
    public const string ClientName = "LegalWatch";
    private const int TextLimit = 16 * 1024 * 1024;   // a vbpl.vn sitemap file is about 4 MB

    public async Task<string?> GetTextAsync(string url, CancellationToken ct)
    {
        var data = await GetBytesAsync(url, TextLimit, ct);
        return data is null ? null : Encoding.UTF8.GetString(data).TrimStart('﻿');
    }

    public async Task<byte[]?> GetBytesAsync(string url, int maxBytes, CancellationToken ct)
    {
        var client = clients.CreateClient(ClientName);
        var current = url;
        for (var hop = 0; hop < 4; hop++)
        {
            if (!LegalWatchPolicy.Allowed(current)) { logger.LogWarning("LEGAL_WATCH_URL_REFUSED {Host}", Uri.TryCreate(current, UriKind.Absolute, out var u) ? u.Host : "?"); return null; }
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(LegalWatchPolicy.UserAgent);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is Uri next)
            {
                current = (next.IsAbsoluteUri ? next : new Uri(new Uri(current), next)).ToString();
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) { logger.LogWarning("LEGAL_WATCH_HTTP {Status} {Host}", (int)response.StatusCode, new Uri(current).Host); return null; }
            if (response.Content.Headers.ContentLength > maxBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > maxBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        return null;
    }
}

public sealed record LegalWatchRunResult(int Scanned, int NewItems, int StatusItems, string? Error);

// The work of one run. Collect: Công báo feed → document pages of tax-looking entries → NEW queue entries (no duplicate
// number, nothing already in the library). CheckStatus: each library document's vbpl.vn page (found once through the
// sitemap, then remembered) → STATUS entries when the status or expiry differs. Notify: monthly email to the people who
// decide the queue. Never writes to the library itself.
public sealed class LegalWatchService(ILegalWatchStore store, ILegalWatchWeb web, IServiceProvider services, ILogger<LegalWatchService> logger)
{
    public static TimeSpan Pause = TimeSpan.FromMilliseconds(700);   // between two requests to the same site
    private const int MaxDetailPages = 60;

    public async Task<LegalWatchRunResult> CollectAsync(Guid runId, CancellationToken ct)
    {
        var xml = await web.GetTextAsync(LegalWatchPolicy.CongBaoFeed, ct);
        if (xml is null) return new(0, 0, 0, "Không đọc được RSS Công báo.");
        IReadOnlyList<LegalFeedEntry> entries;
        try { entries = CongBaoParser.Feed(xml); }
        catch (System.Xml.XmlException) { return new(0, 0, 0, "RSS Công báo không đúng định dạng."); }
        int scanned = 0, added = 0, opened = 0;
        foreach (var entry in entries)
        {
            scanned++;
            if (!CongBaoParser.Worth(entry) || opened >= MaxDetailPages) continue;
            opened++;
            await Task.Delay(Pause, ct);
            var html = await web.GetTextAsync(entry.Link, ct);
            if (html is null) continue;
            var c = CongBaoParser.Detail(html, entry.Link);
            if (c is null || c.Keywords.Length == 0) continue;               // a Ministry of Finance document without a tax subject
            if (await store.IsKnownAsync(c.DocumentNo)) continue;
            if (await store.AddNewAsync(runId, c)) added++;
        }
        return new(scanned, added, 0, null);
    }

    public async Task<LegalWatchRunResult> CheckStatusAsync(Guid runId, CancellationToken ct)
    {
        var docs = await store.DocumentsAsync();
        var urls = docs.Where(d => d.SourceUrl is not null && d.SourceUrl.StartsWith("https://vbpl.vn/", StringComparison.Ordinal))
            .ToDictionary(d => d.Id, d => new List<string> { d.SourceUrl! });
        var missing = docs.Where(d => !urls.ContainsKey(d.Id)).Select(d => (d.Id, d.DocumentNo, d.DocType)).ToList();
        if (missing.Count > 0)
            for (var n = 1; n <= LegalWatchPolicy.VbplSitemapCount; n++)
            {
                await Task.Delay(Pause, ct);
                var sitemap = await web.GetTextAsync(LegalWatchPolicy.VbplSitemap(n), ct);
                if (sitemap is null) continue;
                foreach (var (id, url) in VbplParser.Match(sitemap, missing))
                {
                    if (!urls.TryGetValue(id, out var list)) urls[id] = list = [];
                    if (list.Count < 4 && !list.Contains(url)) list.Add(url);
                }
            }
        int scanned = 0, items = 0;
        foreach (var doc in docs)
        {
            if (!urls.TryGetValue(doc.Id, out var candidates)) continue;
            foreach (var url in candidates)
            {
                await Task.Delay(Pause, ct);
                var html = await web.GetTextAsync(url, ct);
                var status = html is null ? null : VbplParser.Detail(html);
                if (status is null || LegalWatchPolicy.Key(status.DocumentNo) != LegalWatchPolicy.Key(doc.DocumentNo)) continue;
                scanned++;
                var changed = doc.EffectStatus is null
                    ? !LegalWatchPolicy.IsNormal(status.EffectStatus) || (status.ExpiredDate is not null && status.ExpiredDate != doc.ExpiredDate)
                    : !string.Equals(doc.EffectStatus, status.EffectStatus, StringComparison.OrdinalIgnoreCase) || (status.ExpiredDate is not null && status.ExpiredDate != doc.ExpiredDate);
                await store.MarkCheckedAsync(doc.Id, url, changed ? null : status.EffectStatus);
                if (changed && await store.AddStatusAsync(runId, doc, url, status)) items++;
                break;
            }
        }
        return new(scanned, 0, items, null);
    }

    public async Task NotifyAsync(Guid runId, DateTime sinceUtc, string period, CancellationToken ct)
    {
        var (fresh, status) = await store.PendingSinceAsync(sinceUtc);
        var total = await store.PendingCountAsync();
        if (total == 0) { await store.MarkNotifiedAsync(runId); return; }
        var notifications = services.GetService<TAPortal.Integrations.Notifications.NotificationSettings>();
        if (notifications is null) return;
        var subject = $"TAPortal - Văn bản pháp luật mới cần duyệt (tháng {period})";
        var body = Body(period, fresh, status, total);
        var sent = 0;
        foreach (var r in await store.RecipientsAsync())
        {
            ct.ThrowIfCancellationRequested();
            var error = await notifications.SendSystemEmailAsync(r.Email, subject, body.Replace("{name}", r.Name), ct);
            if (error is null) sent++; else logger.LogWarning("LEGAL_WATCH_NOTICE_FAILED {Error}", error);
        }
        if (sent > 0) await store.MarkNotifiedAsync(runId);
    }

    public static string Body(string period, int fresh, int status, int total) =>
        "Xin chào {name},\n\n" +
        $"Kỳ kiểm tra văn bản pháp luật tháng {period}: {fresh} văn bản thuế mới và {status} thay đổi hiệu lực của văn bản trong kho. " +
        $"Hiện có {total} mục đang chờ duyệt.\n\n" +
        "Mở TAPortal → Hợp đồng dịch vụ → Kho văn bản pháp luật → Văn bản mới cần duyệt để nạp vào kho hoặc bỏ qua. " +
        "Văn bản chỉ được AI trích dẫn sau khi đã nạp vào kho.\n\nTư vấn giải pháp T.A";

    // The Word file of a NEW entry, read like an upload. Null with a reason when there is none or it cannot be read.
    public async Task<(LegalUpload? Upload, string? Error)> FileAsync(LegalWatchItem item, CancellationToken ct)
    {
        if (item.FileUrl is null) return (null, "Công báo không có file Word cho văn bản này.");
        var data = await web.GetBytesAsync(item.FileUrl, LegalFileText.MaxBytes, ct);
        if (data is null) return (null, "Không tải được file Word từ Công báo.");
        var name = LegalWatchPolicy.Slug(item.DocumentNo) + ".docx";
        return LegalFileText.Read(name, data);
    }
}

// "Kiểm tra ngay" (manual run) is handed to the scheduler, which runs it in the background.
public sealed class LegalWatchTrigger
{
    private readonly Channel<Guid> requests = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    public bool Request(Guid actor) => requests.Writer.TryWrite(actor);
    public ChannelReader<Guid> Reader => requests.Reader;
}

// Every hour (first one 2 minutes after start-up): a WEEKLY run per ISO week reads Công báo; a MONTHLY run per month also
// checks the library on vbpl.vn and emails the reviewers. A manual request runs at once. UX_LegalWatchRuns_Period keeps
// a period to one run across app instances.
public sealed class LegalWatchScheduler(IServiceScopeFactory scopes, LegalWatchTrigger trigger, ILogger<LegalWatchScheduler> logger) : BackgroundService
{
    internal static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(2), Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var manual = Task.Run(() => Manual(stoppingToken), stoppingToken);
        try { await Task.Delay(FirstRun, stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await Scheduled(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable or migration 048 not applied yet: try again at the next tick.
                logger.LogWarning(ex, "LEGAL_WATCH_SCHEDULER_FAILED");
            }
        }
        while (await NextTick(timer, stoppingToken));
        await manual;
    }

    private async Task Scheduled(CancellationToken ct)
    {
        var today = WorkClock.Today();
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ILegalWatchStore>();
        var month = LegalWatchPolicy.Month(today);
        if (!await store.RunExistsAsync("MONTHLY", month))
        {
            await Run(scope.ServiceProvider, "MONTHLY", month, null, monthly: true, ct);
            return;
        }
        var week = LegalWatchPolicy.Week(today);
        if (!await store.RunExistsAsync("WEEKLY", week)) await Run(scope.ServiceProvider, "WEEKLY", week, null, monthly: false, ct);
    }

    private async Task Manual(CancellationToken ct)
    {
        try
        {
            await foreach (var actor in trigger.Reader.ReadAllAsync(ct))
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await Run(scope.ServiceProvider, "MANUAL", DateTime.UtcNow.ToString("yyyyMMddHHmmss"), actor, monthly: false, ct, withStatus: true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "LEGAL_WATCH_MANUAL_FAILED"); }
        }
        catch (OperationCanceledException) { }
    }

    internal static async Task Run(IServiceProvider services, string kind, string period, Guid? actor, bool monthly, CancellationToken ct, bool withStatus = false)
    {
        var store = services.GetRequiredService<ILegalWatchStore>();
        var service = services.GetRequiredService<LegalWatchService>();
        var logger = services.GetRequiredService<ILogger<LegalWatchScheduler>>();
        if (await store.ClaimRunAsync(kind, period, actor) is not Guid run) return;
        var started = DateTime.UtcNow;
        LegalWatchRunResult collect = new(0, 0, 0, null), status = new(0, 0, 0, null);
        string? error = null;
        try
        {
            collect = await service.CollectAsync(run, ct);
            if (monthly || withStatus) status = await service.CheckStatusAsync(run, ct);
            error = collect.Error ?? status.Error;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "LEGAL_WATCH_RUN_FAILED {Kind}", kind);
            error = "Lỗi khi đọc nguồn: " + ex.GetType().Name;
        }
        await store.FinishRunAsync(run, collect.Scanned + status.Scanned, collect.NewItems, status.StatusItems, error);
        if (monthly) await service.NotifyAsync(run, started.AddDays(-35), period, ct);
        logger.LogInformation("Legal watch {Kind} {Period}: {New} new, {Status} status changes.", kind, period, collect.NewItems, status.StatusItems);
    }

    private static async Task<bool> NextTick(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); } catch (OperationCanceledException) { return false; }
    }
}
