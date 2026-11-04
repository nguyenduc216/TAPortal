# Change TINGEE-INTEGRATION-CHG-20261104T042150Z-621434

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T042150Z-621434`
- Created UTC: 2026-11-04T04:21:50.413424+00:00
- Tiêu đề: VietinBank personal/household links send the mobile number
- Trạng thái: DEPLOYED (portal, invoice)

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
- Trạng thái: DEPLOYED qua `deploy/windows/Deploy-TARemote.ps1` (audit PASS), source 3a3c063, 2026-11-04T04:24Z.
- portal: artifact 1b42a27328f507e3fed4100c4b21bb57f88ab6048eaa91426ac2a6d6d0ef5d7f, package 3BC7A482B31C76B25709DBDA91589A2CA3DAC6ACAE146A5ED7C30784B413A9F6, backup C:\TADeploy\backups\portal-20261104-112406, health 200.
- invoice: artifact 0e9b24a47b650cc6183a023f2c907316b448202568ba18b8b8b7af15103d96fb, package 646BFBA480C58EDE5B4AEDB1CD854CFF2819EE502D875949F8C35BCB136B9CB5, backup C:\TADeploy\backups\invoice-20261104-112414, health 200.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 3a3c063 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
