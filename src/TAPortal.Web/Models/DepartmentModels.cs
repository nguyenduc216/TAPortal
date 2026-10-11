using System.ComponentModel.DataAnnotations;

namespace TAPortal.Web.Models;

public static class DepartmentRules
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 250;

    // FEATURE_ID: TAPORTAL-HR-MASTER-DATA
    // CHANGE_ID: TAPORTAL-20261010-HR-MASTER-DATA-001
    // Department codes have one canonical persisted representation.
    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
    {
        try
        {
            rowVersion = Convert.FromBase64String(value ?? string.Empty);
            return rowVersion.Length == 8;
        }
        catch (FormatException)
        {
            rowVersion = Array.Empty<byte>();
            return false;
        }
    }
}

public sealed record DepartmentRow(Guid Id, string Code, string Name, string? Description, int SortOrder, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, string RowVersion)
{
    public DateTime LastChangedAt => UpdatedAt ?? CreatedAt;
}

public class DepartmentCreateVm
{
    [Required(ErrorMessage = "Vui lòng nhập mã phòng ban.")]
    [StringLength(DepartmentRules.CodeMaxLength, ErrorMessage = "Mã phòng ban tối đa 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên phòng ban.")]
    [StringLength(DepartmentRules.NameMaxLength, ErrorMessage = "Tên phòng ban tối đa 250 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }

    [Range(0, 9999, ErrorMessage = "Thứ tự phải từ 0 đến 9999.")]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class DepartmentUpdateVm : DepartmentCreateVm
{
    [Required] public Guid Id { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class DepartmentMutationVm
{
    [Required] public Guid Id { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed record DepartmentPageVm(
    IReadOnlyList<DepartmentRow> Departments,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete);

public enum DepartmentWriteResult
{
    Success,
    DuplicateCode,
    ConcurrencyConflict,
    HasDependencies,
    NotFound
}
