# Feature: CUSTOMER-PORTAL-FOUNDATION

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Tên: Customer Portal MVP 01 Checkpoint 1
- Mục tiêu:
- Phạm vi / ngoài phạm vi:
- Kiến trúc / bảng liên quan:
- Các phụ thuộc:
- Trạng thái: ACTIVE

## Change history

| Change ID | Ngày UTC | Nội dung | Commit SHA | Migration | Publish | Merge |
|---|---|---|---|---|---|---|
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261027T141827Z-E64647` | 2026-10-27 | Customer Portal MVP 01 Checkpoint 1 | 1d048d1ec3b52cda301c18154949bb1e1ac41c8f | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |

## Verified baseline and checkpoint state
Existing source already implements independent Customer Portal login/logout, company selection, dashboard, profiles, shared password changes and SYS_ADMIN-only membership management. No feature history preceded this change; baseline verified from source/PR #7, not assumed from chat.
Checkpoint 1 adds fixed session lifetime across company switches, no-store sensitive pages, TAPortalSys SQL guards and SQL-free HTTP security tests. Release build: 0 errors/warnings. Tests: 34 pass, 0 fail; 25 legacy SQL test methods skipped by Codec policy.
Status: SOURCE_AND_SAFE_TESTS_COMPLETE. SQL CRUD/real Admin regression, schema audit and IIS/browser UAT await operator. No SQL, publish, deploy or merge performed. See current change/report for exact paths, files, limits and handoff.
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T005216Z-E6FF4A` | 2026-10-28 | Customer Portal MVP01 Publish Checkpoint 3 | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |

## Checkpoint 3 current state
Release build and 34 selected safe tests PASS; 3 Codec tests PASS. Conventional publish paths configured; Customer-only artifact publishing authorized. BLOCKED before publish: IIS services running but effective mappings unreadable due to permissions. No target folder created, no publish/hash, no IIS/SQL changes. Operator reports DB VERIFIED_WITH_WARNINGS and 0 memberships; do not rerun migration 008. See current report/runbook for handoff. Prior checkpoint statements above are historical.
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T074006Z-654653` | 2026-10-28 | DEV TEST machine policy and Customer Portal local publish | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |

## Current DEV/TEST checkpoint ? supersedes earlier publish blocker
Local Customer Portal artifact PUBLISHED to the conventional target. Release build 0 warnings/errors; 34 safe tests and 3 Codec tests PASS. Database VERIFIED_WITH_WARNINGS by operator; no SQL run. IIS NOT_CHECKED / NOT_DEPLOYED; no server access required or permitted. See latest report and SHA256 manifest for evidence. Earlier checkpoint descriptions are historical.
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T075636Z-260F08` | 2026-10-28 | Checkpoint 4 local smoke and manual functional acceptance | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |

## Checkpoint 4 - current status
CHECKPOINT_4A: PASS (existing artifact integrity, isolated localhost startup and 10 anonymous GET checks). CHECKPOINT_4B: PENDING_OPERATOR; UAT-01 through UAT-09 NOT_RUN. CUSTOMER_PORTAL_MVP01: NOT_YET_ACCEPTED. Runtime process stopped, artifact unchanged, no republish or SQL access; database NOT_VERIFIED for this checkpoint. No application source changes. See latest report and docs/customer-portal-checkpoint4-uat.md. Prior entries remain historical.
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T052602Z-922EA0` | 2026-11-04 | Admin resets a Customer Portal password; customer must change it at next sign-in | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T064318Z-148859` | 2026-11-04 | Customer Portal forgot-password by email (SMTP, one-time 30-minute link) | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T103400Z-124DDE` | 2026-11-04 | Xoá 10 khách hàng mẫu KH0001-KH0010 (giữ TVGPTA), có bảng lưu và rollback | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T112825Z-F9FA74` | 2026-11-04 | Gói dịch vụ: quản lý gói (hóa đơn / tra soát giao dịch / combo), số dư khách hàng, giao dịch mua gói; trang mua gói trên Customer Portal | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T155401Z-13E2DF` | 2026-11-04 | Gói dịch vụ: sửa lỗi trùng tên cột DisplayName trong danh sách giao dịch mua gói | cdc3432 | NOT_APPLICABLE | DEPLOYED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261105T235309Z-2AA0D3` | 2026-11-05 | Customer Portal: tự đăng ký doanh nghiệp (tra MST) và khách hàng linh hoạt theo loại (khách lẻ/hộ KD/doanh nghiệp/tổ chức) | b3074b4 | EXECUTED | DEPLOYED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T012601Z-147F73` | 2026-11-06 | Customer Portal: MISA e-invoice settings and automatic invoice for paid QR orders | 71cb50a | EXECUTED | DEPLOYED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T042042Z-D6E634` | 2026-11-06 | Shell: button formaction/formtarget (MISA templates, draft preview) | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T044512Z-10B30D` | 2026-11-06 | MISA template id (GUID): InvoicePattern varchar(50) | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T045730Z-1DF625` | 2026-11-06 | Checkbox hidden fallback order (auto-issue, email, package active) | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T053314Z-D0B894` | 2026-11-06 | Logo doanh nghiệp và khách hàng; trang QR đơn hàng công khai (/pay/{token}) | 024 | EXECUTED | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T062358Z-2721FA` | 2026-11-06 | Logo ở danh sách khách hàng, thanh bên và Admin portal | — | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T064001Z-46830A` | 2026-11-06 | Khach hang: MST dan co khoang trang, dien thoai/Zalo tu bo khoang trang, form rong 3 cot | 7ded836 | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T071511Z-E55D21` | 2026-11-06 | Trang QR: khach hang ben trai, nguoi ban ben phai, icon ngan hang o giua | fdbc25d | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T072739Z-09CEBF` | 2026-11-06 | Trang QR: anh chia se, nut Chuyen khoan mo app ngan hang, luu thong tin hoa don khi tra cuu duoc MST | 0bb7fe1 | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T073457Z-C2D04D` | 2026-11-06 | Trang QR: dau tich xanh khi da thanh toan, chi tiet hang hoa duoi tieu de | c7b4115 | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T074510Z-F0495F` | 2026-11-06 | Danh sach don hang: cot trang thai hoa don dien tu | 1329373 | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T145102Z-9F4A81` | 2026-11-06 | Customer Portal logo: clean vector redraw, favicon and link-preview image | 0037f2a | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T235029Z-86E87C` | 2026-11-06 | E-invoice: create a draft (status DRAFT) when MISA cannot sign, instead of failing | 4c6618d | EXECUTED | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T004158Z-0D7E07` | 2026-11-07 | E-invoice settings: MISA HSM warning and issue mode (HSM signed / draft for manual signing / none) | a917153 | EXECUTED | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T020426Z-95622D` | 2026-11-07 | Customer Portal: staff accounts and per-function permissions (Xem / Quản lý) | c16e082 | EXECUTED | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T022032Z-9A2C1B` | 2026-11-07 | Revert Customer Portal staff accounts and per-function permissions (operator: TA Portal only) | 03432e8 | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T041035Z-5D1386` | 2026-11-07 | Drop the unused Customer Portal member permissions (migration 030) | 9d2764d | EXECUTED | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T070538Z-03398E` | 2026-11-07 | Xung ho Hoa don tu dong tren Customer Portal, dong San pham cua T.A cuoi sidebar | 1bdb338 | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T071821Z-4EA212` | 2026-11-07 | Ten nguoi gui email he thong: Hoa don tu dong | 0a60596 | EXECUTED | NOT_APPLICABLE | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261109T162008Z-1FF4B8` | 2026-11-09 | San pham khach hang va gia rieng theo khach | 7fbdc2c | EXECUTED | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T060913Z-B1DDA6` | 2026-11-10 | Tai anh QR kem thong tin va sua link mo app ngan hang dien san | bd93fff | NOT_APPLICABLE | DEPLOYED | MERGED |
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T064757Z-60CE97` | 2026-11-10 | Sua dau ngoac thua lam trang thanh toan hien '} else {' | — | NOT_APPLICABLE | NOT_PUBLISHED | PENDING_APPROVAL |
