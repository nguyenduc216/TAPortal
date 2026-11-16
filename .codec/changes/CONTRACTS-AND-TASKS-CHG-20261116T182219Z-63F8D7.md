# Change CONTRACTS-AND-TASKS-CHG-20261116T182219Z-63F8D7

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T182219Z-63F8D7`
- Created UTC: 2026-11-16T18:22:19.848361+00:00
- Tiêu đề: Service task lists, services by customer and month (Danh sách dịch vụ), quantities with history on tasks
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator 2026-11-16: a service gets a task list (work catalogue entries, ordered); "Danh sách công việc" becomes "Danh sách dịch vụ": each row is a service done for a customer for a month (progress, start, due, owner; contract link later); its detail lists the tasks of that month; on each task staff enter quantities in its unit day by day with a history (e.g. Kéo hóa đơn +10, +20).
- Migration 065: dbo.ServiceTasks, dbo.ServicePeriods (unique customer + service + month; ContractId reserved), dbo.WorkTasks + ServicePeriodId/ServiceSortOrder/Unit/MetricCode, dbo.WorkTaskQuantities.
- Danh mục dịch vụ > Sửa dịch vụ: tab "Danh sách công việc" (add from the work catalogue, ↑/↓ and drag to order); saved with the service.
- ServicePeriods (Index/Create/Details): creating a month creates one task per service task (checklist template, unit, measure code); add a task later; delete while nothing started; per-measure quantity totals.
- Work/Details: tab "Số lượng" (total, entry form, history; remove by author or task editor; refused on done/cancelled tasks); each entry logged. Tasks named from the catalogue copy its unit and measure code.
- Sidebar: "Danh sách công việc" -> "Danh sách dịch vụ"; all tasks reachable from its "Tất cả công việc" button.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/065-service-periods.sql`, `database/scripts/117-verify-service-periods.sql`, `database/scripts/118-rollback-service-periods.sql`, `src/TAPortal.Web/Services/ServicePeriodStore.cs`, `Controllers/ServicePeriodsController.cs`, `Views/ServicePeriods/{Index,Create,Details}.cshtml`
- MODIFY `Services/ServiceCatalogStore.cs`, `Services/WorkTaskStore.cs`, `Controllers/ServiceCatalogController.cs`, `Controllers/WorkController.cs`, `Models/WorkModels.cs`, `Program.cs`, `Views/ServiceCatalog/Edit.cshtml`, `Views/Work/Details.cshtml`, `Views/Shared/_Layout.cshtml`, `wwwroot/js/ta-services.js`, `wwwroot/js/ta-work.js`, `wwwroot/css/ta-admin.css`; tests `SafeAdminWorkTests.cs`, `SafeServiceCatalogTests.cs` (806 Safe tests)

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
