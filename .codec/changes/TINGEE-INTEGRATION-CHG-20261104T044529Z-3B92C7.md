# Change TINGEE-INTEGRATION-CHG-20261104T044529Z-3B92C7

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T044529Z-3B92C7`
- Created UTC: 2026-11-04T04:45:29.694239+00:00
- Tiêu đề: MB business bank links send the tax code in identity, not taxCode
- Trạng thái: DEPLOYED (portal, invoice)

## Yêu cầu và phạm vi
Operator (2026-11-04, theo Tingee): tài khoản doanh nghiệp MB phải truyền mã số thuế trong `identity`, không dùng `taxCode`.
Tài liệu create-va (đọc lại 2026-11-04): identity "Giấy tờ tùy thân của khách hàng (CMND/CCCD hoặc Mã số thuế). Không áp dụng với VCB, ACB và tài khoản Doanh nghiệp."; taxCode "Mã số thuế của doanh nghiệp / hộ kinh doanh." Tài liệu chưa khớp hướng dẫn của Tingee; làm theo Tingee.
Sửa: catalog MB Doanh nghiệp bắt buộc Identity (nhãn "Mã số thuế doanh nghiệp" ở Sandbox); `TingeeBankRule.TaxCodeAsIdentity` — doanh nghiệp có Identity thì không gửi taxCode (Sandbox ẩn ô MST riêng; Customer không gửi MST hồ sơ). Các ngân hàng/loại tài khoản khác không đổi.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeBankCatalog.cs`, `src/TAPortal.Web/Controllers/TingeeSandboxController.cs`, `src/TAPortal.Web/Views/TingeeSandbox/Index.cshtml`, `src/TACustomerPortal.Web/Controllers/BanksController.cs`.
- MODIFY tests `SafeAdminTingeeSandboxTests.cs`, `SafeCustomerPaymentsTests.cs`.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 343/343 (working tree).

## Publish
- Project:
- Trạng thái: DEPLOYED qua `deploy/windows/Deploy-TARemote.ps1` (audit PASS), source fc6e5e5.
- portal: artifact e0d2f38e86ff421f972d555795bad69057722027524141a6d58a6688c032194b, package 627D23078A5464BD90C1D2EB7F1975A4134DBF0975D1E84A437F5FD7C2050FA6, backup C:\TADeploy\backups\portal-20261104-114755, health 200.
- invoice: artifact 51800aaaed2b639c1d313b7c8f85965daa59dba0143c402f04e5806a6fd4532d, package 715E3537E3F6B55375AC8A21573BBC887ED004F908BD096366B455F1C52B8691, backup C:\TADeploy\backups\invoice-20261104-114809, health 200.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: fc6e5e5 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
