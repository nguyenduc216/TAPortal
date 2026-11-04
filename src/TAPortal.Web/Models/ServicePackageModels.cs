using System.ComponentModel.DataAnnotations;
using TAPortal.Shared;

namespace TAPortal.Web.Models;

// Admin → Gói dịch vụ (catalogue, purchases, payment confirmation) and the customer workspace tab "Gói dịch vụ".
// StoreError is set when the tables of migration 018 cannot be read (missing or no permission).
public sealed record ServicePackagesVm(IReadOnlyList<ServicePackage> Packages, string? StoreError);

public sealed record ServicePackageEditVm(Guid? Id, ServicePackageInput Input);

public sealed record ServicePurchasesVm(IReadOnlyList<ServicePurchase> Purchases, string? Status, string? Query, DateTime NowUtc, string? StoreError);

public sealed record ServicePurchaseVm(ServicePurchase Purchase, ServicePurchaseConfirmInput Confirm, DateTime NowUtc);

public sealed record CustomerServicesVm(CustomerEditVm Customer, ServiceBalance Balance, IReadOnlyList<ServicePurchase> Purchases,
    IReadOnlyList<ServicePackage> Packages, DateTime NowUtc, string? StoreError);

public static class ServicePaymentMethods
{
    public const string BankTransfer = "BANK_TRANSFER";
    public const string Cash = "CASH";
    public const string Other = "OTHER";
    public static readonly IReadOnlyList<(string Code, string Label)> All = [(BankTransfer, "Chuyển khoản"), (Cash, "Tiền mặt"), (Other, "Khác")];
    public static string Label(string? code) => All.FirstOrDefault(m => m.Code == code).Label ?? code ?? "—";
}

/// <summary>Admin confirms a pending purchase was paid (until automatic payment replaces this step).</summary>
public sealed class ServicePurchaseConfirmInput
{
    [Range(typeof(decimal), "0", "100000000000", ErrorMessage = "Số tiền đã nhận không hợp lệ.")]
    public decimal PaidAmount { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn hình thức thanh toán.")]
    public string Method { get; set; } = ServicePaymentMethods.BankTransfer;

    [StringLength(200, ErrorMessage = "Mã tham chiếu tối đa 200 ký tự.")]
    public string? Reference { get; set; }
}
