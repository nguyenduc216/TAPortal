import { expect, test } from '@playwright/test';
import fs from 'node:fs';

// CHANGE_ID: TAPORTAL-20261017-RUNTIME-RECOVERY-001
// Read-only diagnostics; output contains page markup, so keep the Playwright output outside Git.
const state = process.env.TAPORTAL_E2E_STORAGE_STATE;
test.describe('@authenticated runtime click diagnostics', () => {
  test.skip(!state || !fs.existsSync(state), 'External owner session required');
  test.use({ storageState: state });

  for (const viewport of [{ name: 'desktop', width: 1440, height: 900 }, { name: 'mobile', width: 390, height: 844 }]) {
    test(`${viewport.name} Edit targets and overlay hit tests`, async ({ page }) => {
      await page.setViewportSize(viewport);
      const errors: string[] = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
      for (const route of ['/blazor/hr/departments', '/blazor/hr/teams', '/blazor/customers', '/blazor/customers/01900000-0000-4000-8000-000000000041']) {
        await page.goto(route);
        await expect(page).not.toHaveURL(/\/account\/login/i);
        await page.waitForTimeout(1200);
        const snapshot = await page.evaluate(() => {
          const inspect = (element: Element) => {
            const rect = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            return { tag: element.tagName, id: element.id, className: String(element.className), text: element.textContent?.trim().slice(0, 85),
              hidden: (element as HTMLElement).hidden, display: style.display, visibility: style.visibility,
              zIndex: style.zIndex, pointerEvents: style.pointerEvents,
              rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height } };
          };
          const targets = [...document.querySelectorAll('button,a')].filter(element => /^(Sửa|Edit)$/i.test(element.textContent?.trim() || '')).slice(0, 3);
          return { url: location.href, headings: [...document.querySelectorAll('h1')].map(element => element.textContent?.trim()),
            recovery: (window as typeof window & { TAPortalRecovery?: { getDiagnostics(): unknown } }).TAPortalRecovery?.getDiagnostics(),
            layers: [...document.querySelectorAll('.ta-loading-overlay,#ta-recovery-overlay,#components-reconnect-modal,.modal-backdrop,dialog,[role="dialog"]')].map(inspect),
            targets: targets.map(element => { const rect = element.getBoundingClientRect(); const x = rect.x + rect.width / 2; const y = rect.y + rect.height / 2;
              return { target: inspect(element), hit: document.elementFromPoint(x, y) && inspect(document.elementFromPoint(x, y)!),
                stack: document.elementsFromPoint(x, y).slice(0, 6).map(inspect) }; }) };
        });
        console.log(JSON.stringify({ device: viewport.name, route, snapshot, errors: errors.slice(-5) }));
        const edit = page.getByRole('button', { name: 'Sửa', exact: true }).first();
        if (await edit.isVisible()) {
          await edit.click({ timeout: 5000 });
          console.log(JSON.stringify({ device: viewport.name, route, clicked: true, dialogs: await page.locator('dialog,[role="dialog"]').count() }));
        }
      }
      expect(errors).toEqual([]);
    });
  }
});