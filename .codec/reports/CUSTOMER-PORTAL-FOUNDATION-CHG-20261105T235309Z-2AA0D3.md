# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261105T235309Z-2AA0D3

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261105T235309Z-2AA0D3`

1. Mục tiêu và kết quả thực tế: Customer Portal — tự đăng ký doanh nghiệp/hộ kinh doanh với tra cứu MST (tự điền tên, địa chỉ), đăng nhập ngay để chọn gói; khách hàng của doanh nghiệp theo loại (khách lẻ, hộ kinh doanh, doanh nghiệp, tổ chức) với nhóm khách, CCCD, Zalo, tài khoản ngân hàng; chỉ doanh nghiệp bắt buộc MST. DEPLOYED.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261105T235309Z-2AA0D3.md`.
3. Database `TAPortalSys`: migration 021-customer-buyer-profile.sql EXECUTED 2026-11-05T23:55:38Z qua Invoke-TAMigrations (pre-check NOT_APPLIED; dry-run PASS → rollback NOT_APPLIED, trancount 0; backup gần nhất 519 phút; gates 455/455; real run VERIFY_STATUS=PASS).
4. Build/test: Release build 0 lỗi; Safe tests 455/455; test_codec OK; git diff --check OK; codec validate: lỗi publish path có sẵn từ trước.
5. Publish: DEPLOYED invoice be5f2f0 lúc 2026-11-06 06:58 (+07): artifact 639c2346…d7754d, gói FF430606…29D36F, backup C:\TADeploy\backups\invoice-20261106-065837, health 200. Kiểm tra production: /account/register 200, trang đăng nhập có link đăng ký, /account/tax-lookup trả tên và địa chỉ cho MST công khai. Rollback: `Deploy-TARemote.ps1 -Target invoice -Rollback` (+ script 032 nếu cần gỡ cột, chỉ khi cột chưa có dữ liệu).
6. Git: branch `feature/customer-portal-mvp01`, source commit b3074b4, deploy commit be5f2f0; merge main: xem hand-off.
7. Rủi ro/việc còn lại: VietQR giới hạn lượt gọi theo IP (cache + thông báo nhập tay); chưa xác minh email và chưa có captcha khi đăng ký; thanh toán tự động gói dịch vụ là change sau. Form khách hàng chưa xem trên trình duyệt sau đăng nhập (operator kiểm tra).
8. Bằng chứng: SafeCustomerSignupTests, SafeCustomerPaymentsTests.BuyerTypesDecideWhichDetailsAreRequiredAndKept, log migration và deploy, kiểm tra production.
