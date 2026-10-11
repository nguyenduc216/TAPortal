# Phase 7 Playwright suite

The suite never stores credentials. Provision an authorized staging account, log in through an approved local/manual flow, and save Playwright storage state outside the repository. Configure:

- `TAPORTAL_E2E_BASE_URL` — approved non-production URL.
- `TAPORTAL_E2E_STORAGE_STATE` — absolute path to the external authenticated storage-state file.
- `TAPORTAL_E2E_CAN_CREATE_USERS`, `TAPORTAL_E2E_CAN_CREATE_DEPARTMENTS`, `TAPORTAL_E2E_CAN_CREATE_TEAMS` — expected server-granted capabilities (`true`/`false`).

Run `npm ci`, `npx playwright install chromium`, then `npm test`. Anonymous checks can run without credentials. Authenticated tests report skipped until external storage state exists. Write-data submission is intentionally not automated until an approved isolated database and reset fixture are supplied. Reports, traces, screenshots and videos are ignored by Git.
