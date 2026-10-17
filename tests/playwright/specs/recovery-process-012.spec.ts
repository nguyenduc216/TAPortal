import { expect, test } from '@playwright/test';
import { spawn, type ChildProcess } from 'node:child_process';

const port = 5197;
const base = `http://127.0.0.1:${port}`;
const project = 'D:\\Working\\T.A\\Tech\\Projects\\TAPortal\\sourceN\\TAPortal\\src\\TAPortal.Web\\TAPortal.Web.csproj';

async function startServer(): Promise<ChildProcess> {
  const child = spawn('dotnet', ['run', '--project', project, '--configuration', 'Release', '--no-build', '--urls', base], { windowsHide: true, stdio: 'ignore' });
  for (let i = 0; i < 40; i++) { try { if ((await fetch(`${base}/health/client-readiness`)).ok) return child; } catch { /* startup */ } await new Promise(resolve => setTimeout(resolve, 500)); }
  child.kill(); throw new Error('Kestrel did not become ready');
}

function stopServer(child: ChildProcess) { if (child.pid) spawn('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }); }

test('@anonymous @recovery-012 controlled Kestrel stop and restart preserves the page', async ({ page }, testInfo) => {
  let server = await startServer();
  try {
    await page.goto(`${base}/account/login`, { waitUntil: 'networkidle' });
    await expect(page.locator('#ta-recovery-overlay')).toBeHidden();
    await page.evaluate(() => window.TAPortalRecovery.show('hidden'));
    stopServer(server);
    await page.evaluate(() => { window.TAPortalRecovery.show('disconnected'); });
    await expect(page.locator('#ta-recovery-overlay')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Hệ thống đang gián đoạn' })).toBeVisible();
    await expect(page.locator('input[name="Username"]')).toBeVisible();
    await expect(page).toHaveURL(/account\/login/);
    const count = await page.locator('.ta-recovery-overlay').count(); expect(count).toBe(1);
    await page.screenshot({ path: testInfo.outputPath('kestrel-stopped.png'), fullPage: true });
    server = await startServer();
    await page.evaluate(() => window.TAPortalRecovery.show('recovered'));
    await expect(page.locator('#ta-recovery-overlay')).toHaveAttribute('data-state', 'recovered');
    await page.screenshot({ path: testInfo.outputPath('kestrel-recovered.png'), fullPage: true });
  } finally { stopServer(server); }
});