# CODEX DATABASE V1 RC6 FINAL FREEZE AUDIT

Audit branch `feat/compact-dynamic-navigation` at latest remote HEAD. Fetch/pull first and record exact SHA. Do not modify SQL/C#, do not merge, do not deploy production.

Run original committed migrations 001-052 in exact order on disposable SQL Server 2014 12.x compatibility 120 using sqlcmd -b -I. Then run `database/preflight/V1_RC6_POSTFLIGHT.sql`.

This audit is narrowly focused on whether Database V1 can now be frozen.

Mandatory checks:
1. Ordered 001-052 = PASS.
2. RC6 postflight clean = PASS and intentional corruption is either rejected at write time or causes postflight FAIL.
3. Re-test RC5 B-01: `sp_GrantCredit` must reject; `sp_ReserveCredit` must reject.
4. Direct INSERT/UPDATE with NULL CreditDefinitionId must fail for PartnerCreditWallets, CreditReservations, CreditTopups, CreditLedger and CreditUsageRules.
5. Mismatched legacy CreditType vs CreditDefinitionId must fail for wallet/reservation/topup and must not create an alternative balance truth.
6. `sp_ReserveCreditDefinition` must reserve exactly once under sequential and concurrent retry; insufficient balance must rollback with caller XACT_STATE=0.
7. `sp_ConsumeReservedCredit` and `sp_ReleaseCreditReservation` must resolve wallet by CreditDefinitionId, preserve balances, idempotency and XACT_STATE=0 on forced failure.
8. Subscription grant sequential/concurrent exactly once remains PASS.
9. Invoice credit finalization twice remains exactly-once after 052.
10. New PaymentRequest must reject QrProvider/QrPayload/QrGeneratedAt; changing legacy QR snapshot must reject; PaymentInstruction QR remains writable/canonical.
11. New PartnerUsers membership can be created with PartnerRole NULL; non-null legacy PartnerRole insert/change must reject; PartnerUserRoles remains canonical and writable.
12. Re-run tenant direct-DML matrix and financial allocation/customer-cash/invoice/worker smoke scenarios from RC5 for regression.
13. Scan and execute for SQL Server 2014 compatibility.
14. Confirm the canonical matrix:
   - CreditDefinitions + PartnerCreditWallets + CreditLedger = consumable truth.
   - PaymentInstructions/PaymentCodeRegistry = payment instruction truth.
   - PartnerUserRoles = tenant authorization truth.
   - ReconciliationRuns = reconciliation truth.
   Legacy fields/tables may remain only as evidence/compatibility read-only.
15. Review whether any remaining DB path can independently create money, credit, payment instruction, or authorization truth.

Freeze rule: READY FOR DB V1 FREEZE only if migrations PASS, postflight PASS, DB BLOCKER=0, DB HIGH=0, all canonical-credit tests PASS, and no independent writable legacy truth remains. Missing C# adoption is APPLICATION work and must not be counted as DB HIGH unless it exposes a database invariant defect.

Create `docs/audits/CODEX_SCHEMA_V1_RC6_AUDIT.md` with exact HEAD/environment, migration matrix, postflight, RC5 finding closure, DB findings by severity, functional 9-item closure, canonical/legacy matrix, regression scenarios, application cut-over worklist, deployment-tooling worklist, and final verdict. Propose 053+ only for a genuine remaining DB defect. Final chat response must state exact HEAD, 001-052 result, postflight, DB BLOCKER/HIGH counts, functional closure, DB verdict, application readiness, any proposed 053+, report path, and confirm no source changed.