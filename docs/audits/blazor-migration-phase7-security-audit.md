# Blazor Phase 7 server-write security audit

| Operation | Identity / active user | Permission at execution | Input and validation | Concurrency / errors | Result |
|---|---|---|---|---|---|
| Profile update | Server `NameIdentifier`, equality with rendered account, active DB lookup and active UPDATE predicate | Self-service authenticated action | `ProfileEditVm` exposes only display name/email/phone | Generic errors; no RowVersion in existing owner | FIXED/PASS contract |
| Change Password | Server `NameIdentifier`; active lookup and active UPDATE predicate | Self-service authenticated action | Existing password policy, current/reuse/mismatch checks | Generic errors; duplicate guard | FIXED/PASS contract |
| Users Create | Server `NameIdentifier`; `GetEffectivePermissionsAsync` rejects inactive users | `Users.Create` rechecked through FunctionAction immediately before write | Narrow create VM, password policy/hash, duplicate lookup and unique DB owner | Generic exception, transaction in PortalDb | PASS contract |
| Departments Create | Server identity | `Departments.Create` rechecked | DataAnnotations, normalization, parameterized SQL, DB uniqueness | Typed duplicate result; generic exception | PASS contract |
| Departments Status/Delete | Server identity | Update/Delete rechecked | Entity and RowVersion come from server-loaded row | Optimistic predicate; dependency-aware soft delete | PASS contract |
| Teams Create | Server identity | `Teams.Create` rechecked | Active Department and hierarchy validated transactionally; narrow VM | Unique result and generic exception | PASS contract; UI parent field pending |
| Teams Status/Delete | Server identity | Update/Delete rechecked | Entity and RowVersion come from server-loaded row | Optimistic predicate; child/future assignment block | PASS contract |

Explicit DENY, permission revocation and user disablement are resolved from SQL on every `HasFunctionActionAsync` call. UI visibility is not the security boundary. Remaining authenticated permission-revocation and cross-account behavior requires an approved staging account/database and is not claimed as browser PASS.

The public login page previously advertised the predictable bootstrap username `admin`. Phase 7 removed that obsolete hint; bootstrap remains disabled unless explicitly enabled with a secret-supplied password.
