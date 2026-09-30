using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Web.Security;
using TAPortal.Web.Services;
namespace TAPortal.Web.Controllers;
[Authorize(Policy=PermissionPolicies.Prefix+"PARTNER.PARTNERS.VIEW")] public sealed class PartnersController(OperationalDb db):Controller{public async Task<IActionResult> Index()=>View(await db.Partners());}
[Route("banking")][Authorize] public sealed class BankingController(OperationalDb db):Controller{
 [HttpGet("accounts")][Authorize(Policy=PermissionPolicies.Prefix+"BANKING.BANK_ACCOUNTS.VIEW")] public async Task<IActionResult> Accounts()=>View(await db.BankAccounts());
 [HttpGet("transactions")][Authorize(Policy=PermissionPolicies.Prefix+"BANKING.TRANSACTIONS.VIEW")] public async Task<IActionResult> Transactions()=>View(await db.Transactions());}
[Authorize(Policy=PermissionPolicies.Prefix+"BANKING.PAYMENTS.VIEW")] public sealed class PaymentsController(OperationalDb db):Controller{public async Task<IActionResult> Index()=>View(await db.Payments());}
[Authorize(Policy=PermissionPolicies.Prefix+"INVOICE.INVOICES.VIEW")] public sealed class InvoicesController(OperationalDb db):Controller{public async Task<IActionResult> Index()=>View(await db.Invoices());}
[Route("billing")][Authorize] public sealed class BillingController(OperationalDb db):Controller{[HttpGet("credits")][Authorize(Policy=PermissionPolicies.Prefix+"BILLING.CREDITS.VIEW")]public async Task<IActionResult> Credits()=>View(await db.Credits());}
[Route("integrations")][Authorize] public sealed class IntegrationsController(OperationalDb db):Controller{[HttpGet("providers")][Authorize(Policy=PermissionPolicies.Prefix+"INTEGRATION.PROVIDERS.VIEW")]public async Task<IActionResult> Providers()=>View(await db.Providers());}
