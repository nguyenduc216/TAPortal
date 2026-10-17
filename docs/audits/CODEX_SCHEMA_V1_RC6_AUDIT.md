# CODEX Database V1 RC6 Final-Freeze Audit

## 1. Identification and verdict

| Item | Result |
|---|---|
| Branch | `feat/compact-dynamic-navigation` |
| Exact HEAD | `f2d56fb3657de811c6baf0a90c72db4680e9f9d9` |
| Origin | Synchronized `0/0` after fetch/fast-forward |
| Audit date | 2026-10-02 |
| Repeat execution | Independent clean rerun completed on 2026-10-02 after a fresh origin fetch |
| SQL Server | SQL Server 2014 `12.0.2269.0`, Enterprise Edition (64-bit) |
| Compatibility | 120 |
| Database | Disposable local `TAPortal` on `localhost\SQLSERVER2014` |
| Execution | Original committed files via `sqlcmd -b -I -i` |
| DB verdict | **READY FOR DB V1 FREEZE** |
| Application verdict | **NOT RELEASE READY** |

The branch was fast-forwarded from `66c1a65a636814d624c7c89d5c2b9ffb2dd8d7b7`. A subsequent origin fetch confirmed the same exact HEAD at `0/0`, after which the database and every mandatory test were independently recreated and rerun again. No previous RC6 result was substituted. Existing local changes were preserved. No SQL/C# source was modified, no merge was created, and production was not contacted.

## 2. Executive result

Original migrations 001–052 execute continuously. Clean postflight passes; intentional corruption fails postflight and cleanup returns it to PASS. Canonical credit sequential/concurrent tests, QR/PartnerRole stop-write, tenant isolation, financial/cash/invoice and worker regression all pass.

| BLOCKER | HIGH | MEDIUM | LOW |
|---:|---:|---:|---:|
| 0 | 0 | 2 | 0 |

The MEDIUM items concern deployment tooling and payload/secret policy, not DB invariant defects.

## 3. Ordered migrations and compatibility

| Range | Result | Notes |
|---|---|---|
| 001–010 | PASS | Original files |
| 011–020 | PASS | Original files |
| 021–030 | PASS | Original files |
| 031–040 | PASS | Original files |
| 041–051 | PASS | RC5 chain |
| 052 | PASS | Index/nullability cut-over and final stop-write contracts |

**Ordered 001–052: PASS.** The authoritative run used a newly recreated database at compatibility 120. A second clean rebuild was used after correcting audit-only test setup, so no partial test data entered the result.

Static scan found no `MERGE`, `CREATE OR ALTER`, `DROP ... IF EXISTS`, `STRING_AGG`, `JSON_VALUE`, `OPENJSON`, `IIF`, or `FORMAT`. Execution confirms SQL Server 2014 compatibility.

## 4. Postflight and corruption

- Clean immediately after migrations: exit 0, `V1 RC6 POSTFLIGHT: PASS`.
- Clean after the complete regression dataset: PASS.
- Intentional `GRANTED` SubscriptionCreditGrant without CreditLedger link: `GRANTED_WITHOUT_LEDGER = 1`, error 52100, exit 1.
- After deleting the deliberate row: PASS again.
- Other attempted corruptions, including NULL definition and mismatched type/definition writes, were rejected at write time.

## 5. RC5 finding closure

| RC5 finding | Status | New execution evidence |
|---|---|---|
| B-01 type-only/NULL-definition credit bypass | CLOSED | `sp_GrantCredit` rejects 52010; `sp_ReserveCredit` rejects 52011; all five definition columns reject NULL INSERT/UPDATE. |
| H-01 legacy PaymentRequest QR writable | CLOSED | New snapshot and historical change reject 52020; PaymentInstruction QR remains writable. |
| H-02 legacy `PartnerUsers.PartnerRole` writable | CLOSED | NULL membership succeeds; non-null insert/change reject 52021; PartnerUserRoles remains writable. |
| M-01 postflight NULL-definition coverage | CLOSED | Clean/corrupt/clean sequence behaves correctly. |
| M-02 migration hashing/history | OPEN (tooling) | Raw sqlcmd does not calculate/record hashes. |
| M-03 payload sanitization/retention | OPEN (app/ops) | Not a DB invariant defect. |

## 6. Canonical credit matrix

| Scenario | Result |
|---|---|
| Legacy grant/reserve | PASS rejection (52010/52011) |
| NULL definition INSERT/UPDATE: wallet, reservation, top-up, ledger, usage rule | PASS rejection |
| Type/definition mismatch INSERT/UPDATE: wallet, reservation, top-up | PASS rejection |
| Reservation sequential retry | PASS; one row/effect |
| Reservation concurrent retry | PASS; two sessions, one row/effect |
| Insufficient balance | PASS rejection; `XACT_STATE() = 0` |
| Consume retry | PASS; balances correct, one ledger effect |
| Consume forced failure | PASS rejection; `XACT_STATE() = 0` |
| Release retry | PASS; reserved balance restored once |
| Subscription grant sequential retry | PASS; one linked ledger row |
| Subscription grant concurrent retry | PASS; two sessions, wallet +25 once |
| Invoice finalization twice | PASS; wallet 10→9, reserved 1→0, one CONSUME row |

CreditDefinitions + PartnerCreditWallets + CreditLedger are the sole consumable-credit truth. CreditType is constrained compatibility metadata.

## 7. QR and PartnerRole stop-write

| Scenario | Result |
|---|---|
| New PaymentRequest with legacy QR fields | REJECTED (52020) |
| Change simulated historical QR snapshot | REJECTED (52020) |
| PaymentInstruction QR insert/update | PASS |
| New membership with PartnerRole NULL | PASS |
| Non-null legacy PartnerRole insert/change | REJECTED (52021) |
| PartnerUserRoles assignment | PASS |

## 8. RC5 financial/invoice regression

| Scenario | Result |
|---|---|
| 500k receivable paid 400k + 100k | PASS; PAID |
| 300k payment against 100k receivable | PASS; 200k remains |
| One 300k payment split across 100k + 200k receivables | PASS |
| OperationKey retry | PASS; one effect |
| Concurrent allocation of one transaction | PASS; one success, one 51613 |
| Concurrent settlement of one receivable | PASS; one success, one 51613 |
| Cross-Partner allocation | REJECTED 51612; `XACT_STATE() = 0` |
| Reversal | PASS; AmountPaid 0/PENDING |
| Cash +100k, retry, -50k | PASS; balance 50k, two ledger rows |
| Negative cash | REJECTED 51625; `XACT_STATE() = 0` |
| Cash with foreign transaction | REJECTED 51624; `XACT_STATE() = 0` |
| Cash-ledger UPDATE/DELETE | REJECTED |
| Valid invoice freeze | PASS |
| Invalid totals | REJECTED 51802; `XACT_STATE() = 0` |
| Frozen request/item mutation | REJECTED |
| Finalization twice | PASS exactly once |

Release builds of `TAPortal.Web` and `TAPortal.DbMcp` also pass with 0 warnings and 0 errors.

## 9. Tenant direct-DML matrix

| Relationship | Result |
|---|---|
| FinancialAccount → foreign Company | REJECTED 51810 |
| PaymentTransaction → foreign account/company | REJECTED 51811 |
| PaymentInstruction → foreign account | REJECTED 51812 |
| CustomerCashAccount → foreign customer/company | REJECTED 51813 |
| RefundRequest → foreign customer/transaction/cash/company | REJECTED 51814 |
| MatchingAttempt → foreign transaction/request/customer | REJECTED 51815 |
| PaymentReviewAction → foreign transaction | REJECTED 51816 |
| InvoiceRequest → foreign customer/company | REJECTED 51817 |
| SubscriptionCreditGrant → foreign period | REJECTED 51818 |
| PaymentAllocation → foreign request/transaction | REJECTED |
| PartnerUserRole → foreign PartnerRole | REJECTED |
| InvoiceCreditConsumption → foreign reservation | REJECTED |

## 10. Worker regression

| Scenario | Result |
|---|---|
| Inbox/Outbox concurrent claim | PASS; two sessions, one claim/effect each |
| Wrong lease owner | REJECTED for both |
| Inbox renew and complete | PASS |
| Outbox complete | PASS |
| Retry to MaxAttempts | PASS; second failure produced DEAD for both |

## 11. Canonical/legacy matrix

| Domain | Canonical truth | Legacy result |
|---|---|---|
| Consumable credit | CreditDefinitions + wallets + ledger | Type-only APIs disabled; IDs mandatory |
| Payment settlement | PaymentTransactions + allocations | BankTransactions evidence; legacy allocation disabled |
| Payment instruction | PaymentInstructions + PaymentCodeRegistry | Request code/QR snapshots read-only |
| Tenant authorization | PartnerUserRoles | PartnerUsers.PartnerRole nullable/read-only |
| Reconciliation | ReconciliationRuns | Legacy execution tables historical/read-only |
| Invoice credit | Reservation + InvoiceCreditConsumption | Exactly-once canonical consumption |

No remaining DB path was found that independently creates money, consumable credit, payment-instruction truth or tenant-authorization truth.

## 12. DB findings

| ID | Severity | Finding |
|---|---|---|
| M-01 | MEDIUM | Deployment tooling must hash files and atomically record the release manifest. |
| M-02 | MEDIUM | Payload sanitization/retention remains application/operations policy. |

DB BLOCKER = 0; DB HIGH = 0.

## 13. Functional nine-item closure

| # | Item | Status |
|---:|---|---|
| 1 | Partner-scoped RBAC | CLOSED for DB V1 |
| 2 | PaymentInstruction lifecycle | CLOSED for DB V1 |
| 3 | Generic payment allocation | CLOSED |
| 4 | Customer cash/refund | CLOSED for DB V1 |
| 5 | Company/legal ownership | CLOSED |
| 6 | Wallet sole quota + subscription grant | CLOSED |
| 7 | CreditDefinition-based wallet | CLOSED |
| 8 | Immutable invoice snapshot | CLOSED |
| 9 | Review action/disposition history | CLOSED |

Summary: **9 CLOSED for DB V1**.

## 14. Application cut-over worklist

Application remains **NOT RELEASE READY**. Current C# still contains global/non-tenant-filtered reads and legacy reads of BankTransactions, PaymentRequests.PaymentCode and CreditType. It must adopt Partner context/RBAC, PaymentTransactions and PaymentInstructions, canonical cash/credit/invoice procedures, leased workers, secret rotation, and tenant/concurrency integration tests. These are not DB HIGH findings because database invariants fail closed.

## 15. Deployment tooling

Calculate stable hashes, reject MigrationId reuse with changed content, record ID/hash/result atomically, require the full manifest before production postflight, preserve environment logs, and keep a SQL Server 2014 clean-chain/concurrency release gate.

## 16. Proposed 053+

**None.** No genuine remaining DB defect was found. Application and deployment-tooling work must not be disguised as a schema migration.

## 17. Final verdict

**READY FOR DB V1 FREEZE**

At exact HEAD `f2d56fb3657de811c6baf0a90c72db4680e9f9d9`, migrations 001–052, postflight, all canonical-credit tests and the full RC5 regression matrix satisfy the freeze rule with DB BLOCKER 0 and DB HIGH 0. Application remains **NOT RELEASE READY**. No SQL/C# source was changed during the audit.
