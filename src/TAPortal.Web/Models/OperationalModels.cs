namespace TAPortal.Web.Models;
public sealed record PartnerListRow(Guid Id,string Code,string Name,string PartnerType,string? TaxCode,string Status,bool IsPlatformOwner);
public sealed record BankAccountListRow(Guid Id,string BankCode,string AccountNumber,string? AccountHolderName,bool Connected,string Status);
public sealed record BankTransactionListRow(Guid Id,DateTime TransactionDate,string Direction,decimal Amount,string? PaymentCode,string? Content,string AccountNumber);
public sealed record PaymentRequestListRow(Guid Id,string Code,string PaymentCode,decimal AmountDue,decimal AmountPaid,string Status,string? CustomerName);
public sealed record InvoiceListRow(Guid Id,string Ikey,decimal RequestedAmount,string Status,string CustomerName,DateTime RequestedAt);
public sealed record CreditWalletRow(string PartnerName,string CreditType,decimal CurrentBalance,decimal ReservedBalance);
public sealed record ProviderRow(string Code,string Name,string ProviderType,bool IsActive);
