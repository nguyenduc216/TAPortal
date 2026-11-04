# Change TINGEE-INTEGRATION-CHG-20261104T092344Z-73A043

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T092344Z-73A043`
- Created UTC: 2026-11-04T09:23:44.190498+00:00
- Tiêu đề: Sandbox: đếm ngược thời gian hết hạn QR động
- Trạng thái: IMPLEMENTED / TESTS_PASS

## Yêu cầu và phạm vi
Operator (2026-11-04, ảnh mục 3 Sandbox): "Bổ sung phần đếm giây lùi để còn biết bao nhiêu giây" cho QR động.
- Dòng "Hết hạn" có nhãn `còn m:ss` (`data-ta-qr-countdown`, `data-expires` = hạn UTC ISO), cập nhật mỗi giây; còn ≤60 giây đổi màu cam; hết hạn đổi đỏ "đã hết hạn", dừng kiểm tra trạng thái và báo "QR đã hết hạn, chưa nhận đủ tiền"; nhận đủ tiền thì nhãn thành "đã thanh toán".

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/Views/TingeeSandbox/Index.cshtml`, `src/TAPortal.Web/wwwroot/js/ta-tingee-sandbox.js` (deploy portal).
- MODIFY `tests/TACustomerPortal.Tests/SafeAdminTingeeSandboxTests.cs`.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Safe tests 363/363 (working tree).

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
