# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE

## A. Identification
Feature `TINGEE-INTEGRATION`; change `TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE`; agent Claude Code (claude-opus-5-5); branch feature/customer-portal-mvp01.

## B. Audit Summary
| ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status |
|---|---|---|---|---|---|---|---|---|---|
| TG-01 | Integrations | Tingee API | HIGH | Tài khoản Merchant thường không test được | Luôn gửi merchantId, vốn chỉ dành cho Master | Chế độ Direct | TingeeApi.cs, TingeeOptions.cs | SafeTingeeDirectMerchantTests | FIXED |
| TG-02 | Database | CustomerContacts | HIGH (rủi ro, chưa xác nhận lỗi) | Bảng có sẵn trước 011, có thể lệch với code | Migration bỏ qua khi bảng đã tồn tại | Script 014 để kiểm tra | 014-diagnose-tingee-foundation.sql | NOT_RUN | PENDING_OPERATOR |
| TG-03 | Database | Rollback 013 | MEDIUM (rủi ro) | Có thể DROP bảng CustomerContacts không do 011 tạo nếu bảng trống | Rollback không biết nguồn gốc bảng | Chờ kết quả 014 | 013-rollback-tingee-foundation.sql | NOT_RUN | OPEN |

## C. Customer Portal Account Management
NOT_APPLICABLE.

## D. Admin CRUD
Tab Thanh toán Tingee thêm cảnh báo chế độ Direct và chặn tạo tài khoản con.

## E. Changed Files
Xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE.md` và commit source.

## F. Database
- Operator báo đã chạy migration 011 (kết thúc `OK`); log ghi `dbo.CustomerContacts exists`, nghĩa là bảng có từ trước và migration không tạo bảng hay index của nó. Nguồn gốc bảng chưa rõ: không có script nào khác trong repo tạo bảng này.
- Lần cấp quyền đầu lỗi `Msg 15151` vì còn tên principal mẫu; database chỉ có user `TA_User`. Trạng thái cấp quyền sau đó: NOT_VERIFIED.
- ADD `database/scripts/014-diagnose-tingee-foundation.sql`: chỉ đọc, chỉ in text, NOT_EXECUTED. Agent không chạy được vì MCP database đang lỗi (NOT_VERIFIED), nên cú pháp chưa được kiểm chứng trên SQL Server.
- `013-rollback-tingee-foundation.sql` sẽ DROP `CustomerContacts` nếu bảng trống dù bảng không do 011 tạo: không chạy khi chưa rõ nguồn gốc bảng.

## G. Build & Tests
dotnet restore: OK; Release build: 0 warning / 0 error; safe tests: 308 passed / 0 failed / 0 skipped; Codec unit tests: OK; codec validate: PASS; git diff --check: sạch. Tingee thật: NOT_VERIFIED.

## H. Admin Publish
- TAPortal.Web: `src\publish\deployed-TAPortal.Web`, exit 0, 2026-10-30T10:37:08Z→2026-10-30T10:37:17Z, 65 file, SHA256 `f16969a03d1c692863773bf608449856e1441048cc2082cf9c9ea2ccca8a5a1e` (artifact trước ở `publish\deployed-TAPortal.Web`: `9f9fa64103b37c0bcd127ae27bb4f475df6073bc8d2cdf17f2bfe21be79cd244`); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE-TAPortal.Web.sha256`

## I. Customer Publish
- TACustomerPortal.Web: `src\publish\deployed-TACustomerPortal.Web`, exit 0, 2026-10-30T10:37:29Z→2026-10-30T10:37:32Z, 58 file, SHA256 `336a9fb0726c1e4040f78a3596d34e6ecccc4dc856bd1fb3dce39ecd64e1fcb1` (artifact trước ở `publish\deployed-TACustomerPortal.Web`: `d932771b21c577fbe5359d848f035dddb40ae107a6c57a5e1e6fd489986c95e8`); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE-TACustomerPortal.Web.sha256`

Webhooks: - TAPortal.Webhooks: `src\publish\deployed-TAPortal.Webhooks`, exit 0, 2026-10-30T10:37:38Z→2026-10-30T10:37:41Z, 53 file, SHA256 `738d1eacfbc722d73fe3477f14e20f5d4ab43094fcf1d4de2fd620cb78f042fb` (artifact trước ở `publish\deployed-TAPortal.Webhooks`: `a51d29acdf543b7e30105c2a60c4430dc9cb3c1366a0a2a93d44bc9c2f1ad9c5`); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE-TAPortal.Webhooks.sha256`

Tất cả PUBLISHED / NOT_DEPLOYED.

## J. Security and Acceptance
Khóa Tingee chỉ cấu hình qua biến môi trường. Không có secret hoặc PII trong repo và artifact. Không tuyên bố production-ready.

## K. Git Delivery
Source `53dbe68cf9907ff3931fd593203435c15a180029`; base `205febb3dde0933dc80c6df9fab6f8dd172f81f3`; merge PENDING_APPROVAL.

## L. Operator Handoff
1. Chạy `014-diagnose-tingee-foundation.sql` và gửi kết quả text.
2. Cấu hình Admin và Customer: `Tingee__Mode=Direct`, `Tingee__ClientId`, `Tingee__SecretKey`, `Tingee__BaseUrl` đúng môi trường của key.
3. Liên kết một khách hàng test ở Admin rồi thử ngân hàng và đơn QR.
