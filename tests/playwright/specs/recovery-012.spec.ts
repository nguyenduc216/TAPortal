import { expect, test } from '@playwright/test';

test.describe('@anonymous @recovery-012 coordinator acceptance', () => {
  test('dirty state survives submit and clears only on explicit reset/save', async ({ page }) => {
    await page.goto('/account/login', { waitUntil: 'networkidle' });
    await page.evaluate(() => {
      const form = document.createElement('form');
      form.id = 'recovery-dirty-fixture';
      form.innerHTML = '<input name="fixture" />';
      document.body.append(form);
      form.addEventListener('submit', event => event.preventDefault());
    });
    const form = page.locator('#recovery-dirty-fixture');
    await form.locator('input').fill('unsaved');
    await expect(form).toHaveAttribute('data-ta-dirty', 'true');
    await form.evaluate((element) => element.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    await expect(form).toHaveAttribute('data-ta-dirty', 'true');
    await page.evaluate(() => window.TAPortalRecovery.clearDirty(document.querySelector('#recovery-dirty-fixture')));
    await expect(form).not.toHaveAttribute('data-ta-dirty');
    await form.locator('input').fill('again');
    await expect(form).toHaveAttribute('data-ta-dirty', 'true');
    await form.evaluate((element) => element.reset());
    await expect(form).not.toHaveAttribute('data-ta-dirty');
  });

  test('maintenance, reconnecting, recovered and restart states use one accessible overlay', async ({ page }, testInfo) => {
    for (const viewport of [{ width: 1440, height: 900, name: 'desktop' }, { width: 768, height: 1024, name: 'tablet' }, { width: 390, height: 844, name: 'mobile' }]) {
      await page.setViewportSize(viewport);
      await page.goto('/account/login', { waitUntil: 'networkidle' });
      await page.evaluate(() => window.TAPortalRecovery.show('maintenance'));
      const overlay = page.locator('#ta-recovery-overlay');
      await expect(overlay).toBeVisible();
      await expect(overlay).toHaveAttribute('data-state', 'maintenance');
      await expect(overlay.getByRole('dialog')).toBeVisible();
      await expect(overlay.getByRole('heading', { name: 'Hệ thống đang được cập nhật' })).toBeVisible();
      await expect(overlay.getByRole('button', { name: 'Tải lại và tiếp tục' })).toBeHidden();
      const geometry = await overlay.evaluate(element => ({ width: element.getBoundingClientRect().width, height: element.getBoundingClientRect().height, scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
      expect(geometry.width).toBeGreaterThanOrEqual(viewport.width);
      expect(geometry.height).toBeGreaterThanOrEqual(viewport.height);
      expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
      await page.evaluate(() => window.TAPortalRecovery.show('restart'));
      await expect(overlay).toHaveAttribute('data-state', 'restart');
      await expect(overlay.getByRole('button', { name: 'Tải lại và tiếp tục' })).toBeVisible();
      await page.evaluate(() => window.TAPortalRecovery.show('recovered'));
      await expect(overlay).toHaveAttribute('data-state', 'recovered');
      const screenshot = testInfo.outputPath(`recovery-${viewport.name}.png`);
      await page.screenshot({ path: screenshot, fullPage: true });
      await testInfo.attach(`recovery-${viewport.name}`, { path: screenshot, contentType: 'image/png' });
    }
  });

  test('offline and online browser events transition the coordinator without duplicate hosts', async ({ page }) => {
    await page.goto('/account/login', { waitUntil: 'networkidle' });
    await page.evaluate(() => window.TAPortalRecovery.show('hidden'));
    await page.context().setOffline(true);
    await page.evaluate(() => window.dispatchEvent(new Event('offline')));
    await expect(page.locator('#ta-recovery-overlay')).toHaveAttribute('data-state', 'disconnected');
    await page.context().setOffline(false);
    await page.evaluate(() => window.dispatchEvent(new Event('online')));
    await expect(page.locator('#ta-recovery-overlay')).toHaveAttribute('data-state', /reconnecting|recovered/);
    expect(await page.locator('#ta-recovery-overlay').count()).toBe(1);
  });
});