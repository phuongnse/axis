import { Buffer } from 'node:buffer';
import { expect, type Page, test } from '@playwright/test';

const profile = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'app-frame@example.com',
  fullName: 'App Frame User',
  isActive: true,
  language: 'en',
  theme: 'light',
  workspaceId: '22222222-2222-4222-8222-222222222222',
  workspaces: [
    {
      id: '22222222-2222-4222-8222-222222222222',
      name: 'Personal workspace',
      slug: 'personal-workspace',
      type: 'Personal',
      isCurrent: true,
    },
  ],
};

const fieldRuleDefinitions = [
  {
    definitionKey: 'field.required',
    displayName: 'Required',
    description: 'Requires a value before publishing records.',
    supportedFieldTypes: ['Text', 'Integer', 'Decimal', 'Date', 'Boolean', 'SingleSelect'],
    parameters: [],
    category: 'Contract',
  },
  {
    definitionKey: 'field.text_length',
    displayName: 'Text length',
    description: 'Constrains text values to an accepted length range.',
    supportedFieldTypes: ['Text'],
    parameters: [
      { key: 'min', type: 'Integer', isRequired: false, allowMultiple: false },
      { key: 'max', type: 'Integer', isRequired: false, allowMultiple: false },
    ],
    category: 'Text',
  },
];

function base64UrlJson(value: unknown): string {
  return Buffer.from(JSON.stringify(value), 'utf8').toString('base64url');
}

function accessToken(): string {
  return [
    base64UrlJson({ alg: 'none', typ: 'JWT' }),
    base64UrlJson({
      sub: profile.id,
      email: profile.email,
      name: profile.fullName,
    }),
    'signature',
  ].join('.');
}

async function mockAuthenticatedSession(page: Page): Promise<void> {
  await page.addInitScript(() => {
    window.__AXIS_DISABLE_DEVTOOLS__ = true;
    localStorage.setItem('axis.language', 'en');
    localStorage.setItem('axis.theme', 'light');
  });

  await page.route('**/connect/authorize**', async (route) => {
    const requestUrl = new URL(route.request().url());
    const state = requestUrl.searchParams.get('state') ?? '';
    const callbackUrl = new URL('/callback', requestUrl.origin);
    callbackUrl.searchParams.set('code', 'app-frame-code');
    callbackUrl.searchParams.set('state', state);

    await route.fulfill({
      status: 302,
      headers: { location: callbackUrl.toString() },
    });
  });

  await page.route('**/connect/token', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ access_token: accessToken() }),
    });
  });

  await page.route('**/api/users/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(profile),
    });
  });

  await page.route('**/api/rules/field-rule-definitions', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(fieldRuleDefinitions),
    });
  });
}

async function expectNoPageOverflow(page: Page): Promise<void> {
  await expect
    .poll(async () =>
      page.evaluate(() => {
        const documentElement = document.documentElement;
        const body = document.body;
        const tolerance = 1;

        return {
          bodyFits: body.scrollWidth <= window.innerWidth + tolerance,
          documentFits: documentElement.scrollWidth <= window.innerWidth + tolerance,
        };
      }),
    )
    .toEqual({ bodyFits: true, documentFits: true });
}

async function expectNoDocumentScroll(page: Page): Promise<void> {
  await expect
    .poll(async () =>
      page.evaluate(() => {
        const documentElement = document.documentElement;
        const tolerance = 1;

        return documentElement.scrollHeight <= window.innerHeight + tolerance;
      }),
    )
    .toBe(true);
}

async function expectShellRegionsFitViewport(page: Page): Promise<void> {
  const viewportWidth = await page.evaluate(() => window.innerWidth);
  const regions = [
    { name: 'top bar content', locator: page.locator('header > div') },
    { name: 'module navigation', locator: page.getByRole('navigation', { name: 'Modules' }) },
    { name: 'main content', locator: page.getByRole('main') },
    { name: 'footer content', locator: page.locator('footer > div') },
  ];

  for (const region of regions) {
    const box = await region.locator.boundingBox();
    if (!box) {
      throw new Error(`Expected ${region.name} to be visible`);
    }

    expect(Math.round(box.width), region.name).toBeGreaterThan(0);
    expect(Math.round(box.x), region.name).toBeGreaterThanOrEqual(-1);
    expect(Math.round(box.x + box.width), region.name).toBeLessThanOrEqual(viewportWidth + 1);
  }
}

async function expectRouteViewportTouchesMain(
  page: Page,
  expectedPaddingBottom: string,
): Promise<void> {
  await expect
    .poll(async () =>
      page.evaluate(() => {
        const main = document.querySelector('main');
        const routeRoot = main?.firstElementChild as HTMLElement | null;

        if (!main || !routeRoot) {
          return null;
        }

        const mainBox = main.getBoundingClientRect();
        const routeBox = routeRoot.getBoundingClientRect();
        const tolerance = 1;

        return {
          mainPadding: getComputedStyle(main).padding,
          routePaddingBottom: getComputedStyle(routeRoot).paddingBottom,
          touchesBottom: Math.abs(routeBox.bottom - mainBox.bottom) <= tolerance,
          touchesRight: Math.abs(routeBox.right - mainBox.right) <= tolerance,
          touchesTop: Math.abs(routeBox.top - mainBox.top) <= tolerance,
        };
      }),
    )
    .toEqual({
      mainPadding: '0px',
      routePaddingBottom: expectedPaddingBottom,
      touchesBottom: true,
      touchesRight: true,
      touchesTop: true,
    });
}

async function expectAppFrameReady(page: Page, title: string): Promise<void> {
  await expect(page.getByRole('banner')).toContainText(title, { timeout: 15_000 });
}

test.describe('app frame', () => {
  test('AT-002 desktop and mobile frame render without console errors or document overflow', async ({
    page,
  }) => {
    const pageErrors: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') {
        pageErrors.push(message.text());
      }
    });
    page.on('pageerror', (error) => pageErrors.push(error.message));

    await mockAuthenticatedSession(page);
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });

    await expect(page).toHaveURL(/\/dashboard$/);
    await expectAppFrameReady(page, 'Dashboard');
    await expect(page.getByRole('navigation', { name: 'Modules' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Business objects' })).toHaveAttribute(
      'href',
      '/objects',
    );
    await expect(page.getByRole('link', { name: 'Rules' })).toHaveAttribute('href', '/rules');
    await expect(page.getByRole('main')).toHaveText('');
    await expect(page.getByRole('contentinfo')).toContainText('Version 0.1.0');
    await expect(page.getByRole('contentinfo')).toContainText('Axis Platform');
    await expect(page.getByRole('contentinfo')).toContainText('2026');
    await expectShellRegionsFitViewport(page);
    await page.getByRole('button', { name: 'Account menu' }).click();
    await expect(page.getByText(profile.fullName)).toHaveCount(1);
    await expect(page.getByText('Preferences')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);
    await page.keyboard.press('Escape');

    await page.getByRole('link', { name: 'Rules' }).click();
    await expect(page).toHaveURL(/\/rules$/);
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();
    await expectRouteViewportTouchesMain(page, '48px');
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);

    await page.goBack();
    await expect(page).toHaveURL(/\/dashboard$/);

    await page.setViewportSize({ width: 390, height: 844 });

    await expectAppFrameReady(page, 'Dashboard');
    await expect(page.getByRole('navigation', { name: 'Modules' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Business objects' })).toHaveAttribute(
      'href',
      '/objects',
    );
    await expect(page.getByRole('main')).toHaveText('');
    await expect(page.getByRole('contentinfo')).toContainText('Version 0.1.0');
    await expectShellRegionsFitViewport(page);
    await page.getByRole('button', { name: 'Account menu' }).click();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);

    await page.keyboard.press('Escape');
    await page.getByRole('link', { name: 'Rules' }).click();
    await expect(page).toHaveURL(/\/rules$/);
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();
    await expectRouteViewportTouchesMain(page, '32px');
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);
    expect(pageErrors).toEqual([]);
  });
});
