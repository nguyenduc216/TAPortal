using Microsoft.AspNetCore.Mvc;
using TAPortal.Shared;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Customer form → code suggested from the name, and "Tra cứu" beside Mã số thuế: name, address and tax status from the public business register (VietQR,
// sourced from the tax authority). Only the tax code leaves the server. The register has no representative, phone, email
// or logo, so those stay manual.
public sealed partial class CustomersController
{
    [HttpGet]
    public async Task<IActionResult> TaxLookup(string? code, [FromServices] ITaxCodeLookup lookup)
    {
        var result = await lookup.LookupAsync(code, HttpContext.RequestAborted);
        var message = result.Status switch
        {
            TaxCodeLookupStatus.Found => "Đã điền tên và địa chỉ theo đăng ký thuế.",
            TaxCodeLookupStatus.NotFound => "Không tìm thấy mã số thuế này. Kiểm tra lại hoặc nhập tay.",
            TaxCodeLookupStatus.Invalid => "Chỉ tra cứu được mã số thuế doanh nghiệp 10 chữ số (hoặc 10-3).",
            _ => "Tạm thời chưa tra cứu được. Vui lòng nhập tay."
        };
        var info = result.Info;
        return Json(new { ok = info is not null, name = info?.Name, address = info?.Address, taxStatus = info?.TaxStatus, message });
    }

    /// <summary>New customer form: a free customer code suggested from the name (staff may overwrite it).</summary>
    [HttpGet]
    public async Task<IActionResult> SuggestCode(string? name)
    {
        var code = CustomerCodeSuggest.FromName(name);
        if (code.Length > 0) code = CustomerCodeSuggest.Unique(code, await customers.CodesStartingWithAsync(code[..Math.Min(code.Length, CustomerCodeSuggest.MaxLength - 2)]));
        return Json(new { code });
    }
}
