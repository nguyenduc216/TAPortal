# TAPortal.DbMcp

Tuân theo `AGENTS.md` ở thư mục gốc repo, Codec và `.codec/config.json`; file này chỉ bổ sung thông tin riêng của dự án.

- Thư mục publish: không có. Đây là công cụ MCP truy vấn database, không nằm trong danh sách publish của Codec. Muốn thêm thư mục publish phải có operator phê duyệt và cập nhật `.codec/config.json`.
- SQL chỉ đọc và kiểm tra: trả kết quả dạng text (`PRINT`), chỉ trả dạng bảng khi thật sự cần (xem quy tắc ở `AGENTS.md` gốc).
