using System.Globalization;

namespace TACustomerPortal.Web.Services;

/// <summary>Who receives a VietQR payment, read from the QR itself: bank BIN, account, amount and transfer content.</summary>
public sealed record QrTransfer(string Bin, string Account, decimal? Amount, string? Content);

/// <summary>A banking app that VietQR can open with the transfer filled in (https://api.vietqr.io/v2/*-app-deeplinks).</summary>
public sealed record BankApp(string Id, string Name);

/// <summary>
/// "Chuyển khoản" on the public payment page (operator 2026-11-06): opens the payer's banking app with the same transfer as
/// the QR — like tapping a QR image in Zalo. The link is VietQR's app deeplink (https://dl.vietqr.io/pay?app=…), built
/// only from the order's own QR payload, so the app receives exactly what scanning would give it.
/// </summary>
public static class BankAppLinks
{
    /// <summary>Apps VietQR supports on both Android and iOS, most installed first (list of 2026-11-06).</summary>
    public static readonly IReadOnlyList<BankApp> Apps =
    [
        new("vcb", "Vietcombank"), new("mb", "MB Bank"), new("tcb", "Techcombank"), new("bidv", "BIDV"), new("icb", "VietinBank"),
        new("vba", "Agribank"), new("acb", "ACB"), new("vpb", "VPBank"), new("tpb", "TPBank"), new("cake", "Cake"),
        new("timo", "Timo"), new("shb", "SHB"), new("hdb", "HDBank"), new("vib-2", "VIB"), new("ocb", "OCB"),
        new("scb", "SCB"), new("seab", "SeABank"), new("lpb", "LPBank"), new("eib", "Eximbank"), new("nab", "Nam A Bank"),
        new("abb", "ABBank"), new("klb", "KienlongBank"), new("pvcb", "PVcomBank"), new("bvb", "BaoViet Bank"), new("ncb", "NCB"),
        new("shbvn", "Shinhan"), new("wvn", "Woori"), new("cimb", "CIMB"), new("vab", "VietABank"), new("coopbank", "Co-opBank"),
    ];

    public static string Icon(BankApp app) => $"https://cdn.vietqr.io/bank-app-icon/{app.Id}-android.webp";

    public static string Link(BankApp app, QrTransfer t)
    {
        var q = $"https://dl.vietqr.io/pay?app={Uri.EscapeDataString(app.Id)}&ba={Uri.EscapeDataString(t.Account + "@" + t.Bin)}";
        if (t.Amount is decimal am) q += "&am=" + am.ToString("0", CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(t.Content)) q += "&tn=" + Uri.EscapeDataString(t.Content);
        return q;
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
