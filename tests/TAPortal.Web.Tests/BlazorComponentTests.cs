using Bunit;
using Bunit.TestDoubles;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using TAPortal.Web.Components.Layout;
using TAPortal.Web.Components.Shared;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

public sealed class BlazorComponentTests : TestContext
{
    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-007 — render actual Razor components through bUnit.
    [Fact]
    public void ReconnectModal_RendersSingleAccessibleRecoverySurface()
    {
        var cut=RenderComponent<ReconnectModal>();
        Assert.Single(cut.FindAll("#components-reconnect-modal"));
        Assert.Equal("assertive",cut.Find("[role='alert']").GetAttribute("aria-live"));
        Assert.Equal(2,cut.FindAll("button").Count);
    }

    [Fact]
    public void RecursiveMenu_RendersChildAndActiveRoute()
    {
        var child=new PortalMenuNode(new(Guid.NewGuid(),null,"CHILD","Child",null,"/child",1,true,true),Array.Empty<PortalMenuNode>());
        var root=new PortalMenuNode(new(Guid.NewGuid(),null,"ROOT","Root","ti ti-folders",null,1,true,true),new[]{child});
        var cut=RenderComponent<SidebarMenuNode>(p=>p.Add(x=>x.Node,root).Add(x=>x.Depth,0));
        Assert.Contains("Root",cut.Markup); Assert.Contains("Child",cut.Markup); Assert.Contains("/child",cut.Markup);
    }

    [Fact]
    public void GlobalLoadingOverlay_HandlesDelayOverlapAndCleanup()
    {
        var loading=new GlobalLoadingService(); Services.AddSingleton(loading);
        var cut=RenderComponent<GlobalLoadingOverlay>();
        using var first=loading.Begin("First"); using var second=loading.Begin("Second");
        cut.WaitForAssertion(()=>Assert.Contains("Second",cut.Markup),TimeSpan.FromSeconds(2));
        second.Dispose(); Assert.True(loading.IsBusy);
        first.Dispose(); cut.WaitForAssertion(()=>Assert.DoesNotContain("ta-loading-overlay",cut.Markup),TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Topbar_HandlesAuthenticatedAndRevokedAuthenticationState()
    {
        var auth=this.AddTestAuthorization(); auth.SetAuthorized("tester"); auth.SetClaims(new Claim("display_name","Admin Test"));
        var cut=RenderComponent<Topbar>(); cut.WaitForAssertion(()=>Assert.Contains("Admin Test",cut.Markup));
        auth.SetNotAuthorized(); cut.WaitForAssertion(()=>Assert.Contains("Người dùng",cut.Markup));
    }
}
