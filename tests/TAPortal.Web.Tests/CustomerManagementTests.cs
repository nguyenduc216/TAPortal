using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Security.Claims;
using System.Xml.Linq;
using TAPortal.Web.Components.Pages;
using TAPortal.Web.Controllers;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — all mutations are in-memory; no production account or SQL write.
public sealed class CustomerManagementTests : TestContext
{
    private readonly FakePortalService _db = new();
    public CustomerManagementTests()
    {
        Services.AddSingleton<IBlazorPortalService>(_db); Services.AddSingleton(new GlobalLoadingService());
        var auth = this.AddTestAuthorization(); auth.SetAuthorized("customer-test"); auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, FakePortalService.UserId.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
    [Fact]
    public void Validation_RequiredLengthsEmailAndOptionalTax()
    {
        var vm = new CustomerCreateVm(); Assert.False(CustomerRules.IsValid(vm));
        vm.Code = " C001 "; vm.Name = " Customer "; vm.Email = " "; CustomerRules.Normalize(vm);
        Assert.True(CustomerRules.IsValid(vm)); Assert.Null(vm.Email); Assert.Equal("C001", vm.Code);
        vm.Email = "bad-email"; Assert.False(CustomerRules.IsValid(vm));
        vm.Email = null; vm.Name = new string('a',251); Assert.False(CustomerRules.IsValid(vm));
        vm.Name = "Customer"; vm.Status = "UNKNOWN"; Assert.False(CustomerRules.IsValid(vm));
    }
    [Theory]
    [InlineData(CustomerWriteResult.DuplicateCode)]
    [InlineData(CustomerWriteResult.Failed)]
    public void FailedSave_PreservesModalAndValues(CustomerWriteResult result)
    {
        _db.CustomerResult = result;
        var cut = RenderComponent<Customers>(); OpenAndFill(cut);
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains(CustomerRules.Message(result), cut.Find("dialog").TextContent));
        Assert.Equal("C001", cut.Find("input[aria-label='Mã khách hàng']").GetAttribute("value"));
        Assert.Equal(1, _db.CustomerWrites);
    }
    [Fact]
    public async Task SaveSuccess_BlocksDuplicateSubmissionsAndRefreshes()
    {
        _db.CustomerSavePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = RenderComponent<Customers>(); OpenAndFill(cut);
        var save = cut.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        cut.WaitForAssertion(() => Assert.Equal(1, _db.CustomerWrites));
        await cut.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        Assert.Equal(1, _db.CustomerWrites); Assert.Contains("Đang lưu",cut.Markup);
        _db.CustomerSavePending.SetResult(CustomerWriteResult.Success); await save;
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("dialog")));
        Assert.Contains("Customer One",cut.Find("tbody").TextContent); Assert.Contains("thành công",cut.Markup);
    }
    [Fact]
    public void RevokedCreate_IsRejectedBeforeServiceWrite()
    {
        var cut = RenderComponent<Customers>(); OpenAndFill(cut); _db.DeniedActions.Add("Customers.Create");
        cut.Find("form").Submit(); Assert.Equal(0,_db.CustomerWrites); Assert.Contains("không có quyền",cut.Markup);
    }
    [Fact]
    public void QueryError_DoesNotShowEmpty_AndRetryRecovers()
    {
        _db.CustomerReadFails = true; var cut=RenderComponent<Customers>();
        Assert.Empty(cut.FindAll(".ta-customer-empty")); Assert.DoesNotContain("private database detail",cut.Markup);
        _db.CustomerReadFails=false;cut.Find(".alert button").Click();cut.WaitForAssertion(()=>Assert.Single(cut.FindAll(".ta-customer-empty")));
    }
    [Fact]
    public async Task Export_UsesCurrentPermissionFilterAndTextSafeWorkbook()
    {
        _db.Customers.Add(new(Guid.NewGuid(),"001","=HYPERLINK(\"unsafe\")","00001","a@example.com","0123","ACTIVE"));
        _db.Customers.Add(new(Guid.NewGuid(),"002","Other",null,null,null,"ACTIVE"));
        var controller = new CustomersController(_db) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,FakePortalService.UserId.ToString())},"test")) } } };
        var file=Assert.IsType<FileContentResult>(await controller.Export("001"));
        Assert.EndsWith(".xlsx",file.FileDownloadName);Assert.Equal(CustomerWorkbook.ContentType,file.ContentType);
        using var zip=new ZipArchive(new MemoryStream(file.FileContents));
        using var stream=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();var xml=XDocument.Load(stream);XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Equal(2,xml.Descendants(ns+"row").Count());Assert.Empty(xml.Descendants(ns+"f"));Assert.All(xml.Descendants(ns+"c"),c=>Assert.Equal("inlineStr",(string?)c.Attribute("t")));
        Assert.Contains("=HYPERLINK",xml.ToString());Assert.DoesNotContain("Other",xml.ToString());
        var dir=Path.Combine(AppContext.BaseDirectory,"customers-001-fixtures");Directory.CreateDirectory(dir);File.WriteAllBytes(Path.Combine(dir,"customers.xlsx"),file.FileContents);
        _db.DeniedActions.Add("Customers.Index");Assert.IsType<ForbidResult>(await controller.Export(null));Assert.IsType<ForbidResult>(await controller.Create(new(){Code="C",Name="N"}));Assert.Equal(0,_db.CustomerWrites);
    }
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — interact with the actual form rather than invoking private save methods.
    private static void OpenAndFill(IRenderedComponent<Customers> cut)
    {
        cut.Find(".btn-ta-gold").Click();cut.Find("input[aria-label='Mã khách hàng']").Change("C001");cut.Find("input[aria-label='Tên khách hàng']").Change("Customer One");
    }
}
