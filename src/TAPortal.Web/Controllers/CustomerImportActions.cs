using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using TAPortal.Web.Models;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Customers → "Nhập từ Excel": the internal workbook "Thông tin khách hàng.xlsx" (one sheet per customer) is read,
// shown sheet by sheet for review, and saved only for the sheets the user confirms. The file stays in server memory
// for 30 minutes between preview and confirmation (tied to the user), is never written to disk, and is dropped after
// the import. Passwords never appear on the review page; they are encrypted when saved.
public sealed partial class CustomersController
{
    internal const long ImportMaxBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan ImportLifetime = TimeSpan.FromMinutes(30);
    private const string ImportPermission = PermissionPolicies.Prefix + "CRM.CUSTOMERS.CREATE";
    private const string ImportUpdatePermission = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE"; // it also fills existing customers
    private static string ImportKey(Guid actor, Guid token) => $"customer-import:{actor:N}:{token:N}";

    [HttpGet]
    [Authorize(Policy = ImportPermission)]
    [Authorize(Policy = ImportUpdatePermission)]
    public IActionResult Import([FromServices] ICustomerSecretProtector secrets) => View(new CustomerImportVm(null, null, null, secrets.Enabled));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = ImportPermission)]
    [Authorize(Policy = ImportUpdatePermission)]
    [RequestSizeLimit(ImportMaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportMaxBytes + 1024 * 1024)]
    public async Task<IActionResult> ImportPreview(IFormFile? file, [FromServices] ICustomerImportStore imports, [FromServices] ICustomerSecretProtector secrets, [FromServices] IMemoryCache cache)
    {
        if (ActorOrNull() is not Guid actor) return Forbid();
        if (file is null || file.Length == 0) ModelState.AddModelError(string.Empty, "Vui lòng chọn file Excel (.xlsx).");
        else if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) ModelState.AddModelError(string.Empty, "Chỉ nhận file Excel .xlsx (file .xls cũ: mở bằng Excel rồi lưu lại dạng .xlsx).");
        else if (file.Length > ImportMaxBytes) ModelState.AddModelError(string.Empty, "File lớn hơn 10 MB.");
        if (!ModelState.IsValid) return ImportError(secrets);

        byte[] bytes;
        await using (var s = file!.OpenReadStream())
        using (var ms = new MemoryStream())
        {
            await s.CopyToAsync(ms);
            bytes = ms.ToArray();
        }
        var (sheets, parsed, error) = ParseWorkbook(bytes);
        if (error is not null) { ModelState.AddModelError(string.Empty, error); return ImportError(secrets); }
        if (parsed!.Count == 0) { ModelState.AddModelError(string.Empty, "Không tìm thấy sheet hồ sơ khách hàng nào (sheet có khối \"Thông tin chung\")."); return ImportError(secrets); }

        IReadOnlyDictionary<string, CustomerMatch> matches;
        try { matches = await imports.CustomersByTaxCodeAsync(); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Customer import: customers could not be read.");
            ModelState.AddModelError(string.Empty, ProfileStoreUnavailable);
            return ImportError(secrets);
        }

        var token = Guid.NewGuid();
        cache.Set(ImportKey(actor, token), new ImportUpload(file.FileName, bytes), ImportLifetime);
        return View(nameof(Import), new CustomerImportVm(token, Path.GetFileName(file.FileName), Rows(parsed, matches), secrets.Enabled, sheets - parsed.Count));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = ImportPermission)]
    [Authorize(Policy = ImportUpdatePermission)]
    public async Task<IActionResult> ImportConfirm(Guid token, [FromForm] List<int>? include, [FromForm] Dictionary<int, string>? codes,
        [FromServices] ICustomerImportStore imports, [FromServices] ICustomerSecretProtector secrets, [FromServices] IMemoryCache cache)
    {
        if (ActorOrNull() is not Guid actor) return Forbid();
        if (!cache.TryGetValue(ImportKey(actor, token), out ImportUpload? upload) || upload is null)
        {
            TempData["Error"] = "Phiên nhập dữ liệu đã hết hạn (30 phút) hoặc đã được dùng. Vui lòng chọn lại file.";
            return RedirectToAction(nameof(Import));
        }
        var (_, parsed, error) = ParseWorkbook(upload.Bytes);
        if (error is not null || parsed is null) { TempData["Error"] = error; return RedirectToAction(nameof(Import)); }
        var selected = (include ?? []).Distinct().Where(i => i >= 0 && i < parsed.Count).Order().ToList();
        if (selected.Count == 0)
        {
            TempData["Error"] = "Chưa chọn khách hàng nào để nhập.";
            return RedirectToAction(nameof(Import));
        }
        cache.Remove(ImportKey(actor, token)); // one confirmation per upload

        var results = new List<CustomerImportResultRow>();
        IReadOnlyDictionary<string, CustomerMatch> matches;
        try { matches = await imports.CustomersByTaxCodeAsync(); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Customer import: customers could not be read.");
            TempData["Error"] = ProfileStoreUnavailable;
            return RedirectToAction(nameof(Import));
        }
        var codesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in selected)
        {
            var data = parsed[i];
            var match = data.TaxCode is { } tax && matches.TryGetValue(tax, out var m) ? m : null;
            string? code = null;
            if (match is null)
            {
                code = (codes?.GetValueOrDefault(i) ?? data.SuggestedCode).Trim().ToUpperInvariant();
                string? invalid = data.Name is null ? "Không có tên khách hàng trong sheet, không tạo được khách hàng mới."
                    : !CustomerCodePattern().IsMatch(code) ? "Mã khách hàng chỉ gồm chữ không dấu, số, dấu chấm, gạch (tối đa 50 ký tự)."
                    : !codesInFile.Add(code) ? $"Mã khách hàng {code} bị trùng với sheet khác trong lần nhập này." : null;
                if (invalid is not null) { results.Add(new(data.SheetName, code, data.Name, null, null, invalid)); continue; }
            }
            var id = match?.Id ?? Guid.NewGuid();
            try
            {
                var result = await imports.ImportAsync(new CustomerImportItem(id, code, data), actor);
                var message = result.Status switch
                {
                    CustomerImportStatus.DuplicateCode => $"Mã khách hàng {result.ConflictingCode ?? code} đã có. Sửa mã rồi nhập lại sheet này.",
                    CustomerImportStatus.DuplicateTaxCode => $"Mã số thuế đã thuộc khách hàng {result.ConflictingCode}.",
                    CustomerImportStatus.InvalidTaxCode => "Mã số thuế không đúng định dạng.",
                    CustomerImportStatus.NotFound => "Khách hàng khớp MST đã bị xóa.",
                    CustomerImportStatus.AuditUnavailable => AuditUnavailableMessage,
                    _ => null,
                };
                results.Add(new(data.SheetName, match?.Code ?? code, match?.Name ?? data.Name, message is null ? id : null, result, message));
            }
            catch (SqlException ex)
            {
                logger.LogWarning(ex, "Customer import failed for one sheet (customer {CustomerId}).", id);
                results.Add(new(data.SheetName, match?.Code ?? code, match?.Name ?? data.Name, null, null, ProfileStoreUnavailable));
            }
        }
        logger.LogInformation("Customer import by {ActorId}: {Ok} of {Total} sheets saved.", actor, results.Count(r => r.Error is null), results.Count);
        return View("ImportResult", new CustomerImportResultVm(Path.GetFileName(upload.FileName), results, secrets.Enabled));
    }

    private sealed record ImportUpload(string FileName, byte[] Bytes);

    private static (int Sheets, List<ImportedCustomer>? Parsed, string? Error) ParseWorkbook(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var sheets = XlsxReader.Read(ms);
            return (sheets.Count, CustomerWorkbookParser.Parse(sheets), null);
        }
        catch (XlsxFormatException ex) { return (0, null, ex.Message); }
    }

    private static List<CustomerImportRow> Rows(List<ImportedCustomer> parsed, IReadOnlyDictionary<string, CustomerMatch> matches)
    {
        var used = new HashSet<string>(matches.Values.Select(m => m.Code), StringComparer.OrdinalIgnoreCase);
        return parsed.Select((c, i) =>
        {
            var match = c.TaxCode is { } tax && matches.TryGetValue(tax, out var m) ? m : null;
            var code = c.SuggestedCode;
            if (match is null && code.Length > 0) for (var n = 2; !used.Add(code); n++) code = $"{c.SuggestedCode}{n}";
            return new CustomerImportRow(i, c, match, code);
        }).ToList();
    }

    private ViewResult ImportError(ICustomerSecretProtector secrets)
    {
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(nameof(Import), new CustomerImportVm(null, null, null, secrets.Enabled));
    }

    [GeneratedRegex("^[A-Z0-9_.-]{1,50}$")]
    private static partial Regex CustomerCodePattern();
}
