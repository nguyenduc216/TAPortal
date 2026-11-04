# Report TINGEE-INTEGRATION-CHG-20261104T183759Z-3C176A

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261104T183759Z-3C176A`

1. Mục tiêu và kết quả thực tế: sửa lỗi tạo QR 400 'Purpose of transaction is wrong format' (nội dung chuyển khoản bỏ '-'). IMPLEMENTED.
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261104T183759Z-3C176A.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Release 0 lỗi; Safe tests 396/396 pass.
5. Publish: invoice (TACustomerPortal.Web) DEPLOYED 2026-11-04T18:41:35Z (server 2026-11-05 01:41:35), source commit f37576dbaab3c5e3c7757359007849951655915d, 70 files, artifact manifest SHA256 5776a8f91538864ddae30c4d94b55b1921b7849eeca220bba1bd7c305e22b842, package SHA256 D3E8F8151F0A077589691678E9967562F9F68404B79C29FB8235E10C7261E1DF, health HTTP 200, server backup C:\TADeploy\backups\invoice-20261105-014127. Lần chạy đầu FAILED ở bước upload (scp mất kết nối, server không thay đổi); chạy lại thành công.
6. Git: branch `feature/customer-portal-mvp01`, merge PENDING_APPROVAL.
7. Rủi ro/rollback: `Deploy-TARemote.ps1 -Target invoice -Rollback`.
8. Bằng chứng kiểm tra: `SafeCustomerPaymentsTests` (QR động/tĩnh).
