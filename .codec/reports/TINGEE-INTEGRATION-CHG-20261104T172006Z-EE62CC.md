# Report TINGEE-INTEGRATION-CHG-20261104T172006Z-EE62CC

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261104T172006Z-EE62CC`

1. Mục tiêu và kết quả thực tế: Customer Portal "Liên kết tài khoản" — bấm logo ngân hàng mở popup Bank Link (SDK) của đối tác; loại tài khoản, thông tin và OTP nhập trong popup; khi thành công danh sách tài khoản được đồng bộ và thêm tài khoản mới. IMPLEMENTED.
2. Danh sách file cập nhật: xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261104T172006Z-EE62CC.md`.
3. Database `TAPortalSys`: NOT_APPLICABLE (không có migration).
4. Build/test: `dotnet build TAPortal.sln -c Release --no-restore` 0 lỗi; Safe tests 405/405 pass (3 test mới). Popup SDK thật: NOT_VERIFIED.
5. Publish: invoice (TACustomerPortal.Web) qua `Deploy-TARemote.ps1` — DEPLOYED 2026-11-04T17:47:24Z (server time 2026-11-05 00:47:24), source commit c7949c8daecd54630a2c81cf8e25de684bc6d962, 70 files, artifact manifest SHA256 d9572d07ac2a35b1bd2b512c7986a48855fa2ecfb308611fe394695759eacbab, package SHA256 7475EC36F95F32181C7047DA75A92EFB320F5176BF4D66BABACF66472D93C83B, health https://hoadon.tuvangiaiphapta.vn/ HTTP 200. Audit PASSED (WARN: kiểm tra NuGet lỗ hổng không chạy được hết; codec validate publish path khác máy operator). portal/webhooks: chưa republish (Integrations chỉ thêm thuộc tính).
6. Git: branch `feature/customer-portal-mvp01` (đã push), source commit c7949c8, merge PENDING_APPROVAL.
7. Rủi ro/rollback/việc operator cần làm: kiểm tra popup đối tác có hiện tên/logo đối tác không; rollback `Deploy-TARemote.ps1 -Target invoice -Rollback`.
8. Bằng chứng kiểm tra: Safe tests (`SafeCustomerPaymentsTests`).
