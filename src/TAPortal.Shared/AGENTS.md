# TAPortal.Shared

Tuân theo `AGENTS.md` ở thư mục gốc repo, Codec và `.codec/config.json`; file này chỉ bổ sung thông tin riêng của dự án.

- Thư mục publish: không có. Đây là thư viện dùng chung. Thay đổi ở đây phải publish lại `TAPortal.Web`, `TACustomerPortal.Web` và `TAPortal.Webhooks`.
- SQL chỉ đọc và kiểm tra: trả kết quả dạng text (`PRINT`), chỉ trả dạng bảng khi thật sự cần (xem quy tắc ở `AGENTS.md` gốc).
