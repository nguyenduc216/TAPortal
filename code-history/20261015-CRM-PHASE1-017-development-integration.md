# TAPORTAL-20261015-CRM-PHASE1-017 — development integration

FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360

Status: PARTIAL; SQL integration and authenticated browser mutations NOT_RUN. The configured database endpoint is non-local; no isolated test database, approved fixture account or authenticated Playwright storage state was available. No SQL write, migration or IIS deployment was executed. Local Kestrel starts and the existing anonymous Playwright protected-route test passes.

CRM fixes: selectors now filter active employee posting, user, company, branch and team/department consistency and include currently shared branches. A canceled dialog no longer leaves a pending JavaScript open request. Candidate eligibility for future selected dates and transaction-time scope/read parity remain unverified without isolated SQL tests. Do not promote offline tests into database authorization evidence.

Required isolated SQL matrix: in/out-of-scope reads and selector exclusion; all nine relation writes; stale/repeated revoke, overlap and concurrent writes; Customers.Assign/Share/Team ALLOW, explicit DENY, VIEW-only, ASSIGNED/TEAM/BRANCH/COMPANY and SYS_ADMIN DENY; AuditLogs failure rollback and UI refresh. Avoid executing on TAPortalSys without approval.