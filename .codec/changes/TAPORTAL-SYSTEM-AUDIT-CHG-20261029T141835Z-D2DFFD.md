# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD`
- Created UTC: 2026-10-29T14:18:35.835562+00:00
- Tiêu đề: Dual project local publish for production UAT preparation (RC 95e64ce)
- Trạng thái: LOCAL_PUBLISH_COMPLETE
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
Publish lại TAPortal.Web và TACustomerPortal.Web từ cùng một phiên bản source vào hai thư mục cố định, kèm backup, manifest SHA256 và checklist điều kiện production/UAT cho operator. Ngoài phạm vi: sửa source, SQL, IIS/server, chạy lại safe test/browser/localhost UAT (theo chỉ thị operator), merge PR #7.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD .codec/changes/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD.md, .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD.md
- ADD .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD-TAPortal.Web.sha256, .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD-TACustomerPortal.Web.sha256
- MODIFY .codec/features/TAPORTAL-SYSTEM-AUDIT/README.md, history.json (append)
- Không sửa source/test/SQL. Không đụng: database/scripts/004-verify-customer-portal.sql (operator), database/scripts/scene-image.png, src/.claude/ (không phải của agent này)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration mới; không chạy SQL nào. Trạng thái DB do operator báo: Users 4, Customers 10, CustomerPortalMemberships 0, verify 003/004/007/008 không FAIL (OPERATOR_REPORTED; agent không kiểm chứng được).
- Execution: NOT_EXECUTED

## Test và kết quả
- `dotnet restore TAPortal.sln` exit 0; `dotnet build TAPortal.sln -c Release --no-restore` 0 warning / 0 error (SDK 10.0.301, net8.0).
- Safe tests: NOT_RUN theo chỉ thị operator ("không chạy lại toàn bộ safe tests"); source `src`/`tests`/`TAPortal.sln` có tree hash giống hệt RC 95e64ce, nơi 215/215 PASS đã được ghi nhận.
- Browser/localhost UAT: NOT_RUN (chỉ thị operator).

## Publish
- Preflight: HEAD = origin = cd5ccc0403b21e54d35f021575ded7c434d21df8; hai thư mục đích khớp đúng manifest RC (6cfdc285…, cb152b05…), 0 file khóa, 0 reparse point, có quyền ghi.
- Backup: publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD\{TAPortal.Web,TACustomerPortal.Web} — manifest trùng 100% bản trước; các rollback cũ giữ nguyên.
- Phương pháp: làm trống thư mục đích (sau backup) rồi `dotnet publish -c Release --no-restore -o <đích>` → không còn file dư; danh sách file giống hệt bản trước.
- TAPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web: exit 0, 2026-10-29T14:19:18Z→14:19:19Z, 57 files, artifact SHA256 f97e0bcbf198703b539d96fdc87f57450fb1d3ac76d120c51b12caaa71fcf10e
- TACustomerPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web: exit 0, 2026-10-29T14:19:19Z→14:19:21Z, 54 files, artifact SHA256 564e037dd1bc21c827c7097ac88c68fbcb208a4ef24ea2d35af84852b627944b
- TAPortal.Shared.dll giống nhau ở hai artifact: 1dc9333b2f1badf7f82507d2a636120ab0c6b7877ed2649fa762b4bf9a4fdd74
- Khác biệt so với RC: chỉ 5 file/app (main dll/exe/pdb, Shared dll/pdb) do assembly nhúng commit build (InformationalVersion `1.0.0+cd5ccc04…` thay vì `+95e64ced…`); nội dung source giống hệt RC.
- Không secret (ConnectionString rỗng; quét pattern không trúng); web.config inprocess.
- Trạng thái: PUBLISHED (local) / NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Branch: feature/customer-portal-mvp01
- Build commit (source SHA của artifact): cd5ccc0403b21e54d35f021575ded7c434d21df8 (src/tests trùng tree với RC 95e64cedb14479850bc3f32d28ec0aa6c36f7d02)
- Delivery SHA: báo sau push
- PR: https://github.com/nguyenduc216/TAPortal/pull/7 (draft)
- Merge: PENDING_APPROVAL
- Merge SHA: —

## Rủi ro, quyết định và việc còn lại
- Trạng thái: LOCAL_PUBLISH_COMPLETE; production NOT_DEPLOYED.
- Nếu operator cần artifact mang đúng dấu 95e64ce: dùng bản RC trong publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD\ (6cfdc285…, cb152b05…); chức năng tương đương.
- Điều kiện production còn NOT_VERIFIED: connection string, quyền SQL, AuditLogs + quyền INSERT, Data Protection, HTTPS, AllowedHosts, KnownProxies, RequireDurableAudit, backup DB/website, mapping IIS.
- Rollback: chép lại thư mục backup vào đúng thư mục đích.
