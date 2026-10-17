# CRM Phase1-015 relation change and revoke

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-015`
- Status: development implementation; SQL integration NOT_RUN, not production-ready.
- Implemented six PortalDb Change/Revoke service methods for assignments, branch
  shares and service teams. DTOs take relation Id and eight-byte RowVersion,
  not a caller-supplied customer/target. Stored CustomerId drives transaction-bound
  Customers.Assign/Share/Team authorization in a Serializable transaction.
- Change preserves customer and target, checks the existing Create target policies,
  date order and overlapping active periods (excluding its own Id). Only dates
  and assignment role can change. UPDATE guards RowVersion and active status.
- Revoke deactivates, does not delete or erase dates. Repeated revoke returns
  ConcurrencyConflict; stale RowVersion also returns ConcurrencyConflict.
  AuditLogs OldValues/NewValues and mutation commit together; errors roll back.
- No schema migration, SQL execution, UI, commit, merge or IIS deployment.
  Offline tests validate malformed requests. Database allow/deny, explicit DENY,
  cross-customer IDs, overlap, audit failure and concurrent updates NOT_RUN.
- Existing dirty Git state including staged configuration deletion preserved.