# CODEC DELIVERY REPORT — ZALO-MINI-APP-CHG-20261117T142043Z-C7BB65

Feature ID: `ZALO-MINI-APP`
Change ID: `ZALO-MINI-APP-CHG-20261117T142043Z-C7BB65`

1. Mục tiêu và kết quả thực tế: mẫu tin OTP Zalo OA (Template xác thực, nội dung Zalo đề xuất) đã được operator duyệt. Mã mẫu OTP cấu hình trong Admin trên kênh Zalo OA hệ thống, có `ZaloMiniApp:OtpTemplateId` làm dự phòng. Admin có nút "Gửi thử OTP". Kênh bật được mà không cần mẫu báo thanh toán. Áp dụng cho luồng xác minh SĐT ở cả TA Portal và Hóa đơn tự động.
2. Danh sách file cập nhật: xem change record.
3. Database `TAPortalSys`: không có migration (NOT_APPLICABLE).
4. Build/test: restore 0; Release build 0 lỗi; Safe* 820/820 pass; codec unittest OK; git diff --check sạch.
5. Publish: NOT_PUBLISHED, chờ operator đồng ý deploy portal, invoice và webhooks.
6. Git: `feature/zalo-mini-app`; commit và merge SHA ghi trong hand-off.
7. Rủi ro/rollback/việc operator cần làm: tạo mẫu trên ZBS Account và chờ duyệt; nhập App ID, secret, refresh token và mã mẫu OTP trong Admin; bấm Gửi thử OTP. Rollback bằng `Deploy-TARemote.ps1 -Rollback`.
8. Bằng chứng kiểm tra: output test ở trên. Production chỉ-đọc ngày 2026-11-17: chưa có kênh SYSTEM/ZALO_OA.
