# Feature: TAPORTAL-SYSTEM-AUDIT

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Tên: Full system audit, Admin Users/Customers edit, Customer Portal account UI, dual publish
- Mục tiêu: audit Admin + Customer Portal + Shared; sửa an toàn các lỗi CRUD/UI/navigation; thống nhất operating contract cho mọi AI agent; publish hai artifact cục bộ.
- Phạm vi / ngoài phạm vi: source Admin (Users, Customers, CustomerPortalAccounts, layout, JS), Codec policy/validator/tests, safe tests. Ngoài phạm vi: migration/DDL/DML, ghi database, IIS/server, merge main, đổi vai trò người dùng (chờ quy tắc nghiệp vụ).
- Kiến trúc / bảng liên quan: dbo.Users, dbo.Customers, dbo.UserRoles/Roles (chỉ đọc), dbo.CustomerPortalMemberships (qua MembershipAdminStore sẵn có). Schema hiện có đáp ứng; không cần migration mới.
- Các phụ thuộc: `CUSTOMER-PORTAL-FOUNDATION` (migration 008 do operator báo đã áp dụng; PR #7; UAT-01…09 vẫn PENDING_OPERATOR).
- Trạng thái: ACTIVE

## Change history

| Change ID | Ngày UTC | Nội dung | Commit SHA | Migration | Publish | Merge |
|---|---|---|---|---|---|---|
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2` | 2026-10-28 | Full system audit, Admin Users/Customers edit, Customer Portal account UI, dual publish | 336ab7c80552e733a5df9e6c4612aeb163037007 | NONE_REQUIRED / NOT_EXECUTED | PUBLISHED (local, both) / NOT_DEPLOYED | PENDING_APPROVAL |

## Current status
19 findings (0 CRITICAL, 2 HIGH, 8 MEDIUM, 9 LOW); 12 FIXED, 7 OPEN. Release build 0/0; 65 safe tests + 4 Codec tests PASS; validate PASS. Admin artifact `4b308e3b…ea35` (57 files), Customer artifact `ea77b3be…ac34` (54 files), both local only. SQL write paths and authenticated UI rendering await operator UAT. Details: latest report and docs/audits/.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E` | 2026-10-28 | Checkpoint 5 Admin login rate limiting (AUD-13) and authenticated no-store (AUD-14) | 183414102691ffb373d436951b27cf191d902fc2 | NONE_REQUIRED | PUBLISHED (local, Admin only) / NOT_DEPLOYED | PENDING_APPROVAL |

## Checkpoint 5 — current status
AUD-13 and AUD-14 FIXED; new findings AUD-20/21/22 FIXED, AUD-23/24 OPEN. Release build 0/0; 84 safe tests + 4 Codec tests PASS; validate PASS; Admin smoke 11/11. Admin artifact `42d6a733…19cb4` (57 files); Customer NOT_REQUIRED (unchanged `ea77b3be…ac34`). Database not accessed. Prior status lines above are historical.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8` | 2026-10-28 | Checkpoint 6 user role management (AUD-12) | 36db1038ebe071ebab4f588538e118d79fb416e9 | NONE_REQUIRED | PUBLISHED (local, Admin only) / NOT_DEPLOYED | PENDING_APPROVAL |

## Checkpoint 6 — current status
AUD-12 FIXED for role assignment (SYS_ADMIN-only, last SYS_ADMIN protected, stale-page conflicts rejected); permission matrix/role creation/direct UserPermissions PENDING_APPROVAL; AUD-25 (AuditLogs in TAPortalSys unverified) PENDING_OPERATOR; AUD-26 OPEN. Release build 0/0; 104 safe tests + 4 Codec tests PASS; validate PASS; Admin smoke 12/12. Admin artifact `5adf697b…afd36` (57 files); Customer NOT_REQUIRED (unchanged `ea77b3be…ac34`). Database NOT_VERIFIED. Operator decision: no deploy/global UAT until the final checkpoint. Prior status lines above are historical.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B` | 2026-10-28 | Checkpoint 7 account status, tax code integrity, user soft delete, audit preflight (AUD-15/16/17/25) | 6a2462b526de34e9f166541447c6e2f6d51bad59 | 009/010 AUTHORED, NOT_EXECUTED | PUBLISHED (local, Admin only) / NOT_DEPLOYED | PENDING_APPROVAL |

## Checkpoint 7 — current status
AUD-15 FIXED (application); AUD-16 PARTIAL (application check FIXED, DB unique index migration 009 NOT_EXECUTED); AUD-17 PARTIAL (user soft delete FIXED, customer soft delete PENDING_APPROVAL); AUD-25 PARTIAL (verify script 007 + conditional migration 010 + grant template, PENDING_OPERATOR); new AUD-27/28 FIXED, AUD-29 OPEN. Release build 0/0; 136 safe tests + 4 Codec tests PASS; validate PASS; Admin smoke 15/15. Admin artifact `fc042e76…a727` (57 files); Customer NOT_REQUIRED (`ea77b3be…ac34`). Database NOT_VERIFIED. No deploy/UAT/merge per operator decision. Prior status lines above are historical.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T042622Z-036AA1` | 2026-10-29 | Checkpoint 8 dashboard data (AUD-18), admin UX consistency, direct permissions view (AUD-26) | 2013ea2a10be1f4f618a65c9733adabfda1cc873 | NONE_REQUIRED | PUBLISHED (local, Admin only) / NOT_DEPLOYED | PENDING_APPROVAL |

## Checkpoint 8 — current status
AUD-18 FIXED (defined KPIs from SQL, real DB/audit status, explicit error/empty states); AUD-26 FIXED read-only; AUD-30..34 FIXED (UX/accessibility, BROWSER_NOT_VERIFIED). Release build 0/0; 160 safe tests + 4 Codec tests PASS; validate PASS; Admin smoke 15/15. Admin artifact `a6aca5d0…c5ff` (57 files); Customer NOT_REQUIRED (`ea77b3be…ac34`). Database NOT_VERIFIED. Backlog for Checkpoint 9/operator: AUD-23/24/25/29. Prior status lines above are historical.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T050717Z-E1EAC3` | 2026-10-29 | Checkpoint 9 security hardening: login timing (AUD-23), throttle/proxy (AUD-24), durable audit (AUD-25/29) | b6eb780202049b9a8ca3014e28d50cfcd3e2e8dd | NONE_REQUIRED | PUBLISHED (local, Admin + Customer) / NOT_DEPLOYED | PENDING_APPROVAL |

## Checkpoint 9 — current status
AUD-23 FIXED (app; TIMING_NOT_VERIFIED); AUD-24 PARTIAL (memory bound, proxy allowlist; per-process counters remain, PENDING_INFRASTRUCTURE_APPROVAL); AUD-25 PARTIAL (script 007 + runbook, PENDING_OPERATOR); AUD-29 FIXED in code, CONFIGURATION_PENDING; new AUD-35/36/37/38 FIXED. Release build 0/0; 202 safe tests + 4 Codec tests PASS; validate PASS; smoke Admin 15/15, Customer 10/10. Admin `64ff472d…a10e`, Customer `71c104f1…338a` (Shared changed → both). Database NOT_VERIFIED. Prior status lines above are historical.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D` | 2026-10-29 | Checkpoint 10 final regression, release candidate and deployment readiness | 95e64cedb14479850bc3f32d28ec0aa6c36f7d02 | NONE_REQUIRED | RELEASE CANDIDATE (local, Admin + Customer) / NOT_DEPLOYED | PENDING_APPROVAL |

## Checkpoint 10 — release candidate status (current)
42 findings: 0 CRITICAL; CLOSED 21, FIXED_IN_CODE 15, PENDING_OPERATOR 4, PENDING_APPROVAL 2; migration 008 NOT_VERIFIED; UAT not run. New AUD-39/40 CLOSED, AUD-41 PENDING_OPERATOR, AUD-42 PENDING_APPROVAL. Release build 0/0; 215 safe tests + 4 Codec tests PASS; smoke Admin 16/16, Customer 10/10. RC pair from 95e64ce: Admin `6cfdc285…fe69` (57), Customer `cb152b05…3efc` (54), same Shared DLL. Decision: code RC READY; deployment CONDITIONAL GO (Phase A of docs/operations/final-deployment-uat-runbook.md); production NOT READY. Final report: docs/audits/taportal-system-audit-final-TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D.md. Prior status lines above are historical.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD` | 2026-10-29 | Dual project local publish for production UAT preparation (RC 95e64ce) | cd5ccc0403b21e54d35f021575ded7c434d21df8 | NONE_REQUIRED | PUBLISHED (local, Admin + Customer) / NOT_DEPLOYED | PENDING_APPROVAL |

## Production UAT preparation publish — current status
LOCAL_PUBLISH_COMPLETE from cd5ccc0 (source tree identical to RC 95e64ce): Admin `f97e0bcb…f10e` (57), Customer `564e037d…944b` (54), Shared DLL `1dc9333b…dd74` in both. Backup of RC pair in publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T141835Z-D2DFFD\. NOT_DEPLOYED; IIS NOT_CHECKED; no SQL executed; production prerequisites NOT_VERIFIED. Prior status lines above are historical.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T151702Z-ABDD41` | 2026-10-29 | UI/UX redesign per operator mockup: design system, sidebar, Customers/Users, Customer Portal | 70917eb9170426d8843eaed3dd3c6b0822b5800e | NONE_REQUIRED | PUBLISHED (local, Admin + Customer) / NOT_DEPLOYED | PENDING_APPROVAL |

## UI/UX redesign — current status
UI_IMPLEMENTED, BUILD_VERIFIED, LOCAL_PUBLISH_COMPLETE; VISUAL_NOT_VERIFIED on real browser/data/IIS; PRODUCTION_NOT_DEPLOYED. Shared design system (css/ta-design.css + js/ta-shell.js identical in both apps), Tabler bundled locally (no CDN), inline SVG icons (TAPortal.Shared.UiIcon). UI-01..09 FIXED. Safe tests 235/235. Admin `ddb59cce…98e2` (61), Customer `b39bb31c…dc0e` (58), Shared DLL `4af267cc…626a`. Report: docs/audits/ui-redesign-TAPORTAL-SYSTEM-AUDIT-CHG-20261029T151702Z-ABDD41.md. Prior status lines above are historical.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T160850Z-4CCB3B` | 2026-10-29 | Unified Customer Workspace: per-customer Portal accounts, remove standalone sidebar menu | eb8d0e656654a16c8f78930977bc8ad63e6f4a53 | NONE_REQUIRED | PUBLISHED (local, Admin only) / NOT_DEPLOYED | PENDING_APPROVAL |

## Customer Workspace — current status
Sidebar no longer lists 'Tài khoản Customer Portal'; each customer has a workspace (/Customers/Details|Edit|PortalAccounts/{id}) with per-customer membership management (assign existing / create new, role, revoke/reactivate) scoped to the route customer. Legacy /CustomerPortalAccounts kept for compatibility. Safe tests 250/250. Admin `f609696d…e607` (62); Customer unchanged `b39bb31c…dc0e`. NOT_DEPLOYED. Prior status lines above are historical.
