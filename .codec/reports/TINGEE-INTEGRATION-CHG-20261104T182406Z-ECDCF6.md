# Report TINGEE-INTEGRATION-CHG-20261104T182406Z-ECDCF6

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261104T182406Z-ECDCF6`

1. Mục tiêu và kết quả thực tế: mã khách hàng tự động (KH001, KH002...) khi thêm khách hàng ở Customer Portal. IMPLEMENTED.
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261104T182406Z-ECDCF6.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Release 0 lỗi; Safe tests 396/396 pass.
5. Publish: invoice (TACustomerPortal.Web) DEPLOYED 2026-11-04T18:33:23Z (server 2026-11-05 01:33:23), source commit 241d1758b310b72a5ad4260c0b7bf0985d6be93c, 70 files, artifact manifest SHA256 13e483eaa4afd52921312cbbaa8a5a0b9448c2b05fb67b21abbb6585acae6981, package SHA256 49F19395FAE1C9C9979ECEFD73780194B91CB56B9F15129EEAE6EAD6043D8D68, health https://hoadon.tuvangiaiphapta.vn/ HTTP 200, server backup C:\TADeploy\backups\invoice-20261105-013316. Audit PASSED.
6. Git: branch `feature/customer-portal-mvp01`, merge PENDING_APPROVAL.
7. Rủi ro/rollback: `Deploy-TARemote.ps1 -Target invoice -Rollback`.
8. Bằng chứng kiểm tra: `SafeCustomerPaymentsTests.NewCustomerCodesAreNumberedAutomatically`.
