# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261126T212751Z-496ABA

Feature ID: `TINGEE-INTEGRATION`
Change ID: `TINGEE-INTEGRATION-CHG-20261126T212751Z-496ABA`

1. Mục tiêu và kết quả thực tế: kết nối và xuất hóa đơn SoftDreams không còn bị chặn bởi ErrorCode 128 (Ikey chưa có hóa đơn).
2. Danh sách file cập nhật: xem change.
3. Database `TAPortalSys`: NOT_APPLICABLE.
4. Build/test: Release build 0 lỗi; Safe tests 861/861 PASS.
5. Publish: DEPLOYED 2026-11-26 21:31Z từ commit fe82072, health HTTP 200 cả ba. portal artifact f0915ed901bab898c2df149dbf6b94748703ec2c5a25510794cc40b39936a195 / package 67E84CE02DB6D90F0CD1748C092A1A48D2251106EEBEB7697C2A7A9CDC5B331E; invoice artifact 5c76b8ad4b3ddd6eb55942338f85dcff89de446fb173043fc622d85386bf5408 / package 3F518EB6B57F77A87E90BE57D1D69CB949776B2F1CDF97C034738558C120B3B7; webhooks artifact a558581ea962157d6b82e787be66fd835a23960921b8874dfaaafd52b546dd03 / package D136F6B9512A8708FC9ACF4381C8A7FF4308750BC455B2893DF7C911EF58A4D1. Manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261126T212751Z-496ABA-<PROJECT>.sha256`.
6. Git: branch feature/einvoice-softdreams; merge main sau deploy.
7. Rủi ro/rollback/việc operator cần làm: rollback `Deploy-TARemote.ps1 -Target <t> -Rollback`. Operator kết nối lại SoftDreams.
8. Bằng chứng kiểm tra: test cục bộ, health sau deploy.
