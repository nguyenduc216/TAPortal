# CODEX Second Audit — T.A Portal Database Schema V1 RC2

## 1. Review identification

| Item | Result |
|---|---|
| Branch | `feat/compact-dynamic-navigation` |
| Exact HEAD | `ab279d27702199279f3a64e722e4239142133693` |
| Review date | 2026-10-01 |
| Source verification | PASS: migrations `001`–`038`, `database/preflight/V1_RC2_PREFLIGHT.sql`, and `docs/DATABASE_SCHEMA_V1_RC2.md` exist |
| Change discipline | No migration or C# source was modified; this report is the only audit deliverable |
| Final verdict | **NOT READY** |

The branch was fetched and fast-forwarded from `34a7640a12df2ec234eae303f83d4261efc986d3` to the exact HEAD above. Existing local edits in `src/TAPortal.Web/appsettings.json` and existing untracked audit files were preserved.

## 2. Executive result

RC2 is not executable as a migration chain and is not ready for V1 freeze. The first fatal error is migration 014; migrations 029–038 then all contain an invalid sqlcmd batch delimiter (`USE [TAPortal]; GO` on one line). Diagnostic execution after normalizing only that delimiter found additional independent failures in 035 and 038.

The RC2 tables show the intended architecture, but most changes are additive bridges rather than a completed cut-over. Tenant consistency is enforced only for allocations, the only allocation procedure still uses `BankTransactions`, subscription grants do not post to the wallet, customer cash has no immutable/atomic ledger contract, and the application continues to use global roles and unfiltered legacy tables.

Finding counts:

| BLOCKER | HIGH | MEDIUM | LOW |
|---:|---:|---:|---:|
| 6 | 11 | 5 | 1 |

Nine functional must-change closure: **0 CLOSED, 8 PARTIAL, 1 OPEN**.

## 3. Test environment and execution evidence

| Check | Evidence | Result |
|---|---|---|
| SQL Server | Local disposable audit DB on `MINHDUC-LT\SQLSERVER2014`; `SERVERPROPERTY(ProductVersion) = 12.0.2269.0`, Enterprise Edition (64-bit) | SQL Server 2014 actually tested |
| Compatibility | Audit database `TAPortal`, compatibility level `120` | Correct target |
| Production isolation | The configured remote application server was not contacted. A new local database named `TAPortal` was used because every migration hard-codes `USE [TAPortal]`. No existing DB with that name existed on the instance. | PASS |
| Baseline 001–007 | With `sqlcmd -I`, all seven scripts completed | PASS, 24 user tables |
| Session safety | Without `-I`, migration 002 fails creating a filtered/computed-column index because `QUOTED_IDENTIFIER` is not established by the scripts | Deployment contract is underspecified |
| Upgrade 008–038 | 008–013 PASS; 014 fails `Incorrect syntax near the keyword 'MERGE'` | **FAIL** |
| Later independent execution | 015–020 PASS; 021 fails `Invalid column name 'Scope'`; 022–023 PASS; 024 fails because 021 did not create `OutboxMessages`; 025–028 PASS; every 029–038 fails at line 2 near `GO` | **FAIL** |
| Diagnostic RC2 execution | In-memory runner normalized only the same-line `GO`, without editing source. 029–034 PASS structurally; 035 fails when its `UPDATE dbo.CreditLedger` fires `tr_CreditLedger_NoUpdateDelete`; 036–037 PASS; 038 fails `Invalid column name 'PartnerId'` | Confirms two further independent upgrade defects |
| Preflight | Against the partially upgraded audit DB: SQL Server/version row printed, then `Invalid object name 'dbo.PaymentCodeRegistry'`, exit code 1 | **FAIL** |
| Build | `dotnet build TAPortal.sln --configuration Release --no-restore`; .NET SDK 8.0.425 | PASS, 0 warnings, 0 errors |
| Tests | No `*Tests.csproj` exists | Not run; no test project |

The official migration result is **FAIL**. Diagnostic execution is not a substitute for a valid deployment chain.

## 4. Mandatory syntax and migration audit

- `CREATE OR ALTER`: none remains in the migration chain — RC1 B-02 is closed.
- `MERGE`: still present in 008, 014, and 016. This contradicts the RC2 document and mandatory prompt. Migration 014 failed on the tested SQL Server.
- Reserved identifier `[Cursor]`: correctly delimited in 019 and 037 — RC1 B-03 is closed.
- Same-line `GO`: every migration 029–038 uses `USE [TAPortal]; GO`; many also use `PRINT ...; GO`. `GO` is a client batch separator and must be alone on its line.
- Object ordering: 021 references nonexistent `NumberSequences.Scope`; 024 consequently depends on an outbox table that was not created; 035 updates an immutable ledger after its trigger exists; 038 adds and references a column in the same compiled batch.
- Rerun/idempotency: a valid full rerun could not be reached. The scripts are not transactionally versioned; partial execution leaves mixed state. Several unconditional constraint additions and migration-time updates are unsafe after partial failure.
- Migration ledger: `SchemaVersions` does not register/checksum each migration; drift and partial application remain undetectable.

## 5. Findings

| ID | Severity | Exact object | Evidence and impact |
|---|---|---|---|
| B-01 | BLOCKER | migrations 029–038 | `USE [TAPortal]; GO` produces `Incorrect syntax near 'GO'` in sqlcmd. None of the RC2 migrations is deployable as committed. |
| B-02 | BLOCKER | 008, 014, 016 | `MERGE` remains despite the explicit RC2 prohibition; 014 failed at its first `MERGE` on SQL Server 12.0.2269.0. |
| B-03 | BLOCKER | 021 `CK_NumberSequences_Scope` | The script references `NumberSequences.Scope`, but baseline 004 defines `ScopeType`. SQL compilation fails and prevents `OutboxMessages` creation. |
| B-04 | BLOCKER | 035 / `tr_CreditLedger_NoUpdateDelete` | The migration updates `CreditLedger.CreditDefinitionId`; the existing immutable-ledger trigger rejects the update. Upgrade fails for any execution, including an empty ledger. |
| B-05 | BLOCKER | 038 `PaymentAllocations.PartnerId` | ADD, UPDATE, NULL check, and ALTER are in one batch. SQL Server compiles the later references before the column exists and raises `Invalid column name 'PartnerId'`. |
| B-06 | BLOCKER | `sp_AllocateBankTransaction`, `PaymentAllocations` | 034 makes `PaymentTransactionId` NOT NULL and 038 makes `PartnerId` NOT NULL, but the only allocation procedure still inserts only `(PaymentRequestId,BankTransactionId,AllocatedAmount)`. The core payment scenario becomes unusable after a successful RC2 upgrade. |
| H-01 | HIGH | Tenant-owned FK graph | Company/account/request/invoice, financial account/transaction, instruction/registry, cash/refund, matching/review, subscription grant, invoice-credit, and reconciliation FKs independently reference IDs. Only allocation receives composite tenant FKs. Cross-Partner rows remain DB-valid throughout the other aggregates. |
| H-02 | HIGH | `PartnerUserRoles` | `(PartnerId,UserId)` is membership-scoped, but `PartnerRoleId` references `PartnerRoles(Id)` alone. A membership in Partner A can be assigned a role owned by Partner B. No typed organization scope assignment is added. |
| H-03 | HIGH | Payment instruction transition | Legacy `PaymentRequests.PaymentCode`, normalized code, QR fields, aliases, and unique indexes remain active; no backfill into `PaymentInstructions`/`PaymentCodeRegistry`, no authoritative-source rule, and no auto-match guard for expired/revoked instructions exist. Two instruction/code truths remain. |
| H-04 | HIGH | Generic payment transition | `PaymentTransactions` is added and bank rows are backfilled, but ingestion, matching, views, and procedures still center `BankTransactions`. `BankTransactionId` remains mandatory in allocations. The generic core is not operational. |
| H-05 | HIGH | `CustomerCashAccounts` / `CustomerCashLedger` | Balance is mutable, ledger UPDATE/DELETE is not blocked, no atomic post/allocate/refund procedure exists, no balance invariant is enforced, and tenant consistency is absent. This is not yet a safe money ledger. |
| H-06 | HIGH | `SubscriptionCreditGrants`, wallet procedures | The grant table records intent only; no atomic/idempotent procedure posts a GRANT ledger entry and links it. Existing procedures and application still key by `CreditType`; `SubscriptionEntitlements.UsedQuantity` remains. There are multiple quota truths. |
| H-07 | HIGH | Invoice snapshot and consumption | Snapshot columns are nullable and mutable; totals are not constrained/reconciled; no submission immutability/versioning or atomic successful-issue/reserve/consume procedure exists. Cross-tenant request, reservation, and invoice links remain legal. |
| H-08 | HIGH | Inbox/outbox workers | `InboxMessages` has no MaxAttempts/bounded retry or atomic claim/complete/dead procedure. The outbox path is not installed because 021 fails; its prior lease/idempotency gaps therefore remain. |
| H-09 | HIGH | Preflight | `V1_RC2_PREFLIGHT.sql` is not a gate: problem counts are SELECTed but never THROW. Missing-object rows also do not fail. It only failed here because a referenced table did not exist. An unsafe complete schema can exit successfully. |
| H-10 | HIGH | `OperationalDb`, `PortalDb` | Operational queries have no current Partner predicate and read `BankTransactions`, request payment-code fields, and `CreditType`. Authorization reads/writes global `UserRoles`, not `PartnerUserRoles`. |
| H-11 | HIGH | `Program.cs:50` | Bootstrap still hashes the fixed privileged password `Admin@123`. RC1 B-08 remains open. |
| M-01 | MEDIUM | Provider payload persistence | Raw webhook/provider/invoice/outbox payload columns remain unrestricted. 038 clears existing `HostedLinkUrl`, but does not prevent future writes or enforce sanitization/encryption/retention. |
| M-02 | MEDIUM | Reconciliation transition | `BankSyncJobs`, `BankReconciliationRuns`, and `ReconciliationRuns` all remain writable with no compatibility-only marker or migration contract. |
| M-03 | MEDIUM | Wallet/customer cash immutability | Product `CreditLedger` has an immutability trigger, but customer cash does not; both lack a documented controlled compensation API aligned with RC2 objects. |
| M-04 | MEDIUM | Migration governance | No per-file applied-version/checksum transaction exists, so the observed partial state cannot be reliably detected or repaired automatically. |
| M-05 | MEDIUM | SQL session contract | Required SET options such as `QUOTED_IDENTIFIER ON` are not established in scripts; baseline failed under sqlcmd defaults without `-I`. |
| L-01 | LOW | Documentation/encoding | README still describes RC1/001–028 execution and several Vietnamese literals render as mojibake in the checked-out SQL, creating deployment and data-quality risk. |

## 6. Functional coverage verdict

| Domain | Verdict | Audit result |
|---|---|---|
| Tenant/RBAC | FAIL | New tables exist in design, but role ownership and most tenant-owned relationships are not enforced; application remains global/unfiltered. |
| Payment | FAIL | Default MIN logic exists only in the old bank procedure. That procedure becomes invalid under RC2 NOT NULL columns; generic allocations cannot be exercised. |
| Customer cash/refund | FAIL | Tables exist, but safe immutable/atomic monetary behavior is absent. |
| Matching/review | PARTIAL | `AMOUNT_ASSIST` CHECK and evidence/action tables are appropriate, but tenant links and application workflow are absent. |
| Subscription/credit | FAIL | CreditDefinition and grant intent exist; actual wallet grant/cut-over does not. |
| Invoice | FAIL | Independence from payment remains possible and snapshot columns were added, but legal immutability, totals and exactly-once issue/consume are not enforced. |
| Provider/security | PARTIAL | Raw-vs-normalized structures and secret references exist. Plain payload controls and future hosted-link prevention are not enforced. Provider behavior was not invented or assumed. |
| Reliability | FAIL | No complete bounded atomic worker contract; outbox migration fails. |

The required 500k/400k+100k, split payment, default MIN, excess, and reversal scenarios could not be validly executed because the committed chain does not install RC2 and its allocation procedure is incompatible with the final table contract. Static review confirms the old procedure computes MIN and checks transaction over-allocation, but this does not constitute a pass.

## 7. RC1 finding closure matrix

| RC1 finding | Status | RC2 evidence |
|---|---|---|
| B-01 MERGE execution | OPEN | MERGE remains; 014 fails. |
| B-02 CREATE OR ALTER | CLOSED | Replaced with guarded create + ALTER. |
| B-03 reserved `Cursor` | CLOSED | `[Cursor]` is delimited. |
| B-04 migration 021 | OPEN | Now fails on nonexistent `NumberSequences.Scope`. |
| B-05 nullable tenant roots | PARTIAL | 029 hardens Company/Customer, but chain cannot run and other ownership links remain weak. |
| B-06 cross-tenant FK graph | PARTIAL | Composite FKs added only for allocations. |
| B-07 application tenant filters | OPEN | Queries remain unfiltered. |
| B-08 fixed bootstrap credential | OPEN | `Admin@123` remains. |
| H-01 allocation DB enforcement | PARTIAL | Composite FKs planned, but procedure/final schema conflict and direct invariants remain. |
| H-02 reverse concurrency/cache | OPEN | Procedure unchanged. |
| H-03 wallet invariants/idempotency | PARTIAL | Some checks improved earlier; CreditType path and release/idempotency problems remain. |
| H-04 amount-only auto-match | CLOSED | `CK_PMR_AmountNoAuto` is defined in 033. |
| H-05 invoice exactly-once credit | PARTIAL | Link table added; no atomic transition. |
| H-06 worker reliability | OPEN | Inbox table only; no atomic bounded worker contract. |
| H-07 preflight | OPEN | Still observational rather than failing on nonzero counts. |
| H-08 secrets/raw/logs | PARTIAL | Existing hosted URLs are nulled; future writes/raw payloads are uncontrolled. |
| H-09 tenant RBAC | PARTIAL | Tables added; role tenant mismatch remains valid and app uses global roles. |
| H-10 code-normalization upgrade | CLOSED | 026 collision THROW now precedes index enforcement. |
| M-01 dashboard stale AmountPaid | CLOSED | RC2 version uses allocation-derived balance. |
| M-02 entitlement semantics | OPEN | `UsedQuantity` and wallet/grant overlap remain. |
| M-03 status constraints | PARTIAL | New tables are generally constrained; legacy gaps remain. |
| M-04 reverse-FK/worker indexes | PARTIAL | Some added; no workload/concurrency proof. |
| M-05 migration ledger | OPEN | No per-migration checksum ledger. |
| M-06 retention protection | OPEN | No approved purge boundary. |
| M-07 MCP SQL 2014 `STRING_AGG` | OPEN | DbMcp source still requires review/change for SQL 2014 compatibility. |
| M-08 token acquisition contract | OPEN | Lifecycle metadata exists; canonical constrained acquisition remains absent. |
| L-01 README manifest | OPEN | Not updated for RC2 029–038. |
| L-02 naming consistency | OPEN | Mixed conventions continue. |

Summary: **5 CLOSED, 8 PARTIAL, 15 OPEN**.

## 8. Nine functional must-change closure matrix

| # | Must-change | Status | Reason |
|---:|---|---|---|
| 1 | Hybrid Partner-scoped RBAC | PARTIAL | Schema added, but cross-Partner role assignment is legal and application uses global roles. |
| 2 | PaymentInstruction/code/QR separation | PARTIAL | New tables added without migration/cut-over/deprecation or safe expiry matching. |
| 3 | Generic payment allocation source | PARTIAL | Generic rows exist, but allocation procedure and mandatory bank FK remain legacy. |
| 4 | Customer cash/advance/refund | PARTIAL | Tables added without immutable atomic money contract. |
| 5 | Company/legal-entity ownership | PARTIAL | Columns added; same-Partner relationship is not enforced. |
| 6 | Wallet sole consumable quota source + grant linkage | **OPEN** | UsedQuantity remains and grants do not post/link atomically. |
| 7 | Service/CreditDefinition wallets | PARTIAL | Nullable bridge added; old CreditType constraints/procedures/application remain authoritative. |
| 8 | Complete immutable invoice snapshot | PARTIAL | Fields added but nullable/mutable and total invariants are absent. |
| 9 | Review action/disposition history | PARTIAL | Appropriate tables exist, but final chain and tenant/application contract are incomplete. |

## 9. Legacy/transition cleanup matrix

| Legacy object | Classification | Required freeze action |
|---|---|---|
| `BankTransactions` | KEEP AS EVIDENCE | Preserve bank payload/evidence, but make `PaymentTransactions` mandatory business source and rewrite ingestion/matching/allocation APIs. |
| `PaymentAllocations.BankTransactionId` | DEPRECATE | Make nullable or move to evidence link after all allocations use `PaymentTransactionId`; do not require both rails. |
| `BankSyncJobs` / `BankReconciliationRuns` | COMPATIBILITY ONLY | Stop new writes; migrate active/history references or expose read-only compatibility views over `ReconciliationRuns`. |
| `PaymentRequests.PaymentCode`, normalized/QR fields | MUST REMOVE BEFORE FREEZE as writable truth | Backfill instructions/registry, switch all writers/readers, then drop or expose derived compatibility values. |
| Payment-code aliases/rules targeting request fields | DEPRECATE | Resolve through registry/instruction with status/validity checks. |
| `CreditType` on wallets/ledger/reservations/rules/topups | MUST REMOVE BEFORE FREEZE as authoritative key | Backfill a non-null `CreditDefinitionId`, update procedures, then retain strings only as derived compatibility if required. |
| `SubscriptionEntitlements.UsedQuantity` | MUST REMOVE BEFORE FREEZE for consumables | Retain only non-consumable limit semantics or remove; wallet ledger is usage truth. |
| Global `UserRoles` | COMPATIBILITY ONLY | Keep only platform/control-plane roles; tenant authorization must use Partner membership/roles. |
| `PartnerUsers.PartnerRole` string | DEPRECATE | Backfill `PartnerUserRoles`, stop writes, then remove or derive. |
| Mutable `PaymentRequests.AmountPaid` | COMPATIBILITY ONLY | Treat allocation-derived balance as authoritative; remove writes/cache or enforce repair atomically. |

## 10. Application changes required after DB freeze

1. Introduce an authenticated current-Partner context and add mandatory Partner predicates to every tenant query; separately gate platform-support access.
2. Replace global tenant authorization reads/writes in `PortalDb` with `PartnerUsers` + `PartnerUserRoles`; reserve `UserRoles` for control-plane roles.
3. Replace fixed `Admin@123` bootstrap with a one-time secret, forced rotation and audited bootstrap completion.
4. Ingest/provider-normalize into `PaymentTransactions`; retain `BankTransactions` as linked evidence only.
5. Replace `sp_AllocateBankTransaction` calls with a generic allocation API accepting `PaymentTransactionId`, serializing both balances and supplying PartnerId atomically.
6. Create/read payment instructions and codes only through `PaymentInstructions`/`PaymentCodeRegistry`; reject inactive instructions from automatic settlement.
7. Implement matching attempts/evidence/review actions and dispositions transactionally.
8. Implement customer cash/refund through constrained ledger procedures, never through product CreditLedger.
9. Replace `CreditType` UI/service usage with `CreditDefinitionId`; implement idempotent subscription-grant posting and invoice reserve/finalize/release.
10. Snapshot and freeze invoice legal values at submission; finalize provider result and credit consumption exactly once.
11. Implement atomic inbox/outbox claim, lease renewal, completion and dead-letter flows with bounded retry.
12. Add automated integration tests for tenant crossing, concurrent allocation/reversal, wallet races, duplicate provider delivery, payment-code expiry/reuse and invoice issue retries.

## 11. Proposed corrective migrations 039+

Because 029–038 have not deployed to production, delimiter-only corrections may be made before freeze only if repository policy permits amending unshipped RC scripts. All data/contract corrections should remain explicit and reviewable:

| Migration | Purpose |
|---|---|
| 039 | Deployment-chain repair: remove/replace all MERGE, correct 021 `ScopeType`, establish required SET options, split same-batch DDL, make 029–038 valid sqlcmd scripts, and register/checksum every migration. |
| 040 | Tenant-integrity graph: add `(PartnerId,Id)` candidate keys and composite FKs/triggers for roles, company/customer/accounts, instructions, cash/refund, matching/review, grants, invoices and reconciliation. |
| 041 | Payment cut-over: backfill instruction registry; generic allocation/reversal procedures; default MIN/excess handling; drop mandatory bank source; disable unsafe legacy code/QR writes. |
| 042 | Monetary ledgers: immutable/atomic customer cash operations and refund transitions; safe CreditDefinition backfill without violating ledger immutability; non-null cut-over and subscription grant posting. |
| 043 | Invoice finalization: validate/freeze seller/buyer/item/tax/totals and atomically bind successful issue to exactly one credit consumption and lifecycle relation. |
| 044 | Reliability/security/preflight: bounded inbox/outbox claim APIs, secret-bearing payload controls, hosted-link write prevention, and a THROW-based pre/postflight including all cross-tenant and legacy-truth checks. |

Application changes are not migration substitutes and should follow the frozen DB contract.

## 12. Final verdict

**NOT READY**

The migration chain cannot upgrade the 001–007 baseline on the actually tested SQL Server 2014 instance. Preflight fails, and several functional RC2 bridges would still allow contradictory legacy sources or unsafe cross-tenant/money behavior even after syntax repair. All BLOCKER and HIGH findings must be resolved, the chain must execute cleanly and rerun safely, preflight must fail closed, and the mandatory business/concurrency scenarios must be rerun before V1 freeze.
