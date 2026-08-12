import { expect, type Locator, type Page, test } from '@playwright/test';
import { expectCanonicalTestLanguage } from './canonical-test-language';

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

const longIdentityProfile = {
  ...profile,
  email: 'alexandria.montgomery-sanchez@enterprise-reference-platform.example.com',
  fullName: 'Alexandria Catherine Montgomery-Sanchez',
};

interface AuthenticatedSessionOptions {
  language?: 'en' | 'vi';
  theme?: 'dark' | 'light' | 'system';
  userProfile?: typeof profile;
}

const systemRule = (definitionKey: string, name: string, targetTypeKeys: string[]) => ({
  definitionKey,
  name,
  description: `${name} validation.`,
  origin: 'BuiltIn',
  status: 'Published',
  expressionLanguageVersion: 1,
  latestPublishedVersion: 1,
  output: { type: 'Boolean', cardinality: 'Scalar' },
  inputs: [
    {
      key: 'value',
      types: targetTypeKeys.map((type) => (type === 'Choice' ? 'Text' : type)),
      isRequired: true,
      allowMultiple: false,
      allowedValues: [],
    },
  ],
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

async function mockAuthenticatedSession(
  page: Page,
  { language = 'en', theme = 'light', userProfile = profile }: AuthenticatedSessionOptions = {},
): Promise<void> {
  await page.addInitScript(
    ({ initialLanguage, initialTheme }) => {
      (window as Window & { __AXIS_DISABLE_DEVTOOLS__?: boolean }).__AXIS_DISABLE_DEVTOOLS__ = true;
      localStorage.setItem('axis.language', initialLanguage);
      localStorage.setItem('axis.theme', initialTheme);
    },
    { initialLanguage: language, initialTheme: theme },
  );

  await page.route('**/api/auth/session', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        authenticated: true,
        csrfToken: 'app-frame-csrf-token',
        user: {
          userId: userProfile.id,
          workspaceId: userProfile.workspaceId,
          email: userProfile.email,
          name: userProfile.fullName,
        },
      }),
    });
  });

  await page.route('**/api/users/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ ...userProfile, language, theme }),
    });
  });

  await page.route('**/api/workspace-context/eligible', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          workspaceId: userProfile.workspaces[0].id,
          name: userProfile.workspaces[0].name,
          slug: userProfile.workspaces[0].slug,
          type: userProfile.workspaces[0].type,
          organizationId: null,
          isCurrent: true,
        },
      ]),
    });
  });

  await page.route('**/api/module-navigation', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        availableContributionIds: ['businessObjects.definitions', 'rules.fieldDefinitions'],
      }),
    });
  });

  await page.route('**/api/business-object-definitions/actions', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ canStartCreate: true }),
    });
  });

  await page.route('**/api/rules/actions', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ canStartCreate: true }),
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

async function expectAccountIdentityResilience(
  page: Page,
  expectedProfile: Pick<typeof profile, 'email' | 'fullName'>,
): Promise<void> {
  const accountSurface = page.locator('[data-slot="account-surface"]');
  const identity = page.getByRole('region', { name: /Account|Tài khoản/ });

  const inspectText = (text: string) =>
    identity.getByText(text, { exact: true }).evaluate((element) => {
      const style = getComputedStyle(element);
      const bounds = element.getBoundingClientRect();
      return {
        clientWidth: element.clientWidth,
        fontSize: Number.parseFloat(style.fontSize),
        fontWeight: style.fontWeight,
        height: bounds.height,
        inlineEnd: bounds.right,
        inlineStart: bounds.left,
        lineHeight: Number.parseFloat(style.lineHeight),
        overflowWrap: style.overflowWrap,
        scrollWidth: element.scrollWidth,
        whiteSpace: style.whiteSpace,
      };
    });

  const [surfaceBounds, primary, secondary] = await Promise.all([
    accountSurface.boundingBox(),
    inspectText(expectedProfile.fullName),
    inspectText(expectedProfile.email),
  ]);
  if (!surfaceBounds) throw new Error('Account surface layout was not measurable.');

  const [avatarBounds, primaryBounds] = await Promise.all([
    identity.locator('[data-slot="avatar"]').boundingBox(),
    identity.getByText(expectedProfile.fullName, { exact: true }).boundingBox(),
  ]);
  if (!avatarBounds || !primaryBounds) {
    throw new Error('Account identity alignment was not measurable.');
  }
  expect(
    Math.abs(avatarBounds.y - primaryBounds.y),
    'identity avatar remains top-aligned',
  ).toBeLessThanOrEqual(1);

  expect(primary.fontSize).toBeCloseTo(14, 1);
  expect(primary.fontWeight).toBe('500');
  expect(secondary.fontSize).toBeCloseTo(12, 1);
  expect(secondary.fontWeight).toBe('400');
  for (const textLayout of [primary, secondary]) {
    expect(textLayout.whiteSpace).toBe('normal');
    expect(textLayout.overflowWrap).toBe('anywhere');
    expect(textLayout.height).toBeGreaterThan(textLayout.lineHeight);
    expect(textLayout.scrollWidth).toBeLessThanOrEqual(textLayout.clientWidth + 1);
    expect(textLayout.inlineStart).toBeGreaterThanOrEqual(surfaceBounds.x - 1);
    expect(textLayout.inlineEnd).toBeLessThanOrEqual(surfaceBounds.x + surfaceBounds.width + 1);
  }
}

async function expectAccountTargetGeometry(page: Page, minimumHeight: 32 | 44): Promise<void> {
  const targets = page
    .locator('[data-slot="account-surface"]')
    .getByRole('button')
    .or(page.getByRole('button', { name: /Account menu|Menu tài khoản/ }));
  await targets.evaluateAll((elements) =>
    Promise.allSettled(
      elements.flatMap((element) => element.getAnimations()).map((animation) => animation.finished),
    ),
  );
  const measurements = await targets.evaluateAll((elements) =>
    elements.map((element) => ({
      height: element.getBoundingClientRect().height,
      name: element.getAttribute('aria-label') ?? element.textContent?.trim() ?? '',
    })),
  );

  expect(measurements.length).toBeGreaterThan(5);
  expect(
    measurements.every(({ height }) => height >= minimumHeight - 1),
    `all Account targets meet the ${minimumHeight}px target: ${JSON.stringify(measurements)}`,
  ).toBe(true);
}

function observeUnexpectedRuntimeErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

async function expectVisibleFocusIndicator(target: Locator): Promise<void> {
  const focusStyle = await target.evaluate((element) => {
    const style = getComputedStyle(element);
    return {
      boxShadow: style.boxShadow,
      outlineStyle: style.outlineStyle,
      outlineWidth: Number.parseFloat(style.outlineWidth),
    };
  });
  expect(
    focusStyle.boxShadow !== 'none' ||
      (focusStyle.outlineStyle !== 'none' && focusStyle.outlineWidth > 0),
    JSON.stringify(focusStyle),
  ).toBe(true);
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

async function expectRouteViewportTouchesMain(page: Page): Promise<void> {
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
          touchesBottom: Math.abs(routeBox.bottom - mainBox.bottom) <= tolerance,
          touchesRight: Math.abs(routeBox.right - mainBox.right) <= tolerance,
          touchesTop: Math.abs(routeBox.top - mainBox.top) <= tolerance,
        };
      }),
    )
    .toEqual({
      mainPadding: '0px',
      touchesBottom: true,
      touchesRight: true,
      touchesTop: true,
    });
}

async function expectAppFrameReady(page: Page, title: string): Promise<void> {
  await expect(page.getByRole('banner')).toContainText(title, { timeout: 15_000 });
  await expect(page.locator('[data-axis-surface-id="authenticated-frame"]')).toHaveAttribute(
    'data-axis-surface-contract',
    'authenticated-frame',
  );
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

async function expectAccountTextContrast(page: Page): Promise<void> {
  const surface = page.locator('[data-slot="account-surface"]');
  await expect
    .poll(async () => {
      const results = await surface.evaluate((root) => {
        const canvas = document.createElement('canvas');
        canvas.width = 1;
        canvas.height = 1;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        if (!context) throw new Error('Expected a 2D canvas context');

        const rgba = (color: string): [number, number, number, number] => {
          context.clearRect(0, 0, 1, 1);
          context.fillStyle = color;
          context.fillRect(0, 0, 1, 1);
          const value = context.getImageData(0, 0, 1, 1).data;
          return [value[0], value[1], value[2], value[3] / 255];
        };
        const composite = (
          foreground: [number, number, number, number],
          background: [number, number, number, number],
        ): [number, number, number, number] => {
          const alpha = foreground[3] + background[3] * (1 - foreground[3]);
          if (alpha === 0) return [0, 0, 0, 0];
          return [
            (foreground[0] * foreground[3] + background[0] * background[3] * (1 - foreground[3])) /
              alpha,
            (foreground[1] * foreground[3] + background[1] * background[3] * (1 - foreground[3])) /
              alpha,
            (foreground[2] * foreground[3] + background[2] * background[3] * (1 - foreground[3])) /
              alpha,
            alpha,
          ];
        };
        const luminance = ([red, green, blue]: [number, number, number, number]) => {
          const linear = [red, green, blue].map((channel) => {
            const value = channel / 255;
            return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
          });
          return linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722;
        };

        const textByElement = new Map<HTMLElement, string[]>();
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
          const text = node.textContent?.trim();
          const element = node.parentElement;
          if (!text || !element) continue;
          const bounds = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          if (
            bounds.width <= 1 ||
            bounds.height <= 1 ||
            style.display === 'none' ||
            style.visibility === 'hidden'
          ) {
            continue;
          }
          textByElement.set(element, [...(textByElement.get(element) ?? []), text]);
        }

        return [...textByElement].map(([element, text]) => {
          const layers: [number, number, number, number][] = [];
          for (let node: Element | null = element; node; node = node.parentElement) {
            layers.push(rgba(getComputedStyle(node).backgroundColor));
          }
          let background: [number, number, number, number] = [255, 255, 255, 1];
          for (const layer of layers.reverse()) background = composite(layer, background);
          const foreground = composite(rgba(getComputedStyle(element).color), background);
          const foregroundLuminance = luminance(foreground);
          const backgroundLuminance = luminance(background);

          return {
            background: getComputedStyle(element).backgroundColor,
            color: getComputedStyle(element).color,
            ratio:
              (Math.max(foregroundLuminance, backgroundLuminance) + 0.05) /
              (Math.min(foregroundLuminance, backgroundLuminance) + 0.05),
            text: text.join(' '),
          };
        });
      });

      return {
        failures: results.filter(({ ratio }) => ratio < 4.5),
        hasCoverage: results.length > 10,
      };
    })
    .toEqual({ failures: [], hasCoverage: true });
}

async function expectAccountSurfaceScreenshot(page: Page, name: string): Promise<void> {
  await expectCanonicalTestLanguage(page);
  const accountSurface = page.locator('[data-slot="account-surface"]');
  await expect(accountSurface).toBeVisible();
  await expect(accountSurface).toHaveAttribute('data-axis-surface-id', 'account-actions');
  await expect(accountSurface).toHaveAttribute('data-axis-surface-contract', 'account-surface');
  await expect(accountSurface.locator('[aria-busy="true"]')).toHaveCount(0);
  await accountSurface.evaluate((element) =>
    Promise.all(element.getAnimations({ subtree: true }).map((animation) => animation.finished)),
  );
  await page.mouse.move(1, 1);
  await expect(accountSurface).toHaveScreenshot(`${name}.png`, {
    animations: 'disabled',
    caret: 'hide',
    scale: 'css',
  });
}

async function expectAccountRegionRhythmAndActionAffordance(page: Page): Promise<void> {
  const accountSurface = page.locator('[data-slot="account-surface"]');
  const regions = accountSurface.locator('[data-axis-account-region]');
  const regionInsets = await regions.evaluateAll((elements) =>
    elements.map((element) => {
      const style = getComputedStyle(element);
      return {
        end: Number.parseFloat(style.paddingInlineEnd),
        start: Number.parseFloat(style.paddingInlineStart),
      };
    }),
  );
  expect(regionInsets).toHaveLength(4);
  expect(new Set(regionInsets.map(({ start }) => start)).size, 'one region leading inset').toBe(1);
  expect(new Set(regionInsets.map(({ end }) => end)).size, 'one region trailing inset').toBe(1);
  expect(
    regionInsets.every(({ end, start }) => end === start),
    'symmetric region insets',
  ).toBe(true);

  const createOrganization = accountSurface
    .locator('[data-axis-account-region="workspace"]')
    .locator('[data-axis-account-role="section-action"]');
  await expect(createOrganization).toHaveCount(1);
  const createAffordance = await createOrganization.evaluate((element) => {
    const style = getComputedStyle(element);
    return {
      borderColor: style.borderInlineStartColor,
      borderWidth: Number.parseFloat(style.borderInlineStartWidth),
    };
  });
  expect(createAffordance.borderWidth, 'Create Organization resting boundary').toBeGreaterThan(0);
  expect(createAffordance.borderColor, 'Create Organization resting boundary').not.toBe(
    'rgba(0, 0, 0, 0)',
  );
}

async function invokePreferenceAction(
  page: Page,
  action: Locator,
  preference: 'language' | 'theme',
): Promise<void> {
  const response = page.waitForResponse((candidate) =>
    new URL(candidate.url()).pathname.endsWith(`/api/users/me/preferences/${preference}`),
  );
  await action.click();
  expect((await response).ok()).toBe(true);
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
    await expect(page).toHaveURL(/\/rules(?:\?.*)?$/);
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();

    for (const mode of ['light', 'dark'] as const) {
      await page.getByRole('button', { name: /Account menu/ }).click();
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
      const optionSurfaceState = await visualState(page.locator('[data-slot="account-surface"]'));
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

  test('AT-004 account surface visual contract covers canonical EN light and dark desktop and compact', async ({
    page,
  }) => {
    const runtimeErrors = observeUnexpectedRuntimeErrors(page);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await mockAuthenticatedSession(page, { userProfile: longIdentityProfile });
    await page.route('**/api/users/me/preferences/language', async (route) => {
      const language = JSON.parse(route.request().postData() ?? '{}').language ?? 'en';
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ language }),
      });
    });
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
    await expectAppFrameReady(page, 'Dashboard');
    await page.getByRole('button', { name: /Account menu/ }).click();
    await expect(page.getByRole('button', { name: 'Personal' })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    await expectAccountIdentityResilience(page, longIdentityProfile);
    await expectAccountTargetGeometry(page, 32);
    await expectAccountRegionRhythmAndActionAffordance(page);
    await expectAccountTextContrast(page);
    await expectAccountSurfaceScreenshot(page, 'account-surface-light-desktop-en');

    const darkDesktopOption = page.getByRole('button', { name: 'Dark' });
    await invokePreferenceAction(page, darkDesktopOption, 'theme');
    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(darkDesktopOption).toHaveAttribute('aria-pressed', 'true');
    await expect(darkDesktopOption).not.toHaveAttribute('aria-busy');
    await expectAccountTextContrast(page);
    await expectAccountSurfaceScreenshot(page, 'account-surface-dark-desktop-en');

    await page.setViewportSize({ width: 390, height: 844 });
    await expectAccountTargetGeometry(page, 44);
    await expectAccountRegionRhythmAndActionAffordance(page);
    await expectNoPageOverflow(page);
    await expectAccountSurfaceScreenshot(page, 'account-surface-dark-compact-en');

    const lightCompactOption = page.getByRole('button', { name: 'Light' });
    await invokePreferenceAction(page, lightCompactOption, 'theme');
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    await expect(lightCompactOption).toHaveAttribute('aria-pressed', 'true');
    await expect(lightCompactOption).not.toHaveAttribute('aria-busy');
    await expectAccountSurfaceScreenshot(page, 'account-surface-light-compact-en');
    expect(runtimeErrors).toEqual([]);
  });

  test('AT-002 Account reflows localized identity and controls at the 320 CSS pixel boundary', async ({
    page,
  }) => {
    const runtimeErrors = observeUnexpectedRuntimeErrors(page);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await mockAuthenticatedSession(page, {
      language: 'vi',
      userProfile: longIdentityProfile,
    });
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    await expect(page).toHaveTitle('Axis Platform');
    await expectAppFrameReady(page, 'Bảng điều khiển');
    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');

    const accountTrigger = page.getByRole('button', { name: /Menu tài khoản/ });
    await expect(accountTrigger).toHaveAccessibleName(new RegExp(longIdentityProfile.fullName));
    await accountTrigger.click();

    const accountSurface = page.locator('[data-slot="account-surface"]');
    await expect(accountSurface).toHaveAttribute('data-axis-surface-contract', 'account-surface');
    await expect(accountSurface).toHaveAttribute('data-axis-surface-id', 'account-actions');
    await expect(page.getByRole('button', { name: 'Cá nhân' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expectAccountIdentityResilience(page, longIdentityProfile);

    const ariaTree = await accountSurface.ariaSnapshot();
    for (const semanticEntry of [
      'dialog "Menu tài khoản"',
      'region "Tài khoản"',
      'region "Workspace"',
      'region "Tùy chọn"',
      'group "Ngôn ngữ"',
      'group "Giao diện"',
      'button "Đăng xuất"',
    ]) {
      expect(ariaTree).toContain(semanticEntry);
    }

    await page.evaluate(() => {
      for (const element of document.querySelectorAll<HTMLElement>(
        '[data-slot="account-surface"], [data-slot="account-surface"] *',
      )) {
        element.style.setProperty('letter-spacing', '0.12em', 'important');
        element.style.setProperty('line-height', '1.5', 'important');
        element.style.setProperty('word-spacing', '0.16em', 'important');
      }
    });

    await expectAccountIdentityResilience(page, longIdentityProfile);
    await expectAccountTargetGeometry(page, 44);
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);
    const surfaceOverflow = await accountSurface.evaluate((surface) => ({
      clientWidth: surface.clientWidth,
      offenders: Array.from(surface.querySelectorAll<HTMLElement>('*'))
        .map((element) => ({
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
          slot: element.dataset.slot ?? element.dataset.axisAccountRegion ?? element.tagName,
        }))
        .filter(({ clientWidth, scrollWidth }) => scrollWidth > clientWidth + 1),
      scrollWidth: surface.scrollWidth,
    }));
    expect(
      surfaceOverflow.scrollWidth,
      JSON.stringify(surfaceOverflow.offenders),
    ).toBeLessThanOrEqual(surfaceOverflow.clientWidth + 1);

    await page.mouse.move(1, 1);
    await expect(accountSurface).toHaveScreenshot('account-surface-light-compact-vi-reflow.png', {
      animations: 'disabled',
      caret: 'hide',
      scale: 'css',
    });

    const signOut = page.getByRole('button', { name: 'Đăng xuất' });
    for (let attempt = 0; attempt < 24; attempt += 1) {
      if (await signOut.evaluate((element) => element === document.activeElement)) break;
      await page.keyboard.press('Tab');
    }
    await expect(signOut).toBeFocused();
    await expectVisibleFocusIndicator(signOut);
    await expect(signOut).toBeInViewport();
    expect(runtimeErrors).toEqual([]);
  });

  test('AT-002 compact Account keeps a long Workspace set keyboard-reachable', async ({ page }) => {
    const longWorkspaceName = 'Organization Arbeitsunfaehigkeitsbescheinigungsverwaltung 01';
    await mockAuthenticatedSession(page);
    await page.unroute('**/api/workspace-context/eligible');
    await page.route('**/api/workspace-context/eligible', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            workspaceId: profile.workspaces[0].id,
            name: profile.workspaces[0].name,
            slug: profile.workspaces[0].slug,
            type: profile.workspaces[0].type,
            organizationId: null,
            isCurrent: true,
          },
          ...Array.from({ length: 12 }, (_, index) => ({
            workspaceId: `33333333-3333-4333-8333-${String(index + 1).padStart(12, '0')}`,
            name:
              index === 0
                ? longWorkspaceName
                : `Organization Workspace ${String(index + 1).padStart(2, '0')}`,
            slug: `organization-workspace-${index + 1}`,
            type: 'Organization',
            organizationId: `44444444-4444-4444-8444-${String(index + 1).padStart(12, '0')}`,
            isCurrent: false,
          })),
        ]),
      });
    });
    await page.setViewportSize({ width: 390, height: 600 });
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    await expectAppFrameReady(page, 'Dashboard');

    await page.getByRole('button', { name: /Account menu/ }).click();
    const firstOrganization = page.getByRole('button', {
      name: longWorkspaceName,
    });
    const signOut = page.getByRole('button', { name: 'Sign out' });
    await expect(firstOrganization).toBeVisible();
    const labelLayout = await firstOrganization
      .locator('[data-slot="option-item-label"]')
      .evaluate((element) => {
        const style = getComputedStyle(element);
        return {
          clientWidth: element.clientWidth,
          fontSize: style.fontSize,
          fontWeight: style.fontWeight,
          height: element.getBoundingClientRect().height,
          lineHeight: Number.parseFloat(style.lineHeight),
          overflowWrap: style.overflowWrap,
          scrollWidth: element.scrollWidth,
          whiteSpace: style.whiteSpace,
        };
      });
    expect(labelLayout.fontSize).toBe('14px');
    expect(labelLayout.fontWeight).toBe('500');
    expect(labelLayout.whiteSpace).toBe('normal');
    expect(labelLayout.overflowWrap).toBe('break-word');
    expect(labelLayout.height).toBeGreaterThan(labelLayout.lineHeight);
    expect(labelLayout.scrollWidth).toBeLessThanOrEqual(labelLayout.clientWidth + 1);
    await firstOrganization.focus();

    for (let attempt = 0; attempt < 32; attempt += 1) {
      if (await signOut.evaluate((element) => element === document.activeElement)) break;
      await page.keyboard.press('Tab');
    }

    await expect(signOut).toBeFocused();
    await expectVisibleFocusIndicator(signOut);
    await expect(signOut).toBeInViewport();
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);
  });

  test('AT-003 both Workspace directions keep the account view and scroll geometry stable', async ({
    page,
  }) => {
    const personalWorkspace = {
      workspaceId: '22222222-2222-4222-8222-222222222222',
      name: 'Personal workspace',
      slug: 'personal-workspace',
      type: 'Personal' as const,
      organizationId: null,
    };
    const organizationWorkspace = {
      workspaceId: '33333333-3333-4333-8333-333333333333',
      name: 'Axis Reference Product',
      slug: 'axis-reference-product',
      type: 'Organization' as const,
      organizationId: '44444444-4444-4444-8444-444444444444',
    };
    let currentWorkspaceId = organizationWorkspace.workspaceId;
    let targetWorkspaceId = personalWorkspace.workspaceId;
    let releaseConfirmation!: () => void;
    let releaseSessionRestore!: () => void;
    let sessionRestoreStarted!: () => void;
    let confirmationGate!: Promise<void>;
    let sessionRestoreGate!: Promise<void>;
    let sessionRestoreRequest!: Promise<void>;
    const armTransitionGates = () => {
      confirmationGate = new Promise<void>((resolve) => {
        releaseConfirmation = resolve;
      });
      sessionRestoreGate = new Promise<void>((resolve) => {
        releaseSessionRestore = resolve;
      });
      sessionRestoreRequest = new Promise<void>((resolve) => {
        sessionRestoreStarted = resolve;
      });
    };
    armTransitionGates();
    let holdNextSessionRestore = false;
    const workspaces = [personalWorkspace, organizationWorkspace];
    const administratorContributions = [
      'identity.memberships',
      'identity.service-identities',
      'authorization.product-roles',
      'solutions.management',
    ];
    const productContributions = [
      ...administratorContributions.slice(0, 3),
      'businessObjects.definitions',
      'rules.fieldDefinitions',
      administratorContributions[3],
    ];

    await page.addInitScript(() => {
      (window as Window & { __AXIS_DISABLE_DEVTOOLS__?: boolean }).__AXIS_DISABLE_DEVTOOLS__ = true;
      localStorage.setItem('axis.language', 'en');
      localStorage.setItem('axis.theme', 'light');
    });
    await page.route('**/api/auth/session', async (route) => {
      if (holdNextSessionRestore) {
        holdNextSessionRestore = false;
        sessionRestoreStarted();
        await sessionRestoreGate;
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          authenticated: true,
          csrfToken: 'app-frame-csrf-token',
          user: {
            userId: profile.id,
            workspaceId: currentWorkspaceId,
            email: profile.email,
            name: profile.fullName,
          },
        }),
      });
    });
    await page.route('**/api/users/me', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          ...profile,
          workspaceId: currentWorkspaceId,
          workspaces: workspaces.map((workspace) => ({
            id: workspace.workspaceId,
            name: workspace.name,
            slug: workspace.slug,
            type: workspace.type,
            isCurrent: workspace.workspaceId === currentWorkspaceId,
          })),
        }),
      });
    });
    await page.route('**/api/workspace-context/eligible', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(
          workspaces.map((workspace) => ({
            ...workspace,
            isCurrent: workspace.workspaceId === currentWorkspaceId,
          })),
        ),
      });
    });
    await page.route('**/api/workspace-context/begin', async (route) => {
      const request = JSON.parse(route.request().postData() ?? '{}') as {
        targetWorkspaceId?: string;
      };
      targetWorkspaceId = request.targetWorkspaceId ?? targetWorkspaceId;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          transitionId: '55555555-5555-4555-8555-555555555555',
          status: 'Pending',
          expiresAt: '2026-08-09T12:00:00Z',
          authoritativeWorkspaceId: null,
        }),
      });
    });
    await page.route('**/api/workspace-context/confirm', async (route) => {
      await confirmationGate;
      currentWorkspaceId = targetWorkspaceId;
      holdNextSessionRestore = true;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          transitionId: '55555555-5555-4555-8555-555555555555',
          status: 'Completed',
          expiresAt: '2026-08-09T12:00:00Z',
          authoritativeWorkspaceId: currentWorkspaceId,
        }),
      });
    });
    await page.route('**/api/workspace-context/recover', async (route) => {
      await route.fulfill({ status: 500, contentType: 'application/problem+json', body: '{}' });
    });
    await page.route('**/api/module-navigation', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          availableContributionIds:
            currentWorkspaceId === organizationWorkspace.workspaceId
              ? productContributions
              : administratorContributions,
        }),
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
    await expectAppFrameReady(page, 'Dashboard');
    const moduleNavigation = page.getByRole('navigation', { name: 'Modules' });
    await expect(moduleNavigation).toBeVisible();
    await expect(moduleNavigation.getByRole('link', { name: 'Business objects' })).toBeVisible();
    const accountTrigger = page.getByRole('button', { name: /Account menu/ });
    await expect(accountTrigger).toContainText(organizationWorkspace.name);
    await accountTrigger.click();
    const accountView = page.locator('[data-slot="account-surface"]');
    await expect(accountView).toBeVisible();
    await accountView.evaluate((element) =>
      Promise.all(element.getAnimations({ subtree: true }).map((animation) => animation.finished)),
    );
    const organizationWorkspaceOption = accountView.getByRole('button', {
      name: organizationWorkspace.name,
    });
    const organizationCurrentState = await settledVisualState(organizationWorkspaceOption);
    await page.evaluate(() => {
      const probe = window as Window & {
        __axisAccountGeometry?: Array<{
          documentScroll: boolean;
          height: number;
          mainLeft: number;
          mainWidth: number;
          menuScroll: boolean;
        }>;
        __axisStopAccountGeometry?: boolean;
      };
      probe.__axisAccountGeometry = [];
      probe.__axisStopAccountGeometry = false;
      const sample = () => {
        const menu = document.querySelector<HTMLElement>('[data-slot="account-surface"]');
        if (menu) {
          const mainRect = document.querySelector('main')?.getBoundingClientRect();
          probe.__axisAccountGeometry?.push({
            documentScroll: document.documentElement.scrollHeight > window.innerHeight + 1,
            height: menu.getBoundingClientRect().height,
            mainLeft: mainRect?.left ?? -1,
            mainWidth: mainRect?.width ?? -1,
            menuScroll: menu.scrollHeight > menu.clientHeight + 1,
          });
        }
        if (!probe.__axisStopAccountGeometry) requestAnimationFrame(sample);
      };
      requestAnimationFrame(sample);
    });

    const personalWorkspaceOption = accountView.getByRole('button', { name: 'Personal' });
    await personalWorkspaceOption.click();
    expect(await personalWorkspaceOption.getAttribute('aria-busy')).toBe('true');
    expect(await settledVisualState(personalWorkspaceOption)).toEqual(organizationCurrentState);
    await expect(personalWorkspaceOption).not.toHaveAttribute('aria-current');
    await expect(organizationWorkspaceOption).toHaveAttribute('aria-current', 'page');
    await expect(accountView.getByText('Switching Workspace...')).toBeAttached();
    await expect(accountTrigger).toHaveAttribute('aria-expanded', 'true');
    await expect(accountView).toBeVisible();

    releaseConfirmation();
    await sessionRestoreRequest;
    const refreshStatus = page.getByText('Refreshing Workspace');
    await expect(refreshStatus).toBeVisible();
    await expect(moduleNavigation).toBeVisible();
    await expect(moduleNavigation.getByRole('link', { name: 'Business objects' })).toBeVisible();
    await expect(personalWorkspaceOption).toHaveAttribute('aria-current', 'page');
    await expect(accountTrigger).toContainText(profile.fullName);
    await expect(accountTrigger).toHaveAttribute('aria-expanded', 'true');
    await expectNoDocumentScroll(page);

    releaseSessionRestore();
    await expect(
      accountView.getByRole('button', { name: organizationWorkspace.name }),
    ).toBeEnabled();
    await expect(refreshStatus).toBeHidden();
    await expect(moduleNavigation).toBeVisible();
    await expect(moduleNavigation.getByRole('link', { name: 'Business objects' })).toHaveCount(0);
    await expect(accountView).toBeVisible();
    await expect(accountTrigger).toContainText(profile.fullName);
    await expect(accountTrigger).toHaveAttribute('aria-expanded', 'true');
    await expectNoDocumentScroll(page);

    armTransitionGates();
    const personalCurrentState = await settledVisualState(personalWorkspaceOption);
    await organizationWorkspaceOption.click();
    expect(await organizationWorkspaceOption.getAttribute('aria-busy')).toBe('true');
    expect(await settledVisualState(organizationWorkspaceOption)).toEqual(personalCurrentState);
    await expect(organizationWorkspaceOption).not.toHaveAttribute('aria-current');
    await expect(personalWorkspaceOption).toHaveAttribute('aria-current', 'page');
    await expect(accountView.getByText('Switching Workspace...')).toBeAttached();
    await expect(accountTrigger).toHaveAttribute('aria-expanded', 'true');
    await expect(accountView).toBeVisible();

    releaseConfirmation();
    await sessionRestoreRequest;
    await expect(refreshStatus).toBeVisible();
    await expect(moduleNavigation).toBeVisible();
    await expect(moduleNavigation.getByRole('link', { name: 'Business objects' })).toHaveCount(0);
    await expect(
      accountView.getByRole('button', { name: organizationWorkspace.name }),
    ).toHaveAttribute('aria-current', 'page');
    await expect(accountTrigger).toContainText(organizationWorkspace.name);
    await expect(accountTrigger).toHaveAttribute('aria-expanded', 'true');
    await expectNoDocumentScroll(page);

    releaseSessionRestore();
    await expect(personalWorkspaceOption).toBeEnabled();
    await expect(refreshStatus).toBeHidden();
    await expect(moduleNavigation).toBeVisible();
    await expect(moduleNavigation.getByRole('link', { name: 'Business objects' })).toBeVisible();
    await expect(accountView).toBeVisible();
    await expect(accountTrigger).toContainText(organizationWorkspace.name);
    await expect(accountTrigger).toHaveAttribute('aria-expanded', 'true');
    await expectNoDocumentScroll(page);

    const geometry = await page.evaluate(() => {
      const probe = window as Window & {
        __axisAccountGeometry?: Array<{
          documentScroll: boolean;
          height: number;
          mainLeft: number;
          mainWidth: number;
          menuScroll: boolean;
        }>;
        __axisStopAccountGeometry?: boolean;
      };
      probe.__axisStopAccountGeometry = true;
      return probe.__axisAccountGeometry ?? [];
    });
    expect(geometry.length).toBeGreaterThan(1);
    expect(geometry.some((sample) => sample.documentScroll)).toBe(false);
    expect(new Set(geometry.map((sample) => sample.menuScroll))).toEqual(new Set([false]));
    expect(Math.max(...geometry.map((sample) => sample.height))).toBeLessThanOrEqual(
      Math.min(...geometry.map((sample) => sample.height)) + 1,
    );
    expect(Math.max(...geometry.map((sample) => sample.mainLeft))).toBeLessThanOrEqual(
      Math.min(...geometry.map((sample) => sample.mainLeft)) + 1,
    );
    expect(Math.max(...geometry.map((sample) => sample.mainWidth))).toBeLessThanOrEqual(
      Math.min(...geometry.map((sample) => sample.mainWidth)) + 1,
    );
  });

  test('AT-002 desktop and mobile frame render without console errors or document overflow', async ({
    page,
  }) => {
    const pageErrors = observeUnexpectedRuntimeErrors(page);

    await mockAuthenticatedSession(page);
    let completeThemeSave: (() => void) | undefined;
    let markThemeSaveStarted: (() => void) | undefined;
    const themeSaveStarted = new Promise<void>((resolve) => {
      markThemeSaveStarted = resolve;
    });
    await page.route('**/api/users/me/preferences/theme', async (route) => {
      await new Promise<void>((resolve) => {
        completeThemeSave = resolve;
        markThemeSaveStarted?.();
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
    await page.getByRole('button', { name: /Account menu/ }).click();
    const accountIdentity = page.getByRole('region', { name: 'Account' });
    await expect(accountIdentity.getByText(profile.fullName)).toBeVisible();
    await expect(accountIdentity.getByText(profile.email)).toBeVisible();
    await expect(page.getByRole('region', { name: 'Workspace', exact: true })).toBeVisible();
    await expect(page.getByText('Choose Workspace', { exact: true })).toHaveCount(0);
    await expect(page.getByText('Personal Workspace', { exact: true })).toHaveCount(0);
    await expect(page.getByText('Organization Workspaces', { exact: true })).toHaveCount(0);
    await expect(page.getByText('Preferences')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
    const accountMenu = page.locator('[data-slot="account-surface"]');
    await accountMenu.evaluate((element) =>
      Promise.all(element.getAnimations({ subtree: true }).map((animation) => animation.finished)),
    );
    const initialMenuBox = await accountMenu.boundingBox();
    const darkOption = page.getByRole('button', { name: 'Dark' });
    await darkOption.click();
    expect(await darkOption.getAttribute('aria-busy')).toBe('true');
    const pendingMenuBox = await accountMenu.boundingBox();
    expect(Math.round(pendingMenuBox?.height ?? 0)).toBe(Math.round(initialMenuBox?.height ?? 0));
    await themeSaveStarted;
    completeThemeSave?.();
    await expect(darkOption).not.toHaveAttribute('aria-busy', 'true');
    const savedMenuBox = await accountMenu.boundingBox();
    expect(Math.round(savedMenuBox?.height ?? 0)).toBe(Math.round(initialMenuBox?.height ?? 0));
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);
    await page.keyboard.press('Escape');

    await page.getByRole('link', { name: 'Rules' }).click();
    await expect(page).toHaveURL(/\/rules(?:\?.*)?$/);
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();
    await expectRouteViewportTouchesMain(page);
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
    await page.getByRole('button', { name: /Account menu/ }).click();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);

    await page.keyboard.press('Escape');
    await page.getByRole('link', { name: 'Rules' }).click();
    await expect(page).toHaveURL(/\/rules(?:\?.*)?$/);
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();
    await expectRouteViewportTouchesMain(page);
    await expectNoPageOverflow(page);
    await expectNoDocumentScroll(page);
    await expectRulesCatalogScrolls(page);
    expect(pageErrors).toEqual([]);
  });
});
