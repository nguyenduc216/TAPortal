# Customer Portal MVP01 — Publish Checkpoint 3

FEATURE_ID: CUSTOMER-PORTAL-FOUNDATION
CHANGE_ID: CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T005216Z-E6FF4A
Branch: feature/customer-portal-mvp01
Source commit SHA: 383db10a00343c71e3e08fbd963ef09bcea743dd
Policy/report commit: a0a9fab339fdc03cc97220f67e200993f4fa5d77 (PUSHED).
Publish: BLOCKED. IIS: NOT_DEPLOYED. Merge: PENDING_APPROVAL, merge SHA: none.
Database: VERIFIED_WITH_WARNINGS (operator-reported).
Artifact SHA256: N/A. Publish time/exit code: N/A; publish command NOT_RUN.
Target: D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web
Preflight/build/test: 2026-10-28, Asia/Saigon.

## Mục tiêu và phạm vi
Publish riêng Customer Portal sau kiểm tra an toàn; cập nhật quy tắc publish cố định cho các web project. Đã đọc AGENTS, skill/config, feature README/history và change/report liên quan trước source. Không đổi production source, không publish Admin, không chạy SQL, không sửa IIS, không merge main.

## Preflight và lý do chặn
Branch đúng yêu cầu; project net8.0 tồn tại; SDK 8.0.422 và 10.0.301 có sẵn. Working tree ban đầu có database/scripts/004-verify-customer-portal.sql đã sửa từ trước: giữ nguyên, không stage/commit hoặc thực thi.
Đích publish chưa tồn tại. Chưa tạo folder/kiểm tra ghi thực tế vì gate IIS chưa đạt. W3SVC và WAS đang Running. Đọc applicationHost.config bị Access denied; appcmd list vdir /text:physicalPath thất bại vì không đọc được redirection.config do thiếu quyền. Không tự nâng quyền.
Dòng IIS_CONFIG_ABSENT ban đầu là nhánh Test-Path sau lỗi quyền, không phải bằng chứng IIS không tồn tại. Chưa xác minh effective physical paths, overlap hoặc alias nên không kết luận đích không được IIS sử dụng. Dừng trước mọi ghi artifact; không sửa site, pool, binding, secret hoặc recycle/start IIS.

## Thay đổi
AGENTS.md, .agents/skills/codec/SKILL.md, .codec/README.md, .codec/config.json: quy tắc cố định <WORKSPACE_ROOT>\publish\deployed-<PROJECT_NAME>, tên lấy từ csproj; cho tạo đúng folder sau preflight. Đã cấu hình đường dẫn Customer và Admin theo quy ước, nhưng chỉ Customer được phép publish trong checkpoint này. Ghi đè live IIS vẫn cần approval riêng.
.codec/tools/codec.py và test_codec.py: kiểm tra đường dẫn cố định cùng các guard IIS; tests chỉ ghi metadata trong thư mục tạm.
docs/customer-portal-runbook.md: cập nhật preflight, trạng thái DB do operator cung cấp và handoff IIS.
.codec/features/CUSTOMER-PORTAL-FOUNDATION/README.md, history.json và change/report hiện tại: bổ sung checkpoint, giữ nguyên bản ghi cũ.
Rollback source: revert riêng commit checkpoint sau review; không reset thay đổi của người dùng. Không có publish/IIS/SQL mutation cần rollback.

## Kiểm chứng
- dotnet restore TAPortal.sln: PASS.
- dotnet build TAPortal.sln -c Release --no-restore: PASS, 0 warnings, 0 errors.
- dotnet test tests/TACustomerPortal.Tests/TACustomerPortal.Tests.csproj -c Release --no-build --filter FullyQualifiedName~SafePortalTests --logger trx --results-directory TestResults: PASS, 34 passed, 0 failed, 0 skipped. Legacy SQL tests không được chọn.
- TRX ignored: TestResults/PC_DESKTOP-LF41200_2026-10-28_07_53_06_net8.0.trx.
- python -m unittest discover -s .codec/tools -p test_codec.py: PASS, 3 tests.
- python .codec/tools/codec.py validate và git diff --check: PASS.
DLL/deps/runtimeconfig/web.config/wwwroot/dependency/source matching của publish artifact: NOT_VERIFIED vì chưa publish. Không dùng hash build DLL thay thế artifact SHA256.

## Database và bước tiếp theo
Operator xác nhận migration 008 đã chạy thủ công; verification PASS_WITH_WARNINGS, 4 Users / 10 Customers / 0 Memberships. Cảnh báo chưa cấp tài khoản khách hàng. Đây là xác nhận do operator cung cấp, không phải audit SQL mới của Codex. Không chạy lại migration 008; không thực thi SQL/seed/rollback, không tạo DB.
Operator cần cung cấp inventory effective IIS physical paths (kể cả overlap/alias) hoặc quyền đọc để kiểm tra lại, xác nhận đích không được IIS phục vụ. Nếu là live directory, cần approval ghi đè riêng cùng backup/rollback. Khi gate đạt mới publish đúng đích, kiểm tra artifact và tính SHA256 DLL.
Trước bước IIS riêng, xác nhận tên site/application, pool riêng No Managed Code và identity, HTTPS hostname/port/certificate, .NET 8 Hosting Bundle, Production, secret kết nối TAPortalSys, DataProtection key path ngoài web root, ACL, log/retention và backup/rollback. Xác nhận trusted proxy IP nếu dùng, HTTPS/cookie/login throttle/WAF. Operator cấp membership và nghiệm thu browser, tenant isolation, session/password revocation và Admin regression. Không cấu hình hoặc deploy IIS trong checkpoint này.
PR hiện có: https://github.com/nguyenduc216/TAPortal/pull/7; không sửa trạng thái PR. Delivery HEAD báo riêng để tránh tự tham chiếu SHA trong chính commit.
