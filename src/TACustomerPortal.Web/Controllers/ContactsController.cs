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
        return CanManage(m) ? View("Edit", new ContactInput()) : Forbid();
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var c = await store.ContactAsync(m.CustomerId, id);
        return c is null ? NotFound() : View(new ContactInput { Id = c.Id, Code = c.Code, Name = c.Name, LegalName = c.LegalName, TaxCode = c.TaxCode, Phone = c.Phone, Email = c.Email, Address = c.Address, Note = c.Note });
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
        if (ModelState.IsValid)
        {
            var contact = new Contact(input.Id, input.Code, input.Name.Trim(), Blank(input.LegalName), Blank(input.TaxCode), Blank(input.Phone), Blank(input.Email), Blank(input.Address), Blank(input.Note), true);
            if (await store.SaveContactAsync(m.CustomerId, contact, Actor))
            {
                Flash(input.Id == Guid.Empty ? $"Đã thêm khách hàng {contact.Name}." : $"Đã cập nhật khách hàng {contact.Name}.");
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(nameof(input.Code), "Mã khách hàng đã được dùng.");
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", input);
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

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
