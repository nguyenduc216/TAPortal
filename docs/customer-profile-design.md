# Hồ sơ khách hàng (Customer Profile) — Thiết kế cấu trúc dữ liệu

Trạng thái: **Giai đoạn 1 đã code** (change `CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB`, migration 013 NOT_EXECUTED). Các giai đoạn sau: DRAFT.
Nguồn phân tích: `documents/Thông tin khách hàng.xlsx` (file Excel nội bộ hiện dùng để quản lý khách hàng).
Tài liệu này **không chứa** dữ liệu thật của khách hàng (mật khẩu, CCCD, số điện thoại…), chỉ mô tả cấu trúc.

---

## 1. Hiện trạng file Excel

File có 64 sheet, chia thành 3 nhóm:

| Nhóm | Sheet | Nội dung |
|---|---|---|
| **Hồ sơ từng khách hàng** | 51 sheet (`1.Impb`, `2.VNDis`, …, `37.BanhCanhCua`, kể cả 2 mẫu `Sheet2`, `Sheet2 (2)`) | Mỗi sheet = 1 khách hàng, dạng dọc "Danh mục – Nội dung – Ghi chú" |
| **Danh sách dạng bảng** | `HKD`, `Sheet1` | `HKD`: ~20 hộ kinh doanh (MST, mail, eTax mobile, phần mềm bán hàng, hóa đơn). `Sheet1`: thử chuyển hồ sơ sang dạng bảng ngang, bỏ dở (15 dòng) |
| **Vận hành định kỳ** | `Phí`, `Phí dv`, `Thu phí Q3`, `Check tờ khai 2025`, `Check tờ khai Q1.2026`, `CHECK Q2.26`, `CHECK Q3.26`, `Vấn đề phát sinh`, `Cơ hội`, `Thu tu van`, `DATA` | Bảng giá, nguồn khách, thu phí theo quý, tình trạng nộp tờ khai theo kỳ, việc phát sinh, cơ hội tư vấn, checklist hồ sơ tư vấn, danh mục viết tắt |

### 1.1. Các khối thông tin trong một sheet khách hàng

| # | Khối | Trường | Tỷ lệ có dữ liệu (/49 sheet) |
|---|---|---|---|
| 1 | Thông tin chung | Hình thức, MST, Tên, Địa chỉ, Vốn điều lệ, Mail công ty, Năm thành lập, Ngành nghề chính, Quy mô, Cơ quan thuế quản lý, Tình trạng hoạt động | MST 49, Tên 48, Địa chỉ 47, Năm TL 41, Mail 39, Vốn 37, Hình thức 30, Ngành nghề 23, CQ thuế 9, Quy mô 2 |
| 2 | Giám đốc / người phụ trách | Họ tên (GĐ, Tổng GĐ, GĐ thuê), Địa chỉ, CCCD, Ngày cấp, Năm sinh, Giới tính, Mail, SĐT | ~45 |
| 3 | thuedientu.gdt.gov.vn | Tên đăng nhập, Mật khẩu | 38 |
| 4 | hoadondientu.gdt.gov.vn | Tên đăng nhập, Mật khẩu | 36 |
| 5 | Chứng thư số | Nhà cung cấp, Hạn, Loại ký số, Tên đăng nhập, SĐT, Mật khẩu, (PIN) | NCC 23, Hạn 10 |
| 6 | Link xuất hóa đơn | Link, Mail/Tên đăng nhập, SĐT, Mật khẩu (có sheet có 2 link) | ~25 |
| 7 | Phần mềm BHXH | Tên PM, Link, (Mã đơn vị), Tên đăng nhập, Mật khẩu | ~14 |
| 8 | Phần mềm khác | Tên PM (Gmail, KiotViet, Fabi, MISA ID, CSDL giá, Thống kê DN…), Link, (Gian hàng), Tên đăng nhập, Mật khẩu | ~25 |
| 9 | Ngân hàng | Tên NH, Link, Số TK, (Tên đăng nhập, Mật khẩu, PIN) | ~13 |
| 10 | Dịch vụ sử dụng | Loại dịch vụ, Phí phát sinh | **0** (chỉ có chữ mẫu) |
| 11 | Nhân viên phụ trách | Nguồn khách, CSKH, Quản lý, Kế toán | **0** (chỉ có "Chọn") |

Sheet `HKD` bổ sung thêm cho hộ kinh doanh: **eTax Mobile** (tên đăng nhập/mật khẩu, trạng thái "Đã chấp nhận"), phần mềm bán hàng (link, tên gian hàng, tài khoản).

### 1.2. Vấn đề của cách quản lý hiện tại

1. **Mật khẩu để dạng chữ thường** (thuế điện tử, hóa đơn, chữ ký số, ngân hàng, PIN, email). Ai có file là có toàn bộ quyền truy cập hệ thống của ~50 khách hàng. Đây là rủi ro lớn nhất.
2. **Nhập sai ô**: cột Ghi chú chứa ngành nghề / mật khẩu / tên đăng nhập ngân hàng; ô "Mail giám đốc" chứa số vốn; ô "Tên ngân hàng" chứa MST… → khi chuyển dữ liệu cần có bước rà soát thủ công.
3. **Không chuẩn hóa**: "Hình thức" có 11 cách viết cho 4 loại hình; nhà cung cấp chữ ký số có 13 cách viết cho ~5 nhà cung cấp; ngày tháng lẫn kiểu chuỗi và kiểu ngày.
4. **Số lượng cố định**: mỗi khối chỉ có 1 "phần mềm khác", 1 ngân hàng → phải chèn tay "Link xuất hóa đơn 1/2".
5. **Không có mã khách hàng chung**: cùng một khách hàng có tên khác nhau giữa sheet hồ sơ, `Phí dv`, `Check tờ khai`, `Thu phí` (tên đầy đủ / tên viết tắt / tên thường gọi). Số thứ tự sheet bị trùng (32, 33, 34).
6. **Không có lịch sử, không cảnh báo**: hạn chứng thư số, tờ khai chưa nộp, phí chưa thu đều phải dò tay; không biết ai sửa gì, khi nào.
7. **Dữ liệu vận hành tách rời**: mỗi quý tạo một sheet `CHECK Qx` mới với cột khác nhau.

---

## 2. Nguyên tắc thiết kế

- **Mở rộng, không làm lại**: dùng tiếp `dbo.Customers` (đã có Code, Name, TaxCode, Email, Phone, Address, CustomerType, Status, OwnerBranchId) và **không sửa bảng này**; hồ sơ mở rộng nằm ở bảng 1:1 `dbo.CustomerProfiles`, nên các màn hình khách hàng hiện có không phụ thuộc migration mới và rollback chỉ là drop bảng mới.
- **Không đụng bảng Tingee**: `dbo.CustomerBankAccounts` (tài khoản nhận tiền qua Tingee) và `dbo.CustomerBuyers` / `dbo.CustomerContacts` (người mua của khách hàng) giữ nguyên. Thông tin tài khoản ngân hàng điện tử trong Excel là **thông tin đăng nhập**, đưa vào bảng tài khoản hệ thống (mục 3.3).
- **Số dòng không giới hạn**: mỗi khách hàng có N người đại diện, N tài khoản hệ thống, N dịch vụ — thay cho các khối cố định trong Excel.
- **Bí mật được mã hóa trong ứng dụng**: SQL Server 2014 không có Always Encrypted, nên mật khẩu/PIN được mã hóa bằng ASP.NET Core Data Protection (đã cấu hình trong `TAPortal.Web`) trước khi lưu; DB chỉ chứa `varbinary`.
- **Theo chuẩn hiện có của repo**: `dbo`, khóa `uniqueidentifier` + `NEWSEQUENTIALID()`, `CreatedAt/By`, `UpdatedAt/By`, `IsDeleted`, `RowVersion`, mã trạng thái `varchar` + `CHECK` constraint, tương thích SQL Server 2014.

---

## 3. Mô hình dữ liệu đề xuất

```
dbo.Customers
 ├── dbo.CustomerProfiles             Hồ sơ doanh nghiệp (1:1)
 ├── dbo.CustomerRepresentatives      Giám đốc / người phụ trách / kế toán phía KH
 ├── dbo.CustomerSystemAccounts       Tài khoản hệ thống: thuế, hóa đơn, CKS, BHXH, NH, phần mềm…
 ├── dbo.CustomerServices             Dịch vụ T.A cung cấp + mức phí
 ├── dbo.CustomerEmployeeAssignments  Nhân viên T.A phụ trách (CSKH, quản lý, kế toán…)
 ├── dbo.CustomerComplianceItems      Theo dõi tờ khai / hồ sơ / báo cáo theo kỳ
 └── dbo.CustomerCases                Vấn đề phát sinh, yêu cầu tư vấn, cơ hội
```

### 3.1. `dbo.CustomerProfiles` — 1:1 với `dbo.Customers` (khối "Thông tin chung")

Khóa chính = `CustomerId`. Tên, MST, địa chỉ, email, điện thoại vẫn ở `dbo.Customers`; bảng này giữ phần còn lại:

| Cột | Kiểu | Lấy từ Excel | Ghi chú |
|---|---|---|---|
| `ShortName` | nvarchar(100) | Tên sheet / tên gọi trong `Check tờ khai`, `Thu phí` | Tên thường gọi nội bộ, để tìm nhanh |
| `LegalForm` | varchar(30) | Hình thức | `LLC_1` (TNHH 1TV), `LLC_2` (TNHH 2TV+), `JSC` (Cổ phần), `PRIVATE` (DNTN), `HOUSEHOLD` (HKD), `INDIVIDUAL` (Cá nhân), `OTHER` |
| `CharterCapital` | decimal(18,0) | Vốn điều lệ | VNĐ |
| `EstablishedDate` | date | Năm/Ngày thành lập | |
| `MainIndustryCode` | varchar(10) | Ngành nghề chính (phần mã VSIC, vd 5610) | |
| `MainIndustryName` | nvarchar(500) | Ngành nghề chính (phần mô tả) | |
| `BusinessLine` | nvarchar(200) | Cột "Loại hình" trong `CHECK Qx` (Nhà hàng, Thi công, TM…) | Mô tả ngắn để phân nhóm |
| `TaxAuthority` | nvarchar(200) | Cơ quan thuế quản lý | Vd "Thuế cơ sở 2 tỉnh Khánh Hòa" |
| `TaxOfficerName` / `TaxOfficerPhone` | nvarchar(150) / varchar(30) | Cán bộ thuế quản lý | |
| `BusinessStatus` | varchar(20) | Tình trạng hoạt động | `OPERATING`, `SUSPENDED` (tạm ngưng), `DISSOLVING`, `DISSOLVED`. Tách khỏi `Status` (quan hệ dịch vụ với T.A) |
| `BusinessStatusDate` | date | Ghi chú "Tạm ngưng từ T9/2025" | |
| `ScaleNote` | nvarchar(200) | Quy mô | Ít dữ liệu, để dạng text |
| `SourceCode` | varchar(30) | Cột nguồn khách trong `Phí dv`, `Thu phí Q3` (Q, MA, BNI, H…) | Danh mục nguồn khách (mục 3.8) |
| `SourceNote` | nvarchar(250) | Người giới thiệu cụ thể | |
| `ParentCustomerId` | uniqueidentifier NULL | | Liên kết công ty ↔ HKD của cùng chủ (vd 1 công ty và 1 HKD cùng tên) |
| `Note` | nvarchar(max) | Ghi chú chung, vd "Tháng 12 chuyển tiền thuê nhà…" | |

Thời gian T.A phục vụ khách hàng (`ServiceStartDate/EndDate`) chuyển sang `dbo.CustomerServices` (mục 3.4).

### 3.2. `dbo.CustomerRepresentatives` — khối "Giám đốc / người phụ trách"

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id`, `CustomerId` | uniqueidentifier | FK → Customers |
| `RoleCode` | varchar(30) | `DIRECTOR`, `GENERAL_DIRECTOR`, `HIRED_DIRECTOR`, `OWNER` (chủ HKD), `CONTACT` (người phụ trách), `CHIEF_ACCOUNTANT`, `ACCOUNTANT` (kế toán phía KH) |
| `IsLegalRepresentative` | bit | Người đại diện theo pháp luật |
| `IsPrimaryContact` | bit | Người T.A liên hệ chính |
| `FullName` | nvarchar(150) | |
| `Gender` | varchar(10) | `MALE`, `FEMALE` |
| `DateOfBirth` | date | **PII** |
| `IdentityNumber` | varchar(20) | CCCD — **PII**, che khi hiển thị danh sách |
| `IdentityIssueDate`, `IdentityIssuePlace` | date, nvarchar(200) | |
| `Address` | nvarchar(500) | |
| `Email`, `Phone` | nvarchar(256), varchar(30) | |
| `Note`, `SortOrder` | | |
| Audit chuẩn | | `IsDeleted`, `CreatedAt/By`, `UpdatedAt/By`, `RowVersion` |

### 3.3. `dbo.CustomerSystemAccounts` — **lõi**: mọi tài khoản hệ thống của khách hàng

Một bảng chung cho các khối 3–9 của Excel và sheet `HKD`. Mỗi dòng là một tài khoản; khách hàng có bao nhiêu tài khoản cũng được.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id`, `CustomerId` | uniqueidentifier | |
| `SystemType` | varchar(30) | Xem bảng loại hệ thống bên dưới |
| `ProviderName` | nvarchar(100) | MISA, Viettel, VNPT, IntrustCA, KiotViet, iPOS, Sapo, Techcombank… (UI gợi ý danh sách chuẩn) |
| `SystemName` | nvarchar(150) | Tên hiển thị, vd "MISA meInvoice", "VssID doanh nghiệp" |
| `LoginUrl` | nvarchar(500) | |
| `Username` | nvarchar(256) | Tên đăng nhập / mail đăng nhập |
| `LoginPhone` | varchar(30) | SĐT nhận OTP |
| `StoreName` | nvarchar(150) | Tên gian hàng (KiotViet, Fabi…) |
| `UnitCode` | nvarchar(50) | Mã đơn vị BHXH |
| `BankAccountNumber` | varchar(50) | Chỉ dùng cho `BANKING` |
| `CertificateType` | varchar(20) | Chỉ dùng cho `DIGITAL_SIGNATURE`: `USB_TOKEN`, `REMOTE_SIGNING`, `HSM` |
| `CertificateSerial` | varchar(100) | |
| `ValidFrom`, `ExpiresAt` | date | Hạn chứng thư số / hạn gói phần mềm → **cảnh báo sắp hết hạn** |
| `PasswordCipher` | varbinary(max) | Mật khẩu đã mã hóa |
| `PinCipher` | varbinary(max) | PIN/mật khẩu cấp 2 đã mã hóa |
| `SecretChangedAt`, `SecretChangedBy` | datetime2, uniqueidentifier | Lần đổi mật khẩu gần nhất |
| `Status` | varchar(20) | `ACTIVE`, `PENDING` (đã đăng ký, chờ chấp nhận), `LOCKED`, `INVALID` (sai mật khẩu, "k đăng nhập được"), `INACTIVE` |
| `LastVerifiedAt` | datetime2 | Lần gần nhất nhân viên xác nhận đăng nhập được |
| `ManagedBy` | varchar(20) | `TA` (T.A giữ tài khoản), `CUSTOMER` (khách tự làm) |
| `Note`, `SortOrder` | | |
| Audit chuẩn | | |

**Danh mục `SystemType`** (ánh xạ từ Excel):

| SystemType | Khối Excel |
|---|---|
| `TAX_PORTAL` | thuedientu.gdt.gov.vn |
| `EINVOICE_PORTAL` | hoadondientu.gdt.gov.vn |
| `ETAX_MOBILE` | eTax Mobile (sheet `HKD`, một vài sheet hồ sơ) |
| `DIGITAL_SIGNATURE` | Chứng thư số |
| `EINVOICE_PROVIDER` | Link xuất hóa đơn (meInvoice, vInvoice, KiotViet, iPOS, Sapo, xCyber…) |
| `SOCIAL_INSURANCE` | Phần mềm BHXH |
| `BANKING` | Ngân hàng |
| `SALES_POS` | Phần mềm bán hàng (KiotViet, Fabi, POS365, MISA eShop, sàn TMĐT…) |
| `ACCOUNTING_SOFTWARE` | MISA SME / MISA ID… |
| `EMAIL` | Gmail / mail công ty dùng để nhận OTP |
| `GOV_OTHER` | CSDL giá, Thống kê doanh nghiệp… |
| `OTHER` | Phần mềm khác |

### 3.4. `dbo.CustomerServices` — khối "Dịch vụ sử dụng" + sheet phí

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id`, `CustomerId` | | |
| `ServiceCode` | varchar(30) | `ACCOUNTING_MONTHLY` (kế toán trọn gói), `TAX_HOUSEHOLD` (báo cáo thuế HKD), `ANNUAL_REPORT` (BCTC/quyết toán năm), `PAYROLL_SI` (lương, BHXH), `EINVOICE_SETUP`, `DIGITAL_SIGNATURE_SALE`, `ESTABLISHMENT` (thành lập/thay đổi/giải thể), `CONSULTING`, `OTHER` |
| `FeeAmount` | decimal(18,0) | Mức phí mỗi kỳ (trong `Thu phí Q3` đang ghi theo nghìn đồng) |
| `BillingCycle` | varchar(20) | `MONTHLY`, `QUARTERLY`, `YEARLY`, `ONE_TIME` |
| `VatRate` | decimal(5,2) | `Thanh toán` = `Tổng` × 1,08 → VAT 8% |
| `ReceivingAccount` | nvarchar(100) | Thu vào tài khoản nào (Cty / HKD…) |
| `StartDate`, `EndDate` | date | |
| `Status` | varchar(20) | `ACTIVE`, `PAUSED`, `ENDED` |
| `Note` | nvarchar(1000) | Phí phát sinh, thỏa thuận riêng |

Bảng giá theo số hóa đơn (sheet `Phí`: 30 số → 2,5tr, mỗi 10 số +300k…) để giai đoạn 2 (`dbo.ServicePriceTiers`), vì hiện mới là bảng tính nháp.

### 3.5. `dbo.CustomerEmployeeAssignments` — khối "Nhân viên phụ trách"

Bảng này có trong schema cũ nhưng chưa có migration trong repo; tạo nếu chưa có.

| Cột | Ghi chú |
|---|---|
| `CustomerId`, `EmployeeId` | FK → Customers, Employees |
| `RoleCode` | `ACCOUNT_MANAGER` (quản lý), `CUSTOMER_CARE` (CSKH), `ACCOUNTANT` (kế toán làm sổ), `SUPPORT` (hỗ trợ — cột "Hỗ trợ" trong `Thu phí Q3`), `SALES` (người mang khách về) |
| `ValidFrom`, `ValidTo`, `IsActive` | Lưu lịch sử bàn giao khách giữa nhân viên |

Đây cũng là cơ sở **phân quyền dữ liệu**: nhân viên chỉ thấy tài khoản hệ thống của khách hàng mình được phân công.

### 3.6. `dbo.CustomerComplianceItems` — thay các sheet `Check tờ khai …`, `CHECK Qx`

Một dòng = một nghĩa vụ của một khách hàng trong một kỳ. Không cần tạo sheet mới mỗi quý.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `CustomerId` | | |
| `PeriodType`, `PeriodYear`, `PeriodNo` | varchar(10), smallint, tinyint | `MONTH`/`QUARTER`/`YEAR`; vd QUARTER-2026-3 |
| `ItemType` | varchar(30) | `DOCS_RECEIVED` (HS T7/T8/T9 — đã nhận chứng từ tháng), `VAT_RETURN`, `PIT_RETURN`, `LICENSE_TAX` (môn bài), `HKD_01CNKD`, `CIT_RETURN`, `FINANCIAL_STATEMENT`, `PIT_FINALIZATION`, `STATISTICS_REPORT`, `LABOR_REPORT`, `UNION_FEE`, `SOCIAL_INSURANCE` |
| `Status` | varchar(20) | `NOT_STARTED`, `IN_PROGRESS`, `PREPARED` (xong tờ khai chưa nộp), `SUBMITTED` (đã nộp), `RECEIVED` (TB tiếp nhận), `ACCEPTED` (TB chấp nhận), `REJECTED` (không chấp nhận), `NOT_APPLICABLE` (không phát sinh), `CUSTOMER_SELF` (KH tự làm), `EXTENDED` (gia hạn) |
| `DueDate`, `SubmittedAt`, `AcceptedAt` | date | |
| `TaxPayable` | decimal(18,0) | Tiền thuế phải nộp |
| `TaxPaidStatus` | varchar(20) | `UNPAID`, `PAID`, `EXTENDED`, `NONE` |
| `AssigneeEmployeeId` | uniqueidentifier | NV phụ trách kỳ này |
| `Note` | nvarchar(1000) | Việc còn tồn đọng, vd "Lương, chi phí thuê nhà" |

Unique: (`CustomerId`, `PeriodType`, `PeriodYear`, `PeriodNo`, `ItemType`). Đầu mỗi kỳ hệ thống sinh sẵn các dòng dựa trên loại hình + dịch vụ của khách → nhân viên chỉ cập nhật trạng thái.

### 3.7. `dbo.CustomerCases` — thay `Vấn đề phát sinh` và `Cơ hội`

| Cột | Ghi chú |
|---|---|
| `CustomerId` (NULL được) / `ProspectName` | Cơ hội có thể là người chưa phải khách hàng |
| `CaseType` | `ISSUE` (vấn đề phát sinh), `CONSULTING` (nội dung tư vấn), `OPPORTUNITY` (cơ hội: thành lập HKD, đăng ký kê khai, giải thể…) |
| `Title`, `Content`, `WorkPlan`, `Result` | Tương ứng các cột Nội dung tư vấn / Nội dung công việc / Kết quả |
| `Status` | `OPEN`, `IN_PROGRESS`, `WAITING_CUSTOMER`, `DONE`, `CANCELLED` |
| `AssigneeEmployeeId`, `OpenedAt`, `DueDate`, `CompletedAt` | |

### 3.8. Danh mục

Dùng `CHECK` constraint cho các mã ít thay đổi (như các bảng Tingee hiện có). Riêng **nguồn khách** (`Q`, `MA`, `BNI`, `H`…) thay đổi theo kinh doanh nên đặt trong `dbo.Settings` hoặc một bảng `dbo.CustomerSources (Code, Name, IsActive)`. Sheet `DATA` (viết tắt HKD/CT/CN, ngành TM/DV/NH/SX/XD, nhà cung cấp CKS) dùng làm dữ liệu gợi ý cho UI.

Sheet `Thu tu van` (checklist hồ sơ tư vấn: pháp lý, nhân sự, tài sản, kho, bán hàng, TMĐT, marketing, khóa sổ) là tài liệu nghiệp vụ, đưa vào giai đoạn sau như mẫu checklist của `CustomerCases`.

---

## 4. Bảo mật thông tin đăng nhập

Đây là phần bắt buộc, vì toàn bộ giá trị của hệ thống nằm ở chỗ thay được file Excel chứa mật khẩu.

1. **Mã hóa**: `PasswordCipher`, `PinCipher` mã hóa AES-256-GCM trong `TAPortal.Web` bằng **một khóa cố định** `CustomerSecrets__Key` (sinh một lần bằng `deploy/windows/New-TACustomerSecretKey.ps1`). Khóa không tự đổi, nên chỉ cần **giữ một bản sao** ngoài server; mất cả hai = mất toàn bộ mật khẩu đã lưu. Customer Portal (`TACustomerPortal.Web`) không bao giờ đọc các cột này.
2. **Che mặc định**: màn hình chỉ hiện `••••••`. Nút "Hiện" / "Sao chép" cần quyền riêng `CUSTOMER_SECRET_REVEAL`, và chỉ với khách hàng mà nhân viên được phân công (mục 3.5) hoặc quản lý chi nhánh.
3. **Nhật ký**: mỗi lần xem/sao chép/sửa mật khẩu ghi `dbo.AuditLogs` (ActionCode `SECRET_REVEAL` / `SECRET_UPDATE`, EntityId = Id tài khoản), **không ghi giá trị**.
4. **PII**: CCCD, ngày sinh của người đại diện chỉ hiện đầy đủ ở màn hình chi tiết; danh sách và xuất Excel che bớt.
5. **Sau khi chuyển dữ liệu**: thu hồi/xóa các bản Excel đang chia sẻ, và khuyến nghị đổi mật khẩu các tài khoản quan trọng (thuế điện tử, ngân hàng), vì file đã lưu hành dạng chữ thường.

---

## 5. Giao diện (Admin — `TAPortal.Web`)

Trang chi tiết khách hàng gồm các tab, theo đúng thứ tự khối của Excel để nhân viên dễ chuyển đổi:

1. **Thông tin chung** (mục 3.1)
2. **Người đại diện** (3.2)
3. **Tài khoản hệ thống** (3.3) — nhóm theo `SystemType`, mỗi dòng có nút mở link, sao chép tên đăng nhập, hiện/sao chép mật khẩu
4. **Dịch vụ & phí** (3.4)
5. **Nhân viên phụ trách** (3.5)
6. **Tờ khai & hồ sơ theo kỳ** (3.6)
7. **Vấn đề / tư vấn** (3.7)

Màn hình tổng hợp:
- **Theo dõi kỳ** (thay `CHECK Qx`): bảng khách hàng × loại tờ khai của kỳ đang chọn, sửa trạng thái ngay trên lưới, lọc theo nhân viên.
- **Cảnh báo**: chứng thư số / phần mềm hết hạn trong 30 ngày; tài khoản `INVALID`; tờ khai sắp đến hạn chưa nộp.

---

## 6. Chuyển dữ liệu từ Excel

Theo `AGENTS.md`, agent không được chạy seed/DML trên `TAPortalSys`, nên việc nhập dữ liệu làm qua **chức năng Import trong Admin** do nhân viên thực hiện:

1. Upload file Excel → hệ thống đọc từng sheet hồ sơ theo nhãn cột D ("Mã số thuế", "Tên đăng nhập"…) và khối cột B, không phụ thuộc số dòng (các sheet lệch dòng nhau).
2. Chuẩn hóa: map "Hình thức" → `LegalForm`, tên nhà cung cấp CKS → tên chuẩn, ngày dạng chuỗi → `date`, MST dạng số → chuỗi (giữ số 0 đầu).
3. **Màn hình duyệt trước khi lưu**: từng khách hàng, từng trường, đánh dấu chỗ nghi nhập sai ô (ghi chú có dạng mật khẩu/MST/số tiền, mail không có `@`…), trùng MST với khách hàng đã có.
4. Lưu: mật khẩu được mã hóa ngay khi lưu; file upload không được giữ lại trên server.
5. Sheet `HKD` và các sheet vận hành (`CHECK Q3.26`, `Thu phí Q3`) nhập ở bước sau, sau khi đã có danh sách khách hàng chuẩn để khớp tên.

---

## 7. Lộ trình triển khai đề xuất

| Giai đoạn | Nội dung | Migration |
|---|---|---|
| 1 | `CustomerProfiles`, `CustomerRepresentatives`, `CustomerSystemAccounts` (mã hóa, quyền, audit), 3 tab — **đã code** | `013-customer-profile-core.sql` + verify `015` + rollback `016` |
| 2 | Import Excel có màn hình duyệt | — |
| 3 | `CustomerServices`, `CustomerEmployeeAssignments`, tab 4–5, phân quyền dữ liệu theo phân công | `014-…` |
| 4 | `CustomerComplianceItems`, màn hình theo dõi kỳ, cảnh báo | `015-…` |
| 5 | `CustomerCases`, bảng giá theo số hóa đơn, thu phí | `016-…` |

## 8. Câu hỏi cần chốt

1. Ai được xem mật khẩu: chỉ nhân viên được phân công, hay mọi kế toán trong chi nhánh?
2. Công ty và HKD của cùng một chủ: quản lý là 2 khách hàng riêng (có liên kết `ParentCustomerId`) — đồng ý?
3. Mã nguồn khách `Q`, `MA`, `BNI`, `H` nghĩa đầy đủ là gì?
4. Phí trong `Thu phí Q3` đang ghi theo nghìn đồng — xác nhận để import đúng đơn vị.
5. Có cần cho khách hàng xem/cập nhật một phần hồ sơ của họ trên Customer Portal không (hiện thiết kế là **không**)?
