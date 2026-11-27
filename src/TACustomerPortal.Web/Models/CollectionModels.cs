using System.ComponentModel.DataAnnotations;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Models;

// "Thu gộp công nợ" (payment collections, operator 2026-11-27).

public sealed record CollectionsVm(CustomerMembership Customer, bool CanManage, bool Linked, IReadOnlyList<Collection> Collections, DateTime UtcNow);

/// <summary>A buyer with unpaid orders: how many and how much is still owed.</summary>
public sealed record Debtor(Contact Contact, int Orders, decimal Due);

/// <summary>An unpaid order offered for a collection; InCollection is the open collection it already belongs to (then it cannot be chosen).</summary>
public sealed record DebtOrder(Order Order, string Status, (Guid Id, string CollectionNo)? InCollection);

public sealed record CollectionCreateVm(CustomerMembership Customer, bool Linked, IReadOnlyList<Debtor> Debtors, Contact? Contact, IReadOnlyList<DebtOrder> Orders,
    IReadOnlyList<BankAccount> Banks, CollectionInput Input);

public sealed class CollectionInput
{
    public Guid? ContactId { get; set; }

    /// <summary>The chosen orders, in the order the money pays them (the form posts them top to bottom).</summary>
    public List<Guid> OrderIds { get; set; } = [];

    /// <summary>The amount the QR asks for; empty = everything still due on the chosen orders.</summary>
    public decimal? Amount { get; set; }

    public Guid? BankAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự.")]
    public string? Description { get; set; }

    [Range(1, 43200, ErrorMessage = QrLifetime.RangeMessage)]
    public int QrExpireMinutes { get; set; } = 1440;
    [Range(0, 30, ErrorMessage = "Số ngày từ 0 đến 30.")] public int? QrExpireDays { get; set; }
    [Range(0, 23, ErrorMessage = "Số giờ từ 0 đến 23.")] public int? QrExpireHours { get; set; }
    [Range(0, 59, ErrorMessage = "Số phút từ 0 đến 59.")] public int? QrExpireMins { get; set; }
}

public sealed record CollectionDetailsVm(CustomerMembership Customer, bool CanManage, Collection Collection, string Status, Contact? Contact, BankAccount? Bank,
    string? QrSvg, DateTime UtcNow, IReadOnlyList<BankAccount> Banks, string? PayUrl, IReadOnlyList<CollectionPaymentRow> Payments);

/// <summary>The public page of a collection (/pay/c/{token}).</summary>
public sealed record CollectionPayVm(string Token, string SellerName, string? SellerLogo, string? BuyerName, string? BuyerLogo, Collection Collection, string Status,
    string? QrSvg, string? BankName, string? Account, string? AccountName, bool QrOnly, QrTransfer? Transfer, DateTime UtcNow)
{
    public string Title => string.IsNullOrWhiteSpace(Collection.Description) ? "Thanh toán công nợ " + Collection.CollectionNo : Collection.Description!;
    public bool Open => QrSvg is not null;
}

public static class CollectionText
{
    public static (string Label, string Css) Status(string status) => status switch
    {
        PaymentCollectionStatus.Awaiting => ("Chờ thanh toán", "cp-badge--blue"),
        PaymentCollectionStatus.Partial => ("Thu một phần", "cp-badge--gold"),
        PaymentCollectionStatus.Paid => ("Đã thu đủ", "cp-badge--green"),
        PaymentCollectionStatus.Expired => ("QR hết hạn", "cp-badge--red"),
        PaymentCollectionStatus.Cancelled => ("Đã hủy", "cp-badge--soft"),
        _ => (status, "cp-badge--soft")
    };
}
