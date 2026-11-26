# CODEC DELIVERY REPORT — ADMIN-ROLE-PERMISSIONS-CHG-20261126T123328Z-3F2256

Feature ID: `ADMIN-ROLE-PERMISSIONS`
Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261126T123328Z-3F2256`

1. Mục tiêu và kết quả thực tế: "Đặt lại mật khẩu" (và "Cấp quyền cho tài khoản có sẵn") ở tab Tài khoản Portal của khách hàng nhận đúng tài khoản Hóa đơn tự động; trước đó mọi tài khoản khách hàng bị coi là nhân viên TA vì danh sách ứng viên đọc từ danh sách chỉ-nhân-viên.
2. Danh sách file cập nhật: xem change.
3. Database `TAPortalSys`: NOT_APPLICABLE (không có migration). Đọc DB chỉ đọc để xác nhận tài khoản không có vai trò TAPortal.
4. Build/test: Release build 0 lỗi; Safe tests 858/858 PASS.
5. Publish: portal DEPLOYED 2026-11-26 12:45Z từ commit 340fcd9; artifact 4bbe0a0c811512debca338888f0b996d4c71354377a6315d4ebcc37b1553d296; package C46552742B7FD3EFAD85B367556D5B8D251E269F9D3A2683682479056C7A1E0D; health HTTP 200; backup server `C:\TADeployackups\portal-20261126-194513`.
6. Git: branch feature/einvoice-softdreams; merge main sau deploy.
7. Rủi ro/rollback/việc operator cần làm: rollback `Deploy-TARemote.ps1 -Target portal -Rollback`. Operator thử lại nút Đặt lại mật khẩu.
8. Bằng chứng kiểm tra: kết quả test cục bộ, health portal sau deploy.
