import { expect, test } from '@playwright/test';

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-020
const state = process.env.TAPORTAL_E2E_STORAGE_STATE;
test('CRM Razor endpoints serve authenticated GET without missing antiforgery middleware', async ({ browser }) => {
  test.skip(!state, 'Owner-authenticated storage state is required');
  const authenticated = await browser.newContext({ storageState: state });
  try {
    const page = await authenticated.newPage();
    for (const route of ['/blazor/customers', '/blazor/customers/01900000-0000-4000-8000-000000000041']) {
      const response = await page.goto(route);
      expect(response?.status(), `GET ${route}`).toBe(200);
      expect(page.url()).not.toContain('/account/login');
    }
  } finally { await authenticated.close(); }
  const anonymous = await browser.newContext();
  try {
    const page = await anonymous.newPage();
    await page.goto('/blazor/customers');
    await expect(page).toHaveURL(/\/account\/login/i);
  } finally { await anonymous.close(); }
});