import { expect, test, type Page } from '@playwright/test';
import { spawn, type ChildProcess } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';

// CHANGE_ID: TAPORTAL-20261012-RECOVERY-014 — actual Kestrel outages plus isolated framework and rendered-view contracts.
const root = path.resolve(__dirname, '../../..');
const base = 'http://127.0.0.1:5198';
const web = path.join(root, 'src/TAPortal.Web');
let server: ChildProcess;
async function startServer() {
  const child = spawn('dotnet', [path.join(web, 'bin/Release/net8.0/TAPortal.Web.dll'), '--urls', base], {
    cwd: web, windowsHide: true, stdio: 'ignore', env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production' }
  });
  for (let i = 0; i < 60; i++) {
    if (child.exitCode !== null) throw new Error(`Owned Kestrel exited: ${child.exitCode}`);
    try { if ((await fetch(`${base}/health/client-readiness`)).ok) return child; } catch { /* startup */ }
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  await stopServer(child); throw new Error('Kestrel startup timed out');
}
async function stopServer(child: ChildProcess) {
  if (!child?.pid || child.exitCode !== null) return;
  await new Promise<void>(resolve => {
    if (process.platform === 'win32') spawn('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }).once('exit', () => resolve());
    else { child.once('exit', () => resolve()); child.kill(); }
  });
}
async function login(page: Page) {
  await page.goto(`${base}/account/login`, { waitUntil: 'networkidle' });
  await expect(page.locator('#ta-recovery-overlay')).toBeHidden();
}
async function probe(page: Page) { await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange'))); }
async function fixture(page: Page, name: string) {
  await login(page);
  const markup = readFileSync(path.join(root, 'tests/TAPortal.Web.Tests/bin/Release/net8.0/recovery-014-fixtures', `${name}.html`), 'utf8');
  await page.evaluate(markup => {
    window.TAPortalRecovery.dispose();
    document.body.innerHTML = `<main style="padding:16px;max-width:100%;box-sizing:border-box">${markup}</main>`;
  }, markup);
}
async function circuitHarness(page: Page, result: 'true' | 'false' | 'throw') {
  await login(page);
  await page.evaluate(result => {
    window.TAPortalRecovery.dispose(); delete window.TAPortalRecovery;
    const modal = document.createElement('div'); modal.id = 'components-reconnect-modal';
    modal.className = 'ta-reconnect'; modal.style.display = 'none'; document.body.append(modal);
    window.Blazor = { reconnect: async () => { if (result === 'throw') throw new Error('circuit unavailable'); return result === 'true'; } };
  }, result);
  await page.addScriptTag({ url: `${base}/js/taportal-reconnect.js` });
  await expect.poll(() => page.evaluate(() => window.TAPortalRecovery.getDiagnostics().lastHttpStatus)).toBe(200);
}

test.describe('@anonymous @recovery-014', () => {
  test.describe.configure({ mode: 'serial' });
  test.beforeAll(async () => { server = await startServer(); });
  test.afterAll(async () => { await stopServer(server); });

  for (const viewport of [{ width: 1440, height: 900 }, { width: 768, height: 1024 }, { width: 390, height: 844 }]) {
    test(`real stop/restart auto-dismiss ${viewport.width}`, async ({ page }, info) => {
      test.setTimeout(60000);
      await page.setViewportSize(viewport);
      const errors: string[] = []; page.on('pageerror', e => errors.push(e.message));
      await login(page);
      await page.locator('input[name=Username]').fill('preserved-local-input');
      const identity = await page.evaluate(() => { window.recovery014Identity = Math.random(); return window.recovery014Identity; });
      const requests: string[] = []; page.on('request', r => requests.push(r.url()));
      await stopServer(server);
      // No show(), browser event injection or readiness mocking: the production heartbeat detects the actual outage.
      const overlay = page.locator('#ta-recovery-overlay');
      await expect(overlay).toHaveAttribute('data-state', 'disconnected', { timeout: 20000 });
      expect(await overlay.locator('img').evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBeTruthy();
      await page.screenshot({ path: info.outputPath(`offline-${viewport.width}.png`) });
      server = await startServer();
      await expect(overlay).toHaveAttribute('data-state', 'recovered', { timeout: 20000 });
      const recoveredAt = Date.now();
      await expect(overlay).toBeHidden({ timeout: 3500 });
      expect(Date.now() - recoveredAt).toBeGreaterThan(800);
      expect(await page.evaluate(() => window.recovery014Identity)).toBe(identity);
      await expect(page.locator('input[name=Username]')).toHaveValue('preserved-local-input');
      await page.locator('input[name=Username]').fill('interactive-again');
      await expect(page.locator('input[name=Username]')).toBeFocused();
      expect(await page.locator('.ta-recovery-overlay').count()).toBe(1);
      expect(requests.filter(url => url.includes('/images/ta-logo'))).toEqual([]);
      expect(errors).toEqual([]);
    });
  }

  test('new outage cancels old success dismissal; repeated healthy probes do not postpone it', async ({ page }) => {
    let ready = true;
    await page.route('**/health/client-readiness**', route => route.fulfill({ json: { ready } }));
    await login(page);
    ready = false; await probe(page);
    const overlay = page.locator('#ta-recovery-overlay');
    await expect(overlay).toBeVisible();
    ready = true; await probe(page);
    await expect(overlay).toHaveAttribute('data-state', 'recovered');
    await page.waitForTimeout(700);
    ready = false; await probe(page);
    await expect(overlay).toHaveAttribute('data-state', /disconnected|reconnecting/);
    await page.waitForTimeout(1000); await expect(overlay).toBeVisible();
    ready = true; await probe(page);
    await expect(overlay).toHaveAttribute('data-state', 'recovered');
    await probe(page); await page.waitForTimeout(600); await probe(page);
    await expect(overlay).toBeHidden({ timeout: 1700 });
  });

  test('healthy readiness cannot hide an unconfirmed or rejected circuit', async ({ page }) => {
    await circuitHarness(page, 'throw');
    await page.evaluate(() => document.getElementById('components-reconnect-modal')!.classList.add('components-reconnect-show'));
    const overlay = page.locator('#ta-recovery-overlay');
    await expect(overlay).toHaveAttribute('data-state', 'reconnecting');
    await probe(page); await page.waitForTimeout(1800); await expect(overlay).toBeVisible();
    await page.evaluate(() => document.getElementById('components-reconnect-modal')!.className = 'components-reconnect-hide');
    await expect(overlay).toHaveAttribute('data-state', 'recovered');
    await page.waitForTimeout(500);
    await page.evaluate(() => document.getElementById('components-reconnect-modal')!.className = 'components-reconnect-rejected');
    await expect(overlay).toHaveAttribute('data-state', 'restart');
    await probe(page); await page.waitForTimeout(1800);
    await expect(overlay).toHaveAttribute('data-state', 'restart'); await expect(overlay).toBeVisible();
    expect(await page.locator('.ta-recovery-overlay').count()).toBe(1);
  });

  test('framework confirmation dismisses and false reconnect requires restart', async ({ page }) => {
    await circuitHarness(page, 'true');
    await page.evaluate(() => document.getElementById('components-reconnect-modal')!.className = 'components-reconnect-show');
    await expect(page.locator('#ta-recovery-overlay')).toHaveAttribute('data-state', 'recovered');
    await expect(page.locator('#ta-recovery-overlay')).toBeHidden();
    await circuitHarness(page, 'false');
    await page.evaluate(() => document.getElementById('components-reconnect-modal')!.className = 'components-reconnect-show');
    await expect(page.locator('#ta-recovery-overlay')).toHaveAttribute('data-state', 'restart');
  });

  test('dirty reload requires confirmation; dispose cancels events and timers', async ({ page }) => {
    await login(page);
    await page.locator('input[name=Username]').fill('unsaved');
    await page.evaluate(() => window.TAPortalRecovery.show('restart'));
    page.once('dialog', dialog => dialog.dismiss());
    await page.locator('[data-ta-reload]').click();
    await expect(page.locator('input[name=Username]')).toHaveValue('unsaved');
    await expect(page.locator('#ta-recovery-overlay')).toHaveAttribute('data-state', 'restart');
    await page.evaluate(() => { window.TAPortalRecovery.dispose(); window.dispatchEvent(new Event('offline')); });
    await expect(page.locator('#ta-recovery-overlay')).toBeHidden();
    expect(await page.evaluate(() => window.TAPortalRecovery.getDiagnostics().schedulerRunning)).toBe(false);
    await page.addScriptTag({ url: `${base}/js/taportal-reconnect.js` });
    expect(await page.locator('.ta-recovery-overlay').count()).toBe(1);
  });

  test('rendered MVC and Blazor Customers, sidebar and master loading across viewports', async ({ page }, info) => {
    for (const viewport of [{ width: 1440, height: 900 }, { width: 768, height: 1024 }, { width: 390, height: 844 }]) {
      await page.setViewportSize(viewport);
      for (const name of ['mvc-customers-empty', 'blazor-customers-empty']) {
        await fixture(page, name);
        await expect(page.locator('.ta-customer-empty')).toHaveText('Chưa có khách hàng.');
        await expect(page.locator('.ti-user-plus')).toBeVisible();
        const empty = await page.locator('.ta-customer-empty').boundingBox();
        const header = await page.locator('thead').boundingBox();
        expect(empty!.y).toBeGreaterThanOrEqual(header!.y + header!.height);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
        await page.screenshot({ path: info.outputPath(`${name}-${viewport.width}.png`), fullPage: true });
      }
    }
    await fixture(page, 'mvc-customers-rows');
    await page.locator('input[type=search]').fill('missing');
    await expect(page.locator('[data-ta-no-results]')).toBeVisible();
    await page.locator('input[type=search]').fill('Example');
    await expect(page.locator('[data-ta-no-results]')).toBeHidden();
    await expect(page.locator('tbody tr')).toBeVisible();
    for (const name of ['mvc-sidebar', 'blazor-sidebar']) {
      await fixture(page, name); expect(await page.locator('.ta-branch-count').count()).toBe(0);
      await expect(page.locator('a[href="/customers"]')).toBeVisible();
    }
    await fixture(page, 'blazor-loading');
    await expect(page.locator('.ta-master-loading img')).toBeVisible();
    expect(await page.locator('.ta-master-loading-ring').evaluate(e => getComputedStyle(e).animationName)).toBe('ta-recovery-spin');
    expect(await page.locator('.ta-master-loading img').evaluate(e => getComputedStyle(e).transform)).toBe('none');
    await page.emulateMedia({ reducedMotion: 'reduce' });
    expect(await page.locator('.ta-master-loading-ring').evaluate(e => getComputedStyle(e).animationName)).toBe('none');
    await page.screenshot({ path: info.outputPath('master-loading-reduced-motion.png') });
  });
});
