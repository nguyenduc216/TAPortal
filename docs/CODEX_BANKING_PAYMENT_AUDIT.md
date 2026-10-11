# Codex implementation contract — T.A Portal banking/payment phase

Database migrations are the source of truth. Do not redesign tables or silently change business semantics from migrations 008-021.

## Codex owns
- C# provider adapters and HTTP clients
- controllers/services/workers
- Razor UI and interaction polish
- automated tests
- build/static analysis
- code audit and targeted fixes

## Required implementation sequence
1. SePay Bank Hub authentication for SANDBOX using secrets from configuration/secret store; persist only SecretReference and lifecycle metadata.
2. Company/link-token/hosted-link flow mapped to PartnerProviderConnections, ProviderTokenSessions and BankLinkSessions.
3. Bank account synchronization into PartnerBankAccounts.
4. Transaction IPN endpoint: persist raw BankWebhookEvent first; validate auth/signature rules; normalize/de-duplicate into BankTransactions; link every delivery through BankWebhookTransactionLinks.
5. Reconciliation worker using BankReconciliationRuns and provider transaction query; IPN and reconciliation must converge on the same unique (ProviderId, ExternalTransactionId).
6. Payment-code parser and matcher. Exact normalized PaymentCode + Partner + receiving bank account is the primary deterministic match. Do not auto-match by amount alone.
7. Allocate through dbo.sp_AllocateBankTransaction. Never directly update AmountPaid from application code.
8. Unknown/expired/cancelled/ambiguous transactions go to BankTransactionReviewQueue.
9. Credit operations use stored procedures and immutable CreditLedger. Invoice credit is consumed only after successful issue.
10. External side effects are dispatched via OutboxMessages; do not keep SQL transactions open while calling SePay/EasyInvoice.

## Mandatory audit
- dotnet build with 0 errors
- run all tests; add tests for duplicate IPN, IPN+reconciliation same transaction, partial payment, second payment completing debt, overpayment, reversed allocation, cross-partner rejection, insufficient credit, duplicate idempotency key, expired link token
- verify every tenant query is Partner-scoped
- verify secrets/tokens/raw authorization headers never enter logs
- verify permission policies for every new route
- verify no financial/audit row is hard-deleted
- inspect migrations 008-021 for fresh DB and upgrade-from-current-DB behavior
- report blockers rather than changing database contracts without an explicit migration proposal

## UI
Keep compact T.A Portal design. Build detail screens for bank account, transaction/IPN history, payment request with allocation history, review queue, provider connection, credit wallet/ledger. Buttons require icons; loading/error/success states are mandatory.

## Audit report format
Return: HEAD before audit; build/test results; issues by severity; files changed; migration issues; security/multi-tenant findings; provider contract findings; unresolved questions; final commit SHA; push status.
