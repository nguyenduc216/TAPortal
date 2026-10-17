# Recovery 014 — UI and recovery polish

- Date: 2026-10-12 02:49 Asia/Bangkok.
- Agent: OpenAI Codex.
- Project: T.A Portal.
- FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION.
- CHANGE_ID: TAPORTAL-20261012-RECOVERY-014.
- Branch: feat/compact-dynamic-navigation.
- Status: PARTIAL — implementation, local verification and packaging complete; Git checkpoint and authenticated IIS acceptance blocked.

## Request and scope
Fix recovered auto-dismiss, remove sidebar counts, polish Customer actions/table/empty state, share TA logo/ring loading, verify locally and rebuild the canonical deployment package. No CRUD, SQL/schema/configuration/authentication changes, database writes, production IIS deployment or restart. /customers remains MVC (CustomersController); /blazor/customers remains the Blazor trial. Actual IIS binding/physical deployment was not independently inspected.

## Root cause and changes
The readiness scheduler and recovered dismissal shared one timer. schedule() immediately cleared the 1.5-second dismissal created by setState(). Readiness also incorrectly treated server availability as circuit recovery; ensureHost() reused and replaced framework modal classes, and a broad class observer could process unrelated changes.
Separate probe/dismiss timers and cycle tokens now reject stale results/dismissals. The framework modal stays intact and only its class changes are observed. A lost circuit requires framework confirmation; rejected/failed circuits retain restart-required. Recovery restores focus, retains form values, never automatically reloads or replays writes, and prompts before discarding dirty input. Disposal cancels requests/listeners/timers; bfcache pauses/resumes. HTTP diagnostics retain received status on invalid JSON.
The existing TA PNG loads at initialization, before outages; only its shared gradient ring rotates. Business loading remains below recovery. Success/restart have stationary, distinct rings. Reduced-motion is supported. MVC sidebar count markup and CSS columns removed; existing Blazor sidebar was already count-free. Customers use existing button variants with user-plus/export icons and explicit unavailable text. Empty states have warm panels below headers; Blazor failures no longer also render empty, and MVC zero-row searches cannot show two empty panels.

## Files changed by this task
- src/TAPortal.Web/wwwroot/js/taportal-reconnect.js
- src/TAPortal.Web/wwwroot/css/ta-portal.css
- src/TAPortal.Web/wwwroot/css/taportal-blazor.css
- src/TAPortal.Web/Components/Shared/GlobalLoadingOverlay.razor
- src/TAPortal.Web/Components/Pages/Customers.razor
- src/TAPortal.Web/Views/Customers/Index.cshtml
- src/TAPortal.Web/Views/Shared/_SidebarMenuNode.cshtml
- tests/TAPortal.Web.Tests/Recovery014Tests.cs
- tests/playwright/specs/recovery-014.spec.ts
- tests/playwright/specs/recovery-heartbeat-013.spec.ts (wait for a new heartbeat after manually hiding the overlay)
- code-history/features/TAPORTAL-PLATFORM-BLAZOR-MIGRATION.md
- This history entry (exactly one new history file for 014).

## Verification
- dotnet restore TAPortal.sln: PASS.
- dotnet build TAPortal.sln -c Release --no-restore: PASS, 0 warnings/errors.
- dotnet test TAPortal.sln -c Release --no-build: PASS, 79/79.
- node --check src/TAPortal.Web/wwwroot/js/taportal-reconnect.js: PASS.
- git diff --check: PASS (existing LF/CRLF warnings only).
- npx playwright test specs/recovery-014.spec.ts --workers=1: PASS, 8/8.
- Real owned Kestrel stop/restart at 1440x900, 768x1024, 390x844: PASS. No manual show(), readiness mocks or synthetic offline events in these outage cases. Heartbeat detects outage; recovered dismisses, page identity/input persist, interaction resumes, singleton remains, and no logo request starts during outage.
- Framework confirmation/rejection, interrupted success delay, dirty reload cancel, disposal, reduced-motion and actual rendered MVC/Blazor fixtures: PASS. Framework cases are isolated simulations, not authenticated SignalR evidence.
- Existing phase9/recovery-012/process-012/heartbeat-013 suites: initial run 11/12 passed; heartbeat timing race found and fixed. Focused heartbeat rerun then exposed lost HTTP diagnostics on invalid JSON; fixed and final heartbeat rerun PASS 2/2. Other 10 regression cases passed. No failed check is represented as an initial pass.
- Final visual test rerun after button CSS consolidation: PASS 1/1. Screenshots inspected for mobile Customers and desktop outage.
- bUnit/compiled Razor tests export actual UI fixtures; Customers state is seeded only in the test process, without SQL or runtime authentication bypass.
- Authenticated Customers/IIS SignalR/recycle/WebSocket recovery: NOT RUN; no authorized authenticated session. Production replacement readiness: NO.

## Publish and package
- Command: dotnet publish src/TAPortal.Web/TAPortal.Web.csproj -c Release --no-build --no-restore -o publish-TAPortal.
- Path: D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal.
- Backup: sibling publish-TAPortal-backup-20261012-014.
- Result: PASS. DLL hash matches Release build; web.config parses and targets dotnet; latest JS/CSS/logo hashes match source. Existing appsettings restored byte-for-byte and hash verified; source appsettings unchanged.
- Stale inventory: 57 files before/after. Latest publish list plus generated web.config and nine current source static files account for the output; no unexplained stale files removed.
- ZIP: D:\Working\T.A\Tech\Projects\TAPortal\TAPortal-Blazor-IIS-Deploy.zip.
- ZIP SHA-256: A2D1C20D2776604582CE7CAFB8117CBDA05886A78D439C020DEAE64F8502DB49.
- Extraction: all 57 extracted file hashes match canonical output, including approved appsettings. ZIP/extraction ACL inheritance disabled; access restricted to current owner, SYSTEM and Administrators. ZIP remains local and untracked; no credentials logged.

## Git and remaining blockers
Commit SHA: see Git commit containing this history entry
- No Recovery 014 commit created; current HEAD remains 1dbad77668943fd43ccb3508dd152ce150748f88.
- Commit/push: BLOCKED. At entry, Recovery 011–013/configuration changes were uncommitted across the same JS/CSS/Customers files, Program.cs and layout; appsettings removal was already staged. A tested, standalone 014 checkpoint cannot include only this task without absorbing predecessor changes or committing a different baseline. Existing staging and unrelated work were preserved; no staging, commit or push performed.
- Next Git action requires resolving/checkpointing the pre-existing changes under their own scope, then reviewing this task's delta. No force push or destructive reset.
- Ready for IIS staging copy: YES, for controlled validation using the local approved configuration package.
- Ready for production replacement: NO, pending authenticated IIS acceptance and safe Git checkpoint.
