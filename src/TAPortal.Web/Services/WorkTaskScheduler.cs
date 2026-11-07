namespace TAPortal.Web.Services;

// Creates the tasks of recurring templates (Công việc → Việc lặp theo kỳ) once an hour, starting a minute after start-up.
// Idempotent: one task per (template, period) is enforced by SqlWorkTaskStore and UX_WorkTasks_TemplatePeriod, so several
// app instances or a manual "Tạo việc kỳ này ngay" never duplicate a period.
public sealed class WorkTaskScheduler(IServiceScopeFactory scopes, ILogger<WorkTaskScheduler> logger) : BackgroundService
{
    internal static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1), Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstRun, stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var created = await scope.ServiceProvider.GetRequiredService<IWorkTaskStore>().GenerateAsync(WorkClock.Today());
                if (created > 0) logger.LogInformation("Recurring work tasks created: {Count}.", created);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable or migration 031 not applied yet: try again at the next tick.
                logger.LogWarning(ex, "WORK_TEMPLATE_SCHEDULER_FAILED");
            }
        }
        while (await NextTick(timer, stoppingToken));
    }

    private static async Task<bool> NextTick(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); } catch (OperationCanceledException) { return false; }
    }
}

// Admin-only response headers that differ from TAPortal.Shared.SecurityHeaders.
public static class AdminHeaders
{
    public const string PermissionsPolicy = "camera=(), microphone=(self), geolocation=()";
}
