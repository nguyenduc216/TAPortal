using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using TAPortal.Web.Components.Pages;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-016
public sealed class CrmRelationshipComponentTests : TestContext
{
    private readonly FakePortalService _db = new();
    private readonly Guid _customer = Guid.NewGuid();
    public CrmRelationshipComponentTests()
    {
        Services.AddSingleton<IBlazorPortalService>(_db); Services.AddSingleton(new GlobalLoadingService());
        var auth = this.AddTestAuthorization(); auth.SetAuthorized("crm"); auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, FakePortalService.UserId.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
    private void Seed(bool permitted = true)
    {
        var rows = new List<CrmRelationshipRow> { new(Guid.NewGuid(), Guid.NewGuid(), "Existing", "HQ", new(2026, 10, 15), null, true, "PRIMARY", new byte[8]) };
        var options = new List<CrmTargetOption> { new(Guid.NewGuid(), "Candidate", "HQ") };
        _db.RelationshipDetail = new(new(_customer, "C01", "Customer", null, null, null, "ACTIVE"), rows, rows, rows, options, options, options, permitted, permitted, permitted);
    }
    private IRenderedComponent<CustomerRelationships> Render() => RenderComponent<CustomerRelationships>(p => p.Add(x => x.CustomerId, _customer));
    [Fact] public void ScopedDetail_RendersThreeSectionsAndEmptyState()
    {
        Seed(); var cut = Render();
        Assert.Contains("Nhân viên được phân công", cut.Markup); Assert.Contains("Chi nhánh chia sẻ", cut.Markup); Assert.Contains("Đội dịch vụ", cut.Markup);
        Assert.Equal(3, cut.FindAll("tbody tr").Count);
        _db.RelationshipDetail = _db.RelationshipDetail! with { Employees = [], Branches = [], Teams = [] };
        cut = Render(); Assert.Equal(3, cut.FindAll(".ta-table-empty").Count);
    }
    [Fact] public void OutsideScope_AndPermissionDenial_DoNotExposeActions()
    {
        Seed(); var cut = RenderComponent<CustomerRelationships>(p => p.Add(x => x.CustomerId, Guid.NewGuid()));
        Assert.DoesNotContain("Existing", cut.Markup);
        Seed(false); cut = Render(); Assert.Empty(cut.FindAll("button.btn-sm"));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void CreateEditRevoke_UseServiceAndHandleConflicts(int kind)
    {
        Seed(); var cut = Render();
        var section = cut.FindAll(".ta-table-card")[kind]; section.QuerySelector(".card-header button")!.Click();
        cut.Find("select[aria-label='Chọn đối tượng']").Change(_db.RelationshipDetail!.EmployeeOptions[0].Id.ToString());
        cut.Find(".modal-footer .btn-ta-gold").Click(); Assert.Equal(1, _db.RelationWrites);
        section = cut.FindAll(".ta-table-card")[kind]; section.QuerySelector("tbody button")!.Click();
        _db.RelationResult = CrmRelationWriteResult.ConcurrencyConflict;
        cut.Find(".modal-footer .btn-ta-gold").Click(); Assert.Equal(2, _db.RelationWrites); Assert.Contains("đã thay đổi", cut.Markup);
        section = cut.FindAll(".ta-table-card")[kind]; section.QuerySelectorAll("tbody button")[1].Click();
        cut.Find(".modal-footer .btn-ta-gold").Click(); Assert.Equal(3, _db.RelationWrites);
    }
    [Fact] public void InvalidSelection_DoesNotWrite()
    {
        Seed(); var cut = Render(); cut.Find(".card-header button").Click(); cut.Find(".modal-footer .btn-ta-gold").Click();
        Assert.Equal(0, _db.RelationWrites); Assert.Contains("Vui lòng chọn", cut.Markup);
    }
}