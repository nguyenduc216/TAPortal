# CODEX Functional + Database Architecture Review — T.A Portal Schema V1

## 1. Review identification and scope

| Item | Value |
|---|---|
| Branch reviewed | `feat/compact-dynamic-navigation` |
| Exact HEAD | `34a7640a12df2ec234eae303f83d4261efc986d3` |
| Review date | 2026-10-01 |
| Review type | Functional architecture and business/data-model coverage |
| Excluded | SQL syntax, deployment compatibility and implementation defects already covered by the technical audit |

This review answers a different question from the prior audit: **assuming the known technical defects are corrected, is the model the right foundation for the planned T.A Portal product?**

## 2. Executive architecture answer

**Database V1 functional readiness: ARCHITECTURE READY WITH CHANGES.**

The design has the correct backbone:

- Partner is the tenant and commercial boundary;
- User is a global identity with tenant membership;
- provider connections are tenant-owned;
- raw provider events are separated from normalized financial transactions;
- settlement is represented by allocations rather than mutation of source transactions;
- wallet operations use reservations and a ledger;
- invoice processing is request/operation driven rather than performed inside webhook handling;
- outbox, provider operation, event history and financial exception concepts are appropriate.

It does **not** need a ground-up redesign. However, several business abstractions must be clarified before freeze. The most important are:

1. separate the receivable from the payment instruction/QR lifecycle;
2. decide whether V1 payment core is bank-transfer-only or omnichannel, because `BankTransaction` is too narrow for MoMo/9Pay;
3. model unapplied cash/customer credit and refund explicitly;
4. make Partner membership the basis of tenant RBAC, using a hybrid global-template/partner-role model;
5. establish one source of truth between subscription entitlements and consumable credit wallets;
6. complete invoice snapshots with seller/legal-entity and financial/tax snapshots;
7. add review-decision history and explicit dispositions;
8. consolidate overlapping bank synchronization/reconciliation concepts.

These are model decisions, not implementation polish. Deferring them until after V1 would force expensive data migrations and application rewrites.

## 3. Product architecture review

### Partner as tenant boundary — KEEP

Partner is the correct primary boundary because it aligns the things that must be owned and billed together: memberships, provider credentials, bank/payment data, subscription, credits and invoices. It is superior to using Company as tenant because one customer contract can legitimately manage several legal companies.

Partner should mean **commercial account/tenant**, not legal entity. Company should mean **legal or operating entity inside that tenant**. This distinction must be explicit in naming and documentation.

### Company below Partner — KEEP, WITH STRONGER SEMANTICS

The hierarchy is sound:

```text
Partner (tenant/commercial account)
└── Company (legal or operating entity)
    └── Branch
        └── Team
```

Not every record needs CompanyId. PartnerId remains mandatory for tenant isolation; CompanyId should be added only where legal ownership or reporting requires it—for example seller invoice identity, receiving bank account, contract and potentially receivable. This avoids forcing single-company assumptions while keeping tenant queries simple.

### T.A as an ordinary Partner — KEEP, WITH PLATFORM ADMIN SEPARATION

T.A should be a normal Partner for its own business data. Platform administration is a control-plane role, not ownership of T.A business records. Platform operators may manage tenant provisioning and provider catalog, but must enter a Partner context to view business data unless explicitly using a separately audited support-access mechanism.

### Need for another Tenant/Organization abstraction — NO FOR V1

Adding both Tenant and Partner would duplicate responsibility. Keep Partner as the tenant. If grouping several Partners under a reseller/holding organization becomes necessary, introduce a future `PartnerGroup` or `ResellerAccount`; do not weaken the current boundary.

## 4. User, organization and RBAC

### Recommended identity model

Global User + PartnerUsers membership is correct. It supports one person working for several clients without duplicate credentials. Authentication belongs to User; tenant access belongs to PartnerUsers.

PartnerUsers should also carry lifecycle information such as invitation/activation/suspension, joined/left timestamps and default Partner selection. Company/branch/team assignments must be valid only inside an active Partner membership.

### Recommended role model: HYBRID

Pure global roles are too broad; completely tenant-local permission definitions are hard to maintain. The best V1 model is:

- `Permissions`: global, system-defined capability catalog;
- `RoleTemplates`: optional global templates maintained by the platform;
- `PartnerRoles`: tenant-owned role instances, optionally created from a template;
- `PartnerRolePermissions`: permissions assigned to a PartnerRole;
- `PartnerUserRoles`: membership-scoped assignment;
- platform roles: separate control-plane assignments, never reused as tenant business roles;
- data scope: assignment belongs to the membership/partner role, not directly to a global User.

This supports standard roles such as Owner/Accountant while allowing Company A and Company B to customize roles independently.

### Organization scopes

PARTNER, COMPANY, BRANCH, TEAM, ASSIGNED and SELF are enough for V1. CUSTOM should be treated as a future rule mechanism unless its evaluation semantics are defined now. A scope must reference typed organization nodes rather than an untyped `ScopeReferenceId` whose target is inferred by convention.

### V1 recommendation

Keep the hierarchy, but modify role assignment around Partner membership before freeze. Do not add a second Tenant entity.

## 5. Customer master review

The current Customer concept is a reasonable lightweight master: partner-owned code, name, tax/contact fields, type and status, plus bank identities and invoice profile/policy.

### V1 required

- Partner-scoped unique CustomerCode;
- CustomerType and CustomerStatus with controlled vocabulary;
- display/legal name, tax code and primary email/phone;
- active/inactive lifecycle and merge/duplicate handling policy;
- one or more invoice profiles when different buyer identities/addresses are used;
- customer bank identities as optional matching evidence;
- audit trail for identity/profile changes.

### Should change

Add a stable `CustomerExternalReference` or mapping table for imports/integrations. Define whether the same tax code may represent multiple customer records inside one Partner and how duplicates are resolved.

### Future/V2

- `CustomerContacts` for multiple people and contact roles;
- `CustomerAddresses` for billing/service/legal addresses;
- separate `CustomerTaxProfiles` only if tax treatment becomes richer than invoice profiles;
- customer hierarchy/parent account;
- credit limit and collection terms.

Do not split contacts and addresses in V1 unless application requirements already need multiple values. Flat primary contact fields plus reusable invoice profiles are adequate for the first release.

## 6. Provider and banking architecture

### Flow verdict

The planned flow is correct:

```text
Partner
→ ProviderConnection
→ provider authorization/link session
→ financial account
→ raw webhook/reconciliation event
→ normalized incoming transaction
→ matching/review
→ allocation
```

Provider, connection and credential lifecycle are correctly separated. SePay-specific endpoints and token/link metadata should remain in provider adapter/configuration, not in the normalized payment model.

### Where the schema is too bank-specific

VietinBank direct API fits the current model. MoMo, 9Pay and other wallets do not naturally fit `PartnerBankAccounts`, `BankCode`, `AccountNumber` and `BankTransaction`.

Before freeze, make an explicit product decision:

- If V1/V1.x supports only bank transfers, keep the bank-specific schema and document wallet/payment-gateway support as a future parallel rail.
- If MoMo/9Pay are expected to allocate into the same receivables soon, modify the core now: use a generic `PaymentAccount`/`PaymentTransaction` supertype with rail type (`BANK`, `EWALLET`, `GATEWAY`), while keeping bank details in `BankAccountDetails` or bank-specific columns/table.

Recommended direction: make **the allocation source generic** (`PaymentTransaction`) before freeze, with bank-specific ingestion as the first implementation. PaymentAllocation should not need redesign when a new payment rail is added.

### Provider failover

Provider failover is not simply another active connection. It needs routing priority, supported capability, health and account mapping. This is Future/V2 unless contractual failover is a launch requirement.

## 7. Webhook and normalized transaction model

The many-events-to-one-transaction model is correct and should be kept.

```text
Provider event delivery (immutable evidence)
        many-to-many/one link
Normalized financial transaction (business fact)
```

Raw events answer “what did the provider send and when?” Normalized transactions answer “what movement does T.A Portal recognize?” Keeping both is essential for deduplication, replay, incident analysis and reconciliation.

The link should record normalization outcome/version and whether the event created, confirmed or amended the normalized record. Reconciliation observations should use the same event/observation abstraction or a sibling `ProviderTransactionObservation`, so IPN and polling evidence are equally auditable.

## 8. Payment and settlement model

The many-to-many allocation model is the correct core settlement abstraction.

| Business case | Current functional support | Review |
|---|---|---|
| One receivable, many payments | SUPPORTED | Multiple allocations to one request |
| One payment, many receivables | SUPPORTED | Multiple allocations from one transaction |
| Partial payment | SUPPORTED | Derived balance/status fits |
| Overpayment | PARTIALLY SUPPORTED | Status/balance exists, but disposition of excess is absent |
| Reversal | SUPPORTED | Allocation reversal exists; event/reason semantics should be standardized |
| Unallocated payment | SUPPORTED | Transaction and review queue preserve it |
| Refund | NOT SUPPORTED | Needs explicit refund request/execution/link/lifecycle |
| Advance payment | NOT SUPPORTED | Needs unapplied-cash/customer-balance model |
| Customer credit | NOT SUPPORTED | Credit here means money owed to customer, not service-credit wallet |
| Chargeback/returned transfer | NOT SUPPORTED | Needs financial adjustment/dispute concept if relevant to rails |
| Manual accounting adjustment | PARTIALLY SUPPORTED | Can reverse/reallocate, but no controlled adjustment/disposition record |

### Missing cash-disposition layer

An incoming transaction can be allocated to receivables, remain unapplied, become customer advance/credit, or be refunded. The current model supports only allocation or unresolved review.

V1 should add:

- `CustomerCashAccounts` or `CustomerBalances`;
- immutable `CustomerCashLedger` for unapplied/advance money;
- `RefundRequests` and `RefundOperations` with approval/provider/bank execution lifecycle;
- a disposition/action record linking review decisions to allocation, customer credit, refund or ignore.

Do not reuse the product `PartnerCreditWallets` for customer money. Service credits and customer cash are legally and functionally different ledgers.

## 9. PaymentRequest: receivable or collection request?

The current table is effectively both a receivable and a payment instruction. Those responsibilities should be separated conceptually before V1.

### Recommendation

Keep `PaymentRequest` as the V1 **receivable/amount-to-collect aggregate**. Document it as the settlement target rather than as a QR artifact. It is generic enough for accounting service fees, orders, contracts, debt collection, subscriptions and miscellaneous charges if it contains:

- Partner, optional Company and Customer;
- amount/currency;
- due date and collection state;
- source type/source ID plus a stable external reference;
- description and creation/cancellation reason;
- derived settled/unsettled amounts.

Retain `PurposeType`/`PurposeId` for V1, but formalize allowed purpose types. Avoid introducing a second `Receivable` table now, because that would duplicate PaymentRequest. A future accounting subledger can reference PaymentRequest as its collection object.

### Separate PaymentInstruction

Move bank account, payment code, QR provider/payload and validity into `PaymentInstructions`. One PaymentRequest may then have multiple instructions over time or across accounts without changing the receivable. Old instructions remain auditable but inactive; receiving money from an old instruction can still be reviewed safely.

This is cleaner than a separate QR-only table because QR is one representation of a payment instruction.

## 10. Payment code and QR

Never reusing a payment code within a Partner is the correct conservative policy. Old screenshots and scheduled transfers remain possible after a request is closed.

The permanent uniqueness should be owned by a registry/instruction record, not only by the current active PaymentRequest. Recommended structure:

- `PaymentInstructions` — request, destination account, amount/currency, validity and lifecycle;
- `PaymentCodeRegistry` or immutable code rows — Partner + normalized code, issued once;
- optional rendered QR snapshot/file/payload history associated with the instruction.

QR payload should be a snapshot of an instruction, not a transaction or settlement fact.

## 11. Matching engine

The signal priority is sound: exact payment code first, then verified bank identity/customer mapping/content/history; amount only assists.

`TransactionMatches` is enough to hold the chosen candidate/result, but not enough to explain complex automated decisions over time. For V1, avoid a full rules engine. Add lightweight evidence:

- `MatchingAttempts`: transaction, rule/version, started/completed time, outcome;
- `MatchingEvidence`: signal type, normalized value, candidate target, score/reason;
- version/config snapshot or hash on each attempt.

`PaymentMatchingRules` can remain configuration. Full `MatchingRuleVersion` tables are Future/V2 unless business users can edit rules. The key V1 requirement is reproducibility: the system must explain why it suggested or selected a target.

## 12. Review queue workflow

The current queue handles open/resolved/ignored items but does not fully represent the decision.

V1 needs explicit dispositions:

- `ALLOCATED`;
- `CUSTOMER_CREDIT`;
- `REFUND_REQUESTED`;
- `DUPLICATE`;
- `NON_RECEIVABLE`/`IGNORED`;
- `NEEDS_INFORMATION`;
- `ESCALATED`.

Add `BankTransactionReviewActions` (or generic `PaymentReviewActions`) with actor, action, reason, before/after state, target entity and timestamp. A single mutable resolution note is not adequate audit evidence.

## 13. Package and subscription

Service → Package → PackageService → PartnerSubscription is appropriate and not over-engineered. Entitlements and overrides are also justified if their responsibility is clear.

### Correct responsibility split

- Subscription controls **feature access and commercial period**.
- Entitlement is the effective access/limit granted for a subscription period.
- Partner override changes access/limit by exception.
- Credit wallet holds **consumable units** purchased or granted.

Package change mid-cycle needs effective-dated subscription periods or a subscription change/order record. Renewal should create a new period, not silently extend historical entitlement. Top-up remains independent of package.

### Simplification required

Do not count the same invoice/banking usage both in `SubscriptionEntitlements.UsedQuantity` and in the credit wallet. For credit-metered services, entitlement grants credits into the wallet through an idempotent `SubscriptionCreditGrant`/grant batch. The wallet ledger is the usage source of truth. Entitlements retain feature availability and non-consumable limits.

## 14. Wallet and credit architecture

Wallet → Reservation → immutable Ledger is the correct architecture.

Using a hard-coded `CreditType` enum is acceptable only for a closed two-product system. T.A Portal already has a Service catalog, so the better pre-freeze model is a wallet keyed by **ServiceId or CreditDefinitionId**, not by `BANKING`/`EINVOICE` strings.

Recommended model:

- `CreditDefinitions`: service, unit, precision, expiry/consumption policy;
- `PartnerCreditWallets`: Partner + CreditDefinition;
- rules specify usage event and effective dates for a definition;
- ledger and reservation reference the wallet/definition.

This permits future services without altering every CHECK constraint and procedure. BANKING rules can change over time; invoice credit is reserved before provider call and consumed exactly once only after successful issue.

## 15. Invoice architecture and independence from payment

InvoiceRequest is the correct orchestration aggregate. It supports validation, review, provider retries and idempotency before a legal invoice exists.

InvoiceRequest must be allowed without PaymentRequest. The current nullable PaymentRequestId supports:

- invoice before payment;
- invoice unrelated to bank collection;
- manual/provider-imported invoice requests.

The model should also permit one invoice to cover several receivables and one receivable to relate to several invoices where legal adjustments or consolidated billing require it. A junction such as `InvoiceRequestReceivables` is preferable to a single optional PaymentRequestId if consolidation is in V1; otherwise keep the single link for V1 and mark many-to-many as V2.

AUTO/REVIEW/MANUAL is a useful policy, but validation outcome must remain independent of automation mode.

## 16. Invoice snapshot

The current buyer profile snapshot and request items are a good start but not a complete legal/accounting snapshot.

V1 must snapshot:

- seller legal Company identity, tax code, address and provider configuration identity;
- buyer name/company/tax/address/contact;
- currency and exchange-rate context if non-VND is allowed;
- line description, quantity, unit, unit price, discount, taxable amount, VAT rate/amount and line total;
- subtotal, total discount, VAT total, other charges and grand total;
- source/reference description and requested issue date.

Invoice items must become immutable after submission/issue or be versioned through request events. Historical invoices must not resolve live Customer/Company profiles for legal display.

## 17. Invoice lifecycle

InvoiceRelations is a sound foundation. Relation semantics should be explicit:

- ADJUST: source original/current legal invoice → target adjustment invoice;
- REPLACE: source replaced invoice → target replacement invoice;
- CANCEL: source invoice with normally no target;
- each operation has its own provider idempotency record and reason/effective time.

Conditional rules (for example target required for ADJUST/REPLACE and absent for CANCEL) should be part of the model contract. Consider whether multiple adjustments form a chain or always reference the original; choose one convention before implementation.

## 18. Wallet versus entitlement: one clear architecture

Use this rule:

```text
Subscription/Entitlement = may the Partner use the service during this period?
Credit Wallet/Ledger    = how many consumable units remain?
```

For a package containing 500 banking units and 100 invoice units:

1. renewal creates a subscription period and entitlements;
2. an idempotent grant batch credits the corresponding service wallets;
3. usage reserves/consumes wallet credit;
4. top-ups grant the same wallet independently;
5. overrides modify access or cause an explicit compensating/grant ledger entry.

Do not maintain `UsedQuantity` in entitlement for the same units. That creates two conflicting balances.

## 19. Accounting extensibility

T.A Portal does not need a general ledger in V1, but it should preserve the chain:

```text
business source → PaymentRequest/receivable
                → PaymentAllocation ← normalized payment
                → InvoiceRequest/Invoice (independent but linkable)
```

Add Company/legal-entity ownership and stable source references now. Model customer cash/advance/refund separately. These decisions allow a future accounts-receivable subledger and revenue recognition module without reinterpreting bank transactions or invoices.

Do not equate payment with revenue, and do not require payment before invoice issuance.

## 20. Auditability review

The architecture has the correct evidence layers—webhook events, transaction links, allocations/reversals, wallet ledger, request events, provider logs/operations, invoice relations and outbox. It is close to answering what/when/provider outcome.

It is not yet consistently able to answer **who, from which state, and why**. Before freeze, standardize event metadata across financial workflows:

- actor type and actor ID;
- occurred/recorded time;
- correlation/causation ID;
- old state and new state;
- reason code and free-text note;
- source/provider operation reference;
- tenant and Company context where relevant.

Do not force every domain event into generic AuditLogs; domain event tables should preserve business semantics, while AuditLogs cover administrative changes.

## 21. Missing functional capabilities

| Capability | Why needed | Priority | Database impact | Recommendation |
|---|---|---|---|---|
| Unapplied customer cash/advance | Valid incoming money may not match a receivable | MUST | Customer cash account + immutable ledger + transaction disposition | Add before freeze |
| Refund lifecycle | Review may decide money must be returned | MUST | Refund request/approval/execution/provider operation links | Add before freeze |
| Payment instruction history | Multiple/expired QR/account instructions must remain auditable | MUST | Separate instruction/code registry from receivable | Add before freeze |
| Generic payment rail | MoMo/9Pay do not fit bank account semantics | MUST decision | Generic payment account/transaction supertype or explicit future parallel rail | Decide before freeze |
| Tenant-scoped RBAC | Users can work for multiple Partners with different authority | MUST | Partner roles/assignments/scopes | Add before freeze |
| Review action history | Need explainable decisions and dispositions | MUST | Review actions/events | Add before freeze |
| Subscription-to-wallet grant | Avoid two quota sources of truth | MUST | Idempotent period/grant record | Add before freeze |
| Complete invoice snapshots | Legal history must not depend on live master data | MUST | Seller/buyer/tax/total snapshots | Add before freeze |
| Company ownership on financial records | Multi-company Partner needs legal reporting | MUST | Optional/required CompanyId by aggregate | Define before freeze |
| Matching evidence/version | Explain automated/suggested results | SHOULD | Attempts/evidence/config hash | Lightweight V1 implementation |
| Bank account lifecycle history | Link/unlink/reactivate/ownership changes | SHOULD | Account events and effective dates | Add if linking ships in V1 |
| Subscription change/renewal periods | Upgrade/downgrade/proration history | SHOULD | Subscription periods/change orders | Define lifecycle now |
| Customer duplicate/merge | Imports create duplicate masters | SHOULD | Merge aliases/history | Add workflow later in V1.x |
| Invoice–receivable many-to-many | Consolidated invoice or split invoicing | SHOULD | Junction table | Add if launch use case confirmed |
| Provider routing/failover | Multiple providers for same capability | FUTURE | Routing/capability/priority/health | V2 |
| Customer contacts/addresses | Multiple operational contacts/locations | FUTURE | Child master tables | V2 unless already required |
| Dispute/chargeback | Non-bank rails may reverse after settlement | FUTURE | Dispute/adjustment ledger | Add with gateway rails |
| Revenue recognition/general ledger | Accounting extension | FUTURE | AR/revenue journal/subledger | Separate bounded accounting module |

## 22. Simplification review

### Merge or clarify

1. **BankSyncJobs and BankReconciliationRuns** overlap. Use one `ReconciliationRun` aggregate with run type/source, window, cursor, account, counters and outcome. Avoid two worker/run tables for the same polling process.
2. **Subscription entitlement usage and wallet balance** must not both count consumable quota. Remove/stop using entitlement `UsedQuantity` for wallet-metered services.
3. **Generic IdempotencyKeys** should support API/request dedupe only. Domain operations must keep their own unique idempotency keys; do not make the generic table a second source of truth.
4. **CustomerInvoicePolicies** can remain a small one-to-one policy table; do not merge it into Customer if policy history/versioning is expected.

### Keep separate

- TransactionMatches and ReviewQueue: evidence/candidate versus human work item;
- raw webhook event and normalized transaction;
- InvoiceRequest and Invoice;
- Partner subscription/entitlement and credit wallet;
- product credit ledger and customer cash ledger;
- generic AuditLogs and domain financial events.

### Defer

- fully user-configurable matching-rule DSL;
- provider failover engine;
- generic accounting/general ledger;
- complex customer/contact hierarchy;
- automated retention execution.

## 23. Must change before V1 freeze

Count: **9**

1. Define and enforce hybrid Partner-scoped RBAC.
2. Separate PaymentInstruction/payment-code/QR lifecycle from PaymentRequest.
3. Decide and implement generic payment-allocation source if MoMo/9Pay are near-term; otherwise explicitly freeze V1 as bank-transfer-only.
4. Add unapplied customer cash/advance and refund/disposition model.
5. Establish Company/legal-entity ownership rules on bank accounts, receivables and invoices.
6. Make wallet the sole consumable-quota source and add subscription credit-grant linkage.
7. Key wallets by Service/CreditDefinition rather than a closed credit-type enum.
8. Complete immutable invoice seller/buyer/item/tax/total snapshots.
9. Add review action/disposition history and standardized actor/reason/correlation metadata.

## 24. Should change

Count: **8**

1. Consolidate BankSyncJobs and BankReconciliationRuns.
2. Add matching attempts/evidence/config version hash.
3. Add bank/financial-account lifecycle event history.
4. Model subscription renewal/change periods explicitly.
5. Add customer external mappings and duplicate/merge policy.
6. Decide whether invoice–receivable many-to-many is a launch requirement.
7. Add provider observation metadata for reconciliation evidence.
8. Standardize conditional invoice relation rules and chain convention.

## 25. Future / V2

Count: **7**

1. Provider routing/failover.
2. Customer contacts, addresses and hierarchy.
3. Full matching-rule versioning/DSL.
4. Gateway disputes/chargebacks.
5. Reseller/PartnerGroup hierarchy.
6. Accounts-receivable/revenue-recognition subledger.
7. Automated archive/retention execution.

## 26. Final architecture verdicts

| Domain | Verdict | Reason |
|---|---|---|
| Partner/Tenant | **MODIFY** | Correct boundary; membership-scoped RBAC and Company ownership semantics required |
| Banking | **MODIFY** | Correct provider/event flow; transaction/account abstraction is too bank-specific for wallets/gateways |
| Payment | **MODIFY** | Allocation core is correct; instruction, customer cash and refund layers are missing |
| Matching | **MODIFY** | Strategy is sound; explainable attempt/evidence and amount-only prohibition needed |
| Package/Subscription | **MODIFY** | Correct concepts; period/change lifecycle and quota responsibility need clarification |
| Wallet/Credit | **MODIFY** | Ledger/reservation pattern is correct; use Service/CreditDefinition and one quota source |
| Invoice | **MODIFY** | Request/provider/lifecycle design is correct; snapshot and relationship semantics incomplete |
| Provider | **KEEP** | Provider/connection/environment/operation separation is sound; add capabilities/routing later |
| Audit/Outbox | **MODIFY** | Correct patterns; standardized causation/actor/disposition and worker contracts required |

## 27. Final answer

If all previously identified technical defects were fixed, the current architecture would be a **good foundation, but not yet a freeze-ready complete business model**.

The system does not need to be redesigned from scratch. Partner, provider connection, raw event/normalized transaction, allocation ledger, wallet reservation/ledger, invoice request/provider operation and outbox are the right major patterns. The pre-freeze work is to correct the boundaries between those patterns:

- receivable versus payment instruction;
- bank transaction versus generic payment transaction;
- service entitlement versus consumable wallet balance;
- product credit versus customer money;
- global identity versus tenant authorization;
- live master data versus immutable invoice snapshot.

Once the nine must-change decisions are reflected in the schema, the database will be an appropriate V1 foundation for implementing the planned T.A Portal application without locking the product into SePay, one legal company, payment-before-invoice or duplicate quota accounting.

### Change confirmation

No SQL migration, C# source or application configuration was modified. No migration was created. This review adds only this architecture report.
