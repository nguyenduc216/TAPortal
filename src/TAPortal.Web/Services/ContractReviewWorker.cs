namespace TAPortal.Web.Services;

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
            // Token counts are written to the run every 5 seconds while the answer streams in.
            long input = 0, output = 0, written = -1;
            var progress = new Progress<(long Input, long Output)>(p => { Interlocked.Exchange(ref input, p.Input); Interlocked.Exchange(ref output, p.Output); });
            using var stopReporting = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var reporter = Task.Run(async () =>
            {
                while (!stopReporting.IsCancellationRequested)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(5), stopReporting.Token); } catch (OperationCanceledException) { return; }
                    var o = Interlocked.Read(ref output);
                    if (o == written && Interlocked.Read(ref input) == 0) continue;
                    if (o != written || written < 0)
                    {
                        written = o;
                        try { await store.ProgressAsync(job.RunId, Interlocked.Read(ref input), o); } catch (Exception ex) { logger.LogDebug(ex, "CONTRACT_REVIEW_PROGRESS_SKIPPED"); }
                    }
                }
            });
            ReviewAiResult result;
            try { result = await ai.ReviewAsync(new(job.Title, job.Role, job.Paragraphs, library, job.Mask, job.Note), timeout.Token, progress); }
            finally { stopReporting.Cancel(); await reporter; }
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
