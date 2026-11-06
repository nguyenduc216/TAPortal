using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Branding;

namespace TACustomerPortal.Web.Controllers;

// The customer's own customers (payers and e-invoice buyers). Soft state only: contacts are deactivated, never deleted,
// because orders keep pointing at them.
public sealed class ContactsController(ICurrentCustomerContext customer, IPaymentStore store, ILogoStore logos, TimeProvider clock) : CustomerAreaController(customer)
{
    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return View(new ContactsVm(m, CanManage(m), await store.ContactsAsync(m.CustomerId), await logos.BuyerLogoVersionsAsync(m.CustomerId)));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return CanManage(m) ? View("Edit", new ContactInput { Code = NextCode(await store.ContactsAsync(m.CustomerId)) }) : Forbid();
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var c = await store.ContactAsync(m.CustomerId, id);
        if (c is null) return NotFound();
        await LogoVersion(m, c.Id);
        return View(new ContactInput
        {
            Id = c.Id, Code = c.Code, Name = c.Name, LegalName = c.LegalName, TaxCode = c.TaxCode, Phone = c.Phone, Email = c.Email, Address = c.Address, Note = c.Note,
            BuyerType = c.Type, BuyerGroup = c.BuyerGroup, IdentityNumber = c.IdentityNumber, Zalo = c.Zalo, BankName = c.BankName, BankAccountNumber = c.BankAccountNumber,
            BankAccountName = c.BankAccountName
        });
    }

    [HttpPost]
    public Task<IActionResult> Create(ContactInput input, IFormFile? logo) { input.Id = Guid.Empty; return Save(input, logo, false); }

    [HttpPost]
    public Task<IActionResult> Edit(Guid id, ContactInput input, IFormFile? logo, bool removeLogo) { input.Id = id; return Save(input, logo, removeLogo); }

    /// <summary>The customer's logo, shown on its orders' payment pages next to the company's.</summary>
    [HttpGet]
    public async Task<IActionResult> Logo(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (await store.ContactAsync(m.CustomerId, id) is null) return NotFound();
        return await logos.LogoAsync(m.CustomerId, id) is { } image ? PayController.LogoFile(this, image) : NotFound();
    }

    private async Task<IActionResult> Save(ContactInput input, IFormFile? logo, bool removeLogo)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        if (input.Id != Guid.Empty && await store.ContactAsync(m.CustomerId, input.Id) is null) return NotFound();
        var (image, logoError) = await LogoUpload.ReadAsync(logo, clock.GetUtcNow().UtcDateTime, HttpContext.RequestAborted);
        if (logoError is not null) ModelState.AddModelError("Logo", logoError);
        input.Code = input.Code?.Trim().ToUpperInvariant() ?? "";
        var creating = input.Id == Guid.Empty;
        if (creating && input.Code.Length == 0)
        {
            // Left empty on a new customer: the next free code is taken.
            input.Code = NextCode(await store.ContactsAsync(m.CustomerId));
            ModelState.Remove(nameof(input.Code));
        }
        ApplyBuyerType(input);
        if (ModelState.IsValid)
        {
            var contact = new Contact(input.Id, input.Code, input.Name.Trim(), Blank(input.LegalName), Blank(input.TaxCode), Blank(input.Phone), Blank(input.Email), Blank(input.Address), Blank(input.Note), true,
                input.BuyerType, Blank(input.BuyerGroup), Blank(input.IdentityNumber)?.ToUpperInvariant(), Blank(input.Zalo), Blank(input.BankName), Blank(input.BankAccountNumber),
                Blank(input.BankAccountName)?.ToUpperInvariant());
            if (await store.SaveContactAsync(m.CustomerId, contact, Actor))
            {
                // A new contact's id is found by its code (unique per company).
                var savedId = input.Id != Guid.Empty ? input.Id : (await store.ContactsAsync(m.CustomerId)).FirstOrDefault(c => c.Code == contact.Code)?.Id;
                if (savedId is Guid logoOwner)
                {
                    if (image is not null) await logos.SaveLogoAsync(m.CustomerId, logoOwner, image, Actor);
                    else if (removeLogo) await logos.RemoveLogoAsync(m.CustomerId, logoOwner);
                }
                Flash(input.Id == Guid.Empty ? $"Đã thêm khách hàng {contact.Name}." : $"Đã cập nhật khách hàng {contact.Name}.");
                return RedirectToAction(nameof(Index));
            }
            var taken = input.Code;
            if (creating)
            {
                // Another user may have saved the suggested code meanwhile: suggest the next free one.
                input.Code = NextCode(await store.ContactsAsync(m.CustomerId));
                ModelState.Remove(nameof(input.Code));
            }
            ModelState.AddModelError(nameof(input.Code), creating ? $"Mã khách hàng đã được dùng ({taken}). Đã đề xuất mã mới {input.Code}, bấm Lưu để dùng mã này." : "Mã khách hàng đã được dùng.");
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        if (input.Id != Guid.Empty) await LogoVersion(m, input.Id);
        return View("Edit", input);
    }

    private async Task LogoVersion(CustomerMembership m, Guid contactId) =>
        ViewData["LogoVersion"] = await logos.LogoVersionAsync(m.CustomerId, contactId) is { } at ? LogoImage.Version(at) : null;

    /// <summary>
    /// Rules per buyer type (operator 2026-11-06): a business gives a tax code and its registered name; a household or an
    /// organisation may give a tax code; a retail customer (khách lẻ) needs only a name and may give a CCCD instead.
    /// Fields that do not apply to the chosen type are cleared, so a type change leaves no stale tax code or CCCD.
    /// </summary>
    private void ApplyBuyerType(ContactInput input)
    {
        input.BuyerType = BuyerTypes.Normalize(input.BuyerType) ?? BuyerTypes.Individual;
        // Numbers are often typed in groups ("001 088 012345") or pasted with a trailing space: checked again once the
        // spaces are gone (production 2026-11-06: a correct, looked-up tax code was refused for a pasted space).
        input.TaxCode = Revalidate(input, nameof(input.TaxCode), TaxCodeRules.Normalize(input.TaxCode));
        input.Phone = Revalidate(input, nameof(input.Phone), PhoneDigits(input.Phone) ?? Blank(input.Phone));
        // Zalo holds a number or a Zalo name: a number is kept as its digits, a name only trimmed.
        input.Zalo = PhoneDigits(input.Zalo) ?? Blank(input.Zalo);
        input.IdentityNumber = Revalidate(input, nameof(input.IdentityNumber), Blank(input.IdentityNumber)?.Replace(" ", ""));
        input.BankAccountNumber = Revalidate(input, nameof(input.BankAccountNumber), Blank(input.BankAccountNumber)?.Replace(" ", ""));
        switch (input.BuyerType)
        {
            case BuyerTypes.Individual:
                input.TaxCode = null;
                input.LegalName = null;
                ModelState.Remove(nameof(input.TaxCode));
                break;
            case BuyerTypes.Business:
                input.IdentityNumber = null;
                ModelState.Remove(nameof(input.IdentityNumber));
                if (input.TaxCode is null) ModelState.AddModelError(nameof(input.TaxCode), "Khách hàng doanh nghiệp cần mã số thuế.");
                else if (!TaxCodeRules.IsLookupable(input.TaxCode)) ModelState.AddModelError(nameof(input.TaxCode), "Mã số thuế doanh nghiệp gồm 10 chữ số (chi nhánh: 10 chữ số-3 chữ số).");
                if (string.IsNullOrWhiteSpace(input.LegalName)) ModelState.AddModelError(nameof(input.LegalName), "Nhập tên đơn vị theo đăng ký thuế (dùng trên hóa đơn).");
                break;
            case BuyerTypes.Organization:
                input.IdentityNumber = null;
                ModelState.Remove(nameof(input.IdentityNumber));
                if (string.IsNullOrWhiteSpace(input.LegalName)) ModelState.AddModelError(nameof(input.LegalName), "Nhập tên đơn vị (dùng trên hóa đơn).");
                break;
        }
        if (input.BankAccountNumber is not null && string.IsNullOrWhiteSpace(input.BankName))
            ModelState.AddModelError(nameof(input.BankName), "Nhập tên ngân hàng của số tài khoản.");
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(Guid id, [FromForm] bool active)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        if (await store.ContactAsync(m.CustomerId, id) is not { } c) return NotFound();
        await store.SetContactActiveAsync(m.CustomerId, id, active, Actor);
        Flash(active ? $"Đã kích hoạt lại {c.Name}." : $"Đã ngừng sử dụng {c.Name}. Đơn hàng cũ vẫn giữ thông tin.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Next automatic customer code: KH + one more than the highest KH number in use (inactive ones included), at least 3 digits.</summary>
    internal static string NextCode(IEnumerable<Contact> contacts)
    {
        var numbers = contacts.Select(c => System.Text.RegularExpressions.Regex.Match(c.Code, "^KH(\\d{1,9})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Where(x => x.Success).Select(x => x.Groups[1].Value).ToList();
        var next = numbers.Count == 0 ? 1 : numbers.Max(long.Parse) + 1;
        var width = Math.Max(3, numbers.Count == 0 ? 0 : numbers.Max(n => n.Length));
        return "KH" + next.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width, '0');
    }

    private string? Revalidate(ContactInput input, string property, string? value)
    {
        ModelState.Remove(property);
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateProperty(value, new(input) { MemberName = property }, errors))
            foreach (var e in errors) ModelState.AddModelError(property, e.ErrorMessage ?? "Giá trị không hợp lệ.");
        return value;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A phone number copied from elsewhere ("0366 699 961", "0366.699.961", "+84 366 699 961") as its digits with the
    /// leading 0; null when the text is not a phone number (e.g. a Zalo name), so the caller keeps it as typed.
    /// </summary>
    public static string? PhoneDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = string.Concat(value.Where(c => !char.IsWhiteSpace(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format && c is not ('.' or '-' or '(' or ')')));
        if (s.StartsWith('+')) s = s[1..];
        if (s.Length == 0 || !s.All(char.IsAsciiDigit)) return null;
        return s.StartsWith("84", StringComparison.Ordinal) && s.Length == 11 ? "0" + s[2..] : s;
    }
}
