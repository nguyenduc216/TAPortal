# Customer Portal MVP01 ? Publish Checkpoint 3

FEATURE_ID: CUSTOMER-PORTAL-FOUNDATION
CHANGE_ID: CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T005216Z-E6FF4A
Branch: feature/customer-portal-mvp01
Source commit SHA: 383db10a00343c71e3e08fbd963ef09bcea743dd
Publish: BLOCKED
IIS: NOT_DEPLOYED
Database: VERIFIED_WITH_WARNINGS (operator-reported)
Artifact SHA256: N/A ? no publish command executed.
Publish time: N/A. Preflight/test date: 2026-10-28, Asia/Saigon.
Target: `D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web`

## M?c ti?u v? ph?m vi
Publish ri?ng Customer Portal t?i ???ng d?n c? ??nh sau preflight/build/safe tests; c?p nh?t quy t?c cho c?c web project v? sau. Kh?ng publish Admin, kh?ng ch?y SQL, kh?ng s?a IIS, kh?ng merge main. ?? ??c AGENTS, skill/config, README/history v? change/report li?n quan tr??c source.

## Preflight v? l? do BLOCKED
- Branch ??ng; HEAD source nh? tr?n. .NET SDK 8.0.422 v? 10.0.301 c? s?n; project net8.0 t?n t?i.
- Working tree ban ??u c? `database/scripts/004-verify-customer-portal.sql` ?? s?a t? tr??c. Gi? nguy?n, kh?ng stage/commit file n?y. Kh?ng d?ng n? ?? ch?y SQL.
- ??ch publish ch?a t?n t?i. Kh?ng t?o th? m?c ho?c ghi artifact khi ch?a qua IIS preflight; quy?n ghi ??ch ch?a ki?m tra th?c t? v? gate IIS ?? ch?n.
- W3SVC v? WAS ?ang Running. ??c applicationHost.config b? Access denied. appcmd list vdir /text:physicalPath tr? l?i kh?ng ??c ???c redirection.config do thi?u quy?n.
- V? v?y KH?NG k?t lu?n IIS kh?ng s? d?ng ??ch. D?ng IIS_CONFIG_ABSENT t? l?n ki?m tra ??u l? nh?nh Test-Path sau l?i quy?n, kh?ng ph?i b?ng ch?ng IIS kh?ng c?i/c?u h?nh v?ng m?t.
- Kh?ng t? n?ng quy?n. Ch?a x?c minh physical paths/overlap/aliases; d?ng tr??c publish. Kh?ng thay site, pool, binding, secret hay kh?i ??ng/recycle IIS.

## Thay ??i v? quy?t ??nh
Quy t?c c? ??nh `<WORKSPACE_ROOT>\publish\deployed-<PROJECT_NAME>` l?y PROJECT_NAME t? t?n csproj. Config ghi ??ng ??ch Customer v? Admin theo quy ??c; ch? Customer ???c ph?p publish trong y?u c?u n?y. Cho t?o ??ng folder sau preflight, c?m ??ch thay th? v? ghi ?? live IIS khi ch?a c? approval ri?ng. Validator ki?m tra ???ng d?n v? c?c guard IIS; regression tests c?p nh?t theo config m?i. Kh?ng ??i production source.
Rollback: revert ri?ng commit checkpoint n?y sau review n?u c?n; kh?ng x?a l?ch s? v? kh?ng reset thay ??i ng??i d?ng. Kh?ng c? artifact/IIS/SQL mutation ?? rollback.

## Build/test
- `dotnet restore TAPortal.sln`: PASS.
- `dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet test tests/TACustomerPortal.Tests/TACustomerPortal.Tests.csproj -c Release --no-build --filter FullyQualifiedName~SafePortalTests --logger trx --results-directory TestResults`: PASS, 34 passed, 0 failed, 0 skipped. Legacy SQL tests kh?ng ???c ch?n.
- TRX ignored: TestResults/PC_DESKTOP-LF41200_2026-10-28_07_53_06_net8.0.trx.
- `python -m unittest discover -s .codec/tools -p test_codec.py`: PASS, 3 tests (metadata-only).
- `python .codec/tools/codec.py validate`: POLICY PASS.
- `git diff --check`: PASS.
- Publish command: NOT_RUN; exit code N/A. DLL/deps/runtimeconfig/web.config/wwwroot/dependency/source matching: NOT_VERIFIED for publish artifacts. Build output kh?ng ph?i publish artifact; kh?ng d?ng hash build DLL thay th?.

## Database v? operator handoff
Theo x?c nh?n trong y?u c?u: migration 008 ?? ???c operator ch?y th? c?ng, verification PASS_WITH_WARNINGS, 4 Users / 10 Customers / 0 Memberships. C?nh b?o ch?a c?p t?i kho?n kh?ch h?ng. Codex kh?ng audit DB l?i, kh?ng ch?y migration/seed/rollback ho?c SQL kh?c; kh?ng t?o DB. Kh?ng ch?y l?i 008.

Operator c?n cung c?p inventory effective IIS site/application/virtual-directory physical paths ho?c quy?n ??c ?? ki?m tra l?i; x?c nh?n ??ch kh?ng ???c IIS ph?c v? (k? c? overlap/alias). N?u l? live directory, c?n ph? duy?t ghi ?? ri?ng v? backup/rollback tr??c. Sau gate n?y m?i publish ??ng l?nh/??ch ?? y?u c?u, ki?m tra ?? artifact v? SHA256 DLL.
Tr??c b??c IIS ri?ng: x?c nh?n site/application name, dedicated No Managed Code pool/identity, HTTPS hostname/port/certificate, .NET 8 Hosting Bundle, Production, connection secret tr? TAPortalSys, DataProtection key path ngo?i web root, ACL, logging retention v? backup/rollback. X?c nh?n proxy IP tin c?y n?u d?ng, cookie/HTTPS/login throttle/WAF. Operator c?p membership v? nghi?m thu browser/tenant isolation/revocation/Admin regression. Chi ti?t trong runbook; kh?ng t? c?u h?nh trong checkpoint n?y.

## Git v? file
Commit/push checkpoint metadata ???c th?c hi?n sau checks; delivery commit SHA b?o ri?ng v? kh?ng th? ghi SHA c?a ch?nh commit v?o n?i dung commit ??. Source SHA ? tr?n l? production source ???c build/test. Merge PENDING_APPROVAL; merge SHA none. PR hi?n c?: https://github.com/nguyenduc216/TAPortal/pull/7 (kh?ng s?a tr?ng th?i PR).
Files changed:
- AGENTS.md
- .agents/skills/codec/SKILL.md
- .codec/README.md
- .codec/config.json
- .codec/tools/codec.py
- .codec/tools/test_codec.py
- docs/customer-portal-runbook.md
- .codec/features/CUSTOMER-PORTAL-FOUNDATION/README.md
- .codec/features/CUSTOMER-PORTAL-FOUNDATION/history.json
- .codec/changes/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T005216Z-E6FF4A.md
- .codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T005216Z-E6FF4A.md
