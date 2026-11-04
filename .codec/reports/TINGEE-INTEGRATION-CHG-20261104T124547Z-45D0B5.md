# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261104T124547Z-45D0B5

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261104T124547Z-45D0B5`

1. Mục tiêu và kết quả thực tế: đọc tài liệu Tingee "Hóa đơn điện tử" (openapi v1.2.2) và tạo trang Admin "Hóa đơn Sandbox" (menu Tingee) để T.A test kết nối tài khoản nhà cung cấp, lấy mẫu, tạo nháp/phát hành, tải PDF và gửi email. IMPLEMENTED, chưa deploy.
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261104T124547Z-45D0B5.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE (không có migration).
4. Build/test: `dotnet build TAPortal.sln -c Release` 0 lỗi; Safe tests 398/398 pass (9 test mới); `test_codec.py` OK; `git diff --cached --check` OK; `codec.py validate` FAIL có sẵn từ trước: `.codec/config.json` ghi publish path `D:\T.A\TAPortal\TAPortal\...` khác vị trí repo hiện tại `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal` (không do change này).
5. Publish: NOT_PUBLISHED / NOT_DEPLOYED — lệnh `Deploy-TARemote.ps1 -Target portal,invoice` bị chặn bởi quyền của phiên agent; cần operator chạy hoặc cho phép.
6. Git: branch `feature/customer-portal-mvp01`, source commit 43f9b10 (chưa push), merge PENDING_APPROVAL.
7. Rủi ro/rollback/việc operator cần làm: "Phát hành" với tài khoản nhà cung cấp thật tạo hóa đơn thật — chỉ dùng tài khoản demo/test. Rollback deploy: `Deploy-TARemote.ps1 -Target portal -Rollback`. Tingee thật NOT_VERIFIED.
8. Bằng chứng kiểm tra: Safe tests (`SafeAdminTingeeInvoiceSandboxTests`).
