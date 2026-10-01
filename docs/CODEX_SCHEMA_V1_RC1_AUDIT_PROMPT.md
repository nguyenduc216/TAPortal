# CODEX AUDIT — T.A Portal Database Schema V1 RC1

You are the independent second reviewer. Do not assume the migrations are correct because they compile visually. Do not redesign the product or UI in this audit.

## Sources of truth to inspect
- database/migrations/001-create-common-schemas.sql through 028-financial-exception-audit.sql
- database/preflight/V1_SCHEMA_PREFLIGHT.sql
- docs/DATABASE_SCHEMA_V1_RC1.md
- docs/CODEX_BANKING_PAYMENT_AUDIT.md
- current C# code consuming these tables/views/procedures
- database/README.md

## Goal
Decide whether Schema V1 RC1 is internally coherent and safely upgradeable from the current T.A Portal baseline. This is an audit only. Do NOT create the final deployment SQL bundle yet.

## Mandatory database audit
1. Parse every migration in execution order. Identify syntax errors, invalid references, missing columns/tables, duplicate constraints/default names and non-idempotent rerun hazards.
2. Verify upgrade path from the existing production-style baseline, not only a clean database.
3. Verify every FK has compatible type and intended delete behavior.
4. Verify tenant isolation: PartnerId ownership across Company, Customer, provider, banking, payment, wallet, invoice and outbox records. Flag any cross-Partner path.
5. Verify uniqueness/idempotency for provider transaction, webhook retry, payment code, invoice Ikey, provider operation, credit operation and outbox processing.
6. Verify concurrency for sp_AllocateBankTransaction, sp_ReversePaymentAllocation, wallet grant/reserve/consume/release and worker leases. Look for race conditions, deadlocks and lost updates.
7. Verify financial invariants: no over-allocation, no negative available credit, immutable CreditLedger, reversal/compensation rather than delete, partial/multiple payment correctness.
8. Verify status machines and CHECK constraints agree across tables/views/procedures/application code.
9. Verify payment cache AmountPaid cannot silently diverge from allocation source of truth; recommend the smallest correction if needed.
10. Verify SePay sandbox/production persistence model, token/link lifecycle, webhook-to-transaction many-to-one history, reconciliation convergence and sanitized logs.
11. Verify invoice lifecycle CREATE/ISSUE/ADJUST/REPLACE/CANCEL and EasyInvoice Ikey/idempotency semantics without inventing undocumented provider behavior.
12. Verify indexes for expected hot paths and flag likely scans/duplicate indexes.
13. Verify PII/secrets: no plaintext provider secret/access/link token or Authorization header persistence. RawPayload/log policy must be reviewed for sensitive data.
14. Verify retention tables do not authorize deletion of financial/legal records in V1.
15. Execute/compile migrations on a disposable SQL Server database if the environment allows. Run V1_SCHEMA_PREFLIGHT.sql after upgrade.
16. Build the .NET solution and run all available tests. Identify application code that contradicts DB contracts.

## Required scenario tests
- duplicate IPN delivery -> multiple BankWebhookEvents, one BankTransaction
- IPN then reconciliation for same external transaction -> one BankTransaction
- 500k receivable paid 400k then 100k -> PARTIALLY_PAID then PAID
- same PaymentCode reused after PAID/EXPIRED/CANCELLED -> rejected
- overpayment and unallocated excess behavior
- reversed allocation restores settlement correctly
- two concurrent allocations cannot exceed transaction amount
- cross-Partner allocation rejected
- unknown/ambiguous payment -> review queue, not auto-allocation by amount
- credit reserve concurrency and insufficient credit
- duplicate credit idempotency key
- failed invoice issue does not consume final invoice credit
- successful invoice issue consumes credit exactly once
- provider retry/dead-letter behavior
- expired/revoked/consumed link token behavior

## Severity
BLOCKER = migration/data corruption/security/tenant isolation/financial integrity/deployment failure.
HIGH = likely incorrect business state, concurrency/idempotency defect or major operational risk.
MEDIUM = maintainability/performance/audit weakness with workaround.
LOW = naming/documentation/minor optimization.

## Output
Create docs/audits/CODEX_SCHEMA_V1_RC1_AUDIT.md containing:
- audited branch + exact HEAD SHA
- environment/SQL Server/.NET versions used
- migration compile/execution result
- build/test result
- findings table: ID, severity, migration/object, evidence, impact, recommended fix
- tenant/security findings
- banking/payment/wallet findings
- invoice findings
- index/performance findings
- clean-install vs upgrade-path findings
- unresolved provider assumptions
- final gate: PASS, PASS WITH FIXES, or FAIL

Do not modify migrations 001-028 during the first audit pass. First produce the report. If fixes are required, propose them as 029+ corrective migrations so the review trail remains visible. Do not merge to main.
