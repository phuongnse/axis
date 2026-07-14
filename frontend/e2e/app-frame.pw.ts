import { Buffer } from 'node:buffer';
import { expect, type Locator, type Page, test } from '@playwright/test';

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

const systemRule = (definitionKey: string, name: string, targetTypeKeys: string[]) => ({
  definitionKey,
  name,
  description: `${name} validation.`,
  origin: 'System',
  scope: 'Field',
  outcomeKind: 'Validation',
  status: 'Published',
  latestPublishedVersion: 1,
  applicability: { targetTypeKeys, configurationConstraints: {} },
  parameters: [],
});

const fieldRuleDefinitions = {
  items: [
    systemRule('field.required', 'Required value', [
      'Text',
      'Integer',
      'Decimal',
      'Date',
      'DateTime',
      'Boolean',
      'Choice',
    ]),
    systemRule('field.text_length', 'Text length', ['Text']),
    systemRule('field.numeric_range', 'Numeric range', ['Integer', 'Decimal']),
    systemRule('field.decimal_precision', 'Decimal precision', ['Decimal']),
    systemRule('field.date_range', 'Date range', ['Date']),
    systemRule('field.datetime_range', 'Date and time range', ['DateTime']),
    systemRule('field.text_pattern', 'Text pattern', ['Text']),
    systemRule('field.text_format', 'Text format', ['Text']),
    systemRule('field.choice_selection_count', 'Choice selection count', ['Choice']),
  ],
  totalCount: 9,
  page: 1,
  pageSize: 100,
};

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

  await page.route('**/api/rules?**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(fieldRuleDefinitions),
    });
  });

  await page.route('**/api/business-object-definitions?**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items: [], totalCount: 0, page: 1, pageSize: 20 }),
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

async function expectRulesCatalogScrolls(page: Page): Promise<void> {
  const viewport = page.locator('[data-slot="data-table"] [data-slot="data-table-viewport"]');
  await expect(viewport).toBeVisible();
  await expect
    .poll(() =>
      viewport.evaluate((element) => ({
        clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight,
      })),
    )
    .toMatchObject({ clientHeight: expect.any(Number), scrollHeight: expect.any(Number) });

  const canScroll = await viewport.evaluate(
    (element) => element.scrollHeight > element.clientHeight,
  );
  expect(canScroll).toBe(true);
  await viewport.evaluate((element) => element.scrollTo({ top: element.scrollHeight }));
  await expect.poll(() => viewport.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
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

async function visualState(locator: Locator) {
  return locator.evaluate((node) => {
    const style = getComputedStyle(node);
    return { backgroundColor: style.backgroundColor, color: style.color };
  });
}

async function settledVisualState(locator: Locator) {
  await locator.evaluate((element) => {
    const animations = new Set<Animation>();
    for (let node: Element | null = element; node; node = node.parentElement) {
      for (const animation of node.getAnimations()) animations.add(animation);
    }
    return Promise.allSettled([...animations].map((animation) => animation.finished));
  });
  return visualState(locator);
}

async function hoveredVisualState(locator: Locator) {
  await locator.hover();
  return settledVisualState(locator);
}

async function colorDistance(page: Page, first: string, second: string) {
  return page.evaluate(
    ([left, right]) => {
      const canvas = document.createElement('canvas');
      const context = canvas.getContext('2d', { willReadFrequently: true });
      if (!context) throw new Error('Expected a 2D canvas context');

      const rgb = (color: string) => {
        context.clearRect(0, 0, 1, 1);
        context.fillStyle = color;
        context.fillRect(0, 0, 1, 1);
        return context.getImageData(0, 0, 1, 1).data;
      };
      const leftRgb = rgb(left);
      const rightRgb = rgb(right);
      return Math.hypot(
        leftRgb[0] - rightRgb[0],
        leftRgb[1] - rightRgb[1],
        leftRgb[2] - rightRgb[2],
      );
    },
    [first, second] as const,
  );
}

test.describe('app frame', () => {
  test('interaction states share one convention across overlays, navigation, and table menus', async ({
    page,
  }) => {
    await mockAuthenticatedSession(page);
    await page.route('**/api/users/me/preferences/theme', async (route) => {
      const theme = JSON.parse(route.request().postData() ?? '{}').theme ?? 'light';
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ theme }),
      });
    });
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });

    await expect(page).toHaveURL(/\/dashboard$/);
    await page
      .getByRole('navigation', { name: 'Modules' })
      .getByRole('link', { name: 'Rules' })
      .click();
    await expect(page).toHaveURL(/\/rules$/);
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();

    for (const mode of ['light', 'dark'] as const) {
      await page.getByRole('button', { name: 'Account menu' }).click();
      if (mode === 'dark') {
        await page.getByRole('button', { name: 'Dark' }).click();
        await expect(page.locator('html')).toHaveClass(/dark/);
      } else {
        await expect(page.locator('html')).not.toHaveClass(/dark/);
      }

      const selectedOptionState = await settledVisualState(
        page.getByRole('button', { name: 'English' }),
      );
      const selectedOptionHoverState = await hoveredVisualState(
        page.getByRole('button', { name: 'English' }),
      );
      const optionSurfaceState = await visualState(
        page.locator('[data-slot="popover-content"][aria-label="Account menu"]'),
      );
      const optionHoverState = await hoveredVisualState(
        page.getByRole('button', { name: 'Vietnamese' }),
      );
      await page.keyboard.press('Escape');

      const moduleNavigation = page.getByRole('navigation', { name: 'Modules' });
      const currentNavigationState = await settledVisualState(
        moduleNavigation.getByRole('link', { name: 'Rules' }),
      );
      const transientNavigationState = await hoveredVisualState(
        moduleNavigation.getByRole('link', { name: 'Business objects' }),
      );

      const columnMenuTrigger = page
        .locator('[data-slot="table-header"] [data-slot="dropdown-menu-trigger"]')
        .first();
      await columnMenuTrigger.click();
      const tableMenuHighlightState = await hoveredVisualState(
        page.locator('[data-slot="dropdown-menu-item"]:visible').first(),
      );
      await page.keyboard.press('Escape');

      await page.getByRole('combobox', { name: 'Rows per page' }).click();
      const selectHighlightState = await hoveredVisualState(
        page.getByRole('option', { name: '10', exact: true }),
      );
      await page.keyboard.press('Escape');

      expect(currentNavigationState, `${mode} persistent row state`).toEqual(selectedOptionState);
      expect(selectedOptionHoverState, `${mode} persistent state retained on hover`).toEqual(
        selectedOptionState,
      );
      expect(transientNavigationState, `${mode} navigation transient state`).toEqual(
        optionHoverState,
      );
      expect(tableMenuHighlightState, `${mode} table-menu transient state`).toEqual(
        optionHoverState,
      );
      expect(selectHighlightState, `${mode} select transient state`).toEqual(optionHoverState);

      const tableRowHighlightState = await hoveredVisualState(
        page
          .locator('[data-slot="table-body"] [data-slot="table-row"]')
          .first()
          .locator('[data-slot="table-cell"]')
          .first(),
      );
      expect(tableRowHighlightState, `${mode} table-row transient state`).toEqual(optionHoverState);
      expect(optionHoverState, `${mode} transient and persistent states differ`).not.toEqual(
        selectedOptionState,
      );
      const transientDistance = await colorDistance(
        page,
        optionHoverState.backgroundColor,
        optionSurfaceState.backgroundColor,
      );
      const persistentDistance = await colorDistance(
        page,
        selectedOptionState.backgroundColor,
        optionSurfaceState.backgroundColor,
      );
      expect(persistentDistance, `${mode} persistent emphasis exceeds transient`).toBeGreaterThan(
        transientDistance * 1.25,
      );
    }
  });

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
    let completeThemeSave: (() => void) | undefined;
    await page.route('**/api/users/me/preferences/theme', async (route) => {
      await new Promise<void>((resolve) => {
        completeThemeSave = resolve;
      });
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ theme: 'dark' }),
      });
    });
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });

    await expect(page).toHaveURL(/\/dashboard$/);
    await expectAppFrameReady(page, 'Dashboard');
    await expect(page.getByRole('navigation', { name: 'Modules' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Business objects' })).toHaveAttribute(
      'href',
      '/business-objects',
    );
    const rulesLink = page.getByRole('link', { name: 'Rules' });
    await expect(rulesLink).toHaveAttribute('href', '/rules');
    expect(await rulesLink.evaluate((node) => getComputedStyle(node).justifyContent)).toBe(
      'flex-start',
    );
    await expect(page.getByRole('main')).toHaveText('');
    await expect(page.getByRole('contentinfo')).toContainText('Version 0.1.0');
    await expect(page.getByRole('contentinfo')).toContainText('Axis Platform');
    await expect(page.getByRole('contentinfo')).toContainText('2026');
    await expectShellRegionsFitViewport(page);
    await page.getByRole('button', { name: 'Account menu' }).click();
    await expect(page.getByText(profile.fullName)).toHaveCount(1);
    await expect(page.getByText('Preferences')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
    const accountMenu = page.locator('[data-slot="popover-content"][aria-label="Account menu"]');
    await accountMenu.evaluate((element) =>
      Promise.all(element.getAnimations({ subtree: true }).map((animation) => animation.finished)),
    );
    const initialMenuBox = await accountMenu.boundingBox();
    await page.getByRole('button', { name: 'Dark' }).click();
    await expect(page.getByText('Saving...')).toBeVisible();
    const pendingMenuBox = await accountMenu.boundingBox();
    expect(Math.round(pendingMenuBox?.height ?? 0)).toBe(Math.round(initialMenuBox?.height ?? 0));
    completeThemeSave?.();
    await expect(page.getByText('Saving...')).toBeHidden();
    const savedMenuBox = await accountMenu.boundingBox();
    expect(Math.round(savedMenuBox?.height ?? 0)).toBe(Math.round(initialMenuBox?.height ?? 0));
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);
    await page.keyboard.press('Escape');

    await page.getByRole('link', { name: 'Rules' }).click();
    await expect(page).toHaveURL(/\/rules$/);
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();
    await expectRouteViewportTouchesMain(page, '32px');
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);
    await expectRulesCatalogScrolls(page);

    await page.goBack();
    await expect(page).toHaveURL(/\/dashboard$/);

    await page.setViewportSize({ width: 390, height: 844 });

    await expectAppFrameReady(page, 'Dashboard');
    await expect(page.getByRole('navigation', { name: 'Modules' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Business objects' })).toHaveAttribute(
      'href',
      '/business-objects',
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
    await expectRouteViewportTouchesMain(page, '16px');
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);
    await expectRulesCatalogScrolls(page);
    expect(pageErrors).toEqual([]);
  });
});
