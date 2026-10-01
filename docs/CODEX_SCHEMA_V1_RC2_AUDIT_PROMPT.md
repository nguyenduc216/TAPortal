# CODEX SECOND AUDIT — T.A PORTAL DATABASE SCHEMA V1 RC2

This is the second independent audit after:
1. the RC1 technical audit; and
2. the functional/database architecture review.

Audit branch: feat/compact-dynamic-navigation.
Do not audit main.

## First verify the source
Run git fetch, checkout/pull the branch, then record exact HEAD. Confirm migrations 001-038, database/preflight/V1_RC2_PREFLIGHT.sql and docs/DATABASE_SCHEMA_V1_RC2.md exist. If not, STOP and report the mismatch.

## Product model you must validate
RC2 intentionally freezes these decisions:
- Partner = tenant/commercial boundary; Company = legal/operating entity under Partner.
- User = global identity; PartnerUsers membership + PartnerRoles = tenant authorization.
- PaymentRequest = receivable/collection target.
- PaymentInstruction = account/payment-code/QR instruction lifecycle; codes are never reused per Partner.
- PaymentTransaction = rail-neutral normalized movement; BANK is first rail, future EWALLET/GATEWAY must not redesign allocation core.
- PaymentAllocation = immutable/reversible settlement between PaymentTransaction and PaymentRequest.
- Customer cash/advance/refund is money and is separate from T.A product-credit wallets.
- Subscription/Entitlement = access/period; wallet/ledger = sole source of consumable quota.
- CreditDefinition/Service replaces closed product-credit assumptions.
- InvoiceRequest is independent of payment; legal seller/buyer/item/tax/total values are snapshots.
- Matching/review must preserve evidence, actor, reason and disposition.
- Raw provider evidence remains separate from normalized business facts.
- Outbox/inbox/worker retries handle external side effects; provider secrets/tokens must not be stored plaintext.

## Audit goal
Answer BOTH:
A. Can the migration chain safely upgrade the current baseline on the target SQL Server?
B. Does RC2 now implement the agreed functional model without contradictory old structures becoming a second source of truth?

Do not repeat old findings mechanically. Re-test them against the new HEAD.

## Mandatory technical checks
1. Execute migrations in order from a disposable copy/schema equivalent to the current 001-007 baseline.
2. Target SQL Server 2014-era syntax/compatibility. Search the ENTIRE migration chain for unsupported CREATE OR ALTER and MERGE, reserved identifiers, invalid filtered indexes, invalid CHECK/FK/default constraints and object-order dependencies.
3. Test rerun/idempotency behavior where migrations claim to be idempotent.
4. Run database/preflight/V1_RC2_PREFLIGHT.sql after upgrade.
5. Build the .NET solution. Run tests if any; if no test project, say so.
6. Do not modify production DB.

## Mandatory architecture checks
### Tenant
Try to create cross-Partner Company/Customer/account/payment/allocation/invoice relationships. Database or application contract must reject them. Identify every remaining tenant-owned FK that can cross Partner.

### Payment
Test:
- 500k receivable paid 400k + 100k.
- one transaction split across multiple receivables.
- default allocation must be MIN(transaction remaining, receivable remaining).
- excess money remains unallocated/customer cash/review; it must not silently overpay.
- reversal recomputes settlement.
- generic PaymentTransaction is the allocation source; BankTransaction is bank evidence/compatibility, not the long-term core.
- expired/cancelled instruction and old payment code cannot cause unsafe auto-settlement.
- PaymentCodeRegistry prevents reuse.

### Customer cash/refund
Validate unapplied cash, advance balance, immutable cash ledger semantics and refund request/operation lifecycle. Product CreditLedger must never be used as customer money.

### Matching/review
Validate AMOUNT_ASSIST cannot AutoAllocate. Validate MatchingAttempt/Evidence and PaymentReviewActions can explain a decision.

### Subscription/credit
Validate there is ONE consumable quota source. SubscriptionCreditGrant must grant into the wallet/ledger idempotently. Flag any remaining code/view that treats SubscriptionEntitlements.UsedQuantity as the same consumable balance. Verify wallet/reservation concurrency and immutable ledger.

### Invoice
Validate invoice-before-payment and invoice-without-payment. Validate seller Company snapshot, buyer snapshot, item/tax/totals, Ikey uniqueness, issue credit reserve/consume exactly once, and adjustment/replacement/cancellation history.

### Provider/security
Validate SePay sandbox/production connection lifecycle, raw event vs normalized transaction, duplicate IPN + reconciliation convergence, no plaintext client secret/access token/link token/Authorization header, and no persistent HostedLinkUrl if it contains a one-time token.

### Reliability
Validate inbox/outbox uniqueness, lease/claim semantics, bounded retry/dead-letter and domain-specific idempotency. Identify missing atomic worker-claim procedure/contract if still absent.

## Legacy/transition audit
RC2 intentionally preserves some old tables/columns for upgrade compatibility. Identify every legacy object that would create TWO sources of truth, including:
- BankTransactions vs PaymentTransactions
- BankSyncJobs / BankReconciliationRuns vs ReconciliationRuns
- PaymentRequest.PaymentCode/QR fields vs PaymentInstructions/PaymentCodeRegistry
- CreditType strings vs CreditDefinitionId
- SubscriptionEntitlements.UsedQuantity vs wallet usage
- global UserRoles vs PartnerUserRoles

For each, classify:
KEEP AS EVIDENCE / COMPATIBILITY ONLY / DEPRECATE / MUST REMOVE BEFORE FREEZE.
This section is mandatory.

## Application contract
Inspect current C# code and identify code still reading/writing the legacy source instead of the RC2 source. Do NOT treat an application mismatch as a reason to revert the RC2 architecture; list the exact application changes Codex must make after schema freeze.

Also re-check fixed bootstrap credentials and tenant filters. These are application/security responsibilities and remain blockers if still present.

## Required output
Create docs/audits/CODEX_SCHEMA_V1_RC2_AUDIT.md.

Report:
1. Branch + exact HEAD.
2. SQL Server version/compatibility actually tested.
3. Migration execution result 001-038.
4. Preflight result.
5. Build/test result.
6. Functional coverage verdict by domain.
7. Findings table with BLOCKER/HIGH/MEDIUM/LOW, evidence and exact object.
8. RC1 finding closure matrix: CLOSED / PARTIAL / OPEN.
9. Nine functional must-change closure matrix.
10. Legacy/transition cleanup matrix.
11. Application changes required after DB freeze.
12. Proposed corrective migration(s) 039+ ONLY if necessary.
13. Final verdict:
   - READY FOR V1 FREEZE
   - READY AFTER FIXES
   - NOT READY

## Rules
- SECOND AUDIT FIRST: do not modify migrations or C# during this pass.
- Do not merge main.
- Do not deploy production.
- Do not invent provider behavior.
- Do not mark PASS solely because dotnet build succeeds.
- A BLOCKER/HIGH finding means not ready to freeze.
- Distinguish database architecture defects from application implementation work.

Final response must include exact HEAD, migration/preflight execution evidence, finding counts, closure count for the nine must-change items, proposed 039+ list, and report path.
