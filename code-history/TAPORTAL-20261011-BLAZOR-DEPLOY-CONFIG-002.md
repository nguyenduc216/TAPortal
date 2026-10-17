# TAPortal AppSettings Configuration and Final IIS Publish

- FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- CHANGE_ID: `TAPORTAL-20261011-BLAZOR-DEPLOY-CONFIG-002`
- Branch: `feat/compact-dynamic-navigation`
- Source commit: `1dbad77668943fd43ccb3508dd152ce150748f88`
- Status: Completed locally; no production deployment, IIS recycle, commit or push performed.

## Configuration

- The existing `Database:ConnectionString` structure was preserved unchanged in the local `src/TAPortal.Web/appsettings.json`.
- `PortalDb` and `OperationalDb` both read `Database:ConnectionString`.
- No parallel connection-string mechanism was added. `Database__ConnectionString` remains an environment override in ASP.NET Core and can supersede appsettings if defined; deployment guidance warns against accidental override.
- Bootstrap admin creation remains disabled.
- Cookie authentication (`TA.Portal.Auth`), default ASP.NET Core Data Protection, `PasswordService`, and PBKDF2 were not changed.
- No custom appsettings encryption/decryption or custom Data Protection key-ring dependency was found.

## Git and IIS protection

- `src/TAPortal.Web/appsettings.json` was removed from the Git index with `git rm --cached`; the physical file was preserved.
- `.gitignore` now excludes Web runtime `appsettings.json`, environment variants, handoff ZIPs, verification directories and publish backups while allowing `appsettings.example.json`.
- Publish `web.config` denies `.json` files through IIS Request Filtering. Local Kestrel checks returned 404 for `appsettings.json` and `appsettings.Development.json`.
- No server production ACL was changed. NTFS least-privilege guidance is included in the deploy README.

## Validation and publish

- Release build: PASS, 0 warnings/errors.
- Automated tests: PASS, 76/76.
- Kestrel startup and anonymous Playwright: PASS, 6/6.
- Login page: HTTP 200; unknown route: HTTP 404.
- SQL reads remained read-only; SQL client still reported TLS 1.0 negotiation.
- Canonical output: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal`.
- Canonical output was backed up before republish.
- Canonical appsettings was preserved and included in the local deploy ZIP by explicit user decision.

## Deploy package

- ZIP: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal-Blazor-IIS-Deploy.zip`.
- ZIP SHA-256: `6D9DC06CC86023AE595CDF887BE61E41C3E02D9474D3B50421361EE8911337C1`.
- ZIP file count after extraction: 59.
- ZIP contains approved local appsettings: YES.
- Other secret artifact scan: PASS; no private key, Playwright state or unintended secret artifact found.
- ZIP is ignored and was not committed or pushed.

## Security and readiness

The ZIP contains an approved plaintext SQL connection string by design. Treat it as confidential, transfer only through an encrypted/authorized channel, do not place it under IIS wwwroot, and restrict NTFS access. Credential rotation is required if the value has been exposed beyond the authorized local environment or repository history.

- Ready for IIS staging deployment: YES, package/configuration handoff ready; IIS runtime, authenticated, WebSocket/reconnect and TLS checks remain required.
- Ready for production replacement: NO.