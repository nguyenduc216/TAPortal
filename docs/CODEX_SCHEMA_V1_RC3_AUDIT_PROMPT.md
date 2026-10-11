# CODEX THIRD AUDIT — T.A Portal Database V1 RC3

Audit branch `feat/compact-dynamic-navigation`. Fetch latest HEAD and record it. Do not audit an older SHA.

This pass follows RC2 audit report (6 BLOCKER / 11 HIGH). Database owner has amended unshipped RC scripts and added corrective migrations 039-044. **Audit only first; do not modify SQL or C# during this pass.**

## Mandatory execution
1. Create disposable SQL Server 2014 (12.x), compatibility 120 database named TAPortal.
2. Execute 001-044 in exact order with sqlcmd-compatible batching. Do not normalize source in memory.
3. Run `database/preflight/V1_RC3_POSTFLIGHT.sql`. It must exit non-zero on invariant failure.
4. Execute 001-044 a second time only where scripts claim rerunnable; document any partial/rerun defect.
5. Search entire migration chain: MERGE, CREATE OR ALTER, same-line GO, invalid reserved words, SQL 2016+ syntax.
6. Release build. Note app issues separately from database-freeze issues.

## Re-test every RC2 BLOCKER
B-01 GO delimiter; B-02 MERGE; B-03 NumberSequences.ScopeType; B-04 immutable CreditLedger backfill; B-05 DDL batch split; B-06 generic allocation procedure/final schema.

## Mandatory DB scenarios
- 500k receivable paid 400k then 100k.
- 300k transaction against 100k remaining: allocate 100k only; 200k remains unallocated.
- split one transaction across two receivables.
- concurrent allocations cannot exceed transaction or receivable.
- reversal recomputes AmountPaid/status.
- generic PaymentTransaction works with BankTransactionId NULL.
- cross-partner allocation rejected.
- cross-partner PartnerRole assignment rejected.
- AMOUNT_ASSIST AutoAllocate rejected.
- customer cash cannot go negative; ledger UPDATE/DELETE rejected; compensating post works.
- subscription grant posts exactly one CreditLedger entry and rerun cannot double-grant.
- frozen invoice totals reconcile; frozen legal snapshot cannot mutate.
- invoice issue credit finalization is idempotent.
- HostedLinkUrl future persistence rejected.
- Inbox claim cannot double-claim same row under concurrent workers.

## Architecture/transition
Re-evaluate RC2 legacy matrix. The target remains:
PaymentRequest=receivable; PaymentInstruction=collection instruction; PaymentTransaction=rail-neutral movement; BankTransaction=evidence; CustomerCash=money; CreditWallet=product quota; Partner=tenant; Company=legal entity.

Identify any remaining dual-write/dual-truth database object. Distinguish:
A) DB defect that blocks schema freeze;
B) application migration work for Codex after DB freeze.
Do not fail the DB architecture merely because current C# has not yet been migrated, but clearly mark release as not deployable until application work is complete.

## Governance
Verify SchemaMigrationHistory design is sufficient. If actual per-script hashes are not populated automatically, classify whether deployment tooling must own checksum registration before production. Do not pretend the DB can hash its own source file.

## Output
Create `docs/audits/CODEX_SCHEMA_V1_RC3_AUDIT.md` with:
- exact HEAD/environment;
- 001-044 execution;
- postflight;
- RC2 6 BLOCKER closure matrix;
- RC2 11 HIGH closure matrix;
- functional 9-item closure matrix;
- remaining DB blockers/highs;
- application-only worklist;
- legacy cleanup matrix;
- proposed 045+ only if truly needed;
- verdict: READY FOR DB V1 FREEZE / READY AFTER DB FIXES / NOT READY.

No merge. No production deployment.