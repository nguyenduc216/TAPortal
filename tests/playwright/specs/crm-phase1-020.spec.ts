import { expect, test } from '@playwright/test';

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-020
const state = process.env.TAPORTAL_E2E_STORAGE_STATE;
test.describe('@authenticated CRM fixture workflows', () => {
  test.skip(!state, 'Owner-authenticated state required outside the repository');
  test.use({ storageState: state });
  test('active fixture branch share rejects an overlapping interval', async ({ page }) => {
    await page.goto('/blazor/customers/01900000-0000-4000-8000-000000000041');
    const section = page.locator('.ta-table-card').filter({ has: page.getByRole('heading', { name: 'Chi nh├ính chia sß║╗' }) });
    await section.getByRole('button', { name: 'Th├¬m' }).click();
    const dialog = page.locator('#ta-relation-dialog');
    await dialog.getByLabel('Chß╗ìn ─æß╗æi t╞░ß╗úng').selectOption('01900000-0000-4000-8000-000000000003');
    await dialog.getByRole('button', { name: 'L╞░u quan hß╗ç' }).click();
    await expect(dialog.getByRole('alert')).toContainText('Khoß║úng thß╗¥i gian bß╗ï tr├╣ng');
  });
  for (const viewport of [{ name: 'desktop', width: 1440, height: 900, customer: 42 }, { name: 'mobile', width: 390, height: 844, customer: 43 }]) {
    test(`${viewport.name}: list, detail, validation, create/edit/revoke all relationships`, async ({ page }) => {
      const customer = `/blazor/customers/01900000-0000-4000-8000-${String(viewport.customer).padStart(12, '0')}`;
      await page.setViewportSize(viewport);
      const errors: string[] = [];
      page.on('pageerror', e => errors.push(e.message));
      page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
      await page.goto('/blazor/customers');
      await expect(page.getByText('TA_DEV_CRM_019_C1').first()).toBeVisible();
      await page.goto(customer);
      await expect(page.getByRole('heading', { name: `TA_DEV_CRM_019 customer ${viewport.customer - 40}` })).toBeVisible();
      const sections = ['Nh├ón vi├¬n ─æ╞░ß╗úc ph├ón c├┤ng', 'Chi nh├ính chia sß║╗', '─Éß╗Öi dß╗ïch vß╗Ñ'];
      for (const title of sections) {
        const section = page.locator('.ta-table-card').filter({ has: page.getByRole('heading', { name: title }) });
        await section.getByRole('button', { name: 'Th├¬m' }).click();
        const dialog = page.locator('#ta-relation-dialog');
        await expect(dialog).toBeVisible();
        await dialog.getByRole('button', { name: 'L╞░u quan hß╗ç' }).click();
        await expect(dialog.getByRole('alert')).toBeVisible();
        const select = dialog.getByLabel('Chß╗ìn ─æß╗æi t╞░ß╗úng');
        const options = await select.locator('option').evaluateAll(nodes => nodes.slice(1).map(n => ({ value: (n as HTMLOptionElement).value, text: n.textContent || '' })));
        expect(options.length).toBeGreaterThan(0);
        await select.selectOption(options[0].value);
        await dialog.getByRole('button', { name: 'L╞░u quan hß╗ç' }).click();
        await expect(dialog).toHaveCount(0);
        await page.reload();
        const row = section.locator('tbody tr').filter({ hasText: options[0].text.split(' ')[0] }).filter({ has: page.getByRole('button', { name: 'Sß╗¡a' }) }).first();
        await expect(row).toBeVisible();
        await row.getByRole('button', { name: 'Sß╗¡a' }).click();
        await expect(dialog).toBeVisible();
        await dialog.getByRole('button', { name: 'L╞░u quan hß╗ç' }).click();
        await expect(dialog).toHaveCount(0);
        await page.reload();
        await row.getByRole('button', { name: 'Thu hß╗ôi' }).click();
        await dialog.getByRole('button', { name: 'X├íc nhß║¡n thu hß╗ôi' }).click();
        await expect(dialog).toHaveCount(0);
        await page.reload();
        await expect(section.getByText('─É├ú thu hß╗ôi').first()).toBeVisible();
      }
      expect(errors).toEqual([]);
    });
  }
});
