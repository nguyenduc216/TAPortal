using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Integrations;
using TAPortal.Integrations.ZaloMiniApp;

namespace TAPortal.Web.Controllers;

// "Zalo Mini App T.A" of the signed-in staff member (ZALO-MINI-APP, operator 2026-11-13): change one's phone, verify it by
// Zalo OTP, open the one-time Mini App QR, unlink Zalo. Self-service like Profile/ChangePassword: sign-in only, own account only.
public sealed partial class AccountController
{
    public const string ZaloPath = "/account/zalo";
    private Guid SignedInUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private ZaloAccountService ZaloAccounts => services.GetRequiredService<ZaloAccountService>();
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Zalo()
    {
        if (await ZaloAccounts.PanelAsync(SignedInUserId, HttpContext.RequestAborted) is not { } panel) return RedirectToAction(nameof(Login));
        return View(panel);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ZaloPhone(string? phone)
    {
        var error = await ZaloAccounts.ChangePhoneAsync(SignedInUserId, phone, ClientIp, HttpContext.RequestAborted);
        if (error is null) TempData["Success"] = "Đã lưu số điện thoại mới. Vui lòng xác minh số này bằng mã OTP Zalo.";
        else TempData["Error"] = error;
        return RedirectToAction(nameof(Zalo));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ZaloSendOtp()
    {
        var r = await ZaloAccounts.SendOtpAsync(SignedInUserId, ClientIp, HttpContext.RequestAborted);
        if (r.Ok)
        {
            TempData["OtpChallenge"] = r.ChallengeId!.Value.ToString();
            TempData["Success"] = $"Đã gửi mã xác minh qua Zalo tới số {r.MaskedPhone}. Mã có hiệu lực 5 phút.";
        }
        else TempData["Error"] = r.Error;
        return RedirectToAction(nameof(Zalo));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ZaloConfirmOtp(Guid challengeId, string? code)
    {
        var error = await ZaloAccounts.ConfirmOtpAsync(SignedInUserId, challengeId, code, HttpContext.RequestAborted);
        if (error is null) TempData["Success"] = "Đã xác minh số điện thoại.";
        else
        {
            TempData["Error"] = error;
            TempData["OtpChallenge"] = challengeId.ToString();
        }
        return RedirectToAction(nameof(Zalo));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ZaloQr()
    {
        var qr = await ZaloAccounts.CreateQrAsync(SignedInUserId, MiniAppPortal.Portal, HttpContext.RequestAborted);
        if (!qr.Ok)
        {
            TempData["Error"] = qr.Error;
            return RedirectToAction(nameof(Zalo));
        }
        ViewData["QrSvg"] = QrImage.Svg(qr.Link!, 8);
        return View(qr);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ZaloUnlink()
    {
        var count = await ZaloAccounts.UnlinkAsync(SignedInUserId, HttpContext.RequestAborted);
        TempData["Success"] = count > 0 ? "Đã hủy liên kết Zalo. Lần sau mở Mini App sẽ cần đăng nhập lại." : "Tài khoản chưa liên kết với Zalo nào.";
        return RedirectToAction(nameof(Zalo));
    }
}
