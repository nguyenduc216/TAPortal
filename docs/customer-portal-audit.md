# Phase A - repository audit

Baseline: main; solution contains Web and DbMcp only. Application/Domain/Infrastructure directories contain no reusable compiled projects. Added Shared for exact PBKDF2 compatibility, without referencing the admin Web project.

Authentication: cookie TA.Portal.Auth, eight-hour sliding sessions. Password format PBKDF2$SHA256$150000$salt$key. Users filtered by IsActive, IsDeleted, Status. Roles and direct allow/deny permissions exist. Legacy role-permission query did not filter inactive roles; fixed in this change. Home and Customers required only authentication; now require CUSTOMERS.VIEW. Customer-only users have no admin grants and cannot authenticate to the admin site.

Database evidence: migrations 002 and 007 define GUID Users/Customers, no User-Customer mapping. No Agent relationship in Customers migration. Existing migrations hardcode USE TAPortal; do not run them against an unknown environment. New migration never selects a database. Actual server/database, deployed columns/constraints, existing relationship equivalents, production role grants, and migration history are NOT verified: no configured Database connection string was available. Run database/scripts/003-customer-portal-schema-audit.sql first. If a live equivalent relationship exists, adapt rather than introducing a competing mapping.

Creation: existing CreateUserAsync uses a transaction for user plus system role; separate customer provisioning creates user plus membership in one serializable transaction and never grants system roles. Membership has a permanent unique pair and is reactivated rather than soft-deleted.

Risks: shared password changes affect both sites; customer cookies bind to the password hash and revalidate every request. Existing admin cookies need the same password-change invalidation (included). Revocation is checked on every customer request. No production schema changes have been executed. Logs use built-in ASP.NET providers; production must route each app to separate IIS/central logging destinations. Exception pages do not expose SQL details in production.

Removed automatic fixed-password admin bootstrap from application startup. Historical seed documentation/scripts must not be used as production credentials.
