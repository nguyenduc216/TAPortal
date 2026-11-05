using TAPortal.Web.Services;

namespace TAPortal.Web.Models;

// Customers → "Nhập từ Excel". Index = position of the sheet in the parsed workbook (sheet names contain spaces and
// symbols, the same bytes always parse to the same order).
public sealed record CustomerImportRow(int Index, ImportedCustomer Data, CustomerMatch? Match, string SuggestedCode)
{
    public bool CanImport => Match is not null || Data.Name is not null;
    public int Passwords => Data.Accounts.Count(a => !string.IsNullOrEmpty(a.Password)) + Data.Accounts.Count(a => !string.IsNullOrEmpty(a.Pin));
}

public sealed record CustomerImportVm(Guid? Token, string? FileName, IReadOnlyList<CustomerImportRow>? Rows, bool SecretsEnabled, int SkippedSheets = 0);

public sealed record CustomerImportResultRow(string SheetName, string? CustomerCode, string? CustomerName, Guid? CustomerId, CustomerImportResult? Result, string? Error);

public sealed record CustomerImportResultVm(string? FileName, IReadOnlyList<CustomerImportResultRow> Rows, bool SecretsEnabled);
