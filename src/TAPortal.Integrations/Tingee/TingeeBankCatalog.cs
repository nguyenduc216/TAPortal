namespace TAPortal.Integrations.Tingee;

public static class TingeeAccountTypes
{
    public const string Personal = "personal-account";
    public const string Household = "business-household-account";
    public const string Business = "business-account";
    public static readonly IReadOnlyList<string> All = [Personal, Household, Business];

    public static string Label(string? type) => type switch
    {
        Personal => "Cá nhân",
        Household => "Hộ kinh doanh",
        Business => "Doanh nghiệp",
        _ => "—"
    };
}

/// <summary>Inputs the bank-link form may ask for. Values are only sent to Tingee when the rule asks for them.</summary>
public enum TingeeBankField
{
    AccountNumber,
    AccountName,
    Identity,
    Mobile,
    VaSuffix,
    MerchantName,
    VaAccountNumber,
    AcbUserId
}

public sealed record TingeeBankRule(
    IReadOnlySet<TingeeBankField> Required,
    bool SupportsDynamicQr,
    bool SupportsRootNotify,
    bool VaSupported,
    bool AsyncResult,
    string? Note);

public sealed record TingeeBank(string Bin, string Code, string Name, IReadOnlyList<string> AccountTypes, Func<string, TingeeBankRule> RuleFor)
{
    public TingeeBankRule Rule(string accountType) => RuleFor(accountType);
}

/// <summary>
/// Per-bank linking rules from developers.tingee.vn ("Lưu ý quan trọng", create-va, generate-dynamic-qr; openapi v1.2.2).
/// Kept in one place so a Tingee change is a table edit, not a form rewrite.
/// </summary>
public static class TingeeBankCatalog
{
    private static readonly string[] AllTypes = [TingeeAccountTypes.Personal, TingeeAccountTypes.Household, TingeeAccountTypes.Business];

    public static readonly IReadOnlyList<TingeeBank> Banks =
    [
        Bank("970436", "VCB", "Vietcombank", AllTypes, t => Rule(t,
            required: t == TingeeAccountTypes.Personal ? [TingeeBankField.AccountName, TingeeBankField.Mobile] : [],
            dynamicQr: t != TingeeAccountTypes.Personal, root: false, asyncResult: t != TingeeAccountTypes.Business,
            note: t == TingeeAccountTypes.Personal ? "Khách hàng xác thực trên ứng dụng VCB Digibank; kết quả liên kết được ngân hàng gửi về sau."
                : t == TingeeAccountTypes.Business ? "Tài khoản doanh nghiệp cần được cấu hình trước; một số trường hợp phải dùng API key riêng của merchant (Tingee mã 1133)." : null)),
        Bank("970418", "BIDV", "BIDV", AllTypes, t => t == TingeeAccountTypes.Business
            ? Rule(t, [TingeeBankField.AccountNumber, TingeeBankField.VaSuffix, TingeeBankField.MerchantName], dynamicQr: true, root: false,
                note: "Tên hiển thị phải bắt đầu bằng đúng tên doanh nghiệp đã đăng ký tại BIDV.")
            : Rule(t, Personalish(), dynamicQr: false, root: false)),
        Bank("970415", "CTG", "VietinBank", AllTypes, t => t == TingeeAccountTypes.Business
            ? Rule(t, [TingeeBankField.AccountNumber, TingeeBankField.VaSuffix, TingeeBankField.MerchantName], dynamicQr: true, root: false,
                note: "VietinBank không gửi nội dung chuyển khoản trong thông báo giao dịch.")
            : Rule(t, [TingeeBankField.AccountNumber, TingeeBankField.AccountName, TingeeBankField.Identity], dynamicQr: false, root: true,
                note: "VietinBank không gửi nội dung chuyển khoản trong thông báo giao dịch.")),
        Bank("970422", "MBB", "MB Bank", AllTypes, t => Rule(t, t == TingeeAccountTypes.Business ? [TingeeBankField.AccountNumber, TingeeBankField.AccountName, TingeeBankField.Mobile] : Personalish(),
            dynamicQr: true, root: false, note: t == TingeeAccountTypes.Business ? "Tài khoản doanh nghiệp MB cần T.A/Tingee cấu hình trước." : null)),
        Bank("970432", "VPB", "VPBank", AllTypes, t => Rule(t, t == TingeeAccountTypes.Business ? [TingeeBankField.AccountNumber, TingeeBankField.AccountName] : [TingeeBankField.AccountNumber, TingeeBankField.AccountName, TingeeBankField.Identity],
            dynamicQr: t == TingeeAccountTypes.Business, root: t != TingeeAccountTypes.Household,
            note: t == TingeeAccountTypes.Personal ? null : "Có thể cần API key riêng của merchant (Tingee mã 1133).")),
        Bank("970416", "ACB", "ACB", AllTypes, t => Rule(t,
            t == TingeeAccountTypes.Personal ? [TingeeBankField.AccountNumber, TingeeBankField.AccountName, TingeeBankField.Mobile]
                : [TingeeBankField.AccountNumber, TingeeBankField.AccountName, TingeeBankField.Mobile, TingeeBankField.AcbUserId],
            dynamicQr: false, root: true)),
        Bank("970448", "OCB", "OCB", AllTypes, t => t == TingeeAccountTypes.Business
            ? Rule(t, [TingeeBankField.AccountNumber, TingeeBankField.VaSuffix, TingeeBankField.MerchantName], dynamicQr: true, root: true,
                note: "Tài khoản doanh nghiệp OCB cần T.A/Tingee cấu hình trước.")
            : Rule(t, Personalish(), dynamicQr: t == TingeeAccountTypes.Personal, root: t == TingeeAccountTypes.Household)),
        Bank("970423", "TPB", "TPBank", AllTypes, t => Rule(t, [], dynamicQr: false, root: true, asyncResult: true,
            note: "Khách hàng xác thực trên TPBank; kết quả liên kết được ngân hàng gửi về sau.")),
        Bank("970403", "STB", "Sacombank", [TingeeAccountTypes.Personal, TingeeAccountTypes.Household], t => Rule(t, Personalish(), dynamicQr: false, root: false)),
        Bank("970441", "VIB", "VIB", AllTypes, t => Rule(t, t == TingeeAccountTypes.Business ? [TingeeBankField.AccountNumber] : [TingeeBankField.AccountNumber, TingeeBankField.AccountName, TingeeBankField.Identity],
            dynamicQr: false, root: false, note: "Bắt buộc đăng ký \"nhận loa\" trên VIB iBusiness trước khi liên kết.")),
        Bank("970426", "MSB", "MSB", AllTypes, t => Rule(t, [TingeeBankField.VaAccountNumber, TingeeBankField.Mobile], dynamicQr: false, root: false,
            note: "Số tài khoản ảo phải được khai báo trước tại MSB.")),
        Bank("970430", "PGB", "PGBank", AllTypes, t => Rule(t, [TingeeBankField.AccountNumber, TingeeBankField.Identity, TingeeBankField.Mobile], dynamicQr: false, root: true, vaSupported: false)),
        Bank("970446", "COOPBANK", "Co-opBank", AllTypes, t => Rule(t, Personalish(), dynamicQr: false, root: true, vaSupported: false)),
    ];

    public static TingeeBank? Find(string? bin) => Banks.FirstOrDefault(b => b.Bin == bin);

    public static bool SupportsDynamicQr(string? bin, string? accountType) =>
        Find(bin) is { } bank && accountType is not null && bank.AccountTypes.Contains(accountType) && bank.Rule(accountType).SupportsDynamicQr;

    private static TingeeBankField[] Personalish() => [TingeeBankField.AccountNumber, TingeeBankField.AccountName, TingeeBankField.Identity, TingeeBankField.Mobile];

    private static TingeeBank Bank(string bin, string code, string name, string[] types, Func<string, TingeeBankRule> rule) => new(bin, code, name, types, rule);

    private static TingeeBankRule Rule(string type, TingeeBankField[] required, bool dynamicQr, bool root, bool asyncResult = false, bool vaSupported = true, string? note = null) =>
        new(required.ToHashSet(), dynamicQr, root, vaSupported, asyncResult, note);
}
