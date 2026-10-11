import { expect, test, type Page } from '@playwright/test';
import fs from 'node:fs';

const storageState = process.env.TAPORTAL_E2E_STORAGE_STATE;
const authenticatedRoutes = [
  '/', '/blazor/account/profile', '/blazor/account/change-password', '/blazor/users',
  '/blazor/roles', '/blazor/menus', '/blazor/customers', '/blazor/hr/departments', '/blazor/hr/teams'
];

function collectFailures(page: Page) {
  const failures: string[] = [];
  page.on('pageerror', error => failures.push(`pageerror:${error.message}`));
  page.on('console', message => { if (message.type() === 'error') failures.push(`console:${message.text()}`); });
  page.on('requestfailed', request => failures.push(`request:${request.method()} ${request.url()} ${request.failure()?.errorText}`));
  return failures;
}

test('@anonymous login and protected routes fail closed', async ({ page }) => {
  await page.goto('/account/login');
  await expect(page).toHaveURL(/\/account\/login/i);
  await expect(page.locator('input[name="Username"]')).toBeVisible();
  await expect(page.getByText('Tài khoản khởi tạo')).toHaveCount(0);
  for (const route of authenticatedRoutes) {
    await page.goto(route);
    await expect(page).toHaveURL(/\/account\/login/i);
  }
});

test.describe('@authenticated trial-route parity', () => {
  test.skip(!storageState || !fs.existsSync(storageState), 'Authorized storage state is required; credentials are never stored in source.');
  test.use({ storageState });

  test('all trial routes load, refresh, and avoid browser/network errors', async ({ page }) => {
    const failures=collectFailures(page);
    for (const route of authenticatedRoutes) {
      const response=await page.goto(route);
      expect(response?.ok(),`${route} HTTP response`).toBeTruthy();
      await expect(page).not.toHaveURL(/\/account\/login/i);
      await expect(page.locator('#components-reconnect-modal')).toHaveCount(1);
      await page.reload();
      await expect(page).not.toHaveURL(/\/account\/login/i);
    }
    expect(failures).toEqual([]);
  });

  test('MVC and Blazor navigation coexist and multi-tab circuits initialize', async ({ page, context }) => {
    await page.goto('/users'); await expect(page).not.toHaveURL(/\/account\/login/i);
    await page.goto('/blazor/users'); await expect(page).not.toHaveURL(/\/account\/login/i);
    const second=await context.newPage(); await second.goto('/blazor/hr/teams');
    await expect(second).not.toHaveURL(/\/account\/login/i); await expect(second.locator('#components-reconnect-modal')).toHaveCount(1);
  });

  test('write surfaces are present only when granted by server permissions', async ({ page }) => {
    await page.goto('/blazor/users');
    await expect(page.getByRole('button',{name:'Tạo tài khoản'})).toHaveCount(Number(process.env.TAPORTAL_E2E_CAN_CREATE_USERS === 'true'));
    await page.goto('/blazor/hr/departments');
    await expect(page.getByRole('button',{name:'Lưu'})).toHaveCount(Number(process.env.TAPORTAL_E2E_CAN_CREATE_DEPARTMENTS === 'true'));
    await page.goto('/blazor/hr/teams');
    await expect(page.getByRole('button',{name:'Lưu'})).toHaveCount(Number(process.env.TAPORTAL_E2E_CAN_CREATE_TEAMS === 'true'));
  });
});
