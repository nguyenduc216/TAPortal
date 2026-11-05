using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Controllers;

// The customer's own customers (payers and e-invoice buyers). Soft state only: contacts are deactivated, never deleted,
// because orders keep pointing at them.
public sealed class ContactsController(ICurrentCustomerContext customer, IPaymentStore store) : CustomerAreaController(customer)
{
    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return View(new ContactsVm(m, CanManage(m), await store.ContactsAsync(m.CustomerId)));
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
        return c is null ? NotFound() : View(new ContactInput
        {
            Id = c.Id, Code = c.Code, Name = c.Name, LegalName = c.LegalName, TaxCode = c.TaxCode, Phone = c.Phone, Email = c.Email, Address = c.Address, Note = c.Note,
            BuyerType = c.Type, BuyerGroup = c.BuyerGroup, IdentityNumber = c.IdentityNumber, Zalo = c.Zalo, BankName = c.BankName, BankAccountNumber = c.BankAccountNumber,
            BankAccountName = c.BankAccountName
        });
    }

    [HttpPost]
    public Task<IActionResult> Create(ContactInput input) { input.Id = Guid.Empty; return Save(input); }

    [HttpPost]
    public Task<IActionResult> Edit(Guid id, ContactInput input) { input.Id = id; return Save(input); }

    private async Task<IActionResult> Save(ContactInput input)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        if (input.Id != Guid.Empty && await store.ContactAsync(m.CustomerId, input.Id) is null) return NotFound();
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
        return View("Edit", input);
    }

    /// <summary>
    /// Rules per buyer type (operator 2026-11-06): a business gives a tax code and its registered name; a household or an
    /// organisation may give a tax code; a retail customer (khách lẻ) needs only a name and may give a CCCD instead.
    /// Fields that do not apply to the chosen type are cleared, so a type change leaves no stale tax code or CCCD.
    /// </summary>
    private void ApplyBuyerType(ContactInput input)
    {
        input.BuyerType = BuyerTypes.Normalize(input.BuyerType) ?? BuyerTypes.Individual;
        input.TaxCode = TaxCodeRules.Normalize(input.TaxCode);
        // Numbers are often typed in groups ("001 088 012345"): checked again once the spaces are gone.
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
}
