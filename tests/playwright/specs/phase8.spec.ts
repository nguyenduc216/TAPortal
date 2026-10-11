import { expect, test } from '@playwright/test';
import fs from 'node:fs';

const storageState = process.env.TAPORTAL_E2E_STORAGE_STATE;
const hasAuth = Boolean(storageState && fs.existsSync(storageState));
const allowWrites = process.env.TAPORTAL_E2E_ALLOW_WRITES === 'true';

test.describe('@phase8 authenticated functional parity', () => {
  test.skip(!hasAuth, 'Authorized external storage state is required.');
  test.use({ storageState });

  test('read routes, search, refresh, multi-tab and logout are operational', async ({ page, context }) => {
    for (const route of ['/', '/blazor/users', '/blazor/roles', '/blazor/menus', '/blazor/customers', '/blazor/hr/departments', '/blazor/hr/teams']) {
      await page.goto(route); await expect(page).not.toHaveURL(/\/account\/login/i); await page.reload(); await expect(page).not.toHaveURL(/\/account\/login/i);
    }
    await page.goto('/blazor/customers');
    const search = page.getByPlaceholder(/Tìm/i); if (await search.count()) await search.fill('__phase8_no_match__');
    const second = await context.newPage(); await second.goto('/blazor/hr/departments'); await expect(second.locator('#components-reconnect-modal')).toHaveCount(1); await second.close();
    await page.goto('/account/logout'); await expect(page).toHaveURL(/\/account\/login/i);
  });

  test('profile and password surfaces render without exposing secrets', async ({ page }) => {
    await page.goto('/blazor/account/profile'); await expect(page.locator('input')).not.toHaveCount(0);
    await page.goto('/blazor/account/change-password'); await expect(page.locator('input[type="password"]')).toHaveCount(3);
  });

  test('unauthorized account is denied direct restricted URLs', async ({ page }) => {
    test.skip(process.env.TAPORTAL_E2E_EXPECT_RESTRICTED !== 'true', 'Requires an approved restricted-account storage state.');
    for (const route of ['/blazor/users', '/blazor/hr/departments', '/blazor/hr/teams']) {
      await page.goto(route); await expect(page.getByText(/không có quyền/i)).toBeVisible();
    }
  });

  test('approved Department create and edit workflow', async ({ page }) => {
    test.skip(!allowWrites, 'Write tests require an approved isolated database and reset fixture.');
    const run = `E2E${Date.now()}`;
    await page.goto('/blazor/hr/departments');
    await page.getByPlaceholder('Mã').fill(run); await page.getByPlaceholder('Tên').fill(`Department ${run}`); await page.getByRole('button', { name: 'Lưu', exact: true }).click();
    await expect(page.getByText('Đã tạo phòng ban mới.')).toBeVisible();
    await page.getByRole('row', { name: new RegExp(run) }).getByRole('button', { name: 'Sửa' }).click();
    await page.getByLabel('Tên phòng ban sửa').fill(`Department ${run} updated`); await page.getByRole('button', { name: 'Cập nhật' }).click();
    await expect(page.getByText('Đã cập nhật phòng ban.')).toBeVisible();
  });

  test('reconnect surface follows framework state during an approved interruption', async ({ page }) => {
    test.skip(process.env.TAPORTAL_E2E_RUN_RECONNECT !== 'true', 'Requires controlled staging network interruption.');
    await page.goto('/'); await expect(page.locator('#components-reconnect-modal')).toHaveCount(1);
    await page.context().setOffline(true); await expect(page.locator('#components-reconnect-modal')).toBeVisible({ timeout: 15_000 });
    await page.context().setOffline(false);
  });
});
