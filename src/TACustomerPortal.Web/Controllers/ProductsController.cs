using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Branding;

namespace TACustomerPortal.Web.Controllers;

// Product catalogue of the company (operator request 2026-11-09): name, image, reference price including VAT, tax rate,
// status. Products are picked on the order form; prices agreed with a customer are kept per customer (IProductStore).
// Products are deactivated, never deleted, because order lines keep pointing at them.
public sealed class ProductsController(ICurrentCustomerContext customer, IProductStore products, TimeProvider clock) : CustomerAreaController(customer)
{
    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return View(new ProductsVm(m, CanManage(m), await products.ProductsAsync(m.CustomerId)));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return CanManage(m) ? View("Edit", new ProductEditVm(new ProductInput(), null, [])) : Forbid();
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        if (await products.ProductAsync(m.CustomerId, id) is not { } p) return NotFound();
        var input = new ProductInput { Id = p.Id, Name = p.Name, UnitName = p.UnitName, Price = p.Price, TaxRate = p.TaxRateCode, IsActive = p.IsActive };
        return View(new ProductEditVm(input, Version(p), await products.ProductPricesAsync(m.CustomerId, id)));
    }

    [HttpPost]
    public Task<IActionResult> Create(ProductInput input, IFormFile? image) { input.Id = Guid.Empty; return Save(input, image, false); }

    [HttpPost]
    public Task<IActionResult> Edit(Guid id, ProductInput input, IFormFile? image, bool removeImage) { input.Id = id; return Save(input, image, removeImage); }

    private async Task<IActionResult> Save(ProductInput input, IFormFile? image, bool removeImage)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var existing = input.Id == Guid.Empty ? null : await products.ProductAsync(m.CustomerId, input.Id);
        if (input.Id != Guid.Empty && existing is null) return NotFound();
        var (picture, imageError) = await LogoUpload.ReadAsync(image, clock.GetUtcNow().UtcDateTime, HttpContext.RequestAborted);
        if (imageError is not null) ModelState.AddModelError("Image", imageError.Replace("Logo", "Ảnh sản phẩm", StringComparison.Ordinal));
        if (!OrderMath.IsTaxRate(input.TaxRate)) ModelState.AddModelError(nameof(input.TaxRate), "Thuế suất không hợp lệ.");
        if (input.Price is decimal price && decimal.Truncate(price) != price) ModelState.AddModelError(nameof(input.Price), "Đơn giá là số đồng (không lẻ).");
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("Edit", new ProductEditVm(input, existing is null ? null : Version(existing), existing is null ? [] : await products.ProductPricesAsync(m.CustomerId, existing.Id)));
        }
        var product = new Product(input.Id, input.Name.Trim(), string.IsNullOrWhiteSpace(input.UnitName) ? null : input.UnitName.Trim(), input.Price!.Value, input.TaxRate, input.IsActive, null);
        await products.SaveProductAsync(m.CustomerId, product, picture, removeImage, Actor);
        Flash(input.Id == Guid.Empty ? $"Đã thêm sản phẩm {product.Name}." : $"Đã cập nhật sản phẩm {product.Name}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(Guid id, [FromForm] bool active)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        if (await products.ProductAsync(m.CustomerId, id) is not { } p) return NotFound();
        await products.SetActiveAsync(m.CustomerId, id, active, Actor);
        Flash(active ? $"Đã mở bán lại {p.Name}." : $"Đã ngừng bán {p.Name}. Đơn hàng cũ vẫn giữ thông tin.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Removes the price agreed with one customer: that customer is back on the catalogue price.</summary>
    [HttpPost]
    public async Task<IActionResult> RemovePrice(Guid id, Guid buyerId)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        if (await products.ProductAsync(m.CustomerId, id) is null) return NotFound();
        await products.RemoveBuyerPriceAsync(m.CustomerId, buyerId, id);
        Flash("Đã xóa giá riêng. Khách hàng này sẽ dùng giá trong danh mục.");
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Image(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        return await products.ImageAsync(m.CustomerId, id) is { } image ? PayController.LogoFile(this, image) : NotFound();
    }

    private static string? Version(Product p) => p.ImageAt is DateTime at ? LogoImage.Version(at) : null;
}
