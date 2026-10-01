# T.A Portal — Database Schema V1 RC1

Status: **RELEASE CANDIDATE — NOT APPROVED FOR PRODUCTION**

The database architecture track owns schema, keys, constraints, indexes, tenant boundaries, idempotency, financial ledgers and provider persistence. Application/UI code must consume these contracts.

## V1 domains
1. Identity/RBAC/navigation (001-008)
2. Partner/Tenant and organization ownership (009)
3. Provider, package, subscription and credits (010, 018, 020, 022)
4. Banking ingestion, SePay environments, tokens/linking and reconciliation (011, 015-016, 018-019, 024-026)
5. Payment request, matching, allocation and audit history (012, 015, 017, 019, 026)
6. Invoice request/profile/provider lifecycle (013, 023, 027)
7. Reliability: outbox, idempotency, retries, dead letters (021, 024)
8. Operational reporting and financial exceptions/retention (025, 028)

## Frozen V1 invariants proposed for audit
- Partner is the tenant/billing/provider boundary; Company is an organization/legal entity under Partner.
- Users are global identities; PartnerUsers provides tenant membership.
- Every financial/provider operation must resolve Partner ownership.
- Provider secrets/tokens are never persisted plaintext; database stores SecretReference/lifecycle metadata.
- One normalized bank movement is unique by ProviderId + ExternalTransactionId; multiple webhook deliveries remain auditable.
- PaymentCode is unique and never reused within a Partner.
- PaymentRequest settlement derives from ACTIVE PaymentAllocations. Allocation cannot exceed a CREDIT bank transaction.
- Financial history is reversed/compensated, not hard-deleted.
- CreditLedger is immutable; wallet operations are transactional/idempotent.
- External API calls do not execute inside long SQL transactions; Outbox/worker owns side effects.
- Invoice Ikey is stable per Partner. Issue/adjust/replace/cancel operations have explicit idempotency records.
- Invoice credit is consumed only after successful issue; failed provider attempts do not consume final credit.
- Amount-only matching may suggest but must never be the sole automatic allocation rule.
- Expired/cancelled payment requests do not auto-allocate; they enter review.
- V1 stores money as decimal(18,2), currency defaults to VND where modeled.

## Audit gate
RC1 is not a deployment bundle. Codex must independently inspect migrations 001-028, current application code and the original/current database baseline. Findings must be classified BLOCKER/HIGH/MEDIUM/LOW. Any BLOCKER/HIGH schema issue requires a new corrective migration proposal and a second review before V1.0.0 is approved.

After ChatGPT and Codex agree, freeze the migration set, generate deployment/preflight/postflight/rollback guidance, record source commit and mark SchemaVersions 1.0.0 APPROVED.
