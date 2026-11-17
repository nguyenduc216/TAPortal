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
- invoice: commit ec49dbc, artifact a6a2441ee934801eae433e78a5eafe5857c5f2e490776fc61a081ea17e6b923c, DEPLOYED 2026-11-17T03:06:02Z; maker line checked on public pages with curl.

- 2026-11-17 (operator): payment page on a phone: goods lines show the name on its own line, then "qty unit × unit price" ("(chưa thuế)" when taxed) and the amount, which never wraps (billions); the paid message is two lines ("Đơn hàng đã được thanh toán đủ." / "Cảm ơn quý khách!"). No total row (the order total is already shown). Tests: SafeCustomerPayPageTests.
- invoice: commit 9a42d97, artifact de26a482034f277223665033bb9f59a808ecdb9c111fa8273bf21ea519954fb4, DEPLOYED 2026-11-17T08:22:49Z, health 200.
