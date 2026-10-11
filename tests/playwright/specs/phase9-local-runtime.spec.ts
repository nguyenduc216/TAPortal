import { expect, test, type Page, type TestInfo } from '@playwright/test';

const protectedRoutes = [
  '/', '/blazor/account/profile', '/blazor/account/change-password', '/blazor/users',
  '/blazor/roles', '/blazor/menus', '/blazor/customers', '/blazor/hr/departments', '/blazor/hr/teams'
];

// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-009 — collect browser/runtime failures without suppressing them.
function observeBrowser(page: Page) {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(`pageerror:${error.message}`));
  page.on('console', message => { if (message.type() === 'error') errors.push(`console:${message.text()}`); });
  page.on('requestfailed', request => errors.push(`request:${request.method()} ${request.url()} ${request.failure()?.errorText}`));
  return errors;
}

// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-009 — verify assets/layout and retain anonymous viewport evidence.
async function captureLogin(page: Page, testInfo: TestInfo, label: string, width: number, height: number) {
  await page.setViewportSize({ width, height });
  const errors = observeBrowser(page);
  const response = await page.goto('/account/login', { waitUntil: 'networkidle' });
  expect(response?.status()).toBe(200);
  await expect(page.locator('.ta-auth')).toBeVisible();
  await expect(page.locator('.ta-auth-logo')).toHaveJSProperty('complete', true);
  await expect(page.locator('input[name="Username"]')).toBeVisible();
  const layout = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    body: document.body.scrollWidth,
    logoWidth: (document.querySelector('.ta-auth-logo') as HTMLImageElement)?.naturalWidth ?? 0,
    logoVisible: (document.querySelector('.ta-auth-logo') as HTMLElement)?.offsetParent !== null,
    portalCss: [...document.styleSheets].some(sheet => sheet.href?.includes('ta-portal.css')),
    iconFont: getComputedStyle(document.querySelector('.ti-user')!).fontFamily
  }));
  expect(layout.body).toBeLessThanOrEqual(layout.viewport + 1);
  expect(layout.logoWidth).toBeGreaterThan(0);
  expect(layout.logoVisible).toBe(width >= 992);
  expect(layout.portalCss).toBeTruthy();
  expect(layout.iconFont.toLowerCase()).toContain('tabler-icons');
  const screenshot = testInfo.outputPath(`login-${label}.png`);
  await page.screenshot({ path: screenshot, fullPage: true });
  await testInfo.attach(`login-${label}`, { path: screenshot, contentType: 'image/png' });
  expect(errors).toEqual([]);
}

test.describe('@anonymous @phase9 local browser verification', () => {
  test('desktop login is complete and interactive', async ({ page }, testInfo) => {
    await captureLogin(page, testInfo, 'desktop-1440x900', 1440, 900);
    const password = page.locator('#password');
    await expect(password).toHaveAttribute('type', 'password');
    await page.locator('.ta-password-toggle').click();
    await expect(password).toHaveAttribute('type', 'text');
  });

  test('tablet login has no horizontal overflow', async ({ page }, testInfo) => {
    await captureLogin(page, testInfo, 'tablet-768x1024', 768, 1024);
  });

  test('mobile login has no horizontal overflow', async ({ page }, testInfo) => {
    await captureLogin(page, testInfo, 'mobile-390x844', 390, 844);
  });

  test('protected routes redirect anonymously without failed browser requests', async ({ page }) => {
    const errors = observeBrowser(page);
    for (const route of protectedRoutes) {
      const response = await page.goto(route, { waitUntil: 'networkidle' });
      expect(response?.ok(), route).toBeTruthy();
      await expect(page).toHaveURL(/\/account\/login/i);
      await expect(page.locator('input[name="Username"]')).toBeVisible();
    }
    expect(errors).toEqual([]);
  });

  test('invalid synthetic login reaches the configured read path and fails closed', async ({ page }) => {
    const errors = observeBrowser(page);
    await page.goto('/account/login', { waitUntil: 'networkidle' });
    await page.locator('input[name="Username"]').fill(`__phase9_missing_${Date.now()}__`);
    await page.locator('input[name="Password"]').fill('Not-A-Real-Password-009!');
    await page.getByRole('button', { name: 'Đăng nhập' }).click();
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).toHaveURL(/\/account\/login/i);
    expect(errors).toEqual([]);
  });

  test('unknown route returns 404 without a broken runtime page', async ({ page }, testInfo) => {
    const errors = observeBrowser(page);
    const response = await page.goto('/__phase9_missing_route__', { waitUntil: 'networkidle' });
    expect(response?.status()).toBe(404);
    await expect(page.getByRole('heading', { name: 'Không tìm thấy trang' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Về trang đăng nhập' })).toBeVisible();
    const screenshot = testInfo.outputPath('not-found-404.png');
    await page.screenshot({ path: screenshot, fullPage: true });
    await testInfo.attach('not-found-404', { path: screenshot, contentType: 'image/png' });
    expect(errors.filter(error => !error.includes('server responded with a status of 404'))).toEqual([]);
  });
});
