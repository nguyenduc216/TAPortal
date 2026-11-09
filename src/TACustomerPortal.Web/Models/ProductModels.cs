using System.ComponentModel.DataAnnotations;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Models;

public sealed record ProductsVm(CustomerMembership Customer, bool CanManage, IReadOnlyList<Product> Products);

/// <summary>Edit page: the form plus, for an existing product, the customers that have their own price for it.</summary>
public sealed record ProductEditVm(ProductInput Input, string? ImageVersion, IReadOnlyList<BuyerPrice> Prices);

public sealed class ProductInput
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Nhập tên sản phẩm.")]
    [StringLength(500, ErrorMessage = "Tên sản phẩm tối đa 500 ký tự.")]
    public string Name { get; set; } = "";

    [StringLength(50, ErrorMessage = "Đơn vị tính tối đa 50 ký tự.")]
    public string? UnitName { get; set; }

    /// <summary>Reference price including VAT, whole dong.</summary>
    [Required(ErrorMessage = "Nhập đơn giá.")]
    [Range(typeof(decimal), "0", "100000000000", ErrorMessage = "Đơn giá từ 0 đến 100 tỷ đồng.")]
    public decimal? Price { get; set; }

    public string TaxRate { get; set; } = "0";

    public bool IsActive { get; set; } = true;
}
