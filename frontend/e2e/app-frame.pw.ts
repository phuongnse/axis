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

async function mockAuthenticatedSession(page: Page): Promise<void> {
  await page.addInitScript(() => {
    (window as Window & { __AXIS_DISABLE_DEVTOOLS__?: boolean }).__AXIS_DISABLE_DEVTOOLS__ = true;
    localStorage.setItem('axis.language', 'en');
    localStorage.setItem('axis.theme', 'light');
  });

  await page.route('**/api/auth/session', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        authenticated: true,
        csrfToken: 'app-frame-csrf-token',
        user: {
          userId: profile.id,
          workspaceId: profile.workspaceId,
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
      body: JSON.stringify(profile),
    });
  });

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

async function expectAccountSurfaceScreenshot(page: Page, name: string): Promise<void> {
  const accountSurface = page.locator('[data-slot="account-surface"]');
  await expect(accountSurface).toBeVisible();
  await expect(accountSurface).toHaveAttribute('data-axis-surface-id', 'account-actions');
  await expect(accountSurface).toHaveAttribute('data-axis-surface-contract', 'account-surface');
  await expect(accountSurface.locator('fieldset[aria-busy="true"]')).toHaveCount(0);
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

  test('AT-004 account surface visual contract covers light and dark desktop and compact EN and VI', async ({
    page,
  }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await mockAuthenticatedSession(page);
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
    await page.getByRole('button', { name: 'Account menu' }).click();
    await expect(page.getByRole('button', { name: 'Personal workspace' })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    await expectAccountSurfaceScreenshot(page, 'account-surface-light-desktop-en');

    await page.getByRole('button', { name: 'Dark' }).click();
    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(page.getByRole('button', { name: 'Dark' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expectAccountSurfaceScreenshot(page, 'account-surface-dark-desktop-en');

    await page.getByRole('button', { name: 'Vietnamese' }).click();
    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await expect(page.getByRole('button', { name: 'Tiếng Việt' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expectAccountSurfaceScreenshot(page, 'account-surface-dark-desktop-vi');

    await page.getByRole('button', { name: 'Sáng' }).click();
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    await expect(page.getByRole('button', { name: 'Sáng' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expectAccountSurfaceScreenshot(page, 'account-surface-light-desktop-vi');

    await page.setViewportSize({ width: 390, height: 844 });
    await expectAccountSurfaceScreenshot(page, 'account-surface-light-compact-vi');

    await page.getByRole('button', { name: 'Tối' }).click();
    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(page.getByRole('button', { name: 'Tối' })).toHaveAttribute('aria-pressed', 'true');
    await expectAccountSurfaceScreenshot(page, 'account-surface-dark-compact-vi');

    await page.getByRole('button', { name: 'Tiếng Anh' }).click();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.getByRole('button', { name: 'English' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expectAccountSurfaceScreenshot(page, 'account-surface-dark-compact-en');

    await page.getByRole('button', { name: 'Light' }).click();
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    await expect(page.getByRole('button', { name: 'Light' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expectAccountSurfaceScreenshot(page, 'account-surface-light-compact-en');
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
    const accountTrigger = page.getByRole('button', { name: 'Account menu' });
    await expect(accountTrigger).toContainText(organizationWorkspace.name);
    await accountTrigger.click();
    const accountView = page.locator('[data-slot="account-surface"]');
    await expect(accountView).toBeVisible();
    await accountView.evaluate((element) =>
      Promise.all(element.getAnimations({ subtree: true }).map((animation) => animation.finished)),
    );
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

    await accountView.getByRole('button', { name: personalWorkspace.name }).click();
    await expect(accountView.getByText('Switching Workspace...')).toBeAttached();
    await expect(accountTrigger).toHaveAttribute('aria-expanded', 'true');
    await expect(accountView).toBeVisible();

    releaseConfirmation();
    await sessionRestoreRequest;
    const refreshStatus = page.getByText('Refreshing Workspace');
    await expect(refreshStatus).toBeVisible();
    await expect(moduleNavigation).toBeVisible();
    await expect(moduleNavigation.getByRole('link', { name: 'Business objects' })).toBeVisible();
    await expect(accountView.getByRole('button', { name: personalWorkspace.name })).toHaveAttribute(
      'aria-current',
      'page',
    );
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
    await accountView.getByRole('button', { name: organizationWorkspace.name }).click();
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
    await expect(accountView.getByRole('button', { name: personalWorkspace.name })).toBeEnabled();
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
    await expect(darkOption.locator('[data-slot="spinner"]')).toBeVisible();
    const pendingMenuBox = await accountMenu.boundingBox();
    expect(Math.round(pendingMenuBox?.height ?? 0)).toBe(Math.round(initialMenuBox?.height ?? 0));
    completeThemeSave?.();
    await expect(darkOption.locator('[data-slot="spinner"]')).toBeHidden();
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
    await page.getByRole('button', { name: 'Account menu' }).click();
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
