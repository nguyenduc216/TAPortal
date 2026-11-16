# Change CONTRACTS-AND-TASKS-CHG-20261116T165132Z-A6E1C4

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261116T165132Z-A6E1C4`
- Created UTC: 2026-11-16T16:51:32.016276+00:00
- Tiêu đề: Measure codes in service tiers; unit and fee measure code on work catalogue entries
- Trạng thái: PLANNED

## Yêu cầu và phạm vi
Operator 2026-11-16: each measure ("chỉ số hạn mức") of a service gets a short code (typed, or the first letters of its words, e.g. SLCT); codes may repeat across services for the same measure. Each work catalogue entry gets a unit (text: hóa đơn, lần...) and a fee measure code chosen from the measures (list shows code + name), so the quantities entered day by day (next step: task data entry) can be totalled monthly and priced against the tiers.
- `ServiceMetric.Code` in PricingJson (no schema change): `ServicePricingPolicy.AutoCode/CleanCode`, unique within a service; older tables get codes on read. Editor column "Mã" (placeholder = automatic code). `IServiceCatalogStore.MetricsAsync` lists code + name + services.
- Migration 063: `dbo.WorkTaskCatalog.Unit nvarchar(50)`, `MetricCode varchar(20)`; popup fields with datalists; grid column "Đơn vị · Mã hạn mức".

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/063-work-catalog-unit-metric.sql`, `database/scripts/113-verify-work-catalog-unit-metric.sql`, `database/scripts/114-rollback-work-catalog-unit-metric.sql`
- MODIFY `src/TAPortal.Web/Services/ServiceCatalogStore.cs`, `Services/WorkCatalogStore.cs`, `Views/WorkCatalog/Index.cshtml`, `Views/WorkCatalog/_CatalogDialog.cshtml`, `Views/ServiceCatalog/Edit.cshtml`, `wwwroot/js/ta-services.js`, `wwwroot/css/ta-admin.css`; tests `SafeServiceCatalogTests.cs`, `SafeAdminWorkTests.cs`

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
