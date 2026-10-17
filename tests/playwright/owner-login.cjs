// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-020
// Run locally with `node owner-login.cjs`; enter credentials only in the headed browser.
const { chromium } = require('@playwright/test');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

(async () => {
  const base = process.env.TAPORTAL_E2E_BASE_URL || 'http://127.0.0.1:5080';
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'taportal-crm-auth-'));
  fs.chmodSync(dir, 0o700);
  const state = path.join(dir, 'state.json');
  const browser = await chromium.launch({ headless: false });
  try {
    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto(`${base}/account/login`);
    console.log('Enter the existing admin username/password in the browser. Waiting for authenticated profile (up to 5 minutes).');
    await page.waitForURL(url => !url.pathname.toLowerCase().startsWith('/account/login'), { timeout: 300000 });
    const profile = await page.goto(`${base}/account/profile`);
    if (!profile || profile.status() !== 200 || page.url().toLowerCase().includes('/account/login')) throw Error('Protected route did not authenticate.');
    await context.storageState({ path: state });
    fs.chmodSync(state, 0o600);
    const fresh = await browser.newContext({ storageState: state });
    try {
      const check = await fresh.newPage();
      const response = await check.goto(`${base}/account/profile`);
      if (!response || response.status() !== 200 || check.url().toLowerCase().includes('/account/login')) throw Error('Saved state failed fresh-context authentication.');
    } finally { await fresh.close(); }
    console.log('STORAGE_STATE_VALID=PASS. State remains in the restricted temporary directory.');
    console.log('Set TAPORTAL_E2E_STORAGE_STATE locally to the state.json file in the newly created taportal-crm-auth-* temporary directory; do not share its contents.');
    await context.close();
  } catch (error) {
    if (fs.existsSync(state)) fs.unlinkSync(state);
    console.error(`LOGIN_VALIDATION_FAILED: ${error.message}`);
    process.exitCode = 1;
  } finally { await browser.close(); }
})();