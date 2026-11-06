# CODEC DELIVERY REPORT — CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T064001Z-46830A

Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T064001Z-46830A`

1. Mục tiêu và kết quả thực tế: trang Thêm/Sửa khách hàng — MST dán kèm dấu cách/ký tự vô hình không còn bị báo sai; điện thoại và Zalo tự bỏ khoảng trắng/dấu chấm/gạch (+84 ⇒ 0); form 3 cột đều.
2. Danh sách file cập nhật: xem `.codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T064001Z-46830A.md`.
3. Database: không có migration.
4. Build/test: Release build 0 lỗi; Safe tests 478/478.
5. Publish: invoice DEPLOYED 2026-11-06 13:49 (+07) từ commit 7ded836: artifact 1a2ccd15…62b62a7c, gói 17AB827B…89CFC5C, backup C:\TADeploy\backups\invoice-20261106-134923, health 200; production customer.css có `.cp-form-grid--3`.
   - Sự cố: commit e011217 (ghi nhận deploy của change 2721FA, phiên khác) được tạo từ worktree có index cũ (bcec7af) nên đã hoàn tác a5d4552; lần deploy 13:44 (e011217) vì vậy không có bản sửa. Đã khôi phục nguyên nội dung a5d4552 bằng 7ded836 (git diff a5d4552 7ded836 -- src tests rỗng), giữ hồ sơ Codec của cả hai change, deploy lại 13:49. Phiên kia đã xác nhận và reset worktree của họ.
6. Git: branch `feature/customer-portal-mvp01`, source commit a5d4552, khôi phục 7ded836; main fast-forward sau khi ghi nhận.
7. Rủi ro/việc còn lại: khách hàng đã lưu trước đây không tự chuẩn hóa; sửa và lưu lại sẽ chuẩn hóa.
8. Bằng chứng: SafeCustomerPaymentsTests.BuyerTypesDecideWhichDetailsAreRequiredAndKept; log deploy.
