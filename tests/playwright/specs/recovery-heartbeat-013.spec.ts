import { expect, test } from '@playwright/test';

test.describe('@anonymous @recovery-013 heartbeat', () => {
  test('probes while hidden and treats HTTP/JSON failures as unavailable', async ({ page }) => {
    const probes: number[] = [];
    await page.route('**/health/client-readiness**', async route => { probes.push(Date.now()); await route.fulfill({ status: 503, contentType: 'text/html', body: '<html>offline</html>' }); });
    await page.goto('/account/login', { waitUntil: 'networkidle' });
    await page.evaluate(() => window.TAPortalRecovery.show('hidden'));
    // CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — wait for a fresh heartbeat after manually hiding; navigation may already have accumulated failures.
    const probesBeforeHide = probes.length;
    await expect.poll(() => probes.length, { timeout: 35_000 }).toBeGreaterThan(probesBeforeHide);
    await expect.poll(async () => await page.evaluate(() => window.TAPortalRecovery.getDiagnostics().consecutiveFailures), { timeout: 15_000 }).toBeGreaterThanOrEqual(2);
    await expect(page.locator('#ta-recovery-overlay')).toBeVisible();
    const diagnostics = await page.evaluate(() => window.TAPortalRecovery.getDiagnostics());
    expect(diagnostics.lastHttpStatus).toBe(503);
    expect(diagnostics.schedulerRunning).toBeTruthy();
    expect(probes.length).toBeGreaterThanOrEqual(2);
  });

  test('accepts only ready true and rejects HTML/invalid JSON responses', async ({ page }) => {
    let call = 0;
    await page.route('**/health/client-readiness**', async route => {
      call++;
      if (call === 1) return route.fulfill({ status: 404, contentType: 'text/html', body: '<html>not found</html>' });
      if (call === 2) return route.fulfill({ status: 200, contentType: 'application/json', body: '{bad' });
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ready: false, maintenance: false }) });
    });
    await page.goto('/account/login', { waitUntil: 'networkidle' });
    await expect.poll(async () => await page.evaluate(() => window.TAPortalRecovery.getDiagnostics().consecutiveFailures), { timeout: 15_000 }).toBeGreaterThanOrEqual(2);
    await expect(page.locator('#ta-recovery-overlay')).toBeVisible();
    expect(await page.evaluate(() => window.TAPortalRecovery.getDiagnostics().lastHttpStatus)).toBe(200);
  });
});
