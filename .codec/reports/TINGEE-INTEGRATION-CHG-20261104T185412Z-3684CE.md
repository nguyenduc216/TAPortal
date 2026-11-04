# Report TINGEE-INTEGRATION-CHG-20261104T185412Z-3684CE

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261104T185412Z-3684CE`

1. Mục tiêu và kết quả thực tế: chọn tài khoản nhận tiền khi tạo QR cho đơn; thời hạn QR nhập theo ngày/giờ/phút. IMPLEMENTED.
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261104T185412Z-3684CE.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Release 0 lỗi; Safe tests 398/398 pass.
5. Publish: invoice (TACustomerPortal.Web) DEPLOYED 2026-11-04T18:55:50Z (server 2026-11-05 01:55:50), source commit e07f66dd80a06fbe0f185e4f9171895aba1671ae, 70 files, artifact manifest SHA256 94b76188779f85ba680a6eec2f2d5231028b567fde7c8f12d2bc71a405fc1cfd, package SHA256 61EC9BB7C9360E1D890C807BED9D1447E26013EE9F705893BF328F413357547D, health HTTP 200, server backup C:\TADeploy\backups\invoice-20261105-015542.
6. Git: branch `feature/customer-portal-mvp01`, merge PENDING_APPROVAL.
7. Rủi ro/rollback: `Deploy-TARemote.ps1 -Target invoice -Rollback`.
8. Bằng chứng kiểm tra: `SafeCustomerPaymentsTests` (QrLifetimeIsEnteredInDaysHoursAndMinutes, TheDefaultAccountIsPreselectedAndADraftOrderCanSwitchAccountBeforeIssuingTheQr).
