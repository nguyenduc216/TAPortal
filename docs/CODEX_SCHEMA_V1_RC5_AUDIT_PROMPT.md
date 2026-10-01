# CODEX DATABASE V1 RC5 FINAL-FREEZE AUDIT

Audit ONLY branch `feat/compact-dynamic-navigation` at latest remote HEAD. Fetch/pull first and record exact SHA.

This pass verifies the RC4 findings and RC5 fixes. Do not modify SQL/C# during audit. No merge, no production deployment.

## 1. Ordered deployment is the first gate
On disposable SQL Server 2014 12.x, compatibility 120, execute ORIGINAL committed migrations 001-051 in exact order with sqlcmd -b -I. Do not normalize files in memory. If any file fails, DB verdict cannot be READY FOR FREEZE.

Explicitly confirm:
- amended, unshipped 042 no longer references PartnerCreditWallets.UpdatedAt;
- 047 completes despite immutable CustomerCashLedger trigger;
- 049-051 compile and execute.

## 2. Run V1_RC5_POSTFLIGHT
Run `database/preflight/V1_RC5_POSTFLIGHT.sql` only after 001-051 succeed. It must PASS clean schema and FAIL after intentional invariant corruption where corruption is not already rejected at write time.

## 3. Re-test RC4 four blockers
B-01 ordered chain 042.
B-02 047 immutable-ledger migration.
B-03 cross-Partner financial/legal ownership.
B-04 consumable quota dual truth.

Each must be CLOSED/PARTIAL/OPEN with execution evidence.

## 4. Mandatory tenant direct-DML tests
Attempt cross-Partner writes for:
FinancialAccount→Company;
PaymentTransaction→FinancialAccount/Company;
PaymentInstruction→PaymentRequest/FinancialAccount;
CustomerCashAccount→Customer/Company;
RefundRequest→Customer/PaymentTransaction/CashAccount/Company;
MatchingAttempt→PaymentTransaction/request/customer;
PaymentReviewAction→PaymentTransaction;
InvoiceRequest→Customer/Company;
SubscriptionCreditGrant→SubscriptionPeriod.
All ownership-corrupting writes must be rejected.

## 5. Mandatory financial scenarios
Re-run:
- receivable 500k paid by 400k + 100k;
- 300k payment against 100k remaining allocates 100k only;
- split one payment across receivables;
- concurrent transaction/receivable allocation cannot overallocate;
- OperationKey retry exactly once;
- reversal recomputes state;
- customer cash +100k/-50k, negative reject, immutable ledger, idempotency, cross-tenant reject;
- subscription grant sequential + concurrent exactly once;
- invoice valid freeze; invalid totals reject AND caller XACT_STATE must return 0;
- frozen invoice request/items reject mutation;
- invoice credit finalization twice consumes reservation/wallet/ledger exactly once;
- Inbox/Outbox concurrent claim + ownership + retry/dead.

## 6. Canonical source-of-truth tests
Verify:
PaymentInstructions/PaymentCodeRegistry are canonical. New PaymentRequest can be created without legacy PaymentCode. Attempts to insert/change legacy request code after RC5 are rejected. Legacy unique indexes do not block normal receivables.

Verify consumable quota:
CreditDefinitionId + PartnerCreditWallet + CreditLedger are canonical.
SubscriptionEntitlements.UsedQuantity cannot be changed.
vw_PartnerServiceEntitlements must not present UsedQuantity as an independent mutable truth.
Legacy CreditType must not be able to select a CreditDefinition inconsistent with canonical ID.
Test Wallet, Reservation and Topup consistency.

Verify reconciliation:
ReconciliationRuns is canonical.
BankSyncJobs and BankReconciliationRuns, if present, reject new writes but preserve existing history.

## 7. Procedure transaction safety
Force validation failures inside:
sp_AllocatePaymentTransaction
sp_ReversePaymentAllocationV2
sp_PostCustomerCash
sp_GrantSubscriptionCredit
sp_FreezeInvoiceRequest
sp_FinalizeIssuedInvoiceCredit
After caught error, caller XACT_STATE must be 0. Report any -1 as HIGH/BLOCKER depending financial effect.

## 8. SQL Server 2014 scan
Scan 001-051 for MERGE, CREATE OR ALTER, same-line GO, JSON_VALUE/OPENJSON/STRING_AGG/DROP IF EXISTS and unsupported syntax. Also execute, do not rely only on grep.

## 9. Legacy matrix
Classify all old paths CANONICAL / EVIDENCE / COMPATIBILITY READ-ONLY / DISABLED / MUST REMOVE. At minimum:
BankTransactions, PaymentAllocations.BankTransactionId, sp_AllocateBankTransaction, PaymentRequests.PaymentCode/PaymentCodeNormalized, PaymentCodeAliases, CreditType, SubscriptionEntitlements.UsedQuantity, PartnerUsers.PartnerRole, BankSyncJobs, BankReconciliationRuns.

A legacy column may remain for historical compatibility; it is a DB blocker only if it can still create an independent conflicting truth.

## 10. Separate DB freeze from application release
Inspect C# only to produce application cut-over worklist. Current application may remain NOT RELEASE READY without blocking DB freeze if DB contract is sound. Include tenant context, Partner RBAC, generic payments, PaymentInstructions, customer cash/refunds, CreditDefinition wallet path, invoice freeze/finalization, workers, bootstrap secret removal, integration tests.

## 11. Severity and freeze rule
DB BLOCKER: ordered chain fail, cross-tenant financial corruption, double money/credit, unresolved dual source-of-truth, mandatory SQL incompatibility.
DB HIGH: correctness/security defect that should block freeze.
Do not classify missing application adoption as DB HIGH.

READY FOR DB V1 FREEZE requires:
001-051 PASS; RC5 postflight PASS; DB BLOCKER=0; DB HIGH=0; mandatory financial/tenant scenarios PASS; four RC4 blockers CLOSED.

## 12. Output
Create `docs/audits/CODEX_SCHEMA_V1_RC5_AUDIT.md`.

Include:
- exact HEAD/environment;
- 001-051 execution matrix;
- postflight result;
- RC4 blocker closure matrix;
- DB findings counts;
- mandatory scenario matrix;
- tenant matrix;
- source-of-truth/legacy matrix;
- 9 functional must-change CLOSED/PARTIAL/OPEN;
- application-only worklist;
- deployment-tooling worklist;
- proposed 052+ only if a genuine DB defect remains;
- final verdict: READY FOR DB V1 FREEZE / READY AFTER DB FIXES / NOT READY.

Final chat response: exact HEAD, migrations result, postflight, DB BLOCKER/HIGH counts, functional closure, DB verdict, application readiness, proposed 052+ and report path. Confirm no source changed.