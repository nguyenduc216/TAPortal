# T.A Portal Database Schema V1 RC2

Status: **REVIEW CANDIDATE — DO NOT DEPLOY TO PRODUCTION**

RC2 incorporates the first technical audit and the functional architecture review.

## Decisions frozen for RC2 review
- Partner remains the tenant/commercial boundary; no second Tenant entity.
- User remains global identity; tenant authorization is membership-scoped through PartnerUsers/PartnerRoles.
- Company is the legal/operating entity under Partner and is carried by legal/financial aggregates where required.
- PaymentRequest is the receivable/collection target. PaymentInstruction owns account/payment-code/QR lifecycle.
- PaymentTransaction is the rail-neutral allocation source; BANK is the first rail. SePay/VietinBank remain provider adapters/evidence.
- Customer cash/advance/refund is a separate monetary ledger from T.A product credits.
- Subscription/Entitlement controls access/period; consumable quota is sourced only from wallet/ledger through SubscriptionCreditGrants.
- Wallets evolve toward CreditDefinition/Service instead of closed BANKING/EINVOICE enums.
- InvoiceRequest is independent from payment and snapshots seller/buyer/items/tax/totals.
- Matching and review decisions must be explainable through attempts/evidence/actions.
- Domain idempotency remains domain-specific; generic IdempotencyKeys is not the financial source of truth.
- External side effects remain worker/outbox driven.

## Migration review set
001-028 remain the RC baseline, with pre-deployment compatibility corrections because none of these RC migrations has been deployed to production. RC2 functional changes are 029-038.

## SQL Server target
The RC2 migration syntax is being normalized for the project's SQL Server 2014-era compatibility requirement. CREATE OR ALTER and MERGE are not allowed in the migration chain.

## Gate
RC2 requires an independent Codex second audit. The audit must test migration execution from the current baseline, not only inspect SQL. No V1.0.0 freeze until BLOCKER/HIGH findings are resolved and both reviews agree.
