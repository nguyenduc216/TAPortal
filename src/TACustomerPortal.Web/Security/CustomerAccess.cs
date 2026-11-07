using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Web.Security;

/// <summary>
/// The functions of the Customer Portal a company can grant to its staff (operator decision 2026-11-07: per function,
/// two levels Xem / Quản lý). "Tổng quan" is always visible and is not a module.
/// </summary>
public static class CustomerModules
{
    public const string Dashboard = "DASHBOARD";
    public const string Orders = "ORDERS";
    public const string Contacts = "CONTACTS";
    public const string Transactions = "TRANSACTIONS";
    public const string Banks = "BANKS";
    public const string PaymentSettings = "PAYMENT_SETTINGS";
    public const string EInvoice = "EINVOICE";
    public const string Notifications = "NOTIFICATIONS";
    public const string Packages = "PACKAGES";
    public const string Profile = "PROFILE";
    public const string Members = "MEMBERS";

    /// <summary>Grantable modules in display order (the permission matrix rows).</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> All =
    [
        (Orders, "Đơn hàng"),
        (Contacts, "Khách hàng"),
        (Transactions, "Đối soát giao dịch"),
        (Banks, "Tài khoản ngân hàng"),
        (PaymentSettings, "Cài đặt thanh toán"),
        (EInvoice, "Hóa đơn điện tử"),
        (Notifications, "Thông báo"),
        (Packages, "Gói dịch vụ"),
        (Profile, "Hồ sơ doanh nghiệp"),
        (Members, "Tài khoản & phân quyền"),
    ];

    public static bool IsKnown(string? code) => All.Any(m => m.Code == code);

    public static string Label(string code) => code == Dashboard ? "Tổng quan" : All.FirstOrDefault(m => m.Code == code).Label ?? code;
}

public enum CustomerAccessLevel { None = 0, View = 1, Manage = 2 }

/// <summary>
/// Who may do what in the selected company. OWNER and ADMIN manage everything; VIEWER views everything except the
/// staff accounts page; MEMBER follows its own permission string ("ORDERS:M;CONTACTS:V", M implies view). Any other
/// role has no access to the modules. Server-side checks use this class; views use it only to hide links and buttons.
/// </summary>
public static class CustomerAccess
{
    public const string Owner = "OWNER", Admin = "ADMIN", Viewer = "VIEWER", Member = "MEMBER";

    public static CustomerAccessLevel Level(CustomerMembership membership, string module) =>
        Level(membership.Role, membership.Permissions, module);

    public static CustomerAccessLevel Level(string? role, string? permissions, string module)
    {
        if (module == CustomerModules.Dashboard) return CustomerAccessLevel.View;
        if (!CustomerModules.IsKnown(module)) return CustomerAccessLevel.None;
        return role switch
        {
            Owner or Admin => CustomerAccessLevel.Manage,
            Viewer => module == CustomerModules.Members ? CustomerAccessLevel.None : CustomerAccessLevel.View,
            Member => Parse(permissions).GetValueOrDefault(module),
            _ => CustomerAccessLevel.None
        };
    }

    public static bool CanView(CustomerMembership membership, string module) => Level(membership, module) >= CustomerAccessLevel.View;

    public static bool CanManage(CustomerMembership membership, string module) => Level(membership, module) == CustomerAccessLevel.Manage;

    /// <summary>Every module with its effective level for a role and permission string.</summary>
    public static IReadOnlyDictionary<string, CustomerAccessLevel> Levels(string? role, string? permissions) =>
        CustomerModules.All.ToDictionary(m => m.Code, m => Level(role, permissions, m.Code));

    /// <summary>Reads "ORDERS:M;CONTACTS:V"; unknown modules, unknown levels and duplicates (highest wins) are tolerated.</summary>
    public static IReadOnlyDictionary<string, CustomerAccessLevel> Parse(string? permissions)
    {
        var result = new Dictionary<string, CustomerAccessLevel>(StringComparer.Ordinal);
        foreach (var part in (permissions ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2) continue;
            var module = pair[0].ToUpperInvariant();
            if (!CustomerModules.IsKnown(module)) continue;
            var level = pair[1].ToUpperInvariant() switch { "M" => CustomerAccessLevel.Manage, "V" => CustomerAccessLevel.View, _ => CustomerAccessLevel.None };
            if (level > result.GetValueOrDefault(module)) result[module] = level;
        }
        return result;
    }

    /// <summary>Canonical form, modules in display order, modules without access left out; null when nothing is granted.</summary>
    public static string? Format(IReadOnlyDictionary<string, CustomerAccessLevel> levels)
    {
        var parts = CustomerModules.All
            .Select(m => (m.Code, Level: levels.GetValueOrDefault(m.Code)))
            .Where(x => x.Level != CustomerAccessLevel.None)
            .Select(x => x.Code + ":" + (x.Level == CustomerAccessLevel.Manage ? "M" : "V"))
            .ToList();
        return parts.Count == 0 ? null : string.Join(';', parts);
    }

    /// <summary>The matrix as posted: a module ticked under "Quản lý" is managed (and therefore viewed).</summary>
    public static IReadOnlyDictionary<string, CustomerAccessLevel> FromForm(IEnumerable<string>? view, IEnumerable<string>? manage)
    {
        var viewSet = new HashSet<string>(view ?? [], StringComparer.OrdinalIgnoreCase);
        var manageSet = new HashSet<string>(manage ?? [], StringComparer.OrdinalIgnoreCase);
        return CustomerModules.All.ToDictionary(m => m.Code, m =>
            manageSet.Contains(m.Code) ? CustomerAccessLevel.Manage : viewSet.Contains(m.Code) ? CustomerAccessLevel.View : CustomerAccessLevel.None);
    }

    /// <summary>
    /// True when the actor holds at least the given level on every module: a staff manager can neither grant more than
    /// it has nor change an account that has more than it has. OWNER/ADMIN hold everything.
    /// </summary>
    public static bool Covers(CustomerMembership actor, IReadOnlyDictionary<string, CustomerAccessLevel> levels) =>
        CustomerModules.All.All(m => Level(actor, m.Code) >= levels.GetValueOrDefault(m.Code));

    /// <summary>The role's name as the customer sees it.</summary>
    public static string RoleLabel(string? role) => role == Member ? "Thành viên" : PortalRoleText.Label(role);

    /// <summary>Short summary for lists: the role, or for a MEMBER the matching preset or "n quản lý · m xem".</summary>
    public static string Summary(string? role, string? permissions)
    {
        if (role != Member) return RoleLabel(role);
        var levels = Levels(role, permissions);
        if (CustomerPresets.Match(levels) is { } preset) return preset.Label;
        var manage = levels.Count(l => l.Value == CustomerAccessLevel.Manage);
        var view = levels.Count(l => l.Value == CustomerAccessLevel.View);
        return manage == 0 && view == 0 ? "Chưa được cấp quyền" : $"Quản lý {manage} · Xem {view} chức năng";
    }
}

/// <summary>Quick presets of the permission matrix (operator decision 2026-11-07).</summary>
public static class CustomerPresets
{
    public sealed record Preset(string Code, string Label, string Hint, IReadOnlyDictionary<string, CustomerAccessLevel> Levels);

    private static Dictionary<string, CustomerAccessLevel> Make(Func<string, CustomerAccessLevel> level) =>
        CustomerModules.All.ToDictionary(m => m.Code, m => level(m.Code));

    public static readonly IReadOnlyList<Preset> All =
    [
        new("ADMIN", "Quản trị", "Quản lý tất cả chức năng", Make(_ => CustomerAccessLevel.Manage)),
        new("ACCOUNTANT", "Kế toán", "Xem tất cả; quản lý đối soát, hóa đơn, ngân hàng",
            Make(c => c is CustomerModules.Transactions or CustomerModules.EInvoice or CustomerModules.Banks ? CustomerAccessLevel.Manage : CustomerAccessLevel.View)),
        new("SALES", "Bán hàng", "Quản lý đơn hàng, khách hàng; xem đối soát, gói dịch vụ",
            Make(c => c switch
            {
                CustomerModules.Orders or CustomerModules.Contacts => CustomerAccessLevel.Manage,
                CustomerModules.Transactions or CustomerModules.Packages => CustomerAccessLevel.View,
                _ => CustomerAccessLevel.None
            })),
        new("READONLY", "Chỉ xem", "Xem tất cả trừ tài khoản & phân quyền",
            Make(c => c == CustomerModules.Members ? CustomerAccessLevel.None : CustomerAccessLevel.View)),
    ];

    public static Preset? Match(IReadOnlyDictionary<string, CustomerAccessLevel> levels) =>
        All.FirstOrDefault(p => CustomerModules.All.All(m => p.Levels[m.Code] == levels.GetValueOrDefault(m.Code)));
}

/// <summary>
/// Put on a controller (or an action) of the Customer Portal: a member without view right on the module gets the
/// friendly 403 page before the action runs. Manage rights are checked inside each changing action.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class CustomerModuleAttribute(string module) : Attribute, IAsyncActionFilter
{
    public string Module { get; } = module;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var current = await context.HttpContext.RequestServices.GetRequiredService<ICurrentCustomerContext>().Get();
        // No company selected: the action itself sends the user to the company picker.
        if (current is not null && !CustomerAccess.CanView(current, Module))
        {
            context.Result = ModuleDenied.Result(Module, manage: false, context.ModelState);
            return;
        }
        await next();
    }
}

public sealed record ModuleDeniedVm(string Module, string Label, bool Manage);

public static class ModuleDenied
{
    public const string View = "~/Views/Shared/ModuleDenied.cshtml";

    public static IActionResult Result(string module, bool manage, ModelStateDictionary modelState) => new ViewResult
    {
        ViewName = View,
        StatusCode = StatusCodes.Status403Forbidden,
        ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState) { Model = new ModuleDeniedVm(module, CustomerModules.Label(module), manage) }
    };
}
