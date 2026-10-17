# TAPORTAL-20261015-CRM-PHASE1-016 — CRM relationship UI

FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360

Development implementation: customer list links to a relationship detail page. PortalDb reads scoped relationships and same-company candidates, checks transactional action permissions for each section, and returns persisted RowVersion tokens. All nine Create/Change/Revoke operations dispatch through existing authorized service methods. UI presents loading/empty/error states, validation, confirm revoke and concurrency reload.

Security caveat: customer list scope is checked before starting the relationship read transaction; action-specific management authorization is re-evaluated inside it. SQL integration, live scope disclosure race analysis, candidate eligibility parity and authenticated browser verification are not complete. Existing Phase1-015 backend transaction behavior remains unverified. No SQL migration or IIS deployment performed.