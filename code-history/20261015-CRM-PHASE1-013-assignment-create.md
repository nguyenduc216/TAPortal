# CRM Phase1-013 customer employee assignment create

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-013`
- Status: development implementation, SQL integration NOT_RUN; not production-ready.
- `PortalDb.CreateCustomerEmployeeAssignmentAsync`: reject malformed IDs, dates
  and role; one connection and Serializable transaction; caller-transaction
  Customers.Assign permission plus Customers function DataScope; active customer,
  target linked active account/employee, eligible status, exactly one current
  primary HR assignment with valid branch/company/department/team, target
  branch equal to customer owner or covered by an active branch share through
  requested interval. Future-dated interval must fit employment/posting.
- Reject overlapping active periods for same customer/employee regardless of
  role. Insert assignment and audit into same transaction, roll back on any
  failure; generated RowVersion is left to the database. Open-ended periods
  require open-ended employment/posting/share (unless owner branch).
- Audit uses CRM/Customers/ASSIGN/CustomerAssignment. SQL metadata PASS reported
  by user (3 checks, 96 columns), not SQL behavior proof. No integration DB
  credentials available; allow/deny, audit rollback and concurrent inserts
  remain unverified against SQL Server 2014.
- No schema changes or migration. Other relation writers remain Forbidden;
  existing VIEW and Blazor UI unchanged. Dirty Git tree with staged config
  deletion prevents safe branch isolation; no commit/PR/merge.