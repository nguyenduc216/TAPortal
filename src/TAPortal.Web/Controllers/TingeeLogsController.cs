using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Integrations.Tingee;

namespace TAPortal.Web.Controllers;

public sealed record TingeeLogsVm(
    string? Code, string? Path, bool FailedOnly, TingeeCallLogEntry? Found, IReadOnlyList<TingeeCallLogEntry> Recent, string? Error);

// Tingee call log: every request TAPortal/Customer sent to the Tingee Open API (headers, body, Tingee's answer) under a log
// code shown with each error, so the exact request can be looked up and sent to Tingee support. System administrators only:
// bodies hold account numbers and ID numbers. The secret key is never stored.
[Authorize(Policy = AdminPermissions.Policy.IntegrationsLogsView)]
public sealed class TingeeLogsController(ITingeeCallLog log, ILogger<TingeeLogsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? code, string? path, bool failed = false)
    {
        var normalized = TingeeLogCode.Normalize(code);
        TingeeCallLogEntry? found = null;
        IReadOnlyList<TingeeCallLogEntry> recent = [];
        string? error = null;
        try
        {
            if (normalized is not null) found = await log.FindAsync(normalized, HttpContext.RequestAborted);
            recent = await log.RecentAsync(50, path, failed, HttpContext.RequestAborted);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "TINGEE_LOG_UNAVAILABLE");
            error = "Không đọc được bảng log dbo.TingeeApiLogs (đã chạy migration 014 chưa?).";
        }
        if (!string.IsNullOrWhiteSpace(code) && found is null && error is null)
            error = normalized is null ? "Mã log không hợp lệ." : $"Không tìm thấy log {normalized}.";
        ViewData["Title"] = "Tingee log";
        return View(new TingeeLogsVm(normalized ?? code?.Trim(), path, failed, found, recent, error));
    }

    /// <summary>The call as plain text, ready to paste to Tingee support.</summary>
    public static string Report(TingeeCallLogEntry e) =>
        $"""
        Log code: {e.LogCode}
        Time (UTC+7): {e.CreatedAtUtc.AddHours(7):yyyy-MM-dd HH:mm:ss.fff}
        Request: {e.Method} {e.Url}
        x-client-id: {e.ClientId}
        x-request-timestamp: {e.RequestTimestamp}
        x-signature: {e.Signature}
        Content-Type: application/json; charset=utf-8
        Body: {e.RequestBody ?? "(none; signed as {})"}

        Response HTTP {(e.HttpStatus?.ToString() ?? "—")} in {e.DurationMs} ms
        {(e.ResponseBody ?? e.Error ?? "(no response)")}
        """;
}
