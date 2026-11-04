# Report TINGEE-INTEGRATION-CHG-20261104T185412Z-3684CE

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261104T185412Z-3684CE`

1. Mục tiêu và kết quả thực tế: chọn tài khoản nhận tiền khi tạo QR cho đơn; thời hạn QR nhập theo ngày/giờ/phút. IMPLEMENTED.
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261104T185412Z-3684CE.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Release 0 lỗi; Safe tests 398/398 pass.
5. Publish: invoice qua `Deploy-TARemote.ps1` - kết quả ghi ở commit hand-off sau deploy.
6. Git: branch `feature/customer-portal-mvp01`, merge PENDING_APPROVAL.
7. Rủi ro/rollback: `Deploy-TARemote.ps1 -Target invoice -Rollback`.
8. Bằng chứng kiểm tra: `SafeCustomerPaymentsTests` (QrLifetimeIsEnteredInDaysHoursAndMinutes, TheDefaultAccountIsPreselectedAndADraftOrderCanSwitchAccountBeforeIssuingTheQr).
