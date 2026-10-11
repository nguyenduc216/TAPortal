# TAPORTAL-20261011-BLAZOR-MIGRATION-009 — Local runtime and Playwright

- Timestamp: 2026-10-11 13:59 Asia/Bangkok
- Feature: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- Starting SHA: `06c03802b0326214c75e090c1dc9ce4d1621cbad`
- Environment: local Development configuration, Release binaries, Kestrel loopback

## Work performed

- Verified installed .NET 8 SDK/runtime and clean Release application startup with the correct project content root.
- Ran Chromium at desktop, tablet and mobile viewport sizes and retained anonymous screenshots in ignored Playwright evidence.
- Verified portal CSS, logo bytes, Tabler icon font, password-toggle JavaScript, responsive overflow, console/page errors, request failures, anonymous redirects and missing routes.
- Exercised one generated invalid username/password through the normal login form to validate the configured database read path without modifying data.
- Added a branded status-code view and status-code middleware after runtime evidence showed the default unknown route was a blank 404.
- Added a persistent Phase 9 Playwright regression suite and a server-only evidence checklist.

## Verification result

- Release build: PASS, 0 warnings/errors.
- .NET and bUnit: PASS, 76/76.
- Phase 9 Chromium: PASS, 6/6.
- Full Playwright: 7 PASS, 8 SKIPPED.
- Database read connectivity: PASS; negotiated TLS 1.0 warning remains.
- Authenticated/SignalR/reconnect runtime: BLOCKED because no authorized external storage state exists.

No schema, migration, connection string, cookie authentication, password service, permission precedence, production route ownership, account, or business data was changed. Screenshots and browser artifacts were not committed.

Status: **PARTIAL — local anonymous runtime verified; authenticated staging evidence pending**.
Ready for server staging: **NO**.
Ready for production cutover: **NO**.
