using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// Admin → Quản lý khách hàng → Hợp đồng (dbo.CustomerContracts, dbo.CustomerContractFiles of migration 031).
// Every write runs in a transaction that re-checks the actor's CONTRACTS.* permission in SQL; contract updates and deletes
// use RowVersion optimistic concurrency; deletion is soft only (contracts and files). Files are stored in the database.
public sealed record ContractListItem(Guid Id, Guid CustomerId, string? CustomerCode, string CustomerName, string ContractNo, string Title,
    DateTime? SignedDate, DateTime? StartDate, DateTime? EndDate, decimal? ContractValue, string? BillingCycle, string Status,
    Guid? OwnerEmployeeId, string? OwnerName, int FileCount)
{
    public bool IsExpiringSoon(DateTime today) => ContractPolicy.IsExpiringSoon(Status, EndDate, today);
}

public sealed record ContractFileInfo(Guid Id, string FileName, string ContentType, int SizeBytes, DateTime CreatedAt);

public sealed record ContractFileContent(string FileName, string ContentType, byte[] Data);

public sealed record ContractDetail(Guid Id, Guid CustomerId, string? CustomerCode, string CustomerName, string ContractNo, string Title, string? ServiceDescription,
    DateTime? SignedDate, DateTime? StartDate, DateTime? EndDate, decimal? ContractValue, string? BillingCycle, string Status,
    Guid? OwnerEmployeeId, string? OwnerName, string? Note, DateTime CreatedAt, DateTime? UpdatedAt, string Stamp, IReadOnlyList<ContractFileInfo> Files)
{
    public bool IsExpiringSoon(DateTime today) => ContractPolicy.IsExpiringSoon(Status, EndDate, today);
}

public sealed record ContractCustomerOption(Guid Id, string? Code, string Name);

public sealed record ContractEmployeeOption(Guid Id, string? Code, string FullName);

public sealed record ContractLookups(IReadOnlyList<ContractCustomerOption> Customers, IReadOnlyList<ContractEmployeeOption> Employees)
{
    public static readonly ContractLookups Empty = new([], []);
}

public sealed class ContractFilter
{
    public Guid? CustomerId { get; set; }
    public string? Status { get; set; }
    public Guid? OwnerId { get; set; }
    public string? Q { get; set; }
    // "Sắp hết hạn": ACTIVE and EndDate within ContractPolicy.ExpiringDays days from today.
    public bool Expiring { get; set; }

    public string? StatusOrNull => ContractPolicy.IsStatus(Status) ? Status!.Trim().ToUpperInvariant() : null;

    // In-memory equivalent of ContractSql.List (used by the test fake and kept beside the SQL for review).
    public bool Matches(ContractListItem c, DateTime today)
    {
        if (CustomerId is Guid cu && c.CustomerId != cu) return false;
        if (StatusOrNull is string s && c.Status != s) return false;
        if (OwnerId is Guid o && c.OwnerEmployeeId != o) return false;
        if (Expiring && !c.IsExpiringSoon(today)) return false;
        var q = (Q ?? "").Trim();
        return q.Length == 0 || c.ContractNo.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ContractInput
{
    public Guid? CustomerId { get; set; }
    [StringLength(50, ErrorMessage = "Số hợp đồng tối đa 50 ký tự.")] public string? ContractNo { get; set; }
    [StringLength(250, ErrorMessage = "Tên hợp đồng tối đa 250 ký tự.")] public string? Title { get; set; }
    [StringLength(1000, ErrorMessage = "Mô tả dịch vụ tối đa 1000 ký tự.")] public string? ServiceDescription { get; set; }
    [DataType(DataType.Date)] public DateTime? SignedDate { get; set; }
    [DataType(DataType.Date)] public DateTime? StartDate { get; set; }
    [DataType(DataType.Date)] public DateTime? EndDate { get; set; }
    public decimal? ContractValue { get; set; }
    public string? BillingCycle { get; set; }
    public string? Status { get; set; } = ContractPolicy.Draft;
    public Guid? OwnerEmployeeId { get; set; }
    [StringLength(2000, ErrorMessage = "Ghi chú tối đa 2000 ký tự.")] public string? Note { get; set; }
    [ValidateNever] public string? Stamp { get; set; }

    public static ContractInput From(ContractDetail c) => new()
    {
        CustomerId = c.CustomerId, ContractNo = c.ContractNo, Title = c.Title, ServiceDescription = c.ServiceDescription, SignedDate = c.SignedDate,
        StartDate = c.StartDate, EndDate = c.EndDate, ContractValue = c.ContractValue, BillingCycle = c.BillingCycle, Status = c.Status,
        OwnerEmployeeId = c.OwnerEmployeeId, Note = c.Note, Stamp = c.Stamp
    };

    // Trims text, upper-cases codes and turns blanks into null (applied before validation and storage).
    public void Normalize()
    {
        ContractNo = ContractPolicy.NormalizeContractNo(ContractNo);
        Title = Clean(Title);
        ServiceDescription = Clean(ServiceDescription);
        Note = Clean(Note);
        Status = (Status ?? "").Trim().ToUpperInvariant();
        BillingCycle = Clean(BillingCycle)?.ToUpperInvariant();
        SignedDate = SignedDate?.Date; StartDate = StartDate?.Date; EndDate = EndDate?.Date;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ContractUpload(string FileName, string ContentType, byte[] Data);

public enum ContractOutcome { Saved, NoChange, NotFound, Forbidden, Conflict, Invalid, DuplicateNo, InvalidReference, AuditUnavailable }

public sealed record ContractResult(ContractOutcome Outcome, Guid? Id = null, bool AuditPersisted = false);

public sealed record ContractOption(string Code, string Label);

public static class ContractPolicy
{
    public const string Draft = "DRAFT", Active = "ACTIVE", Expired = "EXPIRED", Terminated = "TERMINATED";
    public const int ExpiringDays = 30;
    public const int MaxFileBytes = 10 * 1024 * 1024;   // CK_CustomerContractFiles_Size
    public const decimal MaxValue = 999_999_999_999_999_999m; // decimal(18,0)

    public static readonly IReadOnlyList<ContractOption> Statuses =
        [new(Draft, "Nháp"), new(Active, "Đang hiệu lực"), new(Expired, "Hết hạn"), new(Terminated, "Thanh lý")];

    public static readonly IReadOnlyList<ContractOption> BillingCycles =
        [new("ONE_TIME", "Một lần"), new("MONTHLY", "Hàng tháng"), new("QUARTERLY", "Hàng quý"), new("YEARLY", "Hàng năm")];

    public static bool IsStatus(string? code) => code is not null && Statuses.Any(s => s.Code == code.Trim().ToUpperInvariant());
    public static bool IsBillingCycle(string? code) => code is null || BillingCycles.Any(b => b.Code == code);

    public static string StatusLabel(string? code) => Statuses.FirstOrDefault(s => s.Code == code)?.Label ?? code ?? "—";
    public static string BillingCycleLabel(string? code) => code is null ? "—" : BillingCycles.FirstOrDefault(b => b.Code == code)?.Label ?? code;

    public static string StatusBadge(string? code) => code switch
    {
        Active => "ta-badge--success",
        Draft => "ta-badge--neutral",
        Expired => "ta-badge--warning",
        Terminated => "ta-badge--danger",
        _ => "ta-badge--neutral"
    };

    public static bool IsExpiringSoon(string status, DateTime? endDate, DateTime today) =>
        status == Active && endDate is DateTime end && end.Date >= today.Date && end.Date <= today.Date.AddDays(ExpiringDays);

    public static string? NormalizeContractNo(string? value)
    {
        var text = Regex.Replace(value ?? "", @"\s+", " ").Trim();
        return text.Length == 0 ? null : text;
    }

    // LIKE pattern with ESCAPE '\': user input never acts as a wildcard.
    public static string? Pattern(string? q)
    {
        var text = (q ?? "").Trim();
        if (text.Length == 0) return null;
        if (text.Length > 100) text = text[..100];
        return "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
    }

    // Field errors keyed by "Input.<Property>"; call on a normalized input. Empty when valid.
    public static IReadOnlyList<(string Key, string Message)> Validate(ContractInput i)
    {
        var errors = new List<(string, string)>();
        if (i.CustomerId is null || i.CustomerId == Guid.Empty) errors.Add(("Input.CustomerId", "Vui lòng chọn khách hàng."));
        if (i.ContractNo is null) errors.Add(("Input.ContractNo", "Vui lòng nhập số hợp đồng."));
        else if (i.ContractNo.Length > 50) errors.Add(("Input.ContractNo", "Số hợp đồng tối đa 50 ký tự."));
        if (i.Title is null) errors.Add(("Input.Title", "Vui lòng nhập tên hợp đồng."));
        else if (i.Title.Length > 250) errors.Add(("Input.Title", "Tên hợp đồng tối đa 250 ký tự."));
        if (i.ServiceDescription?.Length > 1000) errors.Add(("Input.ServiceDescription", "Mô tả dịch vụ tối đa 1000 ký tự."));
        if (i.Note?.Length > 2000) errors.Add(("Input.Note", "Ghi chú tối đa 2000 ký tự."));
        if (!IsStatus(i.Status)) errors.Add(("Input.Status", "Trạng thái hợp đồng không hợp lệ."));
        if (!IsBillingCycle(i.BillingCycle)) errors.Add(("Input.BillingCycle", "Chu kỳ thanh toán không hợp lệ."));
        if (i.ContractValue is decimal v)
        {
            if (v < 0) errors.Add(("Input.ContractValue", "Giá trị hợp đồng không được âm."));
            else if (v > MaxValue) errors.Add(("Input.ContractValue", "Giá trị hợp đồng quá lớn (tối đa 18 chữ số)."));
            else if (decimal.Truncate(v) != v) errors.Add(("Input.ContractValue", "Giá trị hợp đồng là số nguyên (đồng), không có phần lẻ."));
        }
        foreach (var (key, date) in new[] { ("Input.SignedDate", i.SignedDate), ("Input.StartDate", i.StartDate), ("Input.EndDate", i.EndDate) })
            if (date is DateTime d && (d.Year < 1990 || d.Year > 2100)) errors.Add((key, "Ngày không hợp lệ (năm từ 1990 đến 2100)."));
        if (i.StartDate is DateTime s && i.EndDate is DateTime e && e < s) errors.Add(("Input.EndDate", "Ngày kết thúc phải từ ngày bắt đầu trở đi."));
        return errors;
    }

    public static bool Unchanged(ContractDetail c, ContractInput i) =>
        c.CustomerId == i.CustomerId && c.ContractNo == i.ContractNo && c.Title == i.Title && c.ServiceDescription == i.ServiceDescription
        && c.SignedDate == i.SignedDate && c.StartDate == i.StartDate && c.EndDate == i.EndDate && c.ContractValue == i.ContractValue
        && c.BillingCycle == i.BillingCycle && c.Status == i.Status && c.OwnerEmployeeId == i.OwnerEmployeeId && c.Note == i.Note;

    public static string Json(ContractInput i) => AuditSql.Json(("customerId", i.CustomerId), ("contractNo", i.ContractNo), ("title", i.Title),
        ("signedDate", i.SignedDate?.ToString("yyyy-MM-dd")), ("startDate", i.StartDate?.ToString("yyyy-MM-dd")), ("endDate", i.EndDate?.ToString("yyyy-MM-dd")),
        ("contractValue", i.ContractValue), ("billingCycle", i.BillingCycle), ("status", i.Status), ("ownerEmployeeId", i.OwnerEmployeeId));

    // ---- Files ---------------------------------------------------------------------------------------------
    private static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    public static readonly string Accept = string.Join(",", Types.Keys.Concat(Types.Values.Distinct()));
    public const string AllowedText = "PDF, PNG, JPG, DOCX, XLSX, tối đa 10 MB";

    public static string? ContentTypeFor(string? fileName) =>
        Types.TryGetValue(Path.GetExtension(fileName ?? ""), out var type) ? type : null;

    // Keeps only the file name (no path), drops control and path characters, and caps it at 255 characters (extension kept).
    public static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/'));
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name) if (!char.IsControl(ch) && ch is not ('"' or '/' or '\\' or ':' or '*' or '?' or '<' or '>' or '|')) sb.Append(ch);
        name = sb.ToString().Trim().TrimStart('.');
        if (name.Length == 0) name = "tep";
        if (name.Length > 255)
        {
            var ext = Path.GetExtension(name);
            name = name[..(255 - ext.Length)] + ext;
        }
        return name;
    }

    // Checks extension, size and content signature (magic bytes; DOCX/XLSX are opened as ZIP packages).
    // Returns the stored content type, or an error message in Vietnamese.
    public static (string? ContentType, string? Error) CheckFile(string? fileName, byte[]? data)
    {
        if (data is null || data.Length == 0) return (null, "Chọn file cần đính kèm.");
        if (data.Length > MaxFileBytes) return (null, "File vượt quá 10 MB.");
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (ContentTypeFor(fileName) is not string type) return (null, $"Chỉ nhận file {AllowedText}.");
        var ok = ext switch
        {
            ".pdf" => StartsWith(data, "%PDF-"u8),
            ".png" => StartsWith(data, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            ".jpg" or ".jpeg" => StartsWith(data, [0xFF, 0xD8, 0xFF]),
            ".docx" => IsOfficePackage(data, "word/document.xml"),
            ".xlsx" => IsOfficePackage(data, "xl/workbook.xml"),
            _ => false
        };
        return ok ? (type, null) : (null, "Nội dung file không khớp với định dạng " + ext.TrimStart('.').ToUpperInvariant() + ".");
    }

    private static bool StartsWith(byte[] data, ReadOnlySpan<byte> magic) => data.Length >= magic.Length && data.AsSpan(0, magic.Length).SequenceEqual(magic);

    private static bool IsOfficePackage(byte[] data, string part)
    {
        if (!StartsWith(data, [0x50, 0x4B, 0x03, 0x04])) return false;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(data, false), ZipArchiveMode.Read);
            return zip.GetEntry(part) is not null && zip.GetEntry("[Content_Types].xml") is not null;
        }
        catch (InvalidDataException) { return false; }
    }

    public static string Size(int bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB" : bytes >= 1024 ? $"{bytes / 1024d:0} KB" : $"{bytes} B";
}

public interface IContractStore
{
    Task<IReadOnlyList<ContractListItem>> ListAsync(ContractFilter filter, DateTime today);
    Task<ContractDetail?> GetAsync(Guid id);
    Task<ContractLookups> LookupsAsync();
    Task<ContractResult> CreateAsync(Guid actorId, ContractInput input, string? ipAddress);
    Task<ContractResult> UpdateAsync(Guid actorId, Guid id, ContractInput input, string? ipAddress);
    Task<ContractResult> DeleteAsync(Guid actorId, Guid id, string? stamp, string? ipAddress);
    Task<ContractResult> AddFileAsync(Guid actorId, Guid contractId, ContractUpload file, string? ipAddress);
    Task<ContractResult> DeleteFileAsync(Guid actorId, Guid contractId, Guid fileId, string? ipAddress);
    Task<ContractFileContent?> DownloadAsync(Guid contractId, Guid fileId);
}

public static class ContractSql
{
    private const string Columns = """
c.Id,c.CustomerId,cu.Code,cu.Name,c.ContractNo,c.Title,c.SignedDate,c.StartDate,c.EndDate,c.ContractValue,c.BillingCycle,c.Status,c.OwnerEmployeeId,e.FullName
""";

    public const string List = "SELECT " + Columns + """
,
 (SELECT COUNT(1) FROM dbo.CustomerContractFiles f WHERE f.ContractId=c.Id AND f.IsDeleted=0)
FROM dbo.CustomerContracts c JOIN dbo.Customers cu ON cu.Id=c.CustomerId LEFT JOIN dbo.Employees e ON e.Id=c.OwnerEmployeeId
WHERE c.IsDeleted=0
 AND (@customer IS NULL OR c.CustomerId=@customer)
 AND (@status IS NULL OR c.Status=@status)
 AND (@owner IS NULL OR c.OwnerEmployeeId=@owner)
 AND (@q IS NULL OR c.ContractNo LIKE @q ESCAPE '\' OR c.Title LIKE @q ESCAPE '\')
 AND (@expiring=0 OR (c.Status='ACTIVE' AND c.EndDate IS NOT NULL AND c.EndDate>=@today AND c.EndDate<=DATEADD(day,30,@today)))
ORDER BY CASE WHEN c.EndDate IS NULL THEN 1 ELSE 0 END, c.EndDate, c.ContractNo;
""";

    public const string Contract = "SELECT " + Columns + """
,
 c.ServiceDescription,c.Note,c.CreatedAt,c.UpdatedAt,c.RowVersion
FROM dbo.CustomerContracts c JOIN dbo.Customers cu ON cu.Id=c.CustomerId LEFT JOIN dbo.Employees e ON e.Id=c.OwnerEmployeeId
WHERE c.Id=@id AND c.IsDeleted=0;
""";

    public const string Files = "SELECT Id,FileName,ContentType,SizeBytes,CreatedAt FROM dbo.CustomerContractFiles WHERE ContractId=@id AND IsDeleted=0 ORDER BY CreatedAt DESC;";

    public const string Download = """
SELECT f.FileName,f.ContentType,f.Data FROM dbo.CustomerContractFiles f JOIN dbo.CustomerContracts c ON c.Id=f.ContractId
WHERE f.Id=@file AND f.ContractId=@id AND f.IsDeleted=0 AND c.IsDeleted=0;
""";

    public const string Customers = "SELECT Id,Code,Name FROM dbo.Customers WHERE IsDeleted=0 ORDER BY Name;";
    public const string Employees = "SELECT Id,EmployeeCode,FullName FROM dbo.Employees WHERE IsDeleted=0 AND IsActive=1 ORDER BY FullName;";

    public const string CustomerExists = "SELECT COUNT(1) FROM dbo.Customers WHERE Id=@customer AND IsDeleted=0;";
    public const string OwnerExists = "SELECT COUNT(1) FROM dbo.Employees WHERE Id=@owner AND IsDeleted=0 AND IsActive=1;";

    public const string Lock = "SELECT RowVersion FROM dbo.CustomerContracts WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id AND IsDeleted=0;";
    // UX_CustomerContracts_ContractNo is filtered on IsDeleted=0: a deleted contract's number can be reused.
    public const string NoTaken = "SELECT COUNT(1) FROM dbo.CustomerContracts WITH (UPDLOCK, HOLDLOCK) WHERE ContractNo=@no AND IsDeleted=0 AND Id<>@id;";

    public const string Insert = """
INSERT dbo.CustomerContracts(Id,CustomerId,ContractNo,Title,ServiceDescription,SignedDate,StartDate,EndDate,ContractValue,BillingCycle,Status,OwnerEmployeeId,Note,CreatedBy)
VALUES(@id,@customer,@no,@title,@service,@signed,@start,@end,@value,@cycle,@status,@owner,@note,@by);
""";

    public const string Update = """
UPDATE dbo.CustomerContracts SET CustomerId=@customer,ContractNo=@no,Title=@title,ServiceDescription=@service,SignedDate=@signed,StartDate=@start,EndDate=@end,
 ContractValue=@value,BillingCycle=@cycle,Status=@status,OwnerEmployeeId=@owner,Note=@note,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;
""";

    // Soft delete only; the files stay with the (hidden) contract.
    public const string SoftDelete = "UPDATE dbo.CustomerContracts SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;";

    public const string InsertFile = "INSERT dbo.CustomerContractFiles(Id,ContractId,FileName,ContentType,SizeBytes,Data,CreatedBy) VALUES(@file,@id,@name,@type,@size,@data,@by);";
    public const string SoftDeleteFile = "UPDATE dbo.CustomerContractFiles SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@file AND ContractId=@id AND IsDeleted=0;";

    public const string FileName = "SELECT FileName FROM dbo.CustomerContractFiles WHERE Id=@file AND ContractId=@id AND IsDeleted=0;";

    public const string Entity = "dbo.CustomerContracts";
    public const string FileEntity = "dbo.CustomerContractFiles";
}

public sealed class SqlContractStore(IConfiguration configuration, AdminAuditOptions audit) : IContractStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<IReadOnlyList<ContractListItem>> ListAsync(ContractFilter filter, DateTime today)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(ContractSql.List, cn);
        cmd.Parameters.Add("@customer", SqlDbType.UniqueIdentifier).Value = Db(filter.CustomerId);
        cmd.Parameters.Add("@status", SqlDbType.VarChar, 20).Value = (object?)filter.StatusOrNull ?? DBNull.Value;
        cmd.Parameters.Add("@owner", SqlDbType.UniqueIdentifier).Value = Db(filter.OwnerId);
        cmd.Parameters.Add("@q", SqlDbType.NVarChar, 210).Value = (object?)ContractPolicy.Pattern(filter.Q) ?? DBNull.Value;
        cmd.Parameters.Add("@expiring", SqlDbType.Bit).Value = filter.Expiring;
        cmd.Parameters.Add("@today", SqlDbType.Date).Value = today.Date;
        var list = new List<ContractListItem>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetGuid(1), S(r, 2), r.GetString(3), r.GetString(4), r.GetString(5), D(r, 6), D(r, 7), D(r, 8),
                r.IsDBNull(9) ? null : r.GetDecimal(9), S(r, 10), r.GetString(11), r.IsDBNull(12) ? null : r.GetGuid(12), S(r, 13), r.GetInt32(14)));
        return list;
    }

    public async Task<ContractDetail?> GetAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await Read(cn, null, id);
    }

    public async Task<ContractLookups> LookupsAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var customers = new List<ContractCustomerOption>();
        await using (var cmd = new SqlCommand(ContractSql.Customers, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) customers.Add(new(r.GetGuid(0), S(r, 1), r.GetString(2)));
        var employees = new List<ContractEmployeeOption>();
        await using (var cmd = new SqlCommand(ContractSql.Employees, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) employees.Add(new(r.GetGuid(0), S(r, 1), r.GetString(2)));
        return new(customers, employees);
    }

    public Task<ContractResult> CreateAsync(Guid actorId, ContractInput input, string? ipAddress) =>
        Write(actorId, AdminPermissions.ContractsCreate, async (cn, tx) =>
        {
            input.Normalize();
            if (ContractPolicy.Validate(input).Count > 0) return new(ContractOutcome.Invalid);
            if (!await ReferencesValid(cn, tx, input, null)) return new(ContractOutcome.InvalidReference);
            var id = Guid.NewGuid();
            if (Convert.ToInt32(await Scalar(cn, tx, ContractSql.NoTaken, ("@no", input.ContractNo!), ("@id", id))) > 0) return new(ContractOutcome.DuplicateNo);
            await Exec(cn, tx, ContractSql.Insert, [("@id", id), .. Values(input, actorId)]);
            var written = await Audit(cn, tx, actorId, "CONTRACT_CREATED", ContractSql.Entity, id, "{}", ContractPolicy.Json(input), ipAddress);
            return new(ContractOutcome.Saved, id, written);
        });

    public Task<ContractResult> UpdateAsync(Guid actorId, Guid id, ContractInput input, string? ipAddress) =>
        Write(actorId, AdminPermissions.ContractsUpdate, async (cn, tx) =>
        {
            var rv = await Lock(cn, tx, id);
            if (rv is null) return new(ContractOutcome.NotFound);
            var current = (await Read(cn, tx, id))!;
            if (current.Stamp != input.Stamp) return new(ContractOutcome.Conflict, id);
            input.Normalize();
            if (ContractPolicy.Validate(input).Count > 0) return new(ContractOutcome.Invalid, id);
            if (ContractPolicy.Unchanged(current, input)) return new(ContractOutcome.NoChange, id);
            if (!await ReferencesValid(cn, tx, input, current)) return new(ContractOutcome.InvalidReference, id);
            if (Convert.ToInt32(await Scalar(cn, tx, ContractSql.NoTaken, ("@no", input.ContractNo!), ("@id", id))) > 0) return new(ContractOutcome.DuplicateNo, id);
            if (await Exec(cn, tx, ContractSql.Update, [("@id", id), ("@rv", rv), .. Values(input, actorId)]) != 1) return new(ContractOutcome.Conflict, id);
            var written = await Audit(cn, tx, actorId, "CONTRACT_UPDATED", ContractSql.Entity, id, ContractPolicy.Json(ContractInput.From(current)), ContractPolicy.Json(input), ipAddress);
            return new(ContractOutcome.Saved, id, written);
        });

    public Task<ContractResult> DeleteAsync(Guid actorId, Guid id, string? stamp, string? ipAddress) =>
        Write(actorId, AdminPermissions.ContractsDelete, async (cn, tx) =>
        {
            var rv = await Lock(cn, tx, id);
            if (rv is null) return new(ContractOutcome.NotFound);
            if (!string.Equals(Convert.ToHexString(rv), stamp, StringComparison.Ordinal)) return new(ContractOutcome.Conflict, id);
            var current = (await Read(cn, tx, id))!;
            if (await Exec(cn, tx, ContractSql.SoftDelete, ("@id", id), ("@rv", rv), ("@by", actorId)) != 1) return new(ContractOutcome.Conflict, id);
            var written = await Audit(cn, tx, actorId, "CONTRACT_SOFT_DELETED", ContractSql.Entity, id, ContractPolicy.Json(ContractInput.From(current)), AuditSql.Json(("isDeleted", true)), ipAddress);
            return new(ContractOutcome.Saved, id, written);
        });

    public Task<ContractResult> AddFileAsync(Guid actorId, Guid contractId, ContractUpload file, string? ipAddress) =>
        Write(actorId, AdminPermissions.ContractsUpdate, async (cn, tx) =>
        {
            var (type, error) = ContractPolicy.CheckFile(file.FileName, file.Data);
            if (type is null || error is not null) return new(ContractOutcome.Invalid, contractId);
            if (await Lock(cn, tx, contractId) is null) return new(ContractOutcome.NotFound);
            var fileId = Guid.NewGuid();
            var name = ContractPolicy.SafeFileName(file.FileName);
            await Exec(cn, tx, ContractSql.InsertFile, ("@file", fileId), ("@id", contractId), ("@name", name), ("@type", type), ("@size", file.Data.Length), ("@data", file.Data), ("@by", actorId));
            var written = await Audit(cn, tx, actorId, "CONTRACT_FILE_ADDED", ContractSql.FileEntity, fileId, "{}",
                AuditSql.Json(("contractId", contractId), ("fileName", name), ("contentType", type), ("sizeBytes", file.Data.Length)), ipAddress);
            return new(ContractOutcome.Saved, fileId, written);
        });

    public Task<ContractResult> DeleteFileAsync(Guid actorId, Guid contractId, Guid fileId, string? ipAddress) =>
        Write(actorId, AdminPermissions.ContractsUpdate, async (cn, tx) =>
        {
            if (await Lock(cn, tx, contractId) is null) return new(ContractOutcome.NotFound);
            var name = await Scalar(cn, tx, ContractSql.FileName, ("@file", fileId), ("@id", contractId)) as string;
            if (name is null || await Exec(cn, tx, ContractSql.SoftDeleteFile, ("@file", fileId), ("@id", contractId), ("@by", actorId)) != 1) return new(ContractOutcome.NotFound);
            var written = await Audit(cn, tx, actorId, "CONTRACT_FILE_SOFT_DELETED", ContractSql.FileEntity, fileId,
                AuditSql.Json(("contractId", contractId), ("fileName", name)), AuditSql.Json(("isDeleted", true)), ipAddress);
            return new(ContractOutcome.Saved, fileId, written);
        });

    public async Task<ContractFileContent?> DownloadAsync(Guid contractId, Guid fileId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(ContractSql.Download, cn);
        cmd.Parameters.Add("@file", SqlDbType.UniqueIdentifier).Value = fileId;
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = contractId;
        await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        if (!await r.ReadAsync()) return null;
        var name = r.GetString(0);
        var type = r.GetString(1);
        var data = (byte[])r.GetValue(2);
        return new(name, type, data);
    }

    // ---- helpers --------------------------------------------------------------------------------------------
    private async Task<ContractResult> Write(Guid actorId, string permission, Func<SqlConnection, SqlTransaction, Task<ContractResult>> body)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actorId, permission)) return new(ContractOutcome.Forbidden);
            var result = await body(cn, tx);
            if (result.Outcome == ContractOutcome.Saved) await tx.CommitAsync();
            return result;
        }
        catch (AuditUnavailableException) { return new(ContractOutcome.AuditUnavailable); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return new(ContractOutcome.DuplicateNo); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { return new(ContractOutcome.Conflict); }
        catch (SqlException ex) when (ex.Number is 547) { return new(ContractOutcome.InvalidReference); }
    }

    // The customer must be a not-deleted customer and the owner an active employee, unless unchanged on update.
    private static async Task<bool> ReferencesValid(SqlConnection cn, SqlTransaction tx, ContractInput input, ContractDetail? current)
    {
        if (current?.CustomerId != input.CustomerId && Convert.ToInt32(await Scalar(cn, tx, ContractSql.CustomerExists, ("@customer", input.CustomerId!.Value))) != 1) return false;
        if (input.OwnerEmployeeId is Guid owner && current?.OwnerEmployeeId != owner
            && Convert.ToInt32(await Scalar(cn, tx, ContractSql.OwnerExists, ("@owner", owner))) != 1) return false;
        return true;
    }

    private async Task<bool> Audit(SqlConnection cn, SqlTransaction tx, Guid actorId, string action, string entity, Guid id, string oldValues, string newValues, string? ip) =>
        (await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actorId, action, entity, id.ToString(), oldValues, newValues, ip)).Written;

    private static (string, object)[] Values(ContractInput i, Guid actorId) =>
    [
        ("@customer", i.CustomerId!.Value), ("@no", i.ContractNo!), ("@title", i.Title!), ("@service", Db(i.ServiceDescription)),
        ("@signed", DbDate(i.SignedDate)), ("@start", DbDate(i.StartDate)), ("@end", DbDate(i.EndDate)), ("@value", (object?)i.ContractValue ?? DBNull.Value),
        ("@cycle", Db(i.BillingCycle)), ("@status", i.Status!), ("@owner", Db(i.OwnerEmployeeId)), ("@note", Db(i.Note)), ("@by", actorId)
    ];

    private static async Task<byte[]?> Lock(SqlConnection cn, SqlTransaction tx, Guid id)
    {
        await using var cmd = new SqlCommand(ContractSql.Lock, cn, tx);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        return await cmd.ExecuteScalarAsync() as byte[];
    }

    private static async Task<ContractDetail?> Read(SqlConnection cn, SqlTransaction? tx, Guid id)
    {
        ContractDetail? c = null;
        await using (var cmd = new SqlCommand(ContractSql.Contract, cn, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                c = new(r.GetGuid(0), r.GetGuid(1), S(r, 2), r.GetString(3), r.GetString(4), r.GetString(5), S(r, 14), D(r, 6), D(r, 7), D(r, 8),
                    r.IsDBNull(9) ? null : r.GetDecimal(9), S(r, 10), r.GetString(11), r.IsDBNull(12) ? null : r.GetGuid(12), S(r, 13), S(r, 15),
                    r.GetDateTime(16), D(r, 17), Convert.ToHexString((byte[])r.GetValue(18)), []);
        }
        if (c is null) return null;
        var files = new List<ContractFileInfo>();
        await using (var cmd = new SqlCommand(ContractSql.Files, cn, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) files.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetDateTime(4)));
        }
        return c with { Files = files };
    }

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static object Db(Guid? value) => value is Guid g ? g : DBNull.Value;
    private static object DbDate(DateTime? value) => value is DateTime d ? d.Date : DBNull.Value;

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Command(cn, tx, sql, args);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<int> Exec(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Command(cn, tx, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    // Only the parameters the statement uses are added. @rv is the rowversion; other byte[] values are file contents.
    private static SqlCommand Command(SqlConnection cn, SqlTransaction tx, string sql, (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args)
        {
            if (!Regex.IsMatch(sql, Regex.Escape(name) + @"\b")) continue;
            if (name == "@rv") cmd.Parameters.Add(name, SqlDbType.Timestamp).Value = value;
            else if (value is byte[] b) cmd.Parameters.Add(name, SqlDbType.VarBinary, -1).Value = b;
            else if (name is "@signed" or "@start" or "@end") cmd.Parameters.Add(name, SqlDbType.Date).Value = value;
            else if (name == "@value") { var p = cmd.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 18; p.Scale = 0; p.Value = value; }
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }
}
