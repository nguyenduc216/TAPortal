using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Web.Models;

// "Gói dịch vụ" page: balance of the company selected at sign-in, packages on sale, the company's purchases.
public sealed record ServicePackagesPageVm(CustomerMembership Customer, bool CanManage, ServiceBalance Balance,
    IReadOnlyList<ServicePackage> Packages, IReadOnlyList<ServicePurchase> Purchases, DateTime NowUtc);
