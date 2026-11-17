# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261117T025701Z-8C4E0F

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261117T025701Z-8C4E0F`
- Created UTC: 2026-11-17T02:57:01.054501+00:00
- Tiêu đề: Payment page: maker line 'Sản phẩm của Tư vấn giải pháp T.A' at the bottom
- Trạng thái: PLANNED

## Yêu cầu và phạm vi

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)

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

## Deploy
- invoice: commit 9fc6dd9, artifact 7b263b46e73ecb40995d3ce8fa93960c2bdf06f802883fee4b8371cdf10c6eff, package B03771CDCC10CB0D1A09ACA2050D8C6A3B08F3BED146FF40E68B480E864D2B92, DEPLOYED 2026-11-17T03:00:29Z, health 200. Served customer.css has .cp-pay__maker; a real payment link was not opened (needs an order token).

## Follow-up (operator 2026-11-17)
- 'Every page people can open from a link': the maker line on all pages reachable without sign-in. Already on Login, Register, RegisterDone, ForgotPassword, ResetPassword, Terms and Pay; added to AccessDenied and Error for visitors not signed in (signed-in customers have it in the sidebar). AGENTS.md and SafeCustomerBrandTests updated.
