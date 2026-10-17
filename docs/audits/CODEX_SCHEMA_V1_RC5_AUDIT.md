# CODEX Database V1 RC5 Final-Freeze Audit

## 1. Identification and verdict

| Item | Result |
|---|---|
| Branch | `feat/compact-dynamic-navigation` |
| Exact HEAD | `9efcbb94a4c9b2c578e1c03582d30620d4024a5b` |
| Audit date | 2026-10-01 |
| SQL Server | Microsoft SQL Server 2014 `12.0.2269.0`, Enterprise Edition (64-bit) |
| Compatibility | 120 |
| Audit database | Disposable local `TAPortal` |
| Execution | Original committed files via `sqlcmd -b -I -i`; no source normalization |
| Database verdict | **READY AFTER DB FIXES** |
| Application verdict | **NOT RELEASE READY** |

The branch was fetched, fast-forwarded and confirmed synchronized with origin (`0/0`). Existing local changes were preserved. No SQL or C# source was modified, no merge was performed, and production was not contacted.

## 2. Executive result

RC5 fixes all four RC4 blockers at their original test points:

- migrations 001–051 execute continuously on SQL Server 2014 compatibility 120;
- RC5 postflight passes the clean schema and fails intentional corruption;
- all tested cross-Partner financial/legal relationships are rejected;
- payment, customer cash, subscription grant, invoice and worker concurrency scenarios pass;
- all mandatory financial procedures return the caller to `XACT_STATE() = 0` on tested validation errors.

RC5 is nevertheless not ready for final DB freeze because the consumable-credit cut-over remains bypassable. Legacy `sp_GrantCredit` created and credited a new wallet with `CreditDefinitionId = NULL`; direct reservations and top-ups with NULL canonical definition were also accepted. This is an unresolved second write path beside the canonical CreditDefinition/wallet/ledger model and is a DB BLOCKER under the prompt's freeze rule.

Two legacy authorization/instruction fields also remain independently writable: request QR fields and `PartnerUsers.PartnerRole`. They should be stopped or explicitly derived before freeze.

DB finding counts:

| BLOCKER | HIGH | MEDIUM | LOW |
|---:|---:|---:|---:|
| 1 | 2 | 3 | 0 |

## 3. Ordered migration execution

All 51 committed files executed in exact order with exit code 0.

| Migration range | Result | Notes |
|---|---|---|
| 001–010 | PASS | Original files |
| 011–020 | PASS | Original files |
| 021–030 | PASS | Original files |
| 031–040 | PASS | Original files |
| 041 | PASS | Generic payment cut-over |
| 042 | PASS | No `PartnerCreditWallets.UpdatedAt` reference remains |
| 043–046 | PASS | Invoice, worker, credit and allocation corrections |
| 047 | PASS | Completes despite immutable CustomerCashLedger trigger |
| 048 | PASS | Worker/tenant release gate |
| 049 | PASS | Financial procedure rollback safety |
| 050 | PASS | Financial tenant ownership guards |
| 051 | PASS | Legacy stop-write contracts |

**Ordered 001–051 result: PASS.**

Whole-chain scan found no `MERGE`, `CREATE OR ALTER`, same-line `GO`, `STRING_AGG`, `JSON_VALUE`, `OPENJSON`, `DROP ... IF EXISTS`, or identified SQL Server 2016+ syntax. Execution, not static search alone, confirms SQL Server 2014 compatibility.

## 4. RC5 postflight

Clean schema result:

- exit code: 0;
- output: `V1 RC5 POSTFLIGHT: PASS`.

Fail-closed test inserted an overallocated transaction/receivable and a GRANTED subscription credit without a ledger link. Postflight returned:

- `OVERALLOCATED_TRANSACTION = 1`;
- `OVERALLOCATED_RECEIVABLE = 1`;
- `CREDIT_GRANT_WITHOUT_LEDGER = 1`;
- exit code 1 / error 51900.

After cleanup, postflight returned PASS again. Several other corruptions could not be inserted because write-time constraints/triggers rejected them.

Postflight does not detect wallets/reservations/top-ups with NULL `CreditDefinitionId`; the legacy grant bypass therefore survives a nominal PASS.

## 5. RC4 blocker closure

| RC4 blocker | Status | Evidence |
|---|---|---|
| B-01 migration 042 ordered-chain failure | CLOSED | 042 executes; ordered chain continues through 051. |
| B-02 migration 047 immutable-ledger failure | CLOSED | 047 executes and installs canonical Partner/idempotency-aware cash procedure. |
| B-03 cross-Partner financial/legal ownership | CLOSED for mandatory matrix | All requested cross-Partner direct-DML tests were rejected by DB. |
| B-04 consumable quota dual truth | **PARTIAL** | UsedQuantity and type/definition mismatch are guarded, but legacy grant and NULL-definition reservation/top-up paths remain writable. |

## 6. Mandatory financial and concurrency scenarios

| Scenario | Result | Evidence |
|---|---|---|
| 500k receivable paid 400k + 100k | PASS | AmountPaid 500k, PAID, two active allocations. |
| 300k payment against 100k remaining | PASS | Allocated 100k; 200k remained unallocated. |
| One 300k payment split 100k + 200k | PASS | Two allocations totaling 300k. |
| Concurrent allocation of one transaction | PASS | One success, one 51613 rejection; active total 100k. |
| Concurrent settlement of one receivable | PASS | One success, one 51613 rejection; active total 100k. |
| OperationKey retry | PASS | One row and one financial effect. |
| Reversal | PASS | AmountPaid recomputed to 0, status PENDING. |
| Cross-Partner allocation | PASS rejection | Error 51612; XACT_STATE 0. |
| Customer cash +100k, retry, -50k | PASS | Balance 50k and exactly two ledger rows. |
| Customer cash negative | PASS rejection | Error 51625; XACT_STATE 0. |
| Customer cash cross-tenant transaction | PASS rejection | Error 51624; XACT_STATE 0. |
| Customer cash ledger UPDATE/DELETE | PASS rejection | Both rejected by immutable trigger. |
| Subscription grant sequential/concurrent retry | PASS | Balance +50 once; one GRANT ledger row and linked ledger ID. |
| Valid invoice freeze | PASS | 100k - 0 + 10k + 0 = 110k. |
| Invalid invoice totals | PASS rejection | Error 51802; XACT_STATE 0. |
| Frozen InvoiceRequest mutation | PASS rejection | Error 51430. |
| Frozen InvoiceItems UPDATE/INSERT/DELETE | PASS rejection | Error 51620 for each operation. |
| Invoice credit finalization twice | PASS | Wallet 10→9, reserved 1→0, reservation/binding CONSUMED, one CONSUME ledger row. |
| Inbox concurrent claim | PASS | Only one worker obtained row. |
| Inbox ownership/renew/retry/dead | PASS | Wrong owner rejected; second failure at MaxAttempts produced DEAD. |
| Outbox concurrent claim | PASS | Only one worker obtained row. |
| Outbox ownership/retry/dead | PASS | Wrong owner rejected; second failure at MaxAttempts produced DEAD. |

## 7. Procedure transaction safety

| Procedure | Forced error/result | Caller XACT_STATE |
|---|---|---:|
| `sp_AllocatePaymentTransaction` | Cross-Partner transaction, error 51612 | 0 |
| `sp_ReversePaymentAllocationV2` | Missing allocation, error 51614 | 0 |
| `sp_PostCustomerCash` | Missing account / negative / tenant mismatch | 0 |
| `sp_GrantSubscriptionCredit` | Missing wallet, error 51602 | 0 |
| `sp_FreezeInvoiceRequest` | Total mismatch, error 51802 | 0 |
| `sp_FinalizeIssuedInvoiceCredit` | Missing binding, error 51621 | 0 |

Missing GrantId is intentionally idempotent/no-op and also returns XACT_STATE 0.

## 8. Tenant direct-DML matrix

| Relationship | Result | Enforcement |
|---|---|---|
| FinancialAccount → foreign Company | REJECTED | Error 51810 |
| PaymentTransaction → foreign account/company | REJECTED | Error 51811 |
| PaymentInstruction → foreign account | REJECTED | Error 51812 |
| PaymentInstruction → foreign request | REJECTED | Composite FK/check |
| CustomerCashAccount → foreign customer/company | REJECTED | Error 51813 |
| RefundRequest → foreign customer/transaction/cash/company | REJECTED | Error 51814 |
| MatchingAttempt → foreign transaction/request/customer | REJECTED | Error 51815 |
| PaymentReviewAction → foreign transaction | REJECTED | Error 51816 |
| InvoiceRequest → foreign customer/company | REJECTED | Error 51817 |
| SubscriptionCreditGrant → foreign subscription period | REJECTED | Error 51818 |
| PaymentAllocation → foreign request/transaction | REJECTED | Composite tenant constraints/procedure |
| PartnerUserRole → foreign PartnerRole | REJECTED | Composite tenant role constraint |
| InvoiceCreditConsumption → foreign reservation | REJECTED | Composite tenant reservation constraint |

Mandatory ownership-corrupting direct writes are fail-closed.

## 9. Canonical source-of-truth tests

### Payment instruction

- A new PaymentRequest was created without legacy payment code: PASS.
- PaymentInstruction and PaymentCodeRegistry creation: PASS.
- New request with legacy PaymentCode/NormalizedCode: rejected with 51830.
- Change to legacy request code: rejected with 51830.
- PaymentCodeAliases is deprecated/read-only.
- Legacy request QR columns remain writable: **FAIL**; `QrProvider` and `QrPayload` update was accepted.

### Consumable credit

- `SubscriptionEntitlements.UsedQuantity` update: rejected with 51831.
- `vw_PartnerServiceEntitlements.UsedQuantity`: NULL, not presented as mutable usage.
- Wallet/reservation/top-up type inconsistent with a non-null CreditDefinitionId: rejected with 51832/51833/51837.
- Legacy `sp_GrantCredit` for a new Partner: **accepted**, created a wallet with `CreditDefinitionId = NULL`, balance 5.
- Direct reservation with NULL CreditDefinitionId: **accepted**.
- Direct top-up with NULL CreditDefinitionId: **accepted**.

Therefore CreditDefinitionId is not yet mandatory at every consumable write boundary.

### Reconciliation

- `ReconciliationRuns`: canonical insert succeeded.
- new `BankSyncJobs` write: rejected with 51834.
- new `BankReconciliationRuns` write: rejected with 51835.
- historical legacy rows remain preservable/readable.

## 10. DB-only findings

| ID | Severity | Object | Finding |
|---|---|---|---|
| B-01 | BLOCKER | `sp_GrantCredit`, CreditReservations, CreditTopups | Legacy grant created a credited wallet with NULL CreditDefinitionId; NULL-definition reservation/top-up inserts also succeed. This permits a consumable balance outside the canonical CreditDefinition contract and is not detected by postflight. |
| H-01 | HIGH | PaymentRequests legacy QR fields | Code fields are read-only, but QrProvider/QrPayload/QrGeneratedAt remain independently writable despite PaymentInstruction being canonical. |
| H-02 | HIGH | `PartnerUsers.PartnerRole` | Legacy membership role string remains writable independently from PartnerUserRoles; test update was accepted, allowing an authorization dual truth if any consumer reads it. |
| M-01 | MEDIUM | RC5 postflight | Does not check NULL CreditDefinitionId across wallet/reservation/top-up/ledger/rules or legacy QR writes. |
| M-02 | MEDIUM | migration governance | SchemaMigrationHistory is available, but raw sqlcmd execution does not populate hashes/history. Deployment tooling must own this. |
| M-03 | MEDIUM | secret-bearing payload policy | HostedLinkUrl is blocked, but raw webhook/API/outbox/invoice payload sanitization and retention remain application/operations policy. |

## 11. Legacy/source-of-truth matrix

| Object/path | Classification | Result |
|---|---|---|
| `PaymentTransactions` | CANONICAL | Rail-neutral settlement source. |
| `BankTransactions` | EVIDENCE | Retain provider/bank evidence. |
| `PaymentAllocations.BankTransactionId` | COMPATIBILITY | Nullable evidence link only. |
| `sp_AllocateBankTransaction` | DISABLED | Throws 51615. |
| PaymentInstructions / PaymentCodeRegistry | CANONICAL | Collection instruction/code truth. |
| PaymentRequests PaymentCode/Normalized | COMPATIBILITY READ-ONLY | Insert/change rejected. |
| PaymentRequests QR fields | MUST DISABLE/DERIVE | Still writable; conflicts with instruction snapshot. |
| PaymentCodeAliases | DISABLED/READ-ONLY | Writes rejected. |
| CreditDefinitionId + wallet + ledger | CANONICAL TARGET | Core works, but NULL bypass remains. |
| CreditType | COMPATIBILITY metadata | Must not create/select a NULL or inconsistent canonical definition. Current legacy grant violates this. |
| SubscriptionEntitlements.UsedQuantity | COMPATIBILITY READ-ONLY | Updates rejected; view exposes NULL. |
| PartnerUserRoles | CANONICAL | Tenant role assignments. |
| PartnerUsers.PartnerRole | MUST DISABLE/DERIVE | Still independently writable. |
| ReconciliationRuns | CANONICAL | New execution model. |
| BankSyncJobs / BankReconciliationRuns | COMPATIBILITY READ-ONLY | New writes rejected; history preserved. |

## 12. Nine functional must-change closure

| # | Item | Status | Evidence |
|---:|---|---|---|
| 1 | Partner-scoped RBAC | PARTIAL | Canonical role graph is tenant-safe; legacy PartnerRole string remains writable. |
| 2 | PaymentInstruction lifecycle | PARTIAL | Code cut-over is enforced; legacy QR fields remain writable. |
| 3 | Generic payment allocation | CLOSED | All settlement, idempotency, concurrency and reversal scenarios pass; legacy procedure disabled. |
| 4 | Customer cash/refund | CLOSED for DB V1 | Cash invariants/idempotency/tenant safety pass; refund remains compensating lifecycle. |
| 5 | Company/legal ownership | CLOSED | Mandatory cross-Partner direct-DML matrix rejects corruption. |
| 6 | Wallet sole quota + subscription grant | OPEN | Legacy grant/NULL-definition paths bypass canonical definition. |
| 7 | CreditDefinition-based wallet | PARTIAL | Mismatch guarded, but canonical ID is not mandatory at every write boundary. |
| 8 | Immutable invoice snapshot | CLOSED | Request/items/totals and exactly-once consumption pass. |
| 9 | Review action/disposition history | CLOSED | Evidence model exists and tenant ownership is enforced. |

Summary: **5 CLOSED, 3 PARTIAL, 1 OPEN**.

## 13. Build and application-only worklist

Release build: PASS, 0 warnings, 0 errors. No test project exists.

The database findings above are independent of application adoption. Separately, the application remains **NOT RELEASE READY** and must:

1. introduce current Partner context and tenant-filter all queries;
2. move tenant authorization to PartnerUsers/PartnerUserRoles and reserve UserRoles for platform control plane;
3. use PaymentTransactions, PaymentInstructions/Registry and generic allocation APIs;
4. implement customer-cash/refund workflows through canonical procedures;
5. use CreditDefinitionId and remove calls to legacy type-only credit procedures;
6. freeze/finalize invoices through the exactly-once contracts;
7. run Inbox/Outbox workers with lease ownership/retry/dead handling;
8. remove hard-coded `Admin@123` bootstrap and require a one-time secret/rotation;
9. add integration tests for tenant isolation and financial/provider concurrency.

Current C# still reads global roles, BankTransactions, request PaymentCode and CreditType without tenant predicates.

## 14. Deployment-tooling worklist

1. Calculate stable file hashes before migration execution.
2. Reject MigrationId reuse with different content.
3. Record migration ID/hash/result atomically in SchemaMigrationHistory.
4. Require the complete release manifest before production postflight.
5. Preserve SQL Server/version/compatibility and execution logs.

Hashing belongs to deployment tooling, not SQL source-file self-inspection.

## 15. Proposed migration 052+

A genuine DB correction remains:

| Migration | Purpose |
|---|---|
| 052 | Complete canonical cut-over: require/backfill CreditDefinitionId on all wallet/reservation/top-up/ledger/rule write paths; replace or disable type-only `sp_GrantCredit`/`sp_ReserveCredit`; extend postflight; make legacy request QR and PartnerUsers.PartnerRole read-only/derived. |

No migration was created during this audit.

## 16. Final verdict

**READY AFTER DB FIXES**

RC5 passes the ordered SQL Server 2014 deployment, clean/fail-closed postflight, tenant ownership, financial correctness, idempotency and concurrency tests. It cannot be frozen while legacy credit writes can create consumable balances with no CreditDefinitionId. After the focused 052 correction, the full 001–latest chain, postflight and canonical-credit scenarios should be rerun before declaring `READY FOR DB V1 FREEZE`.
