# CODEX Verification Audit — T.A Portal Database V1 RC4

## 1. Identification and verdict

| Item | Result |
|---|---|
| Branch | `feat/compact-dynamic-navigation` |
| Exact HEAD | `03fee0989da0d5650a84347eae951043a18c32c8` |
| Audit date | 2026-10-01 |
| SQL Server | Microsoft SQL Server 2014 `12.0.2269.0`, Enterprise Edition (64-bit) |
| Compatibility | 120 |
| Database | Disposable local database named `TAPortal` |
| Execution | Original committed files via `sqlcmd -b -I -i`; no normalization |
| Database verdict | **NOT READY** |
| Application verdict | **NOT RELEASE READY** |

The branch was fetched with prune, checked out, and fast-forwarded to the exact remote HEAD above. Migrations 045–048 and `database/preflight/V1_RC4_POSTFLIGHT.sql` exist. Existing local `appsettings.json` and audit files were preserved.

No migration or C# source was modified. No merge or production deployment was performed.

## 2. Executive result

RC4 is not a valid ordered release candidate. The untouched 001–048 chain stops at migration 042 with SQL error 207 because `sp_GrantSubscriptionCredit` still references nonexistent `PartnerCreditWallets.UpdatedAt`. A normal `sqlcmd -b` deployment therefore never reaches corrective migration 045.

Diagnostic continuation using the original files found another independent migration failure: 047 attempts to backfill `CustomerCashLedger.PartnerId`, but the existing immutable-ledger trigger throws even for that migration statement. Consequently the corrected customer-cash procedure is not installed.

Corrective migrations 045, 046 and 048 work in isolation and close important RC3 issues. Payment, credit-grant and worker concurrency scenarios pass after diagnostic continuation. However, direct-DML tests demonstrate that several financial aggregates still accept cross-Partner ownership links, and consumable quota still has multiple writable representations.

DB finding counts:

| BLOCKER | HIGH | MEDIUM | LOW |
|---:|---:|---:|---:|
| 4 | 7 | 4 | 1 |

## 3. Source status

| Check | Result |
|---|---|
| Remote divergence after pull | `0 0` |
| Migration files | 48 files, 001–048 present |
| 045–048 | Present |
| RC4 postflight | Present |
| Pre-existing worktree change | `src/TAPortal.Web/appsettings.json` modified; preserved and not used against its configured server |
| Existing audit directory | Untracked; preserved |

## 4. Migration execution matrix

| Migration | Result | Error / note |
|---:|---|---|
| 001 | PASS | Original file |
| 002 | PASS | Original file |
| 003 | PASS | Original file |
| 004 | PASS | Original file |
| 005 | PASS | Original file |
| 006 | PASS | Original file |
| 007 | PASS | Original file |
| 008 | PASS | Original file |
| 009 | PASS | Original file |
| 010 | PASS | Original file |
| 011 | PASS | Original file |
| 012 | PASS | Original file |
| 013 | PASS | Original file |
| 014 | PASS | Original file |
| 015 | PASS | Original file |
| 016 | PASS | Original file |
| 017 | PASS | Original file |
| 018 | PASS | Original file |
| 019 | PASS | Original file |
| 020 | PASS | Original file |
| 021 | PASS | Original file |
| 022 | PASS | Original file |
| 023 | PASS | Original file |
| 024 | PASS | Original file |
| 025 | PASS | Original file |
| 026 | PASS | Original file |
| 027 | PASS | Original file |
| 028 | PASS | Original file |
| 029 | PASS | Original file |
| 030 | PASS | Original file |
| 031 | PASS | Original file |
| 032 | PASS | Original file |
| 033 | PASS | Original file |
| 034 | PASS | Original file |
| 035 | PASS | Original file |
| 036 | PASS | Original file |
| 037 | PASS | Original file |
| 038 | PASS | Original file |
| 039 | PASS | Original file |
| 040 | PASS | Original file |
| 041 | PASS | Original file |
| 042 | **FAIL** | SQL error 207, procedure `sp_GrantSubscriptionCredit`, invalid column `PartnerCreditWallets.UpdatedAt` |
| 043 | NOT REACHED | Ordered runner stopped at 042 |
| 044 | NOT REACHED | Ordered runner stopped at 042 |
| 045 | NOT REACHED | Ordered runner stopped at 042 |
| 046 | NOT REACHED | Ordered runner stopped at 042 |
| 047 | NOT REACHED | Ordered runner stopped at 042 |
| 048 | NOT REACHED | Ordered runner stopped at 042 |

**Ordered migration chain: FAIL.**

For diagnostic coverage only, original files 043–048 were then executed independently. 043–046 passed; 047 failed with error 51420 from `tr_CustomerCashLedger_NoUpdateDelete`; 048 passed. This continuation does not change the ordered-chain result.

## 5. SQL Server 2014 compatibility

Whole-chain static scan found no `MERGE`, `CREATE OR ALTER`, `STRING_AGG`, `DROP ... IF EXISTS`, `JSON_VALUE`, `OPENJSON`, same-line `GO`, or identified SQL Server 2016+ syntax. The files that were reached compiled on SQL Server 2014 compatibility 120.

Compatibility is still a FAIL at release level because migration 042 cannot compile against the schema produced by 001–041. Migration 047 also cannot execute against the trigger state produced by its predecessors.

## 6. RC3 blocker closure

| RC3 blocker | Status | RC4 evidence |
|---|---|---|
| B-01 migration 042 / grant procedure | **OPEN** | 042 still stops the chain with the same invalid `UpdatedAt` reference. 045 has a correct `@GrantId` implementation but is unreachable in an ordered deployment. |
| B-02 legacy generic-allocation unique index | CLOSED in 046 | Diagnostic execution drops `UX_PA_RequestTransactionActive`, creates uniqueness on PaymentTransactionId, and permits 400k + 100k as two active allocations. |

## 7. RC3 high closure

| RC3 high | Status | Evidence |
|---|---|---|
| H-01 postflight false deployment PASS | CLOSED/PARTIAL | RC4 checks required procedure parameters and failed on missing cash signature. It still does not verify 001–048 history because history is deployment-tooling owned. |
| H-02 invoice credit false consumption | CLOSED in diagnostic schema | Wallet 10→9, reserved 1→0, reservation and binding CONSUMED, one ledger CONSUME; rerun did not consume twice. |
| H-03 mutable frozen invoice items | CLOSED in diagnostic schema | UPDATE, INSERT and DELETE all rejected with error 51620. |
| H-04 incomplete tenant graph | **OPEN** | Multiple tested financial ownership relations accept cross-Partner direct DML. |
| H-05 generic/legacy settlement dual path | CLOSED/PARTIAL | Old bank allocation procedure is disabled by 046 and generic index is canonical; legacy columns remain compatibility data. |
| H-06 transaction error handling | PARTIAL | 045/046/047 use TRY/CATCH rollback, and allocation failures returned XACT_STATE 0. `sp_FreezeInvoiceRequest` remains unsafe and returned XACT_STATE -1 after total mismatch. |
| H-07 customer-cash tenant/idempotency | **OPEN** | Intended fix exists in 047, but migration fails before its ALTER PROCEDURE. |
| H-08 worker lifecycle | CLOSED for tested DB contract | Concurrent claim, ownership, retry and DEAD lifecycle passed for Inbox and Outbox. |
| H-09 quota cut-over/dual truth | **OPEN** | Grant implementation works diagnostically, but CreditType and `SubscriptionEntitlements.UsedQuantity` remain writable alongside the wallet ledger. |

## 8. Mandatory scenario matrix

The scenarios below marked diagnostic require 043–048 continuation after the official chain had already failed.

| Scenario | Result | Evidence |
|---|---|---|
| 500k paid 400k + 100k | PASS diagnostic | AmountPaid 500k, PAID, two active allocations, NULL BankTransactionId permitted. |
| 300k against 100k receivable | PASS diagnostic | 100k allocated, request PAID, 200k unallocated. |
| One 300k transaction split 100k + 200k | PASS diagnostic | Two active allocations totaling 300k. |
| Same transaction concurrent workers | PASS diagnostic | One 100k success, one error 51613; active total 100k. |
| Same receivable concurrent transactions | PASS diagnostic | One 100k success, one error 51613; active total 100k. |
| Allocation OperationKey retry | PASS diagnostic | Same key produced one row and one financial effect. Different key after exhaustion rejected. |
| Reversal | PASS diagnostic | AmountPaid returned to 0 and status PENDING. |
| Cross-Partner allocation | PASS rejection | Error 51612, XACT_STATE 0. |
| Legacy bank allocation API | PASS disabled | Error 51615. |
| Subscription grant sequential retry | PASS diagnostic | Balance 50, one GRANT row, one linked ledger ID. |
| Subscription grant concurrent retry | PASS diagnostic | Both calls completed; exactly one balance increase and ledger row. |
| Cash +100k / -50k | PASS old contract | Balance 50k, two ledger rows. |
| Cash negative | PASS rejection but unsafe caller state | Rejected; old procedure left XACT_STATE -1 because 047 did not install the replacement. |
| Cash immutable UPDATE/DELETE | PASS rejection | Both rejected with error 51420. |
| Cash idempotency | **BLOCKED** | Corrected signature not installed because 047 fails. |
| Cash cross-tenant transaction | **BLOCKED/unsafe** | Corrected Partner-aware procedure not installed; independent FK path remains. |
| Valid invoice total freeze | PASS diagnostic | 100k - 0 + 10k + 0 = 110k froze. |
| Invalid invoice total freeze | PASS rejection, transaction-safety FAIL | Error 51432 but caller observed XACT_STATE -1. |
| Frozen request legal fields | PASS rejection | Error 51430. |
| Frozen InvoiceItems UPDATE/INSERT/DELETE | PASS diagnostic | All rejected with error 51620. |
| Invoice credit finalization twice | PASS diagnostic | Wallet 9, reserved 0, reservation/binding CONSUMED, exactly one `CONSUME -1` ledger row. |
| AMOUNT_ASSIST AutoAllocate | PASS rejection | CHECK error 547. |
| HostedLinkUrl insert | PASS rejection | Error 51440. |
| Inbox concurrent claim | PASS | One worker received row. |
| Inbox wrong owner/renew/fail/retry/dead | PASS | Wrong owner rejected; second failure at MaxAttempts produced DEAD. |
| Outbox concurrent claim | PASS | One worker received row. |
| Outbox wrong owner/fail/retry/dead | PASS | Wrong owner rejected; second failure at MaxAttempts produced DEAD. |

## 9. Tenant-integrity matrix

| Relationship tested | DB result | Required before freeze? | Risk |
|---|---|---|---|
| PartnerUserRole → PartnerRole | REJECTED | Yes | Enforced by composite role tenant contract. |
| PaymentAllocation → request/transaction | REJECTED cross-tenant | Yes | Enforced by allocation tenant FKs/procedure. |
| InvoiceCreditConsumption → reservation | REJECTED cross-tenant | Yes | 048 adds tenant reservation FK/check. |
| FinancialAccount Partner A → Company B | **ACCEPTED** | **Yes** | Legal ownership corruption. |
| PaymentTransaction Partner A → FinancialAccount B | **ACCEPTED** | **Yes** | Normalized money movement can be attached to another tenant account. |
| PaymentInstruction Partner A → FinancialAccount B | **ACCEPTED** | **Yes** | Collection instruction can route to another tenant account. |
| CustomerCashAccount Partner A → Customer B | **ACCEPTED** | **Yes** | Customer money ownership corruption. |
| Refund Partner A → Customer/transaction B | **ACCEPTED** | **Yes** | Refund can target another tenant's money/evidence. |
| MatchingAttempt Partner A → transaction B | **ACCEPTED** | Yes | Matching evidence and decisions can cross tenants. |
| ReviewAction Partner A → transaction B | **ACCEPTED** | Yes | Audited disposition can be attributed to wrong tenant. |
| InvoiceRequest Partner A → Customer B | **ACCEPTED** | **Yes** | Legal invoice buyer ownership corruption. |
| SubscriptionCreditGrant Partner B → period A | **ACCEPTED** | **Yes** | Quota can be granted from another tenant's subscription period. |
| CreditReservation ownership | Partner only; purpose is untyped | Application + DB procedure | Direct DML can create semantically unrelated reservations. |

These are database ownership defects, not merely missing application filters.

## 10. Postflight RC4

After diagnostic continuation, RC4 postflight exited nonzero with error 51706 because the corrected `sp_PostCustomerCash(@IdempotencyKey...)` signature was not installed. This is the correct result for the partial schema.

Active corruption test inserted an overallocated transaction/receivable and a GRANTED subscription credit without a ledger link. Postflight returned:

- `OVERALLOCATED_TRANSACTION = 1`;
- `OVERALLOCATED_RECEIVABLE = 1`;
- `CREDIT_GRANT_WITHOUT_LEDGER = 1`;
- exit code 1 / error 51700.

Other protected violations were rejected at write time: cross-tenant allocation, wallet balance constraints, AMOUNT_ASSIST auto allocation and HostedLinkUrl persistence. The postflight cannot produce the required clean PASS because the ordered chain and 047 fail.

## 11. DB-only findings

| ID | Severity | Object | Finding |
|---|---|---|---|
| B-01 | BLOCKER | migration 042 | Ordered chain fails with SQL error 207 on nonexistent wallet `UpdatedAt`. A later migration cannot repair a chain that stops under `sqlcmd -b`. |
| B-02 | BLOCKER | migration 047 / cash immutable trigger | Backfill UPDATE is rejected by `tr_CustomerCashLedger_NoUpdateDelete`; migration stops before cash idempotency/tenant procedure is installed. |
| B-03 | BLOCKER | tenant financial FK graph | Direct DML accepted cross-Partner account, transaction, instruction, customer cash, refund, invoice and grant relationships. These permit financial/legal ownership corruption. |
| B-04 | BLOCKER | consumable quota source | Wallet/CreditLedger, CreditType-based legacy writers, and writable `SubscriptionEntitlements.UsedQuantity` remain without an enforced read-only/derived transition, allowing divergent consumable usage truths. |
| H-01 | HIGH | `sp_FreezeInvoiceRequest` | Intentional validation THROW leaves caller XACT_STATE -1; RC4 did not update this mandatory financial procedure. |
| H-02 | HIGH | customer cash procedure | Because 047 fails, deployed signature lacks PartnerId/IdempotencyKey and retains unsafe transaction handling. |
| H-03 | HIGH | PaymentInstruction transition | Registry/instruction are intended canonical, but request payment-code/QR fields remain writable without a DB stop-write/derived contract. |
| H-04 | HIGH | matching/review tenant links | Attempts and review actions accept a Partner different from the referenced transaction. |
| H-05 | HIGH | refund tenant links | Refund lifecycle is structurally compensating, but independent FKs permit cross-tenant refund composition. |
| H-06 | HIGH | invoice/customer ownership | InvoiceRequest can reference another Partner's customer. |
| H-07 | HIGH | subscription period/grant ownership | SubscriptionCreditGrant can claim a Partner different from its subscription period's Partner. |
| M-01 | MEDIUM | refund execution | Schema is adequate for a compensating lifecycle, but approval/execution posting procedures remain application/should-change work. Original payment is not mutated. |
| M-02 | MEDIUM | reconciliation transition | `ReconciliationRuns` is intended canonical, but BankSyncJobs and BankReconciliationRuns remain writable rather than explicitly compatibility-only. |
| M-03 | MEDIUM | raw provider payloads | Hosted URLs are blocked, but raw payload/log fields rely on application sanitization and access/retention policy. SecretReference design does not require plaintext secrets. |
| M-04 | MEDIUM | migration history | Schema is suitable for external hashes, but no deployment runner populated it during raw sqlcmd execution. |
| L-01 | LOW | release packaging | Corrective migrations cannot compensate for an earlier fatal file without either amending the unshipped file or providing a runner contract that is no longer the mandated ordered `-b` chain. |

## 12. Legacy/source-of-truth matrix

| Object | Classification | Audit decision |
|---|---|---|
| `PaymentTransactions` | CANONICAL | Rail-neutral settlement source. |
| `BankTransactions` | KEEP AS EVIDENCE | Provider/bank evidence only. |
| `PaymentAllocations.BankTransactionId` | COMPATIBILITY | Nullable evidence link; not business uniqueness after 046. |
| `sp_AllocateBankTransaction` | DISABLED | 046 throws and directs callers to generic API. |
| `PaymentInstructions` / `PaymentCodeRegistry` | CANONICAL | Intended collection instruction truth. |
| Request PaymentCode/QR columns | DEPRECATED but still writable | Must become derived/read-only or have an explicit transition before freeze. |
| `CreditDefinitionId` + wallet + ledger | CANONICAL target | Correct target, but cut-over is incomplete. |
| CreditType fields/procedures | DEPRECATED but writable | Compatibility must not remain an independent write selector. |
| `SubscriptionEntitlements.UsedQuantity` | MUST REMOVE/DISABLE for consumables | Writable second consumable counter. |
| `PartnerUserRoles` | CANONICAL tenant RBAC | Composite tenant role enforcement passes. |
| `PartnerUsers.PartnerRole` | DEPRECATED | Compatibility field; stop writes after backfill. |
| `ReconciliationRuns` | CANONICAL target | Older run tables require explicit stop-write compatibility classification. |
| `BankSyncJobs`, `BankReconciliationRuns` | COMPATIBILITY/DEPRECATED | Preserve history; do not accept new canonical work. |

## 13. Nine functional must-change verdict

| # | Item | Status | Evidence |
|---:|---|---|---|
| 1 | Partner-scoped RBAC | CLOSED for DB core | Cross-Partner role assignment rejected; application cut-over remains. |
| 2 | PaymentInstruction lifecycle | PARTIAL | Canonical tables exist, but legacy request fields remain writable. |
| 3 | Generic payment allocation | CLOSED diagnostically | Correct multi-payment, split, excess, concurrency, idempotency and reversal behavior after 046. Ordered chain still blocks release. |
| 4 | Customer cash/refund | OPEN | 047 fails; corrected idempotent tenant procedure is not installed. |
| 5 | Company/legal ownership | OPEN | Cross-Partner financial/legal links are accepted. |
| 6 | Wallet sole quota + subscription grant | OPEN | Grant logic works in isolation; chain and dual-truth cut-over remain defective. |
| 7 | CreditDefinition-based wallet | PARTIAL | IDs are populated, but legacy CreditType writer contract remains. |
| 8 | Immutable invoice snapshot | CLOSED diagnostically | Request and items reject legal mutations; totals and real exactly-once credit consumption pass after continuation. |
| 9 | Review action/disposition history | PARTIAL | Evidence is sufficient, but transaction tenant ownership is not enforced. |

Summary: **3 CLOSED, 3 PARTIAL, 3 OPEN**.

## 14. Application-only worklist

These are application release blockers, not reasons by themselves to reject DB freeze:

1. Add current Partner context and mandatory tenant predicates.
2. Use Partner-scoped roles; reserve global UserRoles for platform control plane.
3. Move banking/payment screens and services to PaymentTransactions, instructions/registry, and generic allocation APIs.
4. Implement customer cash/refund workflows through the final constrained procedures.
5. Use CreditDefinitionId, subscription grants, wallet reservations and ledger as the only quota path.
6. Freeze complete invoice snapshots before submission and call exactly-once issue finalization.
7. Implement Inbox/Outbox worker loops with ownership, renew, retry and dead-letter behavior.
8. Remove fixed bootstrap `Admin@123`; use a one-time secret and forced rotation.
9. Add integration tests for tenant isolation, financial concurrency, provider duplicates and invoice retries.

Current Release build passes with 0 warnings and 0 errors. No test project exists. Current C# still uses unfiltered legacy tables/global roles and is **not release ready**.

## 15. Deployment-tooling worklist

1. Calculate a stable hash for each migration file before execution.
2. Reject reuse of a MigrationId with different content.
3. Execute each file with the documented sqlcmd-compatible session and record success/hash atomically.
4. Require complete expected history before production postflight.
5. Preserve logs, SQL error number, server version and compatibility evidence.

`SchemaMigrationHistory` can hold this data. SQL migrations should not pretend to hash their own source files.

## 16. Proposed migration 049+

Correcting 042 itself is mandatory because an unreachable 049 cannot repair an ordered chain stopped at 042. Since these RC files are explicitly unshipped, amend the invalid 042 statement or remove the defective procedure body there while preserving 045 as the final implementation.

After the chain is executable, proposed explicit corrections are:

| Migration | Purpose |
|---|---|
| 049 | Safely migrate CustomerCashLedger around its immutable trigger, install the Partner/idempotency-aware procedure, and make all mandatory financial procedures rollback-safe. |
| 050 | Complete financial tenant ownership using composite keys/FKs or constrained write APIs for account/company, transaction/account, instruction/account, customer cash/customer, refund, invoice/customer, matching/review, and grant/subscription period. |
| 051 | Enforce legacy stop-write/derived contracts for request payment-code/QR fields, CreditType quota writers, consumable entitlement UsedQuantity, and old reconciliation run tables. |

No 049+ file was created during this audit.

## 17. Final verdict

**NOT READY**

RC4 does not meet the required freeze conditions: 001–048 does not run as an ordered chain, migration 047 independently fails, customer-cash RC4 protection is therefore absent, and tested cross-Partner DML can corrupt financial/legal ownership. Although the diagnostic versions of generic payments, credit grants, invoices and workers perform well, they cannot compensate for the failed release chain and remaining DB ownership/source-of-truth defects.
