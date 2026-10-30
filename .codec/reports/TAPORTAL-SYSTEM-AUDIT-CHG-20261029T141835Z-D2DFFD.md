# CODEC DELIVERY REPORT — TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD

Feature ID: `TAPORTAL-SYSTEM-AUDIT`
Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD`
Agent: Claude Code (claude-opus-5-5)

1. Mục tiêu và kết quả thực tế: publish lại cặp Admin + Customer từ cùng một phiên bản source vào hai thư mục cố định để chuẩn bị production UAT. Kết quả: LOCAL_PUBLISH_COMPLETE; NOT_DEPLOYED.
2. Danh sách file cập nhật: chỉ hồ sơ Codec (change, report, 2 manifest, README, history). Không sửa source/test/SQL.
3. Database `TAPortalSys`: không có migration mới, không chạy SQL. Hiện trạng do operator báo (Users 4, Customers 10, CustomerPortalMemberships 0; verify 003/004/007/008 không FAIL): OPERATOR_REPORTED, agent NOT_VERIFIED.
4. Build/test: restore exit 0; Release build 0/0. Safe tests, browser và localhost UAT NOT_RUN theo chỉ thị operator; tree `src`/`tests`/sln trùng RC 95e64ce (215/215 PASS đã ghi nhận ở CP10).
5. Publish:

| Project | Destination | Exit | Files | Artifact SHA256 |
|---|---|---|---|---|
| TAPortal.Web | D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web | 0 | 57 | f97e0bcbf198703b539d96fdc87f57450fb1d3ac76d120c51b12caaa71fcf10e |
| TACustomerPortal.Web | D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web | 0 | 54 | 564e037dd1bc21c827c7097ac88c68fbcb208a4ef24ea2d35af84852b627944b |

   Shared DLL ở cả hai: 1dc9333b2f1badf7f82507d2a636120ab0c6b7877ed2649fa762b4bf9a4fdd74. Source build: cd5ccc0403b21e54d35f021575ded7c434d21df8 (nội dung source = RC 95e64ce; chỉ InformationalVersion khác). Backup: publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD\ (6cfdc285…, cb152b05…).
6. Git: feature/customer-portal-mvp01; delivery SHA báo sau push; PR #7 draft, merge PENDING_APPROVAL.
7. Rủi ro/rollback/việc operator: điều kiện production (DB, AuditLogs, HTTPS, secret, Data Protection, AllowedHosts, proxy, IIS mapping, backup) đều NOT_VERIFIED. Theo docs/operations/final-deployment-uat-runbook.md Phase A→D. Rollback: chép thư mục backup vào thư mục đích.
8. Bằng chứng: manifest .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD-<Project>.sha256 (`sha256sum -c` PASS); git tree hash src 874be79e…, tests e9b3f3a2…, sln 2767951b… trùng giữa 95e64ce và cd5ccc0; danh sách file trùng bản trước, 0 file dư.
