using TAPortal.Web.Services;

namespace TAPortal.Web.Models;

// Items is null when the data could not be read (the page shows an explicit "unavailable" state, not an empty list).
public sealed record ContractsIndexVm(IReadOnlyList<ContractListItem>? Items, ContractFilter Filter, ContractLookups Lookups, DateTime Today, string? Error);

public sealed record ContractDetailsVm(ContractDetail Contract, DateTime Today);

// Contract is null on the create screen.
public sealed record ContractEditVm(ContractDetail? Contract, ContractInput Input, ContractLookups Lookups)
{
    public bool IsCreate => Contract is null;
}
