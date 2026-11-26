# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261126T212751Z-496ABA

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261126T212751Z-496ABA`

1. Mục tiêu và kết quả thực tế: kết nối và xuất hóa đơn SoftDreams không còn bị chặn bởi ErrorCode 128 (Ikey chưa có hóa đơn).
2. Danh sách file cập nhật: xem change.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Release build 0 lỗi; Safe tests 861/861 PASS.
5. Publish: PENDING
6. Git: branch feature/einvoice-softdreams; merge main sau deploy.
7. Rủi ro/rollback/việc operator cần làm: rollback `Deploy-TARemote.ps1 -Target <t> -Rollback`. Operator kết nối lại SoftDreams.
8. Bằng chứng kiểm tra: test cục bộ, health sau deploy.
