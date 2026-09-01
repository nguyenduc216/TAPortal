# TAPortal Database Skill

## Purpose
Quy định cách AI/coder làm việc với SQL Server của TAPortal.

## Mandatory rules
1. Luôn inspect schema thực tế trước khi đề xuất thay đổi database.
2. Không giả định table/column/FK/index đang tồn tại.
3. Kiểm tra dependency trước ALTER hoặc migration.
4. Không DROP object nếu chưa có phê duyệt rõ ràng.
5. Không dùng tài khoản `sa` cho MCP.
6. MCP mặc định dùng tài khoản read-only.
7. Mọi thay đổi schema phải có migration/script lưu trong `database/migrations`.
8. Sau migration phải verify schema thực tế.
9. Không commit connection string, password, token hoặc secret.
10. Foreign key mặc định `NO ACTION`; chỉ CASCADE khi có lý do nghiệp vụ rõ ràng.
11. Query đọc phải giới hạn dữ liệu; tránh SELECT không giới hạn trên bảng lớn.
12. Ưu tiên schema-qualified names, ví dụ `auth.Users`, `sys.Modules`.

## Production safety
- Read tools có thể chạy tự động.
- DDL/DML phải được kiểm soát riêng.
- DROP/TRUNCATE mặc định bị cấm.
- SQL Server public port không được mở cho toàn Internet; whitelist host chạy MCP hoặc dùng private network/VPN.
