# Change TINGEE-INTEGRATION-CHG-20261104T051600Z-8F5DE8

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T051600Z-8F5DE8`
- Created UTC: 2026-11-04T05:16:00.359931+00:00
- Tiêu đề: Tingee menu group; sandbox link/unlink popups with a clear OTP step
- Trạng thái: DEPLOYED (portal)

## Yêu cầu và phạm vi
Operator (2026-11-04, sau khi liên kết thành công MB doanh nghiệp VQRQANIFW1102 và VietinBank cá nhân): đưa Tingee vào menu riêng; trang Sandbox chỉ giữ mục 1 (Cấu hình) và 2 (Kết nối) ở hàng trên; mục 2 có nút mở popup thêm liên kết và nút xóa liên kết từng tài khoản; mục QR lên thành mục 3 (webhook thành mục 4); OTP nằm trong popup và có dấu hiệu nhận biết rõ.
- Menu: nhóm "Tingee" (Tingee Sandbox, Tingee log) tách khỏi "Quản lý khách hàng".
- Popup liên kết (#tgLink): chọn ngân hàng/loại tài khoản, form; tự mở lại khi chọn ngân hàng hoặc có lỗi. Bước OTP thay form: thanh trạng thái vàng, nhãn "Bước 2/2", khung OTP có biểu tượng nhấp nháy, ô nhập lớn tự focus; trên trang có thêm băng "Đang chờ mã OTP" với nút mở lại popup.
- Popup xóa liên kết (#tgUnlink): xác nhận → `delete-va`; nếu ngân hàng yêu cầu OTP thì bước OTP tương tự → `confirm-delete-va`; nếu trả trang xác thực thì hiện nút mở. Action mới `DeleteVa`, `ConfirmDeleteVa` (SystemAdminOnly, CSRF, merchant theo scope như các action khác).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/Controllers/TingeeSandboxController.cs`, `Models/TingeeSandboxModels.cs`, `Views/TingeeSandbox/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `wwwroot/js/ta-tingee-sandbox.js`.
- MODIFY tests `SafeAdminTingeeSandboxTests.cs` (+3).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 346/346 (working tree). Giao diện popup chưa kiểm tra bằng trình duyệt trước deploy (cần đăng nhập production); operator xem lại.

## Publish
- Project:
- Trạng thái: DEPLOYED qua `deploy/windows/Deploy-TARemote.ps1 -Target portal` (audit PASS), source 1b4436d. Customer không đổi.
- portal: artifact 94ce29a4802c3cd93ba96d6d98688ce2af389f7db28f0207df7ae6c03d862e24, package C1850AB90B8B174B1EF6D8BE2493EF100D1D6D777625E3BC55420DD86BE39EAB, backup C:\TADeploy\backups\portal-20261104-121813, health 200.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 1b4436d (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
