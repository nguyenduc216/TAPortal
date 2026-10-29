using System.Collections.Frozen;
using System.Net;
using Microsoft.AspNetCore.Html;

namespace TAPortal.Shared;

// Inline SVG icons shared by TAPortal Admin and Customer Portal. Inline markup needs no CDN, web font or URL, so icons
// render the same behind IIS virtual directories and on servers without internet access.
// Path data: Tabler Icons 3.19.0 outline set (MIT, Copyright (c) 2020-2024 Paweł Kuna), see THIRD-PARTY-NOTICES.md.
public static class UiIcon
{
    private static readonly FrozenDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["alert-circle"] = "<path d=\"M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0\"/><path d=\"M12 8v4\"/><path d=\"M12 16h.01\"/>",
        ["alert-triangle"] = "<path d=\"M12 9v4\"/><path d=\"M10.363 3.591l-8.106 13.534a1.914 1.914 0 0 0 1.636 2.871h16.214a1.914 1.914 0 0 0 1.636 -2.87l-8.106 -13.536a1.914 1.914 0 0 0 -3.274 0z\"/><path d=\"M12 16h.01\"/>",
        ["arrow-left"] = "<path d=\"M5 12l14 0\"/><path d=\"M5 12l6 6\"/><path d=\"M5 12l6 -6\"/>",
        ["arrows-exchange"] = "<path d=\"M7 10h14l-4 -4\"/><path d=\"M17 14h-14l4 4\"/>",
        ["ban"] = "<path d=\"M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0 -18 0\"/><path d=\"M5.7 5.7l12.6 12.6\"/>",
        ["building"] = "<path d=\"M3 21l18 0\"/><path d=\"M9 8l1 0\"/><path d=\"M9 12l1 0\"/><path d=\"M9 16l1 0\"/><path d=\"M14 8l1 0\"/><path d=\"M14 12l1 0\"/><path d=\"M14 16l1 0\"/><path d=\"M5 21v-16a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v16\"/>",
        ["building-bank"] = "<path d=\"M3 21l18 0\"/><path d=\"M3 10l18 0\"/><path d=\"M5 6l7 -3l7 3\"/><path d=\"M4 10l0 11\"/><path d=\"M20 10l0 11\"/><path d=\"M8 14l0 3\"/><path d=\"M12 14l0 3\"/><path d=\"M16 14l0 3\"/>",
        ["chart-bar"] = "<path d=\"M3 13a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v6a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1z\"/><path d=\"M15 9a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v10a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1z\"/><path d=\"M9 5a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v14a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1z\"/><path d=\"M4 20h14\"/>",
        ["check"] = "<path d=\"M5 12l5 5l10 -10\"/>",
        ["checks"] = "<path d=\"M7 12l5 5l10 -10\"/><path d=\"M2 12l5 5m5 -5l5 -5\"/>",
        ["chevron-down"] = "<path d=\"M6 9l6 6l6 -6\"/>",
        ["chevron-right"] = "<path d=\"M9 6l6 6l-6 6\"/>",
        ["circle-check"] = "<path d=\"M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0 -18 0\"/><path d=\"M9 12l2 2l4 -4\"/>",
        ["clock"] = "<path d=\"M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0\"/><path d=\"M12 7v5l3 3\"/>",
        ["database-off"] = "<path d=\"M12.983 8.978c3.955 -.182 7.017 -1.446 7.017 -2.978c0 -1.657 -3.582 -3 -8 -3c-1.661 0 -3.204 .19 -4.483 .515m-2.783 1.228c-.471 .382 -.734 .808 -.734 1.257c0 1.22 1.944 2.271 4.734 2.74\"/><path d=\"M4 6v6c0 1.657 3.582 3 8 3c.986 0 1.93 -.067 2.802 -.19m3.187 -.82c1.251 -.53 2.011 -1.228 2.011 -1.99v-6\"/><path d=\"M4 12v6c0 1.657 3.582 3 8 3c3.217 0 5.991 -.712 7.261 -1.74m.739 -3.26v-4\"/><path d=\"M3 3l18 18\"/>",
        ["device-floppy"] = "<path d=\"M6 4h10l4 4v10a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2\"/><path d=\"M12 14m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0\"/><path d=\"M14 4l0 4l-6 0l0 -4\"/>",
        ["dots-vertical"] = "<path d=\"M12 12m-1 0a1 1 0 1 0 2 0a1 1 0 1 0 -2 0\"/><path d=\"M12 19m-1 0a1 1 0 1 0 2 0a1 1 0 1 0 -2 0\"/><path d=\"M12 5m-1 0a1 1 0 1 0 2 0a1 1 0 1 0 -2 0\"/>",
        ["eye"] = "<path d=\"M10 12a2 2 0 1 0 4 0a2 2 0 0 0 -4 0\"/><path d=\"M21 12c-2.4 4 -5.4 6 -9 6c-3.6 0 -6.6 -2 -9 -6c2.4 -4 5.4 -6 9 -6c3.6 0 6.6 2 9 6\"/>",
        ["eye-off"] = "<path d=\"M10.585 10.587a2 2 0 0 0 2.829 2.828\"/><path d=\"M16.681 16.673a8.717 8.717 0 0 1 -4.681 1.327c-3.6 0 -6.6 -2 -9 -6c1.272 -2.12 2.712 -3.678 4.32 -4.674m2.86 -1.146a9.055 9.055 0 0 1 1.82 -.18c3.6 0 6.6 2 9 6c-.666 1.11 -1.379 2.067 -2.138 2.87\"/><path d=\"M3 3l18 18\"/>",
        ["file-invoice"] = "<path d=\"M14 3v4a1 1 0 0 0 1 1h4\"/><path d=\"M17 21h-10a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2h7l5 5v11a2 2 0 0 1 -2 2z\"/><path d=\"M9 7l1 0\"/><path d=\"M9 13l6 0\"/><path d=\"M13 17l2 0\"/>",
        ["file-text"] = "<path d=\"M14 3v4a1 1 0 0 0 1 1h4\"/><path d=\"M17 21h-10a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2h7l5 5v11a2 2 0 0 1 -2 2z\"/><path d=\"M9 9l1 0\"/><path d=\"M9 13l6 0\"/><path d=\"M9 17l6 0\"/>",
        ["hash"] = "<path d=\"M5 9l14 0\"/><path d=\"M5 15l14 0\"/><path d=\"M11 4l-4 16\"/><path d=\"M17 4l-4 16\"/>",
        ["history"] = "<path d=\"M12 8l0 4l2 2\"/><path d=\"M3.05 11a9 9 0 1 1 .5 4m-.5 5v-5h5\"/>",
        ["home"] = "<path d=\"M5 12l-2 0l9 -9l9 9l-2 0\"/><path d=\"M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2 -2v-7\"/><path d=\"M9 21v-6a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2v6\"/>",
        ["id"] = "<path d=\"M3 4m0 3a3 3 0 0 1 3 -3h12a3 3 0 0 1 3 3v10a3 3 0 0 1 -3 3h-12a3 3 0 0 1 -3 -3z\"/><path d=\"M9 10m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0\"/><path d=\"M15 8l2 0\"/><path d=\"M15 12l2 0\"/><path d=\"M7 16l10 0\"/>",
        ["inbox"] = "<path d=\"M4 4m0 2a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2z\"/><path d=\"M4 13h3l3 3h4l3 -3h3\"/>",
        ["info-circle"] = "<path d=\"M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0\"/><path d=\"M12 9h.01\"/><path d=\"M11 12h1v4h1\"/>",
        ["key"] = "<path d=\"M16.555 3.843l3.602 3.602a2.877 2.877 0 0 1 0 4.069l-2.643 2.643a2.877 2.877 0 0 1 -4.069 0l-.301 -.301l-6.558 6.558a2 2 0 0 1 -1.239 .578l-.175 .008h-1.172a1 1 0 0 1 -.993 -.883l-.007 -.117v-1.172a2 2 0 0 1 .467 -1.284l.119 -.13l.414 -.414h2v-2h2v-2l2.144 -2.144l-.301 -.301a2.877 2.877 0 0 1 0 -4.069l2.643 -2.643a2.877 2.877 0 0 1 4.069 0z\"/><path d=\"M15 9h.01\"/>",
        ["layout-dashboard"] = "<path d=\"M5 4h4a1 1 0 0 1 1 1v6a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1v-6a1 1 0 0 1 1 -1\"/><path d=\"M5 16h4a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1v-2a1 1 0 0 1 1 -1\"/><path d=\"M15 12h4a1 1 0 0 1 1 1v6a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1v-6a1 1 0 0 1 1 -1\"/><path d=\"M15 4h4a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1v-2a1 1 0 0 1 1 -1\"/>",
        ["layout-sidebar-left-collapse"] = "<path d=\"M4 4m0 2a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2z\"/><path d=\"M9 4v16\"/><path d=\"M15 10l-2 2l2 2\"/>",
        ["list-tree"] = "<path d=\"M9 6h11\"/><path d=\"M12 12h8\"/><path d=\"M15 18h5\"/><path d=\"M5 6v.01\"/><path d=\"M8 12v.01\"/><path d=\"M11 18v.01\"/>",
        ["lock"] = "<path d=\"M5 13a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v6a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2v-6z\"/><path d=\"M11 16a1 1 0 1 0 2 0a1 1 0 0 0 -2 0\"/><path d=\"M8 11v-4a4 4 0 1 1 8 0v4\"/>",
        ["lock-access"] = "<path d=\"M4 8v-2a2 2 0 0 1 2 -2h2\"/><path d=\"M4 16v2a2 2 0 0 0 2 2h2\"/><path d=\"M16 4h2a2 2 0 0 1 2 2v2\"/><path d=\"M16 20h2a2 2 0 0 0 2 -2v-2\"/><path d=\"M8 11m0 1a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1v3a1 1 0 0 1 -1 1h-6a1 1 0 0 1 -1 -1z\"/><path d=\"M10 11v-2a2 2 0 1 1 4 0v2\"/>",
        ["lock-open"] = "<path d=\"M5 11m0 2a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v6a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2z\"/><path d=\"M12 16m-1 0a1 1 0 1 0 2 0a1 1 0 1 0 -2 0\"/><path d=\"M8 11v-5a4 4 0 0 1 8 0\"/>",
        ["login-2"] = "<path d=\"M9 8v-2a2 2 0 0 1 2 -2h7a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-7a2 2 0 0 1 -2 -2v-2\"/><path d=\"M3 12h13l-3 -3\"/><path d=\"M13 15l3 -3\"/>",
        ["logout"] = "<path d=\"M14 8v-2a2 2 0 0 0 -2 -2h-7a2 2 0 0 0 -2 2v12a2 2 0 0 0 2 2h7a2 2 0 0 0 2 -2v-2\"/><path d=\"M9 12h12l-3 -3\"/><path d=\"M18 15l3 -3\"/>",
        ["mail"] = "<path d=\"M3 7a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-10z\"/><path d=\"M3 7l9 6l9 -6\"/>",
        ["map-pin"] = "<path d=\"M9 11a3 3 0 1 0 6 0a3 3 0 0 0 -6 0\"/><path d=\"M17.657 16.657l-4.243 4.243a2 2 0 0 1 -2.827 0l-4.244 -4.243a8 8 0 1 1 11.314 0z\"/>",
        ["menu-2"] = "<path d=\"M4 6l16 0\"/><path d=\"M4 12l16 0\"/><path d=\"M4 18l16 0\"/>",
        ["mood-empty"] = "<path d=\"M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0 -18 0\"/><path d=\"M9 10l.01 0\"/><path d=\"M15 10l.01 0\"/><path d=\"M9 15l6 0\"/>",
        ["pencil"] = "<path d=\"M4 20h4l10.5 -10.5a2.828 2.828 0 1 0 -4 -4l-10.5 10.5v4\"/><path d=\"M13.5 6.5l4 4\"/>",
        ["phone"] = "<path d=\"M5 4h4l2 5l-2.5 1.5a11 11 0 0 0 5 5l1.5 -2.5l5 2v4a2 2 0 0 1 -2 2a16 16 0 0 1 -15 -15a2 2 0 0 1 2 -2\"/>",
        ["plus"] = "<path d=\"M12 5l0 14\"/><path d=\"M5 12l14 0\"/>",
        ["qrcode"] = "<path d=\"M4 4m0 1a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1z\"/><path d=\"M7 17l0 .01\"/><path d=\"M14 4m0 1a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1z\"/><path d=\"M7 7l0 .01\"/><path d=\"M4 14m0 1a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1z\"/><path d=\"M17 7l0 .01\"/><path d=\"M14 14l3 0\"/><path d=\"M20 14l0 .01\"/><path d=\"M14 14l0 3\"/><path d=\"M14 20l3 0\"/><path d=\"M17 17l3 0\"/><path d=\"M20 17l0 3\"/>",
        ["receipt-tax"] = "<path d=\"M9 14l6 -6\"/><circle cx=\"9.5\" cy=\"8.5\" r=\".5\" fill=\"currentColor\"/><circle cx=\"14.5\" cy=\"13.5\" r=\".5\" fill=\"currentColor\"/><path d=\"M5 21v-16a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v16l-3 -2l-2 2l-2 -2l-2 2l-2 -2l-3 2\"/>",
        ["refresh"] = "<path d=\"M20 11a8.1 8.1 0 0 0 -15.5 -2m-.5 -4v4h4\"/><path d=\"M4 13a8.1 8.1 0 0 0 15.5 2m.5 4v-4h-4\"/>",
        ["report-money"] = "<path d=\"M9 5h-2a2 2 0 0 0 -2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2 -2v-12a2 2 0 0 0 -2 -2h-2\"/><path d=\"M9 3m0 2a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2v0a2 2 0 0 1 -2 2h-2a2 2 0 0 1 -2 -2z\"/><path d=\"M14 11h-2.5a1.5 1.5 0 0 0 0 3h1a1.5 1.5 0 0 1 0 3h-2.5\"/><path d=\"M12 17v1m0 -8v1\"/>",
        ["search"] = "<path d=\"M10 10m-7 0a7 7 0 1 0 14 0a7 7 0 1 0 -14 0\"/><path d=\"M21 21l-6 -6\"/>",
        ["search-off"] = "<path d=\"M5.039 5.062a7 7 0 0 0 9.91 9.89m1.584 -2.434a7 7 0 0 0 -9.038 -9.057\"/><path d=\"M3 3l18 18\"/>",
        ["settings"] = "<path d=\"M10.325 4.317c.426 -1.756 2.924 -1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543 -.94 3.31 .826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756 .426 1.756 2.924 0 3.35a1.724 1.724 0 0 0 -1.066 2.573c.94 1.543 -.826 3.31 -2.37 2.37a1.724 1.724 0 0 0 -2.572 1.065c-.426 1.756 -2.924 1.756 -3.35 0a1.724 1.724 0 0 0 -2.573 -1.066c-1.543 .94 -3.31 -.826 -2.37 -2.37a1.724 1.724 0 0 0 -1.065 -2.572c-1.756 -.426 -1.756 -2.924 0 -3.35a1.724 1.724 0 0 0 1.066 -2.573c-.94 -1.543 .826 -3.31 2.37 -2.37c1 .608 2.296 .07 2.572 -1.065z\"/><path d=\"M9 12a3 3 0 1 0 6 0a3 3 0 0 0 -6 0\"/>",
        ["shield-check"] = "<path d=\"M11.46 20.846a12 12 0 0 1 -7.96 -14.846a12 12 0 0 0 8.5 -3a12 12 0 0 0 8.5 3a12 12 0 0 1 -.09 7.06\"/><path d=\"M15 19l2 2l4 -4\"/>",
        ["shield-lock"] = "<path d=\"M12 3a12 12 0 0 0 8.5 3a12 12 0 0 1 -8.5 15a12 12 0 0 1 -8.5 -15a12 12 0 0 0 8.5 -3\"/><path d=\"M12 11m-1 0a1 1 0 1 0 2 0a1 1 0 1 0 -2 0\"/><path d=\"M12 12l0 2.5\"/>",
        ["switch-horizontal"] = "<path d=\"M16 3l4 4l-4 4\"/><path d=\"M10 7l10 0\"/><path d=\"M8 13l-4 4l4 4\"/><path d=\"M4 17l9 0\"/>",
        ["trash"] = "<path d=\"M4 7l16 0\"/><path d=\"M10 11l0 6\"/><path d=\"M14 11l0 6\"/><path d=\"M5 7l1 12a2 2 0 0 0 2 2h8a2 2 0 0 0 2 -2l1 -12\"/><path d=\"M9 7v-3a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v3\"/>",
        ["user"] = "<path d=\"M8 7a4 4 0 1 0 8 0a4 4 0 0 0 -8 0\"/><path d=\"M6 21v-2a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4v2\"/>",
        ["user-circle"] = "<path d=\"M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0 -18 0\"/><path d=\"M12 10m-3 0a3 3 0 1 0 6 0a3 3 0 1 0 -6 0\"/><path d=\"M6.168 18.849a4 4 0 0 1 3.832 -2.849h4a4 4 0 0 1 3.834 2.855\"/>",
        ["user-cog"] = "<path d=\"M8 7a4 4 0 1 0 8 0a4 4 0 0 0 -8 0\"/><path d=\"M6 21v-2a4 4 0 0 1 4 -4h2.5\"/><path d=\"M19.001 19m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0\"/><path d=\"M19.001 15.5v1.5\"/><path d=\"M19.001 21v1.5\"/><path d=\"M22.032 17.25l-1.299 .75\"/><path d=\"M17.27 20l-1.3 .75\"/><path d=\"M15.97 17.25l1.3 .75\"/><path d=\"M20.733 20l1.3 .75\"/>",
        ["user-plus"] = "<path d=\"M8 7a4 4 0 1 0 8 0a4 4 0 0 0 -8 0\"/><path d=\"M16 19h6\"/><path d=\"M19 16v6\"/><path d=\"M6 21v-2a4 4 0 0 1 4 -4h4\"/>",
        ["user-shield"] = "<path d=\"M6 21v-2a4 4 0 0 1 4 -4h2\"/><path d=\"M22 16c0 4 -2.5 6 -3.5 6s-3.5 -2 -3.5 -6c1 0 2.5 -.5 3.5 -1.5c1 1 2.5 1.5 3.5 1.5z\"/><path d=\"M8 7a4 4 0 1 0 8 0a4 4 0 0 0 -8 0\"/>",
        ["users"] = "<path d=\"M9 7m-4 0a4 4 0 1 0 8 0a4 4 0 1 0 -8 0\"/><path d=\"M3 21v-2a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4v2\"/><path d=\"M16 3.13a4 4 0 0 1 0 7.75\"/><path d=\"M21 21v-2a4 4 0 0 0 -3 -3.85\"/>",
        ["users-group"] = "<path d=\"M10 13a2 2 0 1 0 4 0a2 2 0 0 0 -4 0\"/><path d=\"M8 21v-1a2 2 0 0 1 2 -2h4a2 2 0 0 1 2 2v1\"/><path d=\"M15 5a2 2 0 1 0 4 0a2 2 0 0 0 -4 0\"/><path d=\"M17 10h2a2 2 0 0 1 2 2v1\"/><path d=\"M5 5a2 2 0 1 0 4 0a2 2 0 0 0 -4 0\"/><path d=\"M3 13v-1a2 2 0 0 1 2 -2h2\"/>",
        ["x"] = "<path d=\"M18 6l-12 12\"/><path d=\"M6 6l12 12\"/>",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Names => Paths.Keys;

    public static bool Exists(string name) => Paths.ContainsKey(name);

    // Decorative by default (aria-hidden). Pass a label only when the icon is the sole content of its control.
    public static IHtmlContent Render(string name, string? cssClass = null, string? label = null)
    {
        if (!Paths.TryGetValue(name, out var paths)) throw new KeyNotFoundException($"Unknown UI icon '{name}'.");
        var css = cssClass is null ? "ta-icon" : "ta-icon " + WebUtility.HtmlEncode(cssClass);
        var a11y = label is null ? "aria-hidden=\"true\"" : $"role=\"img\" aria-label=\"{WebUtility.HtmlEncode(label)}\"";
        return new HtmlString($"<svg class=\"{css}\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" focusable=\"false\" {a11y}>{paths}</svg>");
    }
}
