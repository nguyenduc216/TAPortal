# Report TINGEE-INTEGRATION-CHG-20261104T182406Z-ECDCF6

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261104T182406Z-ECDCF6`

1. Mục tiêu và kết quả thực tế: mã khách hàng tự động (KH001, KH002...) khi thêm khách hàng ở Customer Portal. IMPLEMENTED.
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261104T182406Z-ECDCF6.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Release 0 lỗi; Safe tests 396/396 pass.
5. Publish: invoice qua `Deploy-TARemote.ps1` - kết quả ghi ở commit hand-off sau deploy.
6. Git: branch `feature/customer-portal-mvp01`, merge PENDING_APPROVAL.
7. Rủi ro/rollback: `Deploy-TARemote.ps1 -Target invoice -Rollback`.
8. Bằng chứng kiểm tra: `SafeCustomerPaymentsTests.NewCustomerCodesAreNumberedAutomatically`.
