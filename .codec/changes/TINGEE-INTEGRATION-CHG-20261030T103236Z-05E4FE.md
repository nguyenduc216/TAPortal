# Change TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE`
- Created UTC: 2026-10-30T10:32:36Z
- Tiêu đề: Direct Merchant mode for testing without Master Merchant; text diagnostic script
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE (Admin, Customer, Webhooks) / TINGEE_NOT_VERIFIED / VISUAL_NOT_VERIFIED / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu
Operator đã có tài khoản Tingee, muốn test giao dịch ngân hàng trước; chưa kích hoạt Master nên chưa tạo được khách hàng. Kiểm tra lại các thông tin database đã trao đổi bằng kết quả dạng text.

## Phạm vi đã làm
- `Tingee:Mode` (`Tingee__Mode`): `Master` mặc định, `Direct` dùng key của Merchant thường và không gửi `merchantId` (tài liệu Tingee: merchantId chỉ bắt buộc với Master Merchant).
- Admin ở chế độ Direct: hiện cảnh báo, ẩn và từ chối tạo tài khoản con, vẫn cho liên kết để bật chức năng cho khách hàng test.
- Script chẩn đoán 014 dạng text; thêm hướng dẫn chạy vào template cấp quyền.

## Giới hạn
- Direct dùng chung một tài khoản Tingee cho mọi khách hàng được liên kết, chỉ phù hợp để test với một khách hàng.
- Đơn QR động cập nhật qua kiểm tra trạng thái. Chưa có worker xử lý inbox, nên QR tĩnh chưa tự gạch nợ.
- Không có không gian tên UAT/production tự động: `Tingee__BaseUrl` mặc định là UAT; key production phải đặt BaseUrl production và là giao dịch tiền thật.

## Database
- Operator báo đã chạy migration 011 (kết thúc `OK`); log ghi `dbo.CustomerContacts exists`, nghĩa là bảng có từ trước và migration không tạo bảng hay index của nó. Nguồn gốc bảng chưa rõ: không có script nào khác trong repo tạo bảng này.
- Lần cấp quyền đầu lỗi `Msg 15151` vì còn tên principal mẫu; database chỉ có user `TA_User`. Trạng thái cấp quyền sau đó: NOT_VERIFIED.
- ADD `database/scripts/014-diagnose-tingee-foundation.sql`: chỉ đọc, chỉ in text, NOT_EXECUTED. Agent không chạy được vì MCP database đang lỗi (NOT_VERIFIED), nên cú pháp chưa được kiểm chứng trên SQL Server.
- `013-rollback-tingee-foundation.sql` sẽ DROP `CustomerContacts` nếu bảng trống dù bảng không do 011 tạo: không chạy khi chưa rõ nguồn gốc bảng.

## Test
dotnet restore: OK; Release build: 0 warning / 0 error; safe tests: 308 passed / 0 failed / 0 skipped; Codec unit tests: OK; codec validate: PASS; git diff --check: sạch. Tingee thật: NOT_VERIFIED (chưa có key trên máy này). Giao diện: VISUAL_NOT_VERIFIED.

## Publish
- TAPortal.Web: `src\publish\deployed-TAPortal.Web`, exit 0, 2026-10-30T10:37:08Z→2026-10-30T10:37:17Z, 65 file, SHA256 `f16969a03d1c692863773bf608449856e1441048cc2082cf9c9ea2ccca8a5a1e` (artifact trước ở `publish\deployed-TAPortal.Web`: `9f9fa64103b37c0bcd127ae27bb4f475df6073bc8d2cdf17f2bfe21be79cd244`); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE-TAPortal.Web.sha256`
- TACustomerPortal.Web: `src\publish\deployed-TACustomerPortal.Web`, exit 0, 2026-10-30T10:37:29Z→2026-10-30T10:37:32Z, 58 file, SHA256 `336a9fb0726c1e4040f78a3596d34e6ecccc4dc856bd1fb3dce39ecd64e1fcb1` (artifact trước ở `publish\deployed-TACustomerPortal.Web`: `d932771b21c577fbe5359d848f035dddb40ae107a6c57a5e1e6fd489986c95e8`); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE-TACustomerPortal.Web.sha256`
- TAPortal.Webhooks: `src\publish\deployed-TAPortal.Webhooks`, exit 0, 2026-10-30T10:37:38Z→2026-10-30T10:37:41Z, 53 file, SHA256 `738d1eacfbc722d73fe3477f14e20f5d4ab43094fcf1d4de2fd620cb78f042fb` (artifact trước ở `publish\deployed-TAPortal.Webhooks`: `a51d29acdf543b7e30105c2a60c4430dc9cb3c1366a0a2a93d44bc9c2f1ad9c5`); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE-TAPortal.Webhooks.sha256`
- Target mới, trống trước khi publish; artifact trước vẫn nguyên ở `publish\` và là bản rollback.
- Không có secret trong artifact. NOT_DEPLOYED; IIS NOT_CHECKED.

## Git
Branch feature/customer-portal-mvp01; base 205febb3dde0933dc80c6df9fab6f8dd172f81f3; source 53dbe68cf9907ff3931fd593203435c15a180029; delivery SHA báo sau khi push; PR #7 chưa merge.
