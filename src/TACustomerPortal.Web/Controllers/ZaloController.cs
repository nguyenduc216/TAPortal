using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Integrations;
using TAPortal.Integrations.ZaloMiniApp;

namespace TACustomerPortal.Web.Controllers;

/// <summary>
/// "Ứng dụng trên Zalo" of the signed-in user (ZALO-MINI-APP, operator 2026-11-13): change one's phone, verify it with a
/// Zalo OTP, open the one-time QR that signs into the Zalo app, unlink Zalo. Only ever the signed-in user's own account.
/// </summary>
[Route("account/zalo"), ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ZaloController(ZaloAccountService zalo) : Controller
{
    public const string Path = "/account/zalo";
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (await zalo.PanelAsync(UserId, HttpContext.RequestAborted) is not { } panel) return RedirectToAction("Login", "Account");
        return View(panel);
    }

    [HttpPost("phone")]
    public async Task<IActionResult> Phone(string? phone)
    {
        var error = await zalo.ChangePhoneAsync(UserId, phone, Ip, HttpContext.RequestAborted);
        if (error is null) TempData["Success"] = "Đã lưu số điện thoại mới. Vui lòng xác minh số này bằng mã OTP Zalo.";
        else TempData["Error"] = error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp()
    {
        var r = await zalo.SendOtpAsync(UserId, Ip, HttpContext.RequestAborted);
        if (r.Ok)
        {
            TempData["OtpChallenge"] = r.ChallengeId!.Value.ToString();
            TempData["Success"] = $"Đã gửi mã xác minh qua Zalo tới số {r.MaskedPhone}. Mã có hiệu lực 5 phút.";
        }
        else TempData["Error"] = r.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("confirm-otp")]
    public async Task<IActionResult> ConfirmOtp(Guid challengeId, string? code)
    {
        var error = await zalo.ConfirmOtpAsync(UserId, challengeId, code, HttpContext.RequestAborted);
        if (error is null) TempData["Success"] = "Đã xác minh số điện thoại.";
        else
        {
            TempData["Error"] = error;
            TempData["OtpChallenge"] = challengeId.ToString();   // keep the code form open for another try
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Renders a new one-time QR (POST: every QR is a new token).</summary>
    [HttpPost("qr")]
    public async Task<IActionResult> Qr()
    {
        var qr = await zalo.CreateQrAsync(UserId, MiniAppPortal.Invoice, HttpContext.RequestAborted);
        if (!qr.Ok)
        {
            TempData["Error"] = qr.Error;
            return RedirectToAction(nameof(Index));
        }
        ViewData["QrSvg"] = QrImage.Svg(qr.Link!, 8);
        return View(qr);
    }

    [HttpPost("unlink")]
    public async Task<IActionResult> Unlink()
    {
        var count = await zalo.UnlinkAsync(UserId, HttpContext.RequestAborted);
        TempData["Success"] = count > 0 ? "Đã hủy liên kết Zalo. Lần sau mở ứng dụng trên Zalo sẽ cần đăng nhập lại." : "Tài khoản chưa liên kết với Zalo nào.";
        return RedirectToAction(nameof(Index));
    }

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();
}
