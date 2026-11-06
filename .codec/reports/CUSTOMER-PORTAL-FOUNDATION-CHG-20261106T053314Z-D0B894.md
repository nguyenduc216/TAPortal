# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T053314Z-D0B894

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T053314Z-D0B894`

1. Mục tiêu và kết quả thực tế: logo doanh nghiệp (Hồ sơ doanh nghiệp) và logo từng khách hàng (Khách hàng); trang thanh toán công khai
   `/pay/{token}` cho mỗi đơn (2 logo kiểu kết nối, tiêu đề đơn, MST + tên xuất hóa đơn sửa được với nút Lưu nhỏ, QR, số tiền, mã đơn, đếm ngược);
   nút "Xem link QR" + sao chép link trên trang đơn hàng. Hóa đơn điện tử dùng MST/tên người mua đã sửa. DEPLOYED.
2. File: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T053314Z-D0B894.md`.
3. Database `TAPortalSys`: `database/migrations/024-logos-and-order-pay-page.sql` (verify 037, rollback 038) — EXECUTED 2026-11-06T06:01:03Z.
4. Build/test: restore OK; Release build 0 errors; Safe tests 477/477 PASS; codec unittest OK; git diff --check OK.
5. Publish/deploy: invoice, webhooks, portal DEPLOYED từ source b2f1876 (artifact/package SHA256 trong change record); health 200.
6. Git: feature/customer-portal-mvp01, source commit b3a3889; merge main: xem hand-off.
7. Rủi ro/rollback: `Apply-TARelease.ps1 -Rollback` (1 bản backup/target); SQL rollback 038 từ chối nếu đã có logo/thông tin người mua.
   Link trang thanh toán cho phép người có link xem đơn và sửa MST/tên đến khi hóa đơn được xuất (theo yêu cầu).
8. Bằng chứng: log migration `.codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T053314Z-D0B894-migrations-20261106T055821Z.log`; smoke `/pay/<token lạ>` → 404 trên hoadon.tuvangiaiphapta.vn.
