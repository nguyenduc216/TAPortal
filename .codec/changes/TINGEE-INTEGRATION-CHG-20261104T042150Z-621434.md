# Change TINGEE-INTEGRATION-CHG-20261104T042150Z-621434

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T042150Z-621434`
- Created UTC: 2026-11-04T04:21:50.413424+00:00
- Tiêu đề: VietinBank personal/household links send the mobile number
- Trạng thái: IMPLEMENTED / TESTS_PASS

## Yêu cầu và phạm vi
Operator (2026-11-04): lỗi liên kết VietinBank cá nhân 400 "Verify string error … Verify string is invalid" là do thiếu số điện thoại. Tài liệu create-va vẫn ghi `mobile` "Không bắt buộc với … VietinBank (CTG)" nhưng thực tế ngân hàng cần. Sửa: catalog VietinBank Cá nhân/Hộ kinh doanh bắt buộc `mobile` (form Sandbox và Customer hiện ô số điện thoại, kiểm 10 số bắt đầu bằng 0, gửi lên Tingee). Doanh nghiệp không đổi.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeBankCatalog.cs`.
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs` (+3 assert).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests pass.

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
