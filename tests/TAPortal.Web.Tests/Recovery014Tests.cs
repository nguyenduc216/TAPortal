using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using TAPortal.Web.Components.Layout;
using TAPortal.Web.Components.Pages;
using TAPortal.Web.Components.Shared;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — isolated real-component/view rendering, without database access or runtime auth bypass.
public sealed class Recovery014Tests : TestContext
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static void Export(string name, string markup)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "recovery-014-fixtures");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".html"), markup);
    }
    private static PortalMenuNode Menu() => new(new(Guid.NewGuid(), null, "ROOT", "Customers", "ti ti-users", "#", 1, true, true),
        new[] { new PortalMenuNode(new(Guid.NewGuid(), null, "CHILD", "Customer list", null, "/customers", 1, true, true), Array.Empty<PortalMenuNode>()) });

    [Fact]
    public void BlazorMenuAndLoading_RenderSharedBrandWithoutCounts()
    {
        var loading = new GlobalLoadingService(); Services.AddSingleton(loading);
        var menu = RenderComponent<SidebarMenuNode>(p => p.Add(x => x.Node, Menu()));
        Assert.Empty(menu.FindAll(".ta-branch-count"));
        Assert.Equal(2, menu.FindAll("a").Count);
        var cut = RenderComponent<GlobalLoadingOverlay>();
        using var busy = loading.Begin("Đang xử lý...");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".ta-master-loading img")));
        Assert.Equal("/images/ta-logo.png", cut.Find("img").GetAttribute("src"));
        Assert.Single(cut.FindAll(".ta-master-loading-ring"));
        Export("blazor-loading", cut.Markup);
        Export("blazor-sidebar", menu.Markup);
        busy.Dispose();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".ta-loading-overlay")));
    }

    [Fact]
    public void Customers_EmptyFilteredErrorAndLoadingAreDistinct()
    {
        this.AddTestAuthorization().SetAuthorized("isolated-render-only");
        Services.AddSingleton(new GlobalLoadingService());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Server=unused;Database=unused" }).Build();
        // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — use the shared customer service contract in the retained recovery fixture.
        Services.AddSingleton<IBlazorPortalService>(new FakePortalService());
        // No NameIdentifier: initialization cannot query SQL. Seed view state only inside this isolated rendering test.
        var cut = RenderComponent<Customers>();
        void State(string field, object? value) => typeof(Customers).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, value);
        State("_allowed", true); cut.Render();
        Assert.Contains("Chưa có khách hàng.", cut.Find(".ta-customer-empty").TextContent);
        Assert.Equal(6, cut.FindAll("thead th").Count);
        Assert.Single(cut.FindAll("button[disabled]"));
        Assert.Single(cut.FindAll(".ti-user-plus"));
        Export("blazor-customers-empty", cut.Markup);
        State("_customers", new List<CustomerRow> { new(Guid.NewGuid(), "TEST", "Example", null, null, null, "Active") }); cut.Render();
        Assert.Empty(cut.FindAll(".ta-customer-empty"));
        cut.Find("input").Input("missing");
        Assert.Contains("Không tìm thấy", cut.Find(".ta-customer-empty").TextContent);
        cut.Find("input").Input(""); Assert.Single(cut.FindAll("tbody tr"));
        State("_error", "Read failed"); cut.Render();
        Assert.Empty(cut.FindAll(".ta-customer-empty")); Assert.Contains("Read failed", cut.Find("[role=alert]").TextContent);
        State("_loading", true); cut.Render();
        Assert.Empty(cut.FindAll(".ta-customer-empty")); Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public async Task MvcViews_RenderActualEmptyAndMenuMarkup()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(Root, "src/TAPortal.Web"), EnvironmentName = "Testing" });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PortalDb).Assembly);
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        async Task<string> Render(string path, object model)
        {
            var services = scope.ServiceProvider;
            var http = new DefaultHttpContext { RequestServices = services }; http.Request.Path = "/customers";
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var view = services.GetRequiredService<IRazorViewEngine>().GetView(null, path, false);
            Assert.True(view.Success, string.Join(",", view.SearchedLocations ?? Array.Empty<string>()));
            using var output = new StringWriter();
            var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
            var temp = new TempDataDictionary(http, services.GetRequiredService<ITempDataProvider>());
            await view.View.RenderAsync(new ViewContext(action, view.View, data, temp, output, new HtmlHelperOptions()));
            return output.ToString();
        }
        var empty = await Render("/Views/Customers/Index.cshtml", Array.Empty<CustomerRow>());
        Assert.Contains("ta-customer-empty", empty); Assert.DoesNotContain("data-ta-no-results", empty);
        Export("mvc-customers-empty", empty);
        var rows = await Render("/Views/Customers/Index.cshtml", new[] { new CustomerRow(Guid.NewGuid(), "TEST", "Example", null, null, null, "Active") });
        Assert.Contains("data-ta-no-results", rows);
        Export("mvc-customers-rows", rows);
        var menu = await Render("/Views/Shared/_SidebarMenuNode.cshtml", Menu());
        Assert.DoesNotContain("ta-branch-count", menu); Assert.Contains("ta-submenu-chevron", menu); Assert.Contains("aria-expanded=\"true\"", menu);
        Export("mvc-sidebar", menu);
    }
}
