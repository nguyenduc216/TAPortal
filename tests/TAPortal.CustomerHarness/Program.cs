using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.FileProviders;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using TAPortal.CustomerHarness;

// CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — explicit opt-in loopback test host; no PortalDb, SQL configuration or production authentication changes.
if(Environment.GetEnvironmentVariable("TAPORTAL_CUSTOMERS_HARNESS")!="1") throw new InvalidOperationException("Test harness requires explicit opt-in.");
var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5201");
var service=DispatchProxy.Create<IBlazorPortalService,CustomerStore>();var store=(CustomerStore)(object)service;
builder.Services.AddSingleton(service);builder.Services.AddScoped<GlobalLoadingService>();
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(TAPortal.Web.Controllers.CustomersController).Assembly);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication("Harness").AddScheme<AuthenticationSchemeOptions,HarnessAuthentication>("Harness", _=>{});
builder.Services.AddAuthorization(o=>{foreach(var action in new[]{"Customers.Index","Customers.Create"}) o.AddPolicy("ACTION:"+action,p=>p.RequireAssertion(_=>action=="Customers.Create"?store.AllowCreate:store.AllowView));});
var app=builder.Build();
app.Use(async (context,next)=>{context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"11111111-1111-1111-1111-111111111111")},"IsolatedHarness"));await next();});
app.UseStaticFiles(new StaticFileOptions{FileProvider=new PhysicalFileProvider(Path.GetFullPath("../../src/TAPortal.Web/wwwroot",app.Environment.ContentRootPath))});
app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseAntiforgery();
app.MapGet("/test/reset",()=>{store.Rows.Clear();store.AllowCreate=store.AllowView=true;return Results.Ok();});
app.MapGet("/test/deny",()=>{store.AllowCreate=false;return Results.Ok();});
app.MapGet("/health",()=>Results.Ok());
app.MapGet("/customers",async (HttpContext http)=>{
 var action=new ActionContext(http,new RouteData(),new ActionDescriptor());
 var engine=http.RequestServices.GetRequiredService<IRazorViewEngine>();var view=engine.GetView(null,"/Views/Customers/Index.cshtml",false).View ?? throw new InvalidOperationException("Customer view missing");
 var data=new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()){Model=store.Rows.ToList()};data["CanCreate"]=store.AllowCreate;
 var temp=new TempDataDictionary(http,http.RequestServices.GetRequiredService<ITempDataProvider>());
 using var output=new StringWriter();await view.RenderAsync(new ViewContext(action,view,data,temp,output,new HtmlHelperOptions()));
 return Results.Content("<!doctype html><html lang='vi'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/@tabler/core@1.0.0-beta21/dist/css/tabler.min.css'><link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/@tabler/icons-webfont@3.19.0/dist/tabler-icons.min.css'><link rel='stylesheet' href='/css/ta-portal.css'></head><body><main class='container-xl py-3'>"+output+"</main><script src='/js/ta-portal.js'></script><script src='/js/taportal-customers.js'></script></body></html>","text/html");
});
app.MapRazorComponents<HarnessRoot>().AddInteractiveServerRenderMode();
app.MapControllerRoute("default","{controller}/{action=Index}/{id?}");
app.Run();

// CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — this handler exists only in the loopback test executable, never the published web app.
public sealed class HarnessAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
 protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"11111111-1111-1111-1111-111111111111")},"Harness")),"Harness")));
}

// CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — deterministic fake implements only customer operations and fails on unexpected service calls.
public class CustomerStore:DispatchProxy
{
 public List<CustomerRow> Rows {get;}=[];public bool AllowCreate=true,AllowView=true;
 protected override object? Invoke(MethodInfo? method,object?[]? args)
 {
  switch(method!.Name){
   case "HasFunctionActionAsync":return Task.FromResult((string)args![1]! == "Customers.Create"?AllowCreate:AllowView);
   case "GetCustomersAsync":return Task.FromResult(Rows.ToList());
   case "CreateCustomerAsync":return Save((CustomerCreateVm)args![0]!);
   default:throw new NotSupportedException("Unexpected isolated service method");
  }
 }
 // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — emulate the verified unique code rule and latency without SQL writes.
 private async Task<CustomerWriteResult> Save(CustomerCreateVm vm){await Task.Delay(300);if(!AllowCreate)return CustomerWriteResult.Forbidden;CustomerRules.Normalize(vm);if(!CustomerRules.IsValid(vm))return CustomerWriteResult.Invalid;if(Rows.Any(x=>x.Code.Equals(vm.Code,StringComparison.OrdinalIgnoreCase)))return CustomerWriteResult.DuplicateCode;Rows.Add(new(Guid.NewGuid(),vm.Code,vm.Name,vm.TaxCode,vm.Email,vm.Phone,vm.Status));return CustomerWriteResult.Success;}
}
