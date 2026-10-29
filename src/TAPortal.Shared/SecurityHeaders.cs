using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TAPortal.Shared;

// AUD-39: baseline browser security headers for both TAPortal Admin and Customer Portal, on every response
// (pages, redirects, errors and static files). CSP is limited to frame-ancestors so existing CDN assets and
// inline handlers keep working; a full script/style CSP needs a separate reviewed change.
public static class SecurityHeaders
{
    public static readonly IReadOnlyDictionary<string, string> Values = new Dictionary<string, string>
    {
        ["X-Content-Type-Options"] = "nosniff",
        ["X-Frame-Options"] = "DENY",
        ["Referrer-Policy"] = "strict-origin-when-cross-origin",
        ["Content-Security-Policy"] = "frame-ancestors 'none'",
        ["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()"
    };

    public static IApplicationBuilder UseTaSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                foreach (var (name, value) in Values)
                    context.Response.Headers[name] = value;
                return Task.CompletedTask;
            });
            return next(context);
        });
}
