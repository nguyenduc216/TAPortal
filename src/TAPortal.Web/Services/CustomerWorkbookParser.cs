using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// Reads the internal workbook "Thông tin khách hàng.xlsx": one sheet per customer, laid out vertically as
// block (column B) / label (column D) / value (column E) / note (column F). A few sheets have an extra "STT" column in
// front, shifting everything one column right. Rows are matched by label text, never by row number, because the
// sheets drift apart. Values the staff typed as hints ("Chọn", "Nam/Nữ"…) are ignored. Nothing here touches the
// database; CustomerImportStore saves what the user confirmed.
public sealed class ImportedAccount
{
    public SystemAccountInput Input { get; } = new() { Status = "ACTIVE", ManagedBy = "TA" };
    public string? Password { get; set; }
    public string? Pin { get; set; }
    public bool HasData => !string.IsNullOrWhiteSpace(Input.Username) || !string.IsNullOrWhiteSpace(Password) || !string.IsNullOrWhiteSpace(Input.LoginUrl)
        || !string.IsNullOrWhiteSpace(Input.BankAccountNumber) || !string.IsNullOrWhiteSpace(Input.ProviderName) || !string.IsNullOrWhiteSpace(Input.SystemName) || Input.ExpiresAt is not null
        || !string.IsNullOrWhiteSpace(Pin) || !string.IsNullOrWhiteSpace(Input.CertificateSerial) || !string.IsNullOrWhiteSpace(Input.UnitCode) || !string.IsNullOrWhiteSpace(Input.StoreName)
        || !string.IsNullOrWhiteSpace(Input.LoginPhone);
}

public sealed class ImportedCustomer
{
    public required string SheetName { get; init; }
    public string? TaxCode { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public CustomerProfileVm Profile { get; } = new();
    public List<RepresentativeInput> Representatives { get; } = [];
    public List<ImportedAccount> Accounts { get; } = [];
    public List<string> Warnings { get; } = [];
    public string SuggestedCode => CustomerWorkbookParser.SuggestCode(SheetName, Name);
    public bool IsEmpty => TaxCode is null && Name is null;
}

public static partial class CustomerWorkbookParser
{
    // Sheets that are tables, not customer profiles (fees, tax-return checks, issues…); imported in later phases.
    private static readonly string[] OperationalSheets = ["phi", "phi dv", "data", "hkd", "co hoi", "van de phat sinh", "thu tu van", "sheet1"];
    private static readonly string[] OperationalPrefixes = ["check to khai", "check q", "thu phi"];
    private static readonly string[] Placeholders = ["chon", "hkd/cong ty/ca nhan", "hoat dong/ tam ngung/ giai the", "hoat dong/tam ngung/giai the", "ke toan thang/", "nam/nu", "x"];

    public static List<ImportedCustomer> Parse(IReadOnlyList<XlsxSheet> sheets)
    {
        var result = new List<ImportedCustomer>();
        foreach (var sheet in sheets)
        {
            var key = Key(sheet.Name);
            if (OperationalSheets.Contains(key) || OperationalPrefixes.Any(o => key.StartsWith(o, StringComparison.Ordinal))) continue;
            var customer = ParseSheet(sheet);
            if (customer is not null) result.Add(customer);
        }
        return result;
    }

    private enum Block { None, General, Person, Account, Ignored }

    private static ImportedCustomer? ParseSheet(XlsxSheet sheet)
    {
        // Column of the block names: B normally, C when column B holds a leading "STT".
        var shift = Key(sheet.Text(1, 2)) == "stt" ? 1 : 0;
        int blockCol = 2 + shift, labelCol = 4 + shift, valueCol = 5 + shift, noteCol = 6 + shift;
        if (!Enumerable.Range(1, Math.Min(sheet.MaxRow, 15)).Any(r => Key(sheet.Text(r, blockCol)).Contains("thong tin chung", StringComparison.Ordinal))) return null;

        var c = new ImportedCustomer { SheetName = sheet.Name };
        var block = Block.None;
        string blockName = "";
        RepresentativeInput? person = null;
        ImportedAccount? account = null;
        var used = new HashSet<string>();
        var generalNotes = new List<string>();

        for (var row = 2; row <= Math.Min(sheet.MaxRow, 400); row++)
        {
            if (sheet.Text(row, blockCol) is { } blockText)
            {
                Close();
                blockName = blockText;
                (block, var systemType) = Classify(Key(blockText));
                if (block == Block.Person) person = NewPerson(Key(blockText));
                if (block == Block.Account) account = NewAccount(systemType!);
            }
            var label = sheet.Text(row, labelCol);
            var cell = sheet[row, valueCol];
            var value = Clean(cell?.Text);
            var note = Clean(sheet.Text(row, noteCol));
            if (label is null || block is Block.None or Block.Ignored) continue;
            var field = Key(label).TrimEnd(':', ' ');
            if (value is null && note is null) continue;

            switch (block)
            {
                case Block.General: General(c, field, value, cell, note, generalNotes, label); break;
                case Block.Person:
                    // A second name in the same block starts another person.
                    if (IsNameLabel(field) && person!.FullName.Length > 0 && value is not null) { Close(); person = NewPerson(Key(blockName)); }
                    PersonField(c, person!, field, value, cell, label);
                    if (note is not null) person!.Note = Join(person.Note, $"{label.TrimEnd(':')}: {note}");
                    break;
                case Block.Account:
                    var slot = AccountSlot(field);
                    // A label already filled in this block (e.g. a second "Link xuất hoá đơn") starts another account.
                    if (slot is not null && value is not null && !used.Add(slot)) { var type = account!.Input.SystemType; Close(); account = NewAccount(type); used.Add(slot); }
                    AccountField(c, account!, slot, value, cell, label);
                    if (note is not null)
                    {
                        if (LooksLikeSecret(note)) c.Warnings.Add($"{blockName} / {label.TrimEnd(':')}: ghi chú giống mật khẩu nên không nhập, hãy kiểm tra lại.");
                        else account!.Input.Note = Join(account.Input.Note, $"{label.TrimEnd(':')}: {note}");
                    }
                    break;
            }
        }
        Close();
        if (generalNotes.Count > 0) c.Profile.Note = Join(c.Profile.Note, string.Join("\n", generalNotes));
        if (c.IsEmpty) return null;
        if (c.TaxCode is null) c.Warnings.Add("Không có mã số thuế: sẽ tạo khách hàng mới, không khớp được với khách hàng đã có.");
        if (c.Name is null) c.Warnings.Add("Không có tên khách hàng: chỉ nhập được vào khách hàng đã có cùng mã số thuế.");
        return c;

        void Close()
        {
            if (person is not null && person.FullName.Length > 0) c.Representatives.Add(person);
            else if (person is not null && (person.Phone ?? person.Email ?? person.IdentityNumber) is not null)
                c.Warnings.Add($"{blockName}: có thông tin liên hệ nhưng không có họ tên nên không nhập.");
            if (account is not null && account.HasData) { account.Input.SortOrder = c.Accounts.Count(a => a.Input.SystemType == account.Input.SystemType); c.Accounts.Add(account); }
            person = null;
            account = null;
            used.Clear();
        }
    }

    private static (Block, string?) Classify(string block) => block switch
    {
        _ when block.Contains("thong tin chung") => (Block.General, null),
        _ when block.Contains("giam doc") || block.Contains("nguoi phu trach") || block.Contains("ke toan truong") => (Block.Person, null),
        _ when block.Contains("thuedientu") => (Block.Account, "TAX_PORTAL"),
        _ when block.Contains("hoadondientu") => (Block.Account, "EINVOICE_PORTAL"),
        _ when block.Contains("chung thu so") || block.Contains("chu ky so") => (Block.Account, "DIGITAL_SIGNATURE"),
        _ when block.Contains("xuat hoa don") => (Block.Account, "EINVOICE_PROVIDER"),
        _ when block.Contains("bhxh") || block.Contains("bao hiem") => (Block.Account, "SOCIAL_INSURANCE"),
        _ when block.Contains("etax") => (Block.Account, "ETAX_MOBILE"),
        _ when block.Contains("ngan hang") => (Block.Account, "BANKING"),
        _ when block.Contains("phan mem") || block.Contains("pham mem") => (Block.Account, "OTHER"),
        _ => (Block.Ignored, null), // "Dịch vụ sử dụng", "Nhân viên phụ trách": later phases
    };

    private static RepresentativeInput NewPerson(string block)
    {
        var contact = block.Contains("nguoi phu trach") && !block.Contains("giam doc");
        return new RepresentativeInput { RoleCode = contact ? "CONTACT" : "DIRECTOR", IsLegalRepresentative = !contact, FullName = "" };
    }

    private static ImportedAccount NewAccount(string systemType)
    {
        var a = new ImportedAccount();
        a.Input.SystemType = systemType;
        return a;
    }

    private static void General(ImportedCustomer c, string field, string? value, XlsxCell? cell, string? note, List<string> notes, string label)
    {
        if (note is not null && !LooksLikeSecret(note)) notes.Add($"{label.TrimEnd(':')}: {note}");
        if (value is null) return;
        var p = c.Profile;
        switch (field)
        {
            case "ma so thue":
                c.TaxCode = TaxCode(value, cell, out var taxWarning);
                if (taxWarning is not null) c.Warnings.Add(taxWarning);
                break;
            case "ten": c.Name = Limit(value, 250); break;
            case "dia chi": c.Address = Limit(value, 1000); break;
            case "mail cong ty" or "email" or "mail":
                if (IsEmail(value)) c.Email = value; else { notes.Add($"{label.TrimEnd(':')}: {value}"); c.Warnings.Add("Mail công ty không đúng định dạng email: đã chuyển vào ghi chú hồ sơ."); }
                break;
            case "hinh thuc":
                p.LegalForm = LegalForm(Key(value));
                if (p.LegalForm is null) notes.Add($"Hình thức: {value}");
                break;
            case "von dieu le":
                if (cell?.Number is { } capital && capital is >= 0 and < 1e18) p.CharterCapital = Math.Round((decimal)capital, 0);
                else if (decimal.TryParse(Regex.Replace(value, @"[^\d]", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0) p.CharterCapital = parsed;
                else notes.Add($"Vốn điều lệ: {value}");
                break;
            case "nam thanh lap" or "ngay thanh lap":
                if (DateValue(value, cell) is { } established) p.EstablishedDate = established;
                else notes.Add($"Năm thành lập: {value}");
                break;
            case "nganh nghe kinh doanh chinh" or "nganh nghe chinh" or "nganh nghe":
                var m = Regex.Match(value, @"^\s*(\d{4,5})\s*[:\-–.]\s*(.+)$", RegexOptions.Singleline);
                if (m.Success) { p.MainIndustryCode = m.Groups[1].Value; p.MainIndustryName = Limit(m.Groups[2].Value.Trim(), 500); }
                else p.MainIndustryName = Limit(value, 500);
                break;
            case "quy mo": p.ScaleNote = Limit(value, 200); break;
            case "co quan thue quan ly" or "co quan thue": p.TaxAuthority = Limit(value, 200); break;
            case "tinh trang hoat dong" or "tinh trang":
                p.BusinessStatus = BusinessStatus(Key(value));
                if (p.BusinessStatus is null) notes.Add($"Tình trạng hoạt động: {value}");
                break;
            default:
                if (LooksLikeSecret(value)) c.Warnings.Add($"Thông tin chung / {label.TrimEnd(':')}: giá trị giống mật khẩu nên không nhập, hãy kiểm tra.");
                else notes.Add($"{label.TrimEnd(':')}: {value}");
                break;
        }
    }

    private static bool IsNameLabel(string field) => field is "giam doc" or "tong giam doc" or "giam doc thue" or "ho ten" or "ho va ten" or "nguoi phu trach" or "ten";

    private static void PersonField(ImportedCustomer c, RepresentativeInput r, string field, string? value, XlsxCell? cell, string label)
    {
        if (value is null) return;
        switch (field)
        {
            case var f when IsNameLabel(f):
                r.FullName = Limit(value, 150);
                if (f == "tong giam doc") r.RoleCode = "GENERAL_DIRECTOR";
                if (f == "giam doc thue") { r.RoleCode = "HIRED_DIRECTOR"; r.IsLegalRepresentative = false; }
                break;
            case "cccd" or "cmnd" or "cccd/cmnd" or "so cccd":
                var id = cell?.Number is { } n ? n.ToString("0", CultureInfo.InvariantCulture) : Regex.Replace(value, @"\s", "");
                if (Regex.IsMatch(id, "^[0-9]{8,12}$"))
                {
                    if (cell?.Number is not null && id.Length is 11) { id = "0" + id; c.Warnings.Add($"CCCD của {Who(r)} được lưu dạng số trong Excel: đã thêm số 0 ở đầu, hãy kiểm tra."); }
                    r.IdentityNumber = id;
                }
                else r.Note = Join(r.Note, $"CCCD: {value}");
                break;
            case "ngay cap": r.IdentityIssueDate = DateValue(value, cell); if (r.IdentityIssueDate is null) r.Note = Join(r.Note, $"Ngày cấp: {value}"); break;
            case "noi cap": r.IdentityIssuePlace = Limit(value, 200); break;
            case "nam sinh" or "ngay sinh":
                r.DateOfBirth = DateValue(value, cell);
                if (r.DateOfBirth is null) r.Note = Join(r.Note, $"Năm sinh: {value}");
                break;
            case "gioi tinh":
                var g = Key(value);
                r.Gender = g is "nam" or "male" ? "MALE" : g is "nu" or "female" ? "FEMALE" : null;
                break;
            case var f when f.Contains("mail"):
                if (IsEmail(value)) r.Email = value;
                else { r.Note = Join(r.Note, $"{label.TrimEnd(':')}: {value}"); c.Warnings.Add($"Mail của {Who(r)} không đúng định dạng email: đã chuyển vào ghi chú."); }
                break;
            case "so dien thoai" or "dien thoai" or "sdt": r.Phone = Limit(Phone(value, cell), 30); break;
            case "dia chi": r.Address = Limit(value, 500); break;
            default:
                if (LooksLikeSecret(value)) c.Warnings.Add($"{Who(r)} / {label.TrimEnd(':')}: giá trị giống mật khẩu nên không nhập, hãy kiểm tra.");
                else r.Note = Join(r.Note, $"{label.TrimEnd(':')}: {value}");
                break;
        }
    }

    private static string? AccountSlot(string field) => field switch
    {
        _ when field.StartsWith("ten dang nhap") || field.StartsWith("mail dang nhap") || field == "tai khoan" || field == "user" => "user",
        _ when field.StartsWith("mat khau") || field.StartsWith("pass") || field is "mk" or "pw" or "pwd" => "password",
        _ when field is "pin" or "ma pin" => "pin",
        _ when field.StartsWith("link") || field == "website" => "url",
        _ when field.StartsWith("so dien thoai") || field == "sdt" => "phone",
        _ when field.StartsWith("nha cung cap") || field == "ten ngan hang" => "provider",
        _ when field.StartsWith("han") => "expires",
        _ when field.StartsWith("loai ky so") => "certtype",
        _ when field.StartsWith("ten phan mem") => "system",
        _ when field.StartsWith("ma don vi") => "unit",
        _ when field.Contains("gian hang") => "store",
        _ when field.StartsWith("so tai khoan") => "bank",
        _ when field.StartsWith("serial") => "serial",
        _ => null,
    };

    private static void AccountField(ImportedCustomer c, ImportedAccount a, string? slot, string? value, XlsxCell? cell, string label)
    {
        if (value is null) return;
        var i = a.Input;
        switch (slot)
        {
            case "user":
                if (cell?.Number is not null && i.SystemType is "TAX_PORTAL" or "EINVOICE_PORTAL" or "ETAX_MOBILE" && TaxCode(value, cell, out _) is { } taxLogin) i.Username = taxLogin;
                else i.Username = Limit(cell?.Number is { } n ? n.ToString("0", CultureInfo.InvariantCulture) : value, 256);
                if (cell?.Number is not null) NumericWarning(c, a, "tên đăng nhập");
                break;
            case "password":
                a.Password = cell?.Number is { } pn ? pn.ToString("0.###############", CultureInfo.InvariantCulture) : value;
                if (cell?.Number is not null) NumericWarning(c, a, "mật khẩu");
                break;
            case "pin":
                a.Pin = cell?.Number is { } pin ? pin.ToString("0", CultureInfo.InvariantCulture) : value;
                if (cell?.Number is not null) NumericWarning(c, a, "PIN");
                break;
            case "url":
                var url = value.Trim();
                if (!url.Contains("://", StringComparison.Ordinal) && Regex.IsMatch(url, @"^[\w-]+(\.[\w-]+)+(/.*)?$")) url = "https://" + url;
                if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)) i.LoginUrl = Limit(url, 500);
                else if (i.SystemType is "OTHER" or "SOCIAL_INSURANCE" && i.SystemName is null) i.SystemName = Limit(value, 150);
                else i.Note = Join(i.Note, $"{label.TrimEnd(':')}: {value}");
                break;
            case "phone": i.LoginPhone = Limit(Phone(value, cell), 30); break;
            case "provider": i.ProviderName = Limit(Provider(value), 100); break;
            case "expires": i.ExpiresAt = DateValue(value, cell); if (i.ExpiresAt is null) i.Note = Join(i.Note, $"Hạn: {value}"); break;
            case "certtype":
                var k = Key(value);
                i.CertificateType = k.Contains("tu xa") || k.Contains("remote") ? "REMOTE_SIGNING" : k.Contains("token") || k.Contains("usb") ? "USB_TOKEN" : k.Contains("hsm") ? "HSM" : null;
                if (i.CertificateType is null) i.Note = Join(i.Note, $"Loại ký số: {value}");
                break;
            case "system":
                i.SystemName = Limit(value, 150);
                if (i.SystemType == "OTHER") i.SystemType = OtherSoftwareType(Key(value));
                break;
            case "unit": i.UnitCode = Limit(value, 50); break;
            case "store": i.StoreName = Limit(value, 150); break;
            case "bank":
                if (cell?.Number is { } bn && bn >= 1e15) { i.Note = Join(i.Note, "Số tài khoản: cần nhập lại (Excel lưu dạng số làm sai các chữ số cuối)"); c.Warnings.Add("Số tài khoản ngân hàng quá dài bị Excel lưu dạng số nên sai chữ số: không nhập, hãy nhập lại."); }
                else i.BankAccountNumber = Limit(cell?.Number is { } bn2 ? bn2.ToString("0", CultureInfo.InvariantCulture) : value, 50);
                if (cell?.Number is not null) NumericWarning(c, a, "số tài khoản");
                break;
            case "serial": i.CertificateSerial = Limit(value, 100); break;
            default:
                if (LooksLikeSecret(value)) c.Warnings.Add($"{Blocks.Label(i.SystemType)} / {label.TrimEnd(':')}: giá trị giống mật khẩu ở dòng không nhận diện được nên không nhập, hãy kiểm tra.");
                else i.Note = Join(i.Note, $"{label.TrimEnd(':')}: {value}");
                break;
        }
    }

    private static void NumericWarning(ImportedCustomer c, ImportedAccount a, string what) =>
        c.Warnings.Add($"{Blocks.Label(a.Input.SystemType)}: {what} được Excel lưu dạng số, có thể đã mất số 0 ở đầu. Hãy kiểm tra sau khi nhập.");

    private static class Blocks
    {
        public static string Label(string systemType) => CustomerProfileCodes.Label(CustomerProfileCodes.SystemTypes, systemType);
    }

    private static string OtherSoftwareType(string name) => name switch
    {
        _ when name.Contains("gmail") || name.Contains("email") || name.Contains("mail") || name.Contains("outlook") => "EMAIL",
        _ when name.Contains("kiotviet") || name.Contains("fabi") || name.Contains("sapo") || name.Contains("pos") || name.Contains("ban hang") => "SALES_POS",
        _ when name.Contains("misa") && !name.Contains("ca") || name.Contains("ke toan") || name.Contains("fast") => "ACCOUNTING_SOFTWARE",
        _ when name.Contains("csdl") || name.Contains("thong ke") || name.Contains("dich vu cong") || name.Contains("dkkd") => "GOV_OTHER",
        _ => "OTHER",
    };

    private static string Provider(string value) => Key(value) switch
    {
        var k when k.Contains("viettel") => "Viettel",
        var k when k.Contains("vnpt") => "VNPT",
        var k when k.Contains("misa") => "MISA",
        var k when k.Contains("fpt") => "FPT",
        var k when k.Contains("bkav") => "BKAV",
        var k when k.Contains("easyca") || k.Contains("easy ca") => "EasyCA",
        var k when k.Contains("nacencomm") || k.Contains("ca2") => "Nacencomm (CA2)",
        var k when k.Contains("newca") => "NewCA",
        _ => value.Trim(),
    };

    private static string? LegalForm(string v) => v switch
    {
        _ when v.Contains("1 tv") || v.Contains("1tv") || v.Contains("mot thanh vien") => "LLC_1",
        _ when v.Contains("2 tv") || v.Contains("2tv") || v.Contains("hai thanh vien") || v.Contains("2 thanh vien") => "LLC_2",
        _ when v.Contains("co phan") || v == "cp" || v.Contains("jsc") => "JSC",
        _ when v.Contains("tu nhan") || v == "dntn" => "PRIVATE",
        _ when v.Contains("hop danh") => "PARTNERSHIP",
        _ when v.Contains("hkd") || v.Contains("ho kinh doanh") => "HOUSEHOLD",
        _ when v.Contains("ca nhan") => "INDIVIDUAL",
        _ when v.Contains("tnhh") => null, // TNHH without the member count: kept in the note
        _ => null,
    };

    private static string? BusinessStatus(string v) => v switch
    {
        _ when v.Contains("dang giai the") => "DISSOLVING",
        _ when v.Contains("giai the") => "DISSOLVED",
        _ when v.Contains("tam ngung") || v.Contains("tam dung") => "SUSPENDED",
        _ when v.Contains("hoat dong") || v.Contains("dang hd") => "OPERATING",
        _ => null,
    };

    public static string? TaxCode(string value, XlsxCell? cell, out string? warning)
    {
        warning = null;
        var text = cell?.Number is { } n ? n.ToString("0", CultureInfo.InvariantCulture) : Regex.Replace(value, @"\s", "");
        // 12 digits: personal identification number used as tax code (individuals, household businesses).
        if (Regex.IsMatch(text, @"^\d{11,12}$"))
        {
            if (text.Length == 11) { text = "0" + text; warning = "Mã số thuế (12 số) lưu dạng số trong Excel bị mất số 0 đầu: đã thêm lại, hãy kiểm tra."; }
            return text;
        }
        var m = Regex.Match(text, @"^(\d{9,10})(-)?(\d{3})?$");
        if (!m.Success || (m.Groups[3].Success && !m.Groups[2].Success && m.Groups[1].Length == 9)) { warning = $"Mã số thuế \"{value}\" không đúng định dạng: không nhập MST, hãy sửa sau."; return null; }
        var main = m.Groups[1].Value;
        if (main.Length == 9) { main = "0" + main; warning = "Mã số thuế lưu dạng số trong Excel bị mất số 0 đầu: đã thêm lại, hãy kiểm tra."; }
        return m.Groups[3].Success ? $"{main}-{m.Groups[3].Value}" : main;
    }

    private static DateOnly? DateValue(string value, XlsxCell? cell)
    {
        if (cell?.Date is { } d) return d;
        if (cell?.Number is { } n && n is > 20000 and < 80000) return DateOnly.FromDateTime(DateTime.FromOADate(n));
        string[] formats = ["d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "d.M.yyyy", "yyyy-MM-dd", "d/M/yy"];
        return DateOnly.TryParseExact(value.Trim().Split(' ')[0], formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    private static string Phone(string value, XlsxCell? cell)
    {
        if (cell?.Number is { } n)
        {
            var digits = n.ToString("0", CultureInfo.InvariantCulture);
            return digits.Length == 9 ? "0" + digits : digits; // Excel drops the leading 0 of mobile numbers
        }
        return value.Trim();
    }

    private static bool IsEmail(string value) => Regex.IsMatch(value.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$") && value.Length <= 256;

    // A note with no spaces mixing letters with digits or symbols looks like a password typed into the wrong column.
    internal static bool LooksLikeSecret(string note)
    {
        var t = note.Trim();
        if (t.Length is < 6 or > 40 || t.Contains(' ') || t.Contains("://", StringComparison.Ordinal) || t.Contains('@') && t.Contains('.')) return false;
        var classes = (t.Any(char.IsLower) ? 1 : 0) + (t.Any(char.IsUpper) ? 1 : 0) + (t.Any(char.IsDigit) ? 1 : 0) + (t.Any(ch => !char.IsLetterOrDigit(ch)) ? 1 : 0);
        return classes >= 3;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        var k = Key(t);
        return Placeholders.Any(p => k == p || p.EndsWith('/') && k.StartsWith(p, StringComparison.Ordinal)) ? null : t;
    }

    private static string Who(RepresentativeInput r) => r.FullName.Length > 0 ? r.FullName : "người đại diện";
    private static string? Join(string? a, string b) { var s = string.IsNullOrEmpty(a) ? b : a + "\n" + b; return s.Length > 1000 ? s[..1000] : s; }
    private static string Limit(string value, int max) { var t = value.Trim(); return t.Length > max ? t[..max] : t; }

    // Lower case, no diacritics (đ → d), single spaces: labels are compared on this form.
    internal static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var d = text.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ");
    }

    // "12.TCoT10" → "TCOT10", "37.BanhCanhCua" → "BANHCANHCUA": a starting point the user can edit before importing.
    public static string SuggestCode(string sheetName, string? customerName)
    {
        var source = Regex.Replace(sheetName, @"^\s*\d+\s*[.\-_]\s*", "");
        var code = Regex.Replace(Key(source).ToUpperInvariant(), "[^A-Z0-9]", "");
        if (code.Length < 2 && customerName is not null) code = Regex.Replace(Key(customerName).ToUpperInvariant(), "[^A-Z0-9]", "");
        return code.Length > 30 ? code[..30] : code;
    }
}
