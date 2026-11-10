using System.Globalization;

namespace TACustomerPortal.Web.Services;

/// <summary>Who receives a VietQR payment, read from the QR itself: bank BIN, account, amount and transfer content.</summary>
public sealed record QrTransfer(string Bin, string Account, decimal? Amount, string? Content);

/// <summary>
/// A banking app that VietQR can open (https://api.vietqr.io/v2/*-app-deeplinks). <paramref name="Autofill"/>: the app opens
/// its transfer screen with account, amount and content filled in from the QR (VietQR's "autofill" flag, the same on
/// Android and iOS); the others only open the app's home screen.
/// </summary>
public sealed record BankApp(string Id, string Name, bool Autofill = false);

/// <summary>
/// "Chuyển khoản" on the public payment page (operator 2026-11-06): opens the payer's banking app with the same transfer as
/// the QR — like tapping a QR image in Zalo. The link is VietQR's app deeplink (https://dl.vietqr.io/pay?app=…), built
/// only from the order's own QR payload, so the app receives exactly what scanning would give it.
/// Operator 2026-11-10: the app opened without the transfer. VietQR expects <c>ba=ACCOUNT@BANKCODE</c> with its short bank
/// code (mb, vcb…), not the BIN, and a return <c>url</c>; with both it hands an autofill app the QR itself (verified with MB:
/// <c>mbbank://applink?targetPage=QRPay&amp;qrContent=…</c>), so the app shows the transfer locked as when scanning.
/// </summary>
public static partial class BankAppLinks
{
    /// <summary>Apps VietQR supports on both Android and iOS: the autofill ones first, then by installs (lists of 2026-11-10).</summary>
    public static readonly IReadOnlyList<BankApp> Apps =
    [
        new("mb", "MB Bank", true), new("bidv", "BIDV", true), new("icb", "VietinBank", true), new("acb", "ACB", true), new("ocb", "OCB", true),
        new("vcb", "Vietcombank"), new("tcb", "Techcombank"), new("vba", "Agribank"), new("vpb", "VPBank"), new("tpb", "TPBank"),
        new("cake", "Cake"), new("timo", "Timo"), new("shb", "SHB"), new("hdb", "HDBank"), new("vib-2", "VIB"),
        new("scb", "SCB"), new("seab", "SeABank"), new("lpb", "LPBank"), new("eib", "Eximbank"), new("nab", "Nam A Bank"),
        new("abb", "ABBank"), new("klb", "KienlongBank"), new("pvcb", "PVcomBank"), new("bvb", "BaoViet Bank"), new("ncb", "NCB"),
        new("shbvn", "Shinhan"), new("wvn", "Woori"), new("cimb", "CIMB"), new("vab", "VietABank"), new("coopbank", "Co-opBank"),
    ];

    /// <summary>VietQR bank codes by BIN (https://api.vietqr.io/v2/banks, 2026-11-10), for the <c>ba</c> parameter.</summary>
    private static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>
    {
        ["970415"] = "icb", ["970436"] = "vcb", ["970418"] = "bidv", ["970405"] = "vba", ["970448"] = "ocb", ["970422"] = "mb",
        ["970407"] = "tcb", ["970416"] = "acb", ["970432"] = "vpb", ["970423"] = "tpb", ["970403"] = "stb", ["970437"] = "hdb",
        ["970454"] = "vccb", ["970429"] = "scb", ["970441"] = "vib", ["970443"] = "shb", ["970431"] = "eib", ["970426"] = "msb",
        ["546034"] = "cake", ["546035"] = "ubank", ["963388"] = "timo", ["970400"] = "sgicb", ["970409"] = "bab", ["971133"] = "pvdb",
        ["970412"] = "pvcb", ["970414"] = "mbv", ["970419"] = "ncb", ["970424"] = "shbvn", ["970425"] = "abb", ["970427"] = "vab",
        ["970428"] = "nab", ["970430"] = "pgb", ["970433"] = "vietbank", ["970438"] = "bvb", ["970440"] = "seab", ["970446"] = "coopbank",
        ["970449"] = "lpb", ["970452"] = "klb", ["668888"] = "kbank", ["970406"] = "vikki", ["970439"] = "pbvn", ["970410"] = "scvn",
        ["422589"] = "cimb", ["970458"] = "uob", ["970444"] = "cbb", ["970408"] = "gpb", ["970421"] = "vrb", ["970457"] = "wvn",
        ["458761"] = "hsbc", ["970442"] = "hlbvn", ["970434"] = "ivb",
    };

    /// <summary>VietQR's bank code for a BIN; the BIN itself when unknown.</summary>
    public static string BankCode(string bin) => Codes.TryGetValue(bin, out var code) ? code : bin;

    public static string Icon(BankApp app) => $"https://cdn.vietqr.io/bank-app-icon/{app.Id}-android.webp";

    /// <param name="back">Where the bank app returns after paying: the payment page itself (absolute URL).</param>
    public static string Link(BankApp app, QrTransfer t, string back)
    {
        var q = $"https://dl.vietqr.io/pay?app={Uri.EscapeDataString(app.Id)}&ba={Uri.EscapeDataString(t.Account + "@" + BankCode(t.Bin))}";
        if (t.Amount is decimal am) q += "&am=" + am.ToString("0", CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(t.Content)) q += "&tn=" + Uri.EscapeDataString(t.Content);
        return q + "&url=" + Uri.EscapeDataString(back);
    }

    /// <summary>
    /// Reads an EMVCo VietQR payload: merchant account info (tag 38: 01 → 00 BIN, 01 account), amount (54) and the
    /// purpose of the transaction (62 → 08). Null when the payload is not a readable bank-transfer QR.
    /// </summary>
    public static QrTransfer? Read(string? payload)
    {
        var top = Tags(payload);
        if (top is null || !top.TryGetValue("38", out var merchant) || Tags(merchant) is not { } m || !m.TryGetValue("01", out var beneficiary)
            || Tags(beneficiary) is not { } b || !b.TryGetValue("00", out var bin) || !b.TryGetValue("01", out var account)
            || bin.Length != 6 || !bin.All(char.IsAsciiDigit) || account.Length == 0 || !account.All(char.IsAsciiLetterOrDigit))
            return null;
        decimal? amount = top.TryGetValue("54", out var am) && decimal.TryParse(am, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var a) && a > 0 ? a : null;
        var content = top.TryGetValue("62", out var extra) && Tags(extra) is { } x && x.TryGetValue("08", out var purpose) ? purpose : null;
        return new(bin, account, amount, content);
    }

    private static Dictionary<string, string>? Tags(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var tags = new Dictionary<string, string>();
        for (var i = 0; i < s.Length;)
        {
            if (i + 4 > s.Length || !int.TryParse(s.AsSpan(i + 2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var len) || i + 4 + len > s.Length) return null;
            tags[s.Substring(i, 2)] = s.Substring(i + 4, len);
            i += 4 + len;
        }
        return tags;
    }
}
