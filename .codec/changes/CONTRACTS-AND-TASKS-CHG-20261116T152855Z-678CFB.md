# Change CONTRACTS-AND-TASKS-CHG-20261116T152855Z-678CFB

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T152855Z-678CFB`
- Created UTC: 2026-11-16T15:28:55.532112+00:00
- Tiêu đề: Service catalogue: service groups, services with scope, fee rules and pricing tiers; industries on customers
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator 2026-11-16 (start of the contract / customer / task rework after the new T.A process; quote "Báo giá phí dịch vụ" attached): contracts need service groups (kế toán thuế tháng, kế toán nội bộ, pháp lý, nhân sự...), each service with name, scope of work, rules and "hạn mức" (labelled measures with units, numeric ranges, fee per tier; accounting fees per industry); industries are a catalogue set on customers.
- Hợp đồng > Danh mục dịch vụ (CONTRACTS.SERVICES.MANAGE): tabs Dịch vụ / Nhóm dịch vụ / Ngành nghề; service page with tabs Thông tin chung (group, name, pricing mode TIER/FIXED/QUOTE, fee label + unit, package fee), Hạn mức & phí (1-3 measures, tiers with Từ/Đến per measure, fee or fee per industry; wwwroot/js/ta-services.js), Phạm vi công việc, Nguyên tắc & ghi chú.
- Customer form: Ngành nghề (dbo.Customers.IndustryId).
- Not in scope yet: contract templates, quoting from the tiers, removing obsolete contract fields (next steps with the operator).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/061-service-catalog.sql`, `database/scripts/109-verify-service-catalog.sql`, `database/scripts/110-rollback-service-catalog.sql`
- ADD `src/TAPortal.Web/Services/ServiceCatalogStore.cs`, `Controllers/ServiceCatalogController.cs`, `Views/ServiceCatalog/Index.cshtml`, `Views/ServiceCatalog/Edit.cshtml`, `wwwroot/js/ta-services.js`
- MODIFY `Security/AdminPermissions.cs` (CONTRACTS.SERVICES.MANAGE, Migration061Codes), `Program.cs`, `Views/Shared/_Layout.cshtml` (menu + script), `Models/PortalModels.cs`, `Services/CustomerAdminStore.cs`, `Views/Customers/Edit.cshtml`, `wwwroot/css/ta-admin.css`
- Tests: ADD `SafeServiceCatalogTests.cs`; MODIFY `SafeAdminWorkTests.cs` (HTTP), `SafeAdminContractTests.cs` (031 codes exclude the 061 code)
- Pricing editor checked in a local harness page (3 measures, industry columns, Vietnamese number entry, tier reorder); 800 Safe tests.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại

## Migration execution
- 061-service-catalog.sql: PRECHECK NOT_APPLIED; DRYRUN PASS/NOT_APPLIED (trancount 0); gates PASS (800); REAL RUN VERIFY_STATUS=PASS => EXECUTED 2026-11-16T15:31:25Z
