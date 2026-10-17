# CODEX Schema V1 RC1 Audit

> Previous audit: **INVALID — WRONG GIT REF**
> Audited branch: **`feat/compact-dynamic-navigation`**
> Audited HEAD: **`34a7640a12df2ec234eae303f83d4261efc986d3`**

## 1. Audit Identification

| Item | Result |
|---|---|
| Repository | `nguyenduc216/TAPortal` |
| Branch | `feat/compact-dynamic-navigation` |
| Exact HEAD | `34a7640a12df2ec234eae303f83d4261efc986d3` |
| Audit date | 2026-10-01 (Asia/Bangkok) |
| RC1 artifacts | Migrations 008–028: **YES**; preflight: **YES**; three required RC1 documents: **YES** |
| Difference from main | All 21 migrations 008–028 appear in `git diff main...HEAD` |
| Working tree at start | Pre-existing local modification in `src/TAPortal.Web/appsettings.json`; previous invalid audit report was untracked |
| SQL Server tested | 12.0.2269.0 Enterprise (SQL Server 2014), compatibility 120; 15.0.2190.7 Developer (diagnostic continuation) |
| .NET | SDK 8.0.425; runtime 8.0.31; projects target `net8.0` |
| Audit mode | First-pass only; migrations 001–028 and application source were not modified |

The review was restarted from zero on the correct ref. Conclusions from the invalid main-branch audit were not reused.

## 2. Executive Summary

**Overall result: FAIL**

**V1 freeze recommendation: NOT READY.**

The RC1 branch contains the intended domains and is substantially more complete than main, but the migration chain is not deployable as committed:

1. the documented SQL Server 2014 target cannot parse `CREATE OR ALTER` used from migration 015 onward;
2. the baseline seed fails under the tested SQLCMD/SQL Server execution path because top-level `MERGE` is not prefixed with `;`;
3. migration 019 fails on reserved identifier `Cursor`;
4. migration 021 references nonexistent `DataScopes.IsDeleted`, looks for the wrong CHECK-constraint name, and cannot introduce `PARTNER` scope;
5. tenant ownership remains nullable/inconsistent and the application exposes unfiltered cross-partner lists;
6. critical financial invariants rely on callers using procedures and can be bypassed by direct inserts/updates.

Build succeeds, but there is no test project. None of the mandatory concurrent/idempotency/provider scenarios is dynamically proven.

| BLOCKER | HIGH | MEDIUM | LOW |
|---:|---:|---:|---:|
| 8 | 10 | 8 | 2 |

## 3. Functional Coverage Matrix

| Functional Area | Main objects | Coverage | Material gap/risk |
|---|---|---|---|
| Partner/Tenant | `Partners`, `PartnerUsers` | Partial | Company/customer ownership nullable; global RBAC; no tenant-consistent composite FKs |
| Organization | Companies/branches/teams | Partial | Child hierarchy does not carry/enforce PartnerId; old global code uniqueness remains |
| Customer | `Customers`, bank identity, invoice profile/policy | Partial | Cross-partner FK combinations are legal |
| Provider/SePay | Providers, connections, environments, token/link sessions, webhook config/log | Partial | Secret-bearing hosted URL/raw payload risk; no tenant-consistent relationship enforcement |
| Bank Account | `PartnerBankAccounts` | Partial | Partner/provider/connection consistency not enforced |
| Webhook/Transaction | events, event links, transactions | Good foundation | No executable ingestion contract/tests; sensitive raw payload policy weak |
| Reconciliation | jobs/runs | Partial | Runs do not link results to transactions; convergence depends on application upsert |
| Payment Request | requests, normalized permanent codes, QR fields/events | Broad | Upgrade collision gate missing; QR validity window incomplete |
| Allocation | ledger, procedures, balance views | Partial | Direct DML bypasses tenant/over-allocation rules; reversal/cache race |
| Matching/Review | matches, rules, review queue | Partial | DB permits amount-only rule with `AutoAllocate=1` |
| Package/Subscription | catalog, subscriptions, entitlements, overrides, top-ups | Partial | Weak lifecycle constraints and view semantics |
| Wallet/Credit | wallets, ledger, reservations, usage rules, procedures | Partial | Missing balance CHECKs; idempotency scope defects; direct DML bypass |
| Invoice/EasyInvoice | requests, snapshots, invoices/items/relations/operations/files/logs | Partial | No DB-enforced credit-to-success exactly-once contract; cross-partner paths |
| Outbox/Retry | outbox, leases, attempts, dead letters | Partial | No unique message idempotency key, maximum attempts, or lease procedures |
| Audit/Retention | events/logs/exceptions/policies | Partial | Policies are descriptive, not enforced; payload sanitation depends on caller |
| Reporting | operational views | Partial | Banking dashboard trusts mutable `AmountPaid` cache |

## 4. Migration Audit (001–028)

“Execution” reports the unmodified chain unless explicitly marked diagnostic. The harness changed only `USE [TAPortal]` in temporary copies to route to isolated audit databases; no repository file was edited.

| Migration | Purpose | Static/Execution result | Upgrade/idempotency finding |
|---|---|---|---|
| 001 | Legacy schema transfer | Executed | Source/destination collision is silently left unresolved |
| 002 | Global auth/RBAC | Executed with `QUOTED_IDENTIFIER ON` | Whole-table guard does not repair partial legacy tables |
| 003 | Organization | Executed | Global model; whole-table upgrade guard |
| 004 | Menu/settings/sequences | Executed | Polymorphic scopes lack referential ownership |
| 005 | Data scope | Executed | Creates `CK_DataScopes_Type`; 021 expects a different name |
| 006 | Audit/logging | Executed | No tenant context or enforced sanitation/retention |
| 007 | Customer | Executed | Global customer uniqueness and no PartnerId until 009 |
| 008 | Navigation hierarchy | Diagnostic execution passed | `MERGE`/seed dependency; assumes baseline portal seeds |
| 009 | Partner foundation/backfill | Diagnostic execution passed | Backfills but leaves Company/Customer PartnerId nullable |
| 010 | Provider/billing/credit foundation | Diagnostic execution passed | Financial tables lack several invariant CHECKs |
| 011 | Banking core | Diagnostic execution passed | Tenant/provider/account relationships are independent FKs |
| 012 | Payment/allocation/matching | Diagnostic execution passed | Direct allocations can violate tenant/amount invariants |
| 013 | Invoice foundation | Diagnostic execution passed | Cross-partner request/customer/payment/provider paths legal |
| 014 | Navigation permissions | Diagnostic execution passed | Permissions remain global, not partner-scoped |
| 015 | Webhook links/payment view | **Fails SQL Server 2014** | `CREATE OR ALTER VIEW` unsupported on declared minimum version |
| 016 | SePay environments/logs | Diagnostic execution passed on SQL 2019 | Config/log sanitation is convention only |
| 017 | Allocation/reversal procedures | Diagnostic execution passed on SQL 2019 | SQL 2014 incompatible; reverse/allocation cache race remains |
| 018 | Tokens/link/wallet rules | Diagnostic execution passed on SQL 2019 | Hosted URL may persist link secret; no token-active computed enforcement |
| 019 | Banking/payment hardening | **Fails SQL 2019** | Unquoted reserved column `Cursor`; migration stops |
| 020 | Wallet procedures/immutability | Passed only after diagnostic correction to 019 | Idempotency and invariant gaps remain |
| 021 | Tenant/outbox hardening | **Fails SQL 2019** | Invalid `IsDeleted`; wrong constraint name; `PARTNER` insertion conflicts with old CHECK |
| 022 | Subscription lifecycle | Passed only after diagnostic corrections | Status/date/quantity lifecycle weakly constrained |
| 023 | Invoice profiles/events | Passed only after diagnostic corrections | Cross-partner profile/customer/request combinations legal |
| 024 | Retry/dead letter | Passed only after diagnostic corrections | No bounded-retry or lease-claim procedure |
| 025 | Operational views | Passed only after diagnostic corrections | Dashboard reads mutable payment cache |
| 026 | Payment-code matching | Passed only after diagnostic corrections | Normalization may expose duplicates only when unique index is created |
| 027 | Invoice lifecycle/provider ops | Passed only after diagnostic corrections | Exactly-once credit consumption not enforced |
| 028 | Exceptions/retention/version | Passed only after diagnostic corrections | Retention policy is metadata only; version ledger records RC1, not each migration |

Raw clean-install chain result: **FAILED**. Upgrade execution against a production-style copy was not safe to attempt after deterministic clean-chain failures. The diagnostic SQL 2019 run reached 028 only after temporary fixes for top-level `MERGE`, `[Cursor]`, and migration 021; those fixes were not applied to source.

## 5. Findings

| ID | Severity | Object | Evidence | Impact | Recommended correction |
|---|---|---|---|---|---|
| B-01 | BLOCKER | Seeds 001/002 and MERGE migrations | SQL Server execution reports `Incorrect syntax near MERGE`; top-level statements lack leading `;` | Documented chain stops before RC1 | 029: replace risky MERGE or terminate preceding statement explicitly; add sqlcmd-based install test |
| B-02 | BLOCKER | 015, 017–020, 022, 025 | `CREATE OR ALTER` is unsupported by SQL Server 2014 | Declared supported server cannot deploy V1 | Decide supported minimum; either raise it and enforce preflight or use 2014-compatible guarded batches |
| B-03 | BLOCKER | 019 `BankReconciliationRuns` | SQL Server 2019 reports syntax error near `Cursor` | Migration 019 cannot compile | 029: rename or delimit `[Cursor]`; test clean and upgrade execution |
| B-04 | BLOCKER | 021 `DataScopes` | Table has no `IsDeleted`; migration checks `CK_DataScopes_ScopeType` but 005 created `CK_DataScopes_Type` | Migration 021 always fails; PARTNER data scope is not installed | 029: correct column/constraint handling with data validation and idempotent upgrade logic |
| B-05 | BLOCKER | Companies/Customers | 009 adds nullable PartnerId, backfills once, never changes to NOT NULL | New orphan tenant records remain legal | 029: preflight/backfill, make NOT NULL, add tenant-scoped uniqueness |
| B-06 | BLOCKER | Tenant-owned FK graph | Customer/payment/bank/provider/invoice relationships use independent ID FKs without `(PartnerId, Id)` consistency | Cross-partner allocation/profile/invoice/account/provider linkage is DB-valid | 030: add alternate keys and composite FKs or equivalent enforced ownership constraints |
| B-07 | BLOCKER | `OperationalDb`, `PortalDb` | All partners/accounts/transactions/payments/invoices/wallets/customers are queried without current PartnerId | Any user with global permission can read every tenant’s data | Introduce current-partner context and mandatory tenant predicates; separate platform-admin routes |
| B-08 | BLOCKER | Admin bootstrap | `Admin@123` is hard-coded in `Program.cs` and a known hash/password is in seed 003 | Predictable privileged credential if bootstrap/seed is used | Remove fixed credential; inject one-time secret, force rotation, audit bootstrap |
| H-01 | HIGH | `PaymentAllocations` | No PartnerId/composite FK/trigger or aggregate constraint; procedure checks are bypassable by direct insert | Cross-tenant and over-allocation possible outside procedure | Enforce invariants at DB boundary; revoke direct DML and/or add constrained ledger API/trigger |
| H-02 | HIGH | `sp_ReversePaymentAllocation` / `AmountPaid` | Reverse does not serialize on request like allocate; both recompute/write cache | Concurrent reverse/allocate can leave cache/status stale | Lock request and transaction consistently; make derived view authoritative and add repair/check |
| H-03 | HIGH | Wallet/credit procedures | No wallet CHECKs; release clamps corruption; consume idempotency fallback is not partner-scoped and nullable keys are allowed | Silent success, negative/inconsistent balance, double consume risk | Add invariant CHECKs, required scoped keys, strict reservation transition and compensation |
| H-04 | HIGH | `PaymentMatchingRules` | `AMOUNT_ASSIST` rows can set `AutoAllocate=1` | Amount-only auto-allocation is schema-valid | CHECK forbidding auto allocation for `AMOUNT_ASSIST`; enforce matching evidence |
| H-05 | HIGH | Invoice/credit | Provider operation, invoice success, reservation and consume ledger are not linked by an enforced idempotent transition | Retry can issue/consume inconsistently | Add atomic finalization procedure and unique credit-consumption key tied to successful ISSUE |
| H-06 | HIGH | Outbox/retry/dead letter | Attempts/lease columns exist, but no MaxAttempts CHECK/claim procedure/message idempotency uniqueness | Infinite retry, duplicate side effects and lease races remain possible | Add bounded policy, atomic lease/complete/dead procedures and unique event key |
| H-07 | HIGH | Preflight | Script only selects counts, returns success when counts are nonzero, and omits major checks | Unsafe database can pass automation | THROW on any problem and add ownership/idempotency/invariant checks |
| H-08 | HIGH | Token/raw/log persistence | `HostedLinkUrl`, webhook raw payload, invoice integration payloads and outbox payload lack enforced redaction/encryption | Link tokens, authorization data or PII can persist | Do not store secret-bearing URL; sanitize/encrypt raw evidence with access/retention controls |
| H-09 | HIGH | RBAC | Roles/grants are global; PartnerUsers role is a separate string | Partner-specific roles and scoped administration cannot be safely expressed | Add partner role/grant model and membership-aware authorization |
| H-10 | HIGH | 026 upgrade | Raw codes can normalize to the same value; no collision gate before NOT NULL/unique index | Upgrade can fail midway | Add duplicate-normalization preflight, remediation workflow and transaction |
| M-01 | MEDIUM | `vw_PartnerBankingDashboard` | Outstanding amount uses mutable `PaymentRequests.AmountPaid` | Dashboard can report stale money | Aggregate from active allocations/balance view |
| M-02 | MEDIUM | Subscription/entitlement | Weak status/date/used-quantity checks; overrides absent from entitlement view | Incorrect entitlement decisions | Add lifecycle checks and define override precedence |
| M-03 | MEDIUM | Status columns | Several statuses/provider types lack CHECK constraints | Vocabulary drift | Add explicit checks/reference tables |
| M-04 | MEDIUM | Indexes | Missing reverse-FK/worker indexes in junction/event/relation paths | Scans/blocking at scale | Add workload-backed indexes |
| M-05 | MEDIUM | Migration governance | `SchemaVersions` appears only in 028 and does not checksum 001–028 | Partial migration/drift not reliably detected | Per-migration immutable ledger/checksum |
| M-06 | MEDIUM | Retention | Policies do not enforce deletion restrictions | Cleanup code can delete evidence | Restrict permissions and use approved purge procedures |
| M-07 | MEDIUM | MCP SQL compatibility | C# uses `STRING_AGG`, unavailable on SQL Server 2014 | Inspection contradicts minimum server | Raise minimum or use compatible aggregation |
| M-08 | MEDIUM | Link/token lifecycle | Status/expiry exist but no canonical constrained acquisition API | Expired/revoked token may be selected | Add active-session view/procedure and suitable uniqueness |
| L-01 | LOW | README | Section titled “008–015” lists through 028 | Operational confusion | Rename and publish exact manifest |
| L-02 | LOW | Naming | Mixed generic constraint/default and code naming | Maintenance friction | Standardize prospectively |

## 6. Tenant Isolation Audit

Tenant isolation is **not safe for V1 freeze**.

- `PartnerUsers` establishes membership, but application authorization never selects a current partner or checks membership.
- `Companies.PartnerId` and `Customers.PartnerId` remain nullable.
- Global unique indexes on company/customer codes remain from migrations 003/007.
- Tenant-owned rows commonly contain both PartnerId and an object ID, but separate FKs permit mismatched partners. Affected paths include bank account/provider, payment request/customer/bank account, customer bank identity/customer, invoice request/customer/payment/profile, invoice/request/provider, invoice relations, and provider operations.
- Allocation procedure checks partner equality, but direct `PaymentAllocations` DML does not.
- Every operational application query is cross-tenant. Controller permissions are global claims, not tenant membership checks.

## 7. Banking Audit

Provider-neutral modeling, environment separation, token metadata, webhook history and normalized transaction uniqueness are present. `(ProviderId, ExternalTransactionId)` can converge duplicate IPN/reconciliation candidates when ingestion performs an atomic upsert. Multiple webhook deliveries can link to one transaction.

However, there is no ingestion/reconciliation implementation or test. `BankReconciliationRuns.Cursor` prevents migration 019. Partner/provider/account consistency is not enforced. `HostedLinkUrl` may contain the link token that the design says must not be persisted. Raw webhook payload is mandatory but has no field-level encryption/redaction/access policy.

## 8. Payment/Reconciliation Audit

The active-allocation views correctly derive paid/remaining/overpaid values and preserve partial/multiple payments. The allocation procedure uses serializable locks and rejects debit, cross-partner and transaction over-allocation.

Those guarantees are not table-level: direct inserts bypass them. The reversal procedure uses a different locking strategy and can race cache recomputation. Payment-code normalized uniqueness prevents reuse once installed, but upgrade can fail on normalization collisions. Expired/cancelled requests are rejected by the procedure, while review routing remains an application obligation. The schema does not prohibit amount-only auto allocation.

## 9. Wallet/Credit Audit

The immutable-ledger trigger guards against UPDATE/DELETE, and wallet/reservation procedures take update/hold locks. Still:

- wallet balances have no `CurrentBalance >= 0`, `ReservedBalance >= 0`, `ReservedBalance <= CurrentBalance` CHECK;
- direct ledger inserts and wallet updates remain possible;
- consume idempotency fallback searches only by key, not partner/type/reservation;
- nullable idempotency permits non-idempotent consume calls;
- release clamps inconsistent reserved balance to zero rather than raising/recording an exception;
- invoice issue success and final credit consumption are not one enforced transition.

## 10. Invoice Audit

The schema covers profiles/snapshots, request events, invoice/items/files, relations and provider operations with a unique scoped idempotency key. Original invoice rows are retained for adjustment/replacement/cancellation.

Cross-partner relationships remain possible through independent FKs. Integration payloads are not structurally sanitized. There is no atomic procedure tying successful ISSUE, invoice creation, provider-operation completion, reservation consumption and outbox completion together; failed-issue and retry-after-success scenarios remain unproven.

## 11. Security Audit

- Fixed administrator credential is a release blocker.
- Application routes use permissions, but permissions are global and queries do not filter tenant.
- `CustomersController` requires authentication only, not `CRM.CUSTOMERS.VIEW`.
- Claims are cached in a sliding eight-hour cookie, so grant revocation is not immediate.
- Provider/raw/invoice/outbox payload columns depend entirely on caller sanitation.
- The pre-existing local appsettings edit contains configured connection information; its value was not copied into this report.

## 12. Concurrency and Idempotency Audit

Allocation uses strong locking, but direct DML and reversal/cache races undermine the contract. Credit reservation locking is directionally sound, but idempotency scope and invariant constraints are insufficient. Outbox/webhook tables have lease columns but no atomic acquisition procedures or bounded retry rule. Provider-operation uniqueness is useful, while completion/credit finalization is not atomic. Mandatory two-worker scenarios have no automated coverage.

## 13. Index/Performance Audit

Important hot-path indexes exist for transactions, payment code, allocations, wallet ledger, queues and provider operations. Gaps remain around reverse FK lookups and tenant/time queries. Reporting avoids obvious 1:N multiplication in the banking dashboard through `OUTER APPLY`, but reads the mutable payment cache. `vw_InvoiceOperations` can return multiple invoices per request and must be treated as 1:N.

## 14. Application Contract Audit

New controllers use permission policies, but `OperationalDb` has no tenant parameter and returns all records. `Partners()` exposes the full catalog to any holder of the global permission. Existing users/roles/customers/dashboard queries remain global. No C# provider adapter, webhook ingestion, reconciliation worker, allocation caller, wallet caller, invoice worker, outbox dispatcher or dead-letter processor exists, so intended contracts are not consumed or tested.

No direct financial hard-delete was found in current C#; absence of implementation is not evidence that future workers are safe.

## 15. Build/Test and Database Execution Results

| Check | Result |
|---|---|
| `dotnet restore TAPortal.sln` | PASS, exit 0 |
| `dotnet build TAPortal.sln --no-restore` | PASS, **0 errors, 0 warnings** |
| Test projects | **NO TEST PROJECT** |
| Tests | Passed 0 / Failed 0 / Skipped 0; none discovered |
| SQL Server 2014 raw chain | FAIL: seed 001 `MERGE`; diagnostic continuation then FAIL at 015 `CREATE OR ALTER` |
| SQL Server 2019 diagnostic chain | FAIL at 019 `[Cursor]`, then 021 `IsDeleted`; reached 028 only after temporary corrections |
| Preflight | Exit 0 only on corrected empty clean-install diagnostic DB; not a pass for committed chain or upgrade |
| Cleanup | Both disposable audit databases dropped; no production DB touched |

## 16. Preflight Assessment

The preflight includes the six minimum requested count queries, but is insufficient as a release gate:

- it does not THROW/return failure when `ProblemCount > 0`;
- orphan checks ignore `PartnerId IS NULL`;
- duplicate payment-code check cannot run before 026 creates/backfills `PaymentCodeNormalized`;
- it omits cross-partner relationships, duplicate external transactions, invoice/provider idempotency, ledger-wallet reconciliation, reservation totals, expired leases, dead-letter bounds and constraint/index presence;
- object inventory prints rows but does not fail for missing objects.

## 17. Required Scenario Assessment

| Scenario | Static capability | Dynamic proof |
|---|---|---|
| Duplicate IPN / IPN+reconciliation | Unique provider external transaction + event links | Not tested |
| 400k + 100k payment | Supported by allocation ledger/view | Not tested |
| Payment-code reuse | Normalized unique index after 026 | Chain fails before reliable install |
| Overpayment/excess | Request overpayment allowed; transaction total capped | Not tested; excess policy incomplete |
| Reversal | Supported, cache race exists | Not tested |
| Concurrent/cross-partner allocation | Procedure guards; direct DML bypass | Not tested |
| Unknown/ambiguous/amount-only | Review queue exists; amount-only prohibition absent | Not tested |
| Credit concurrency/idempotency | Partial support; defects noted | Not tested |
| Invoice failure/success/retry credit | Not atomically enforced | Not tested |
| Retry/dead-letter | Tables only; bounded worker absent | Not tested |
| Expired/revoked/consumed link token | States exist; canonical enforcement absent | Not tested |

## 18. Proposed 029+ Corrective Migrations (Proposal Only)

No corrective migration was implemented.

| Migration | Purpose | Principal objects | Why required |
|---|---|---|---|
| 029-rc1-deployment-repair | Fix MERGE execution, `[Cursor]`, DataScopes handling; declare/enforce SQL minimum | seeds/compatibility, reconciliation, DataScopes | Current chain does not deploy |
| 030-tenant-integrity-hardening | NOT NULL tenant roots, tenant uniqueness and composite ownership FKs | organization/customer/provider/banking/payment/invoice/outbox | Cross-tenant relationships are legal |
| 031-financial-ledger-invariants | Enforce allocation/wallet constraints and repair locking/idempotency/cache | requests, allocations, wallets, ledger, reservations/procedures | Financial integrity/concurrency |
| 032-matching-payment-code-upgrade | Detect normalization collisions and forbid amount-only auto allocation | requests/aliases/rules/matches | Upgrade/matching safety |
| 033-invoice-credit-finalization | Atomic exactly-once provider success/invoice/credit/outbox transition | invoice operations, reservations, ledger, outbox | Duplicate issue/consume risk |
| 034-worker-retry-security | Atomic leases, max attempts, dead letters, outbox idempotency, payload controls | webhook/outbox/log/dead-letter/token link | Side effects and secret exposure |
| 035-preflight-reporting-governance | Failing preflight, migration checksums, cache-free reporting, retention enforcement | preflight, versions, views, retention | Release evidence/operations |
| 036-partner-rbac-lifecycle | Partner roles/grants and subscription/token lifecycle constraints | RBAC, memberships, subscriptions, tokens | Authorization/lifecycle correctness |

Application tenant filtering and bootstrap credential removal also require source changes after audit review; they cannot be solved solely by migration.

## 19. V1 Freeze Recommendation

**NOT READY**

Eight BLOCKER and ten HIGH findings prevent freeze. After corrective migrations are reviewed, rerun raw clean install on the supported SQL Server, upgrade from a representative 001–007 copy with collision/orphan fixtures, failing pre/postflight, mandatory concurrent scenarios, and tenant-filtered application integration tests.

### First-pass change confirmation

No migration 001–028 was modified. No migration 029+ was created. No application source was modified, no merge/push occurred, and no production database was touched. This report replaces the invalid wrong-ref report and is the only audit-created artifact. The pre-existing local `appsettings.json` modification remains untouched.
