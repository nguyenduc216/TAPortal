using System.ComponentModel.DataAnnotations;
using TAPortal.Integrations.Tingee;

namespace TAPortal.Web.Models;

// Tingee Sandbox (Admin → Hệ thống): T.A tests its own Tingee Merchant account (Direct mode) end to end — connection,
// bank account linking, a test QR and the webhooks that arrive — before any customer uses Tingee.

public sealed record TingeeSandboxConfig(bool Configured, bool Direct, string BaseUrl, bool Production, bool InboxAvailable,
    string PaymentWebhookUrl, string VaStatusWebhookUrl, string BankLinkWebhookUrl, string? VaStatusWebhookConfigured);

public sealed record TingeeSandboxConnection(bool Ok, IReadOnlyList<TingeeVaItem> Accounts, string? Error);

public sealed record TingeeSandboxQr(string Svg, string Payload, string Type, string BankName, string Account, decimal Amount, string? Purpose,
    string? QrAccount, string? BillId, DateTime? ExpiresAtUtc);

public sealed record TingeeSandboxOtp(string BankBin, string BankName, string ConfirmId, string? OtpMethod);

public sealed record TingeeSandboxLinkResult(string BankName, string Message, string? ActionLink, TingeeSandboxOtp? Otp);

public sealed record TingeeSandboxWebhook(long Id, string Kind, string Status, DateTime ReceivedAtUtc, decimal? Amount, string? Content,
    string? Account, string? TransactionCode, string? TransactionTime, string? Bank, string RawBody);

public sealed record TingeeSandboxVm(
    TingeeSandboxConfig Config,
    TingeeSandboxConnection? Connection,
    TingeeBank? Bank,
    string? AccountType,
    TingeeSandboxBankInput BankInput,
    TingeeSandboxLinkResult? LinkResult,
    TingeeSandboxQrInput QrInput,
    TingeeSandboxQr? Qr,
    IReadOnlyList<TingeeSandboxWebhook> Webhooks,
    string? WebhookError);

public sealed class TingeeSandboxBankInput
{
    public string? Bin { get; set; }
    public string? AccountType { get; set; }
    [StringLength(30)] public string? AccountNumber { get; set; }
    [StringLength(100)] public string? AccountName { get; set; }
    [StringLength(20)] public string? Identity { get; set; }
    [RegularExpression("^0[0-9]{9}$", ErrorMessage = "Số điện thoại gồm 10 chữ số, bắt đầu bằng 0.")] public string? Mobile { get; set; }
    [StringLength(20)] public string? VaSuffix { get; set; }
    [StringLength(100)] public string? MerchantName { get; set; }
    [StringLength(30)] public string? VaAccountNumber { get; set; }
    [StringLength(50)] public string? AcbUserId { get; set; }
    public bool NotifyRootAccount { get; set; } = true;
}

public sealed class TingeeSandboxQrInput
{
    /// <summary>"bankBin|vaOrAccountNumber|accountType" of an account returned by get-va-paging.</summary>
    [Required(ErrorMessage = "Chọn tài khoản nhận tiền.")] public string? Account { get; set; }
    [Range(1000, 10_000_000, ErrorMessage = "Số tiền thử từ 1.000đ đến 10.000.000đ.")] public decimal Amount { get; set; } = 2000;
    [StringLength(50)] public string? Purpose { get; set; } = "TA SANDBOX";
    /// <summary>"dynamic" (Tingee tracks the bill) or "static" (plain VietQR; payments arrive only by webhook).</summary>
    public string Type { get; set; } = "dynamic";
}
