# CODEX Third Audit — T.A Portal Database V1 RC3

## 1. Audit identification

| Item | Result |
|---|---|
| Branch | `feat/compact-dynamic-navigation` |
| Exact HEAD | `cebdfdab0e1ecb3bef927ee57802c2c2d5d4eea6` |
| Audit date | 2026-10-01 |
| SQL Server | `12.0.2269.0`, Enterprise Edition (64-bit) |
| Compatibility | 120 |
| Audit database | Disposable local database named `TAPortal` |
| Source discipline | Migrations were executed directly with `sqlcmd -i`; no in-memory normalization and no SQL/C# edits |
| Verdict | **READY AFTER DB FIXES** |

The branch was fetched and fast-forwarded from RC2 HEAD `ab279d27702199279f3a64e722e4239142133693` to the exact RC3 HEAD above. Existing local changes were preserved.

## 2. Executive result

RC3 closes five of the six RC2 blockers and materially improves the executable database contract. Migrations 001–041 execute in order on SQL Server 2014 compatibility 120. The chain still fails at migration 042 because `sp_GrantSubscriptionCredit` references nonexistent column `PartnerCreditWallets.UpdatedAt`. Migrations 043 and 044 execute independently, but that does not make the ordered chain successful.

The postflight incorrectly reports PASS after the failed migration because migration 042 leaves behind a placeholder `sp_GrantSubscriptionCredit` and the postflight checks only object existence, not its signature/definition or migration-history completion.

Scenario testing also found two freeze-significant payment/invoice defects:

- legacy unique index `UX_PA_RequestTransactionActive(PaymentRequestId, BankTransactionId)` prevents a receivable from receiving a second generic allocation because both rows have `BankTransactionId = NULL`;
- invoice finalization marks the binding `CONSUMED` but does not consume the reservation/wallet ledger, and frozen invoice items remain mutable.

Finding counts:

| BLOCKER | HIGH | MEDIUM | LOW |
|---:|---:|---:|---:|
| 2 | 9 | 4 | 1 |

## 3. Original migration execution

All migrations were executed unmodified using `sqlcmd -b -I -i <file>`.

| Range | Result | Evidence |
|---|---|---|
| 001–007 | PASS | Clean baseline installed successfully |
| 008–028 | PASS | RC1/RC2 chain installed without MERGE or batch errors |
| 029–038 | PASS | RC2 functional migrations installed as committed |
| 039–041 | PASS | Governance, tenant-role/instruction integrity, and generic allocation procedure installed |
| 042 | **FAIL** | SQL error 207 in `sp_GrantSubscriptionCredit`: `Invalid column name 'UpdatedAt'` |
| 043–044 | PASS when executed independently | Diagnostic continuation only; ordered chain remains failed |

Static whole-chain search found no `MERGE`, `CREATE OR ALTER`, same-line `GO`, or identified SQL 2016+ syntax. `[Cursor]` remains correctly delimited.

### Rerun

A second direct execution was attempted after scenario setup because the scripts use guarded object creation and print completion markers. Migrations 001–041 and 043–044 completed again; migration 042 failed again on the same nonexistent column. The scripts do not populate `SchemaMigrationHistory`, so this rerun is not controlled by an applied-script ledger.

## 4. Postflight

`database/preflight/V1_RC3_POSTFLIGHT.sql` returned exit code 0 and printed `V1 RC3 POSTFLIGHT: PASS`, both immediately after the failed ordered chain and again after scenario testing.

This is a **false PASS**:

- `SchemaMigrationHistory` contained zero rows;
- the required-object check accepted the zero-parameter placeholder procedure created immediately before the failed `ALTER PROCEDURE`;
- it does not validate required procedure parameters or implementation;
- it does not validate invoice-item immutability, actual invoice credit consumption, legacy generic-allocation indexes, or completion of migrations 001–044.

The invariant-count portion is fail-closed when a listed count is nonzero, but deployment completeness is not fail-closed.

## 5. Build and tests

| Check | Result |
|---|---|
| `dotnet build TAPortal.sln --configuration Release --no-restore` | PASS, 0 warnings, 0 errors |
| Automated test projects | None found (`*Tests.csproj`) |
| Application readiness | Not deployable: current C# still uses global roles, unfiltered tenant queries, bank-specific transactions, request payment-code fields, `CreditType`, and fixed bootstrap password |

Application mismatch is reported separately and is not used by itself to reject the DB architecture.

## 6. Mandatory scenario results

| Scenario | Result | Evidence |
|---|---|---|
| 500k receivable paid 400k + 100k | **FAIL** | First generic allocation succeeds; second fails error 2601 on `UX_PA_RequestTransactionActive` because both have NULL `BankTransactionId`. Request remains 400k/PARTIALLY_PAID. |
| 300k transaction against 100k remaining | PASS | Default allocation = 100k; request PAID; 200k remains unallocated. |
| One transaction split across two receivables | PASS | Two active allocations total 300k. |
| Concurrent allocations cannot exceed transaction | PASS | Two simultaneous 100k attempts against one 100k transaction produced one success and one error 51413; one active row totaling 100k. |
| Concurrent allocations cannot exceed receivable | Partial | Procedure serializes request/transaction, but the legacy NULL-bank index blocks valid multi-payment before this invariant can be fully exercised. |
| Reversal recomputes state | PASS | 100k request returned to AmountPaid 0 and PENDING. |
| Generic transaction with NULL BankTransactionId | PASS with defect | Four generic allocations persisted with NULL bank evidence, but only one active NULL-bank row is allowed per receivable. |
| Cross-Partner allocation | PASS rejection | Error 51412; however caught errors can leave the caller transaction uncommittable because procedures do not rollback in CATCH. |
| Cross-Partner PartnerRole assignment | PASS rejection | Composite tenant role FK/check rejected the row with error 547. |
| AMOUNT_ASSIST AutoAllocate | PASS rejection | `CK_PMR_AmountNoAuto` rejected with error 547. |
| Customer cash negative balance | PASS rejection | Error 51422. |
| Customer cash UPDATE/DELETE | PASS rejection | Trigger rejected both with error 51420. |
| Customer cash compensating post | PASS | +100k then -50k created two ledger rows; balance 50k. |
| Subscription grant exactly once | **BLOCKED/FAIL** | Migration 042 left a zero-parameter stub. Calls with GrantId fail; grant remains PENDING, balance 0, ledger rows 0. |
| Frozen invoice totals reconcile | PASS | Valid 100k - 0 + 10k + 0 = 110k froze to READY. |
| Frozen request snapshot cannot mutate | PASS | Request total update rejected with error 51430. |
| Frozen invoice item cannot mutate | **FAIL** | `InvoiceItems.ItemName` update after freeze was accepted. |
| Invoice issue credit finalization idempotent | **FAIL semantics** | Repeated calls are idempotent for `InvoiceCreditConsumptions.Status`, but reservation remains ACTIVE and no `CreditLedger` consume row is written. |
| HostedLinkUrl persistence | PASS rejection | Insert rejected with error 51440. |
| Concurrent Inbox claim | PASS | Two simultaneous workers returned the row to only one worker; final AttemptCount 1 and one LockId. |

## 7. Findings

| ID | Severity | Exact object | Evidence and impact |
|---|---|---|---|
| B-01 | BLOCKER | 042 / `sp_GrantSubscriptionCredit` | Ordered migration chain stops with error 207 because `PartnerCreditWallets.UpdatedAt` does not exist. The resulting procedure remains the no-op placeholder. |
| B-02 | BLOCKER | `UX_PA_RequestTransactionActive` | Index keys `(PaymentRequestId,BankTransactionId)` with filter `Status='ACTIVE'`. SQL Server permits only one NULL bank key per request, so 400k + 100k generic payments cannot settle one 500k receivable. |
| H-01 | HIGH | RC3 postflight | Reports PASS after migration 042 failed; accepts placeholder procedure and empty `SchemaMigrationHistory`. Deployment completeness is not verified. |
| H-02 | HIGH | `sp_FinalizeIssuedInvoiceCredit` | Marks binding CONSUMED but never calls credit-consume logic, updates reservation, wallet or CreditLedger. “Consumed” is a false financial state. |
| H-03 | HIGH | Invoice snapshot immutability | Trigger protects only selected `InvoiceRequests` columns. `InvoiceItems` legal fields remain mutable after freeze; test update was accepted. |
| H-04 | HIGH | Tenant integrity graph | 040 enforces roles and instructions, and 038 enforces allocations, but many Partner-bearing relationships still use independent FKs: financial accounts/transactions, customer cash/refunds, matching/review, grants, invoices/consumptions and reconciliation. Cross-tenant direct DML remains possible. |
| H-05 | HIGH | Generic payment transition | Legacy `sp_AllocateBankTransaction`, request payment/QR columns, aliases and old active index remain writable. Generic and bank-specific paths remain competing settlement truths. |
| H-06 | HIGH | Allocation procedure error handling | Procedures use `XACT_ABORT` and THROW without TRY/CATCH rollback. A rejected cross-tenant allocation left the batch with error 3998/uncommittable transaction when the caller caught the error. |
| H-07 | HIGH | Customer cash tenant contract | Monetary posting is serialized and immutable, but `sp_PostCustomerCash` takes only AccountId and accepts a PaymentTransactionId without same-Partner enforcement or idempotency key. Retry can double-post and cross-tenant evidence can be linked. |
| H-08 | HIGH | Worker lifecycle | Inbox atomic claim passes, but no complete/renew/fail/dead procedure contract is supplied; equivalent atomic outbox claim/completion is still absent. |
| H-09 | HIGH | Subscription/credit cut-over | Besides the compile failure, legacy CreditType procedures/constraints and `SubscriptionEntitlements.UsedQuantity` remain writable sources alongside CreditDefinition/wallet usage. |
| M-01 | MEDIUM | `SchemaMigrationHistory` | Design correctly reserves externally supplied hashes, but no deployment tooling populated rows. Tooling must atomically register per-script hash/status; the DB cannot hash source files itself. |
| M-02 | MEDIUM | Payment procedure uniqueness/idempotency | Generic allocations have no caller idempotency key; retries can create duplicate allocations where legacy index does not block them. |
| M-03 | MEDIUM | Provider payload security | Hosted link is now blocked, but unrestricted raw provider/webhook/invoice/outbox payload persistence still relies on application sanitization and access policy. |
| M-04 | MEDIUM | Reconciliation transition | `BankSyncJobs`, `BankReconciliationRuns`, and `ReconciliationRuns` remain writable with no enforced stop-write transition. |
| L-01 | LOW | Deployment/session documentation | Scripts require a sqlcmd/SSMS session honoring expected SET options; 039 cannot retroactively protect 001–038. Publish the exact runner contract. |

## 8. RC2 blocker closure matrix

| RC2 blocker | RC3 status | Evidence |
|---|---|---|
| B-01 same-line GO 029–038 | CLOSED | Original scripts execute via sqlcmd. |
| B-02 MERGE 008/014/016 | CLOSED | No MERGE remains; all three execute on SQL Server 2014. |
| B-03 NumberSequences.Scope | CLOSED | Corrected to `ScopeType`; 021 executes. |
| B-04 immutable CreditLedger backfill | CLOSED | 035 executes cleanly on first run and rerun. |
| B-05 PaymentAllocations DDL batch split | CLOSED | 038 executes cleanly. |
| B-06 generic allocation/final schema | PARTIAL | New generic procedure works for one allocation and concurrency, but legacy NULL-bank unique index breaks many-payments-to-one-receivable. |

Summary: **5 CLOSED, 1 PARTIAL, 0 OPEN**.

## 9. RC2 high closure matrix

| RC2 high | RC3 status | Evidence |
|---|---|---|
| H-01 tenant-owned FK graph | PARTIAL | Roles, instructions and allocations improved; most graph remains independently keyed. |
| H-02 PartnerUserRoles tenant mismatch | CLOSED | Composite FK plus equality CHECK rejects cross-Partner role. |
| H-03 payment-instruction transition | PARTIAL | Instruction tenant FK exists; legacy request code/QR truth remains writable. |
| H-04 generic payment transition | PARTIAL | Generic procedure added; old index/procedure and BankTransaction path remain. |
| H-05 customer cash safety | PARTIAL | Negative/immutability/compensation pass; tenant and retry idempotency are missing. |
| H-06 subscription grant/quota | OPEN | 042 fails and leaves placeholder; legacy quota truths remain. |
| H-07 invoice snapshot/consumption | PARTIAL | Request freeze works; item freeze and actual credit consumption fail. |
| H-08 inbox/outbox workers | PARTIAL | Inbox claim passes; complete failure/dead and outbox contracts remain incomplete. |
| H-09 fail-closed preflight | OPEN | Invariant counts fail closed, but failed deployment receives PASS. |
| H-10 application tenant/legacy reads | APPLICATION-ONLY OPEN | DB freeze does not require current C# migration, but release does. |
| H-11 fixed bootstrap credential | APPLICATION-ONLY OPEN | `Admin@123` remains in C#. |

## 10. Nine functional must-change closure

| # | Item | Status | Result |
|---:|---|---|---|
| 1 | Partner-scoped RBAC | PARTIAL | Core role assignment is tenant-safe; scoped application authorization and typed org scopes remain. |
| 2 | PaymentInstruction lifecycle | PARTIAL | Structure exists; old request fields remain active and transition is incomplete. |
| 3 | Generic payment allocation | PARTIAL | Generic flow works, but valid second payment is blocked by legacy index. |
| 4 | Customer cash/refund | PARTIAL | Safe basic cash ledger passes; idempotency/tenant/refund execution contract incomplete. |
| 5 | Company/legal ownership | PARTIAL | Columns exist; complete same-Partner graph enforcement is absent. |
| 6 | Wallet sole quota + subscription grant | **OPEN** | Migration/procedure fails and entitlement/legacy paths remain. |
| 7 | CreditDefinition-based wallet | PARTIAL | Definition IDs exist, but legacy CreditType remains authoritative in procedures/schema/app. |
| 8 | Immutable invoice snapshot | PARTIAL | Request fields freeze; items and real credit consumption fail. |
| 9 | Review action/disposition history | CLOSED (DB structure) | Attempts, evidence, actions, actor/reason/dispositions exist; application workflow remains post-freeze work. |

Summary: **1 CLOSED, 7 PARTIAL, 1 OPEN**.

## 11. Legacy cleanup matrix

| Object | Classification | Required action |
|---|---|---|
| `BankTransactions` | KEEP AS EVIDENCE | Retain immutable provider/bank evidence linked to `PaymentTransactions`. |
| `PaymentAllocations.BankTransactionId` | COMPATIBILITY ONLY | Keep nullable evidence link; remove it from business uniqueness and allocation identity. |
| `UX_PA_RequestTransactionActive` | MUST REPLACE BEFORE FREEZE | Unique active identity must use `PaymentTransactionId`, not nullable bank evidence. |
| `sp_AllocateBankTransaction` | DEPRECATE / DISABLE | Prevent a second writable settlement API after generic cut-over. |
| Request PaymentCode/QR columns | DEPRECATE | Stop writes after instruction/registry backfill; expose derived compatibility values only if needed. |
| Bank sync/reconciliation legacy tables | COMPATIBILITY ONLY | Stop writes and route new work to `ReconciliationRuns`. |
| CreditType columns/procedures | DEPRECATE | Cut over to non-null CreditDefinitionId; keep only derived compatibility if unavoidable. |
| `SubscriptionEntitlements.UsedQuantity` | MUST REMOVE FOR CONSUMABLES | Wallet ledger must be the only consumable usage truth. |
| Global `UserRoles` | PLATFORM ONLY | Tenant roles must use Partner membership; current C# migration is application work. |
| `PartnerUsers.PartnerRole` | DEPRECATE | Stop writes after role-assignment backfill. |
| `PaymentRequests.AmountPaid` | COMPATIBILITY CACHE | Update only through the generic allocation/reversal API or derive from active allocations. |

## 12. Application-only worklist after DB freeze

1. Add authenticated current-Partner context and tenant filters to all operational queries.
2. Move tenant authorization from global UserRoles to PartnerUsers/PartnerUserRoles; reserve global roles for platform control plane.
3. Remove fixed `Admin@123`; use a one-time bootstrap secret, forced rotation and audit.
4. Write/read PaymentTransactions and PaymentInstructions/PaymentCodeRegistry; treat BankTransactions as evidence.
5. Call only generic allocation/reversal procedures with idempotency and proper transaction-error handling.
6. Replace CreditType with CreditDefinitionId and run subscription grants through the corrected exactly-once API.
7. Use customer-cash/refund procedures and matching/review evidence/actions.
8. Freeze complete invoice snapshots before submission and bind provider issue to real credit consumption.
9. Implement inbox/outbox complete, retry, lease renewal and dead-letter flows.
10. Add integration tests for tenant crossings, concurrent payments, wallet/customer-cash races, duplicate deliveries and invoice retries.

Release remains not deployable until this application work is complete, even after DB freeze blockers are corrected.

## 13. Proposed corrective migrations 045+

| Migration | Required correction |
|---|---|
| 045 | Repair credit cut-over: correct wallet update, replace placeholder grant procedure, enforce non-null CreditDefinition cut-over, exactly-once grant ledger link, and retire consumable UsedQuantity/legacy writers. |
| 046 | Generic allocation completion: replace legacy NULL-bank unique index with active `(PaymentRequestId,PaymentTransactionId)` identity, add operation idempotency, safe TRY/CATCH rollback, and disable old bank allocation writes. |
| 047 | Invoice/cash integrity: freeze InvoiceItems, make invoice finalization actually consume reservation/wallet ledger atomically, add customer-cash idempotency and same-Partner transaction enforcement. |
| 048 | Deployment/tenant/worker gate: validate migration history/signatures in postflight, complete composite tenant relationships, and add inbox/outbox complete/fail/dead/lease APIs. |

Migration-history hashes must be calculated and registered by deployment tooling. SQL migration code should validate supplied history; it should not pretend to hash its own source file.

## 14. Final verdict

**READY AFTER DB FIXES**

The RC3 architecture is substantially closer to the intended V1 model, and many mandatory scenarios now pass on the actual SQL Server 2014 target. It is not ready for DB V1 freeze at this HEAD because the ordered chain fails at 042, the core many-payments-to-one-receivable scenario is blocked by a legacy unique index, invoice credit “consumption” does not consume credit, frozen invoice items remain mutable, and postflight can falsely pass a failed deployment.

Re-run the full unmodified 001–latest chain, postflight, rerun checks and all scenarios after 045+ corrections before approving DB V1 freeze.
