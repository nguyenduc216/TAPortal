using Microsoft.AspNetCore.Html;
using System.Text.Encodings.Web;
using TAPortal.Web.Services;

namespace TAPortal.Web.Models;

// One avatar: the employee's photo when there is one, otherwise the initials (operator 2026-11-14).
public static class Avatar
{
    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "TA" : parts[^1][..1].ToUpperInvariant();
    }

    public static string? Url(AvatarIndex index, Guid? employeeId, Guid? userId)
    {
        if (employeeId is Guid e && index.ByEmployee.TryGetValue(e, out var v)) return $"/Account/Avatar/{e}?v={v}";
        if (userId is Guid u && index.ByUser.TryGetValue(u, out var hit)) return $"/Account/Avatar/{hit.EmployeeId}?v={hit.Version}";
        return null;
    }

    // cssClass: extra classes for the span (size / colour), e.g. "ta-avatar--lg".
    public static IHtmlContent Render(AvatarIndex index, string? name, Guid? employeeId = null, Guid? userId = null, string? cssClass = null, string? initials = null)
    {
        var cls = "ta-avatar" + (string.IsNullOrWhiteSpace(cssClass) ? "" : " " + cssClass);
        var url = Url(index, employeeId, userId);
        var enc = HtmlEncoder.Default;
        return url is null
            ? new HtmlString($"<span class=\"{enc.Encode(cls)}\" aria-hidden=\"true\">{enc.Encode(initials ?? Initials(name))}</span>")
            : new HtmlString($"<span class=\"{enc.Encode(cls)} ta-avatar--photo\" aria-hidden=\"true\"><img src=\"{enc.Encode(url)}\" alt=\"\" loading=\"lazy\" decoding=\"async\" /></span>");
    }
}

public sealed record PhotoFormVm(string Action, bool HasPhoto, IHtmlContent Avatar);
