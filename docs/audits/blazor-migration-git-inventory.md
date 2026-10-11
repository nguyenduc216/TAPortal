# Blazor migration Git inventory

Inventory date: 2026-10-11. Baseline: `feat/compact-dynamic-navigation` at `4a644bcd92d7b7ede82860902404d3498facc923`, synchronized with origin before Phase 7.

| Path | Status | Ownership / CHANGE_ID | Secret risk | Eligibility / decision |
|---|---|---|---|---|
| `src/TAPortal.Web/Components/**` | untracked | Blazor 001–007 | Low | Include: migration source |
| `src/TAPortal.Web/Services/GlobalLoadingService.cs` | untracked | Blazor 001–002 | Low | Include |
| `src/TAPortal.Web/wwwroot/css/taportal-blazor.css` | untracked | Blazor 001–006 | Low | Include |
| `src/TAPortal.Web/wwwroot/js/taportal-reconnect.js` | untracked | Blazor 002 | Low | Include |
| `src/TAPortal.Web/Program.cs` | modified | Blazor 001–003 | Medium configuration surface | Include; bootstrap plaintext default removed |
| `src/TAPortal.Web/Models/PortalModels.cs` | modified | Blazor 004 | Low | Include |
| `src/TAPortal.Web/Services/PortalDb.cs` | modified | Blazor 004–007 plus committed HR owner | Medium SQL surface | Include after security review |
| `src/TAPortal.Web/Controllers/AccountController.cs` | modified | Blazor 007 hardening | Low | Include |
| `src/TAPortal.Web/TAPortal.Web.csproj` | modified | Blazor foundation | Low | Include |
| `src/TA.Portal-workspace.code-workspace` | modified | Blazor test workspace | Low | Include |
| `tests/TAPortal.Web.Tests/**` migration files and project | untracked/modified | Blazor 001–007 | Low | Include automated/component tests |
| `tests/playwright/**` | untracked | Blazor 007 | Low; external state only | Include suite/config/lock; exclude artifacts and storage state |
| `code-history/*BLAZOR*`, `*SQL-CONNECTIVITY*`, feature history | untracked | Blazor 001–007/prechecks | Medium operational metadata | Include; reviewed for secret values |
| `docs/audits/blazor-*`, `TAPORTAL-...-SECURITY.md` | untracked | Blazor 002–007 | Medium operational metadata | Include selected Blazor audits |
| `src/TAPortal.Web/appsettings.json` | modified | User-local connectivity | **Critical: credential** | Exclude; rotate credential; never stage |
| `docs/audits/CODEX_SCHEMA_V1_*.md` | untracked | Pre-existing DB audit work | Medium environment metadata | Exclude as unrelated local work |
| `publish-TAPortal*`, `publish/`, `src/publish/` | ignored | Generated outputs | High if config copied | Exclude; scan/retain locally under operations policy |
| `.vs/`, `bin/`, `obj/`, `*.user` | ignored | Tool output | Low–medium | Exclude |
| Playwright `test-results/`, `playwright-report/`, `blob-report/`, storage state | ignored/external | Browser evidence | High session/PII risk | Exclude; sanitize before sharing |

Staging must use exact-path `git add`; `git add .` and `git add -A` are prohibited for this checkpoint. The staged diff must exclude appsettings, schema audits, publish outputs and browser evidence.

Governance note: source comments identify `TAPORTAL-20261011-BLAZOR-MIGRATION-003`, but no standalone CHANGE 003 history file exists in the working tree. Phase 7 does not reconstruct that missing historical record; the integrated checkpoint preserves the tagged source and records this gap for follow-up.
