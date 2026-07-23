import { Buffer } from 'node:buffer';
import { expect, type Locator, type Page, test } from '@playwright/test';

const profile = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'objects@example.com',
  fullName: 'Objects User',
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
const definitionId = '33333333-3333-4333-8333-333333333333';
const fieldId = '44444444-4444-4444-8444-444444444444';
const versionId = '55555555-5555-4555-8555-555555555555';
const now = '2026-07-07T00:00:00Z';
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
    systemRule('field.numeric_range', 'Numeric range', ['Integer', 'Decimal']),
    systemRule('field.date_range', 'Date range', ['Date']),
    systemRule('field.datetime_range', 'Date and time range', ['DateTime']),
    systemRule('field.text_length', 'Text length', ['Text']),
    systemRule('field.text_pattern', 'Text pattern', ['Text']),
    systemRule('field.text_format', 'Text format', ['Text']),
    systemRule('field.decimal_precision', 'Decimal precision', ['Decimal']),
    systemRule('field.choice_selection_count', 'Choice selection count', ['Choice']),
  ],
  totalCount: 9,
  page: 1,
  pageSize: 100,
};

type BusinessObjectFieldType =
  | 'Text'
  | 'Integer'
  | 'Decimal'
  | 'Date'
  | 'DateTime'
  | 'Boolean'
  | 'Choice';

interface BusinessObjectFieldRuleRequest {
  definitionKey: string;
  definitionVersion?: number;
  parameters?: Record<string, string[]>;
}

interface BusinessObjectFieldRequest {
  fieldKey: string;
  label: string;
  fieldType?: BusinessObjectFieldType;
  rules?: BusinessObjectFieldRuleRequest[];
  choiceConfiguration?: {
    selectionMode: 'Single' | 'Multiple';
    options: { optionKey: string; label: string }[];
  };
  order?: number;
}

interface BusinessObjectDefinitionRequest {
  name: string;
  fields?: BusinessObjectFieldRequest[];
}

type TestTheme = 'light' | 'dark';

interface MockBusinessObjectDefinitionApiOptions {
  createDefinitionFailure?: {
    status: number;
    body: unknown;
  };
}

interface MockBusinessObjectDefinitionRequest {
  method: string;
  path: string;
  body?: unknown;
}

type BusinessObjectDefinitionRequests = (() => string[]) & {
  details: () => readonly MockBusinessObjectDefinitionRequest[];
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

function unpublishedDetail({
  name,
  objectKey,
  revision,
  fields = [],
}: {
  name: string;
  objectKey: string;
  revision: number;
  fields?: BusinessObjectFieldRequest[];
}) {
  return {
    id: definitionId,
    workspaceId: profile.workspaceId,
    name,
    objectKey,
    status: 'Unpublished',
    revision,
    latestPublishedVersionNumber: null,
    createdAt: now,
    updatedAt: now,
    fields: fields.map((field, index) => ({
      id: index === 0 ? fieldId : `44444444-4444-4444-8444-${String(index).padStart(12, '0')}`,
      order: index,
      ...field,
    })),
    latestPublishedVersion: null,
  };
}

type BusinessObjectDefinitionDetail = ReturnType<typeof unpublishedDetail>;

function deriveObjectKey(name: string): string {
  return (
    name
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '')
      .replace(/_{2,}/g, '_') || 'object'
  );
}

function publishedDetail(definition: BusinessObjectDefinitionDetail) {
  const fields = definition.fields.map((field) => ({
    fieldKey: field.fieldKey,
    label: field.label,
    order: field.order,
    fieldType: field.fieldType ?? 'Text',
    rules: field.rules ?? [],
    choiceConfiguration: field.choiceConfiguration,
  }));

  return {
    ...unpublishedDetail({
      name: definition.name,
      objectKey: definition.objectKey,
      revision: definition.revision,
      fields,
    }),
    status: 'Published',
    latestPublishedVersionNumber: 1,
    latestPublishedVersion: {
      id: versionId,
      versionNumber: 1,
      publishedByUserId: profile.id,
      publishedAt: now,
      fields: fields.map((field, index) => ({
        id: index === 0 ? fieldId : `44444444-4444-4444-8444-${String(index).padStart(12, '0')}`,
        order: index,
        ...field,
      })),
    },
  };
}

async function mockAuthenticatedSession(
  page: Page,
  options: { theme?: TestTheme } = {},
): Promise<void> {
  const theme = options.theme ?? 'light';
  const sessionProfile = { ...profile, theme };

  await page.addInitScript((selectedTheme) => {
    window.__AXIS_DISABLE_DEVTOOLS__ = true;
    localStorage.setItem('axis.language', 'en');
    localStorage.setItem('axis.theme', selectedTheme);
  }, theme);

  await page.route('**/connect/authorize**', async (route) => {
    const requestUrl = new URL(route.request().url());
    const state = requestUrl.searchParams.get('state') ?? '';
    const callbackUrl = new URL('/callback', requestUrl.origin);
    callbackUrl.searchParams.set('code', 'objects-code');
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
      body: JSON.stringify(sessionProfile),
    });
  });

  await page.route('**/api/auth/sign-out', async (route) => {
    await route.fulfill({ status: 204 });
  });
}

async function mockBusinessObjectDefinitionApi(
  page: Page,
  options: MockBusinessObjectDefinitionApiOptions = {},
): Promise<BusinessObjectDefinitionRequests> {
  let currentDefinition = unpublishedDetail({
    name: 'Customer',
    objectKey: 'customer',
    revision: 1,
  });
  let hasDefinition = false;
  const requests: MockBusinessObjectDefinitionRequest[] = [];

  await page.route('**/api/rules?**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(fieldRuleDefinitions),
    });
  });

  await page.route('**/api/business-object-definitions**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const requestEntry: MockBusinessObjectDefinitionRequest = { method, path: url.pathname };
    requests.push(requestEntry);

    if (method === 'GET' && url.pathname === '/api/business-object-definitions') {
      const items = hasDefinition
        ? [
            {
              id: currentDefinition.id,
              name: currentDefinition.name,
              objectKey: currentDefinition.objectKey,
              status: currentDefinition.status,
              revision: currentDefinition.revision,
              latestPublishedVersionNumber: currentDefinition.latestPublishedVersionNumber,
              updatedAt: currentDefinition.updatedAt,
            },
          ]
        : [];

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items, totalCount: items.length, page: 1, pageSize: 20 }),
      });
      return;
    }

    if (method === 'GET' && url.pathname === `/api/business-object-definitions/${definitionId}`) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(currentDefinition),
      });
      return;
    }

    if (method === 'POST' && url.pathname === '/api/business-object-definitions') {
      if (options.createDefinitionFailure) {
        await route.fulfill({
          status: options.createDefinitionFailure.status,
          contentType: 'application/problem+json',
          body: JSON.stringify(options.createDefinitionFailure.body),
        });
        return;
      }

      const body = request.postDataJSON() as BusinessObjectDefinitionRequest;
      requestEntry.body = body;
      currentDefinition = unpublishedDetail({
        name: body.name,
        objectKey: deriveObjectKey(body.name),
        revision: 1,
      });
      hasDefinition = true;
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(currentDefinition),
      });
      return;
    }

    if (
      method === 'PUT' &&
      url.pathname === `/api/business-object-definitions/${definitionId}/unpublished`
    ) {
      const body = request.postDataJSON() as BusinessObjectDefinitionRequest;
      requestEntry.body = body;
      currentDefinition = unpublishedDetail({
        name: body.name,
        objectKey: currentDefinition.objectKey,
        revision: 2,
        fields: body.fields,
      });
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(currentDefinition),
      });
      return;
    }

    if (
      method === 'POST' &&
      url.pathname === `/api/business-object-definitions/${definitionId}/publish`
    ) {
      currentDefinition = publishedDetail(currentDefinition);
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(currentDefinition),
      });
      return;
    }

    await route.fulfill({ status: 404, body: `${method} ${url.pathname}` });
  });

  const requestPaths = (() =>
    requests.map(
      (request) => `${request.method} ${request.path}`,
    )) as BusinessObjectDefinitionRequests;
  requestPaths.details = () => requests;

  return requestPaths;
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

async function expectNoDesktopDocumentScroll(page: Page): Promise<void> {
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

async function expectMobileDockAboveFooter(page: Page): Promise<Locator> {
  const dock = page.locator('[data-slot="managed-window-dock"]');
  const host = page.locator('[data-slot="managed-window-host"]');
  const footer = page.getByRole('contentinfo');
  await expect(dock).toBeVisible();
  const [dockBox, hostBox, footerBox] = await Promise.all([
    dock.boundingBox(),
    host.boundingBox(),
    footer.boundingBox(),
  ]);
  if (!dockBox || !hostBox || !footerBox) {
    throw new Error('Managed dialog dock geometry was not available');
  }

  expect(dockBox.width).toBeGreaterThanOrEqual(200);
  expect(dockBox.width).toBeLessThanOrEqual(256);
  expect(hostBox.x + hostBox.width - (dockBox.x + dockBox.width)).toBeCloseTo(12, 0);
  const footerGap = footerBox.y - (dockBox.y + dockBox.height);
  expect(footerGap).toBeGreaterThanOrEqual(8);
  expect(footerGap).toBeLessThanOrEqual(12);
  return dock;
}

async function expectDarkReadableContrast(locator: Locator): Promise<void> {
  await expect
    .poll(async () =>
      locator.evaluate((element) => {
        if (!document.documentElement.classList.contains('dark')) return true;

        type Rgba = { r: number; g: number; b: number; a: number };

        function parseCssColor(value: string): Rgba | null {
          const normalized = value.trim();
          if (normalized === 'transparent') return { r: 0, g: 0, b: 0, a: 0 };

          const alpha = (part: string | undefined) => {
            if (part === undefined) return 1;

            const parsed = Number.parseFloat(part);
            return Number.isFinite(parsed) ? parsed : 1;
          };

          const rgbMatch = normalized.match(/^rgba?\((.+)\)$/);
          if (rgbMatch) {
            const parts = rgbMatch[1]
              .replace(/\s*\/\s*/g, ' ')
              .split(/[,\s]+/)
              .filter(Boolean);
            if (parts.length >= 3) {
              const channel = (part: string) =>
                part.endsWith('%')
                  ? (Number.parseFloat(part) / 100) * 255
                  : Number.parseFloat(part);
              return {
                r: channel(parts[0]),
                g: channel(parts[1]),
                b: channel(parts[2]),
                a: alpha(parts[3]),
              };
            }
          }

          const srgbMatch = normalized.match(/^color\(srgb\s+(.+)\)$/);
          if (srgbMatch) {
            const parts = srgbMatch[1]
              .replace(/\s*\/\s*/g, ' ')
              .split(/\s+/)
              .filter(Boolean);
            if (parts.length >= 3) {
              return {
                r: Number.parseFloat(parts[0]) * 255,
                g: Number.parseFloat(parts[1]) * 255,
                b: Number.parseFloat(parts[2]) * 255,
                a: alpha(parts[3]),
              };
            }
          }

          const oklchMatch = normalized.match(/^oklch\((.+)\)$/);
          if (oklchMatch) {
            const parts = oklchMatch[1]
              .replace(/\s*\/\s*/g, ' ')
              .split(/\s+/)
              .filter(Boolean);
            if (parts.length >= 3) {
              const chroma = Number.parseFloat(parts[1]);
              const hue = (Number.parseFloat(parts[2]) * Math.PI) / 180;
              const alphaPart = parts[3] ? ` / ${parts[3]}` : '';

              return parseCssColor(
                `oklab(${parts[0]} ${chroma * Math.cos(hue)} ${chroma * Math.sin(hue)}${alphaPart})`,
              );
            }
          }

          const oklabMatch = normalized.match(/^oklab\((.+)\)$/);
          if (oklabMatch) {
            const parts = oklabMatch[1]
              .replace(/\s*\/\s*/g, ' ')
              .split(/\s+/)
              .filter(Boolean);

            if (parts.length >= 3) {
              const lightness = parts[0].endsWith('%')
                ? Number.parseFloat(parts[0]) / 100
                : Number.parseFloat(parts[0]);
              const a = Number.parseFloat(parts[1]);
              const b = Number.parseFloat(parts[2]);
              const lPrime = lightness + 0.3963377774 * a + 0.2158037573 * b;
              const mPrime = lightness - 0.1055613458 * a - 0.0638541728 * b;
              const sPrime = lightness - 0.0894841775 * a - 1.291485548 * b;
              const l = lPrime ** 3;
              const m = mPrime ** 3;
              const s = sPrime ** 3;
              const linear = {
                r: 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                g: -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                b: -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
              };
              const toSrgb = (channel: number) => {
                const clamped = Math.min(Math.max(channel, 0), 1);
                return (
                  (clamped <= 0.0031308 ? 12.92 * clamped : 1.055 * clamped ** (1 / 2.4) - 0.055) *
                  255
                );
              };

              return {
                r: toSrgb(linear.r),
                g: toSrgb(linear.g),
                b: toSrgb(linear.b),
                a: alpha(parts[3]),
              };
            }
          }

          const probe = document.createElement('span');
          probe.style.color = normalized;
          document.body.append(probe);
          const resolved = getComputedStyle(probe).color;
          probe.remove();

          if (resolved && resolved !== normalized) {
            return parseCssColor(resolved);
          }

          return null;
        }

        function composite(foreground: Rgba, background: Rgba): Rgba {
          const alpha = Math.min(Math.max(foreground.a, 0), 1);

          return {
            r: foreground.r * alpha + background.r * (1 - alpha),
            g: foreground.g * alpha + background.g * (1 - alpha),
            b: foreground.b * alpha + background.b * (1 - alpha),
            a: 1,
          };
        }

        function distance(first: Rgba, second: Rgba): number {
          return Math.hypot(first.r - second.r, first.g - second.g, first.b - second.b);
        }

        const elementStyle = getComputedStyle(element);
        const pageBackground = parseCssColor(getComputedStyle(document.body).backgroundColor);
        const surfaceBackground = parseCssColor(elementStyle.backgroundColor);
        const textColor = parseCssColor(elementStyle.color);

        if (!pageBackground || !surfaceBackground || !textColor) return false;

        const compositedBackground = composite(surfaceBackground, pageBackground);
        return distance(textColor, compositedBackground) >= 35;
      }),
    )
    .toBe(true);
}

test.describe('define business object', () => {
  test('managed draft survives navigation and sign-out clears the window workspace', async ({
    page,
  }) => {
    const pageErrors: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') pageErrors.push(message.text());
    });
    page.on('pageerror', (error) => pageErrors.push(error.message));

    await mockAuthenticatedSession(page);
    await mockBusinessObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/business-objects');
    await page.getByRole('button', { name: 'New definition' }).click();
    let dialog = page.getByRole('dialog', { name: 'Define business object' });
    await dialog.getByLabel('Name', { exact: true }).fill('Customer');
    await dialog.getByRole('button', { name: 'Start definition' }).click();

    dialog = page.getByRole('dialog', { name: 'Customer' });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel('Name', { exact: true }).fill('Customer draft');
    await dialog.getByRole('button', { name: 'Minimize dialog' }).click();
    await expect(dialog).toBeHidden();

    await page.getByRole('link', { name: 'Rules' }).click();
    await expect(page).toHaveURL(/\/rules$/);
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();

    await page.getByRole('button', { name: 'Windows (1)' }).click();
    await page.getByRole('menuitem', { name: /Customer/ }).click();
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue('Customer draft');

    await dialog.getByRole('button', { name: 'Close dialog' }).click();
    const discardDialog = page.getByRole('alertdialog', { name: 'Discard unsaved changes?' });
    await expect(discardDialog).toBeVisible();
    await discardDialog.getByRole('button', { name: 'Keep editing' }).click();
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue('Customer draft');

    await page.getByRole('button', { name: 'Account menu' }).click();
    await page.getByRole('button', { name: 'Sign out' }).click();
    await expect(page).toHaveURL(/\/sign-in$/);
    await expect(page.getByRole('button', { name: 'Windows (1)' })).toHaveCount(0);
    await expect(page.locator('[data-slot="managed-dialog-window"]')).toHaveCount(0);
    await expectNoPageOverflow(page);
    expect(pageErrors).toEqual([]);
  });

  test('AT-013 browser journey creates, saves, and publishes a definition', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') {
        pageErrors.push(message.text());
      }
    });
    page.on('pageerror', (error) => pageErrors.push(error.message));

    await mockAuthenticatedSession(page);
    const objectRequests = await mockBusinessObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1920, height: 940 });
    await page.goto('/business-objects');

    await expect(page).toHaveURL(/\/business-objects\?page=1$/);
    await expect(page.getByRole('banner')).toContainText('Business Objects');
    await expect(page.getByRole('heading', { name: 'Business objects' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Modules' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Business objects', exact: true })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expect(page.getByLabel('Definitions').getByText('No business objects')).toBeVisible();
    await expectNoDesktopDocumentScroll(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.getByRole('button', { name: 'New definition' }).click();
    const dialog = page.locator('[data-slot="dialog-content"]');
    await expect(dialog.getByRole('heading', { name: 'Define business object' })).toBeVisible();
    await expect(dialog.locator('[data-slot="managed-dialog-window"]')).toHaveAttribute(
      'data-dialog-preset',
      'windowed',
    );
    await dialog.getByLabel('Name', { exact: true }).fill('Customer');
    await expect(dialog.getByLabel('Object key')).toHaveValue('customer');
    await expect(dialog.getByLabel('Object key')).toHaveJSProperty('readOnly', true);
    await dialog.getByRole('button', { name: 'Start definition' }).click();

    await expect(page).toHaveURL(/\/business-objects\?page=1$/);
    await expect(dialog.getByRole('heading', { name: 'Customer' })).toBeVisible();
    await dialog.getByRole('tab', { name: 'Fields' }).click();
    await expect(dialog.getByRole('button', { name: 'Publish', exact: true })).toBeDisabled();

    await dialog.getByRole('button', { name: 'Add field' }).click();
    await dialog.getByLabel('Label', { exact: true }).fill('Name');
    await dialog.getByLabel('Field key').fill('name');
    await dialog.getByRole('button', { name: 'Save changes' }).click();
    await expect(dialog.getByRole('button', { name: 'Save changes' })).toBeDisabled();
    await expect(dialog.getByRole('button', { name: 'Publish', exact: true })).toBeEnabled();

    await dialog.getByRole('button', { name: 'Publish', exact: true }).click();

    await expect(dialog.getByRole('tab', { name: 'Published version' })).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Publish', exact: true })).toHaveCount(0);
    await expectNoPageOverflow(page);

    await page.setViewportSize({ width: 390, height: 844 });
    await expect(dialog).toBeVisible();
    await expectNoPageOverflow(page);

    await dialog.getByRole('button', { name: 'Minimize dialog' }).click();
    const mobileDock = await expectMobileDockAboveFooter(page);
    await expect(dialog).toBeHidden();
    await page.keyboard.press('Escape');
    await expect(mobileDock).toBeVisible();
    await mobileDock.getByRole('button', { name: 'Restore dialog' }).click();
    await expect(dialog).toBeVisible();
    await expect(dialog.locator('[data-slot="managed-dialog-window"]')).toHaveAttribute(
      'data-dialog-preset',
      'fullscreen',
    );

    await dialog.getByRole('button', { name: 'Close dialog' }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByText('Published', { exact: true })).toBeVisible();

    expect(objectRequests()).toContain('POST /api/business-object-definitions');
    expect(objectRequests()).toContain(
      `PUT /api/business-object-definitions/${definitionId}/unpublished`,
    );
    expect(objectRequests()).toContain(
      `POST /api/business-object-definitions/${definitionId}/publish`,
    );
    expect(pageErrors).toEqual([]);
  });

  test('AT-008 browser journey configures field rules and publishes the typed contract', async ({
    page,
  }, testInfo) => {
    testInfo.setTimeout(60_000);
    const pageErrors: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') {
        pageErrors.push(message.text());
      }
    });
    page.on('pageerror', (error) => pageErrors.push(error.message));

    await mockAuthenticatedSession(page);
    const objectRequests = await mockBusinessObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/business-objects');
    await page.getByRole('button', { name: 'New definition' }).click();
    const dialog = page.locator('[data-slot="dialog-content"]');
    await dialog.getByLabel('Name', { exact: true }).fill('Application');
    await dialog.getByRole('button', { name: 'Start definition' }).click();
    await expect(dialog.getByRole('heading', { name: 'Application' })).toBeVisible();
    await dialog.getByRole('tab', { name: 'Fields' }).click();

    await dialog.getByRole('button', { name: 'Add field' }).click();
    await dialog.getByLabel('Field key').fill('status');
    await dialog.getByLabel('Label', { exact: true }).fill('Status');
    await dialog.getByLabel('Type').click();
    await page.getByRole('option', { name: 'Choice' }).click();

    await expect(dialog.getByRole('button', { name: 'Publish', exact: true })).toBeDisabled();
    const options = dialog.getByRole('region', { name: 'Options' });
    for (const [key, label] of [
      ['draft', 'Draft'],
      ['submitted', 'Submitted'],
      ['approved', 'Approved'],
    ] as const) {
      await options.getByRole('button', { name: 'Add option' }).click();
      const optionIndex = (await options.getByLabel('Option key').count()) - 1;
      await options.getByLabel('Option key').nth(optionIndex).fill(key);
      await options.getByLabel('Label', { exact: true }).nth(optionIndex).fill(label);
    }
    await dialog.getByLabel('Add rule').click();
    await page.getByRole('option', { name: 'Required value' }).click();
    await dialog.getByRole('button', { name: 'Save changes' }).click();

    await expect
      .poll(
        () =>
          objectRequests
            .details()
            .find(
              (request) =>
                request.method === 'PUT' &&
                request.path === `/api/business-object-definitions/${definitionId}/unpublished` &&
                JSON.stringify(request.body).includes('field.required') &&
                JSON.stringify(request.body).includes('choiceConfiguration'),
            )?.body,
      )
      .toMatchObject({
        name: 'Application',
        fields: [
          {
            fieldKey: 'status',
            label: 'Status',
            fieldType: 'Choice',
            choiceConfiguration: {
              selectionMode: 'Single',
              options: [
                { optionKey: 'draft', label: 'Draft' },
                { optionKey: 'submitted', label: 'Submitted' },
                { optionKey: 'approved', label: 'Approved' },
              ],
            },
            rules: [{ definitionKey: 'field.required', definitionVersion: 1, parameters: {} }],
          },
        ],
      });
    await expect(dialog.getByRole('button', { name: 'Publish', exact: true })).toBeEnabled();

    await dialog.getByRole('button', { name: 'Publish', exact: true }).click();

    await expect(dialog.getByRole('tab', { name: 'Published version' })).toBeVisible();
    await expect(dialog.getByLabel('Type')).toContainText('Choice');
    await expect(options.getByLabel('Option key').nth(0)).toHaveValue('draft');
    await expect(options.getByLabel('Label', { exact: true }).nth(2)).toHaveValue('Approved');
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);

    await page.setViewportSize({ width: 390, height: 844 });
    await expect(dialog).toBeVisible();
    await expectNoPageOverflow(page);

    expect(objectRequests()).toContain(
      `POST /api/business-object-definitions/${definitionId}/publish`,
    );
    expect(pageErrors).toEqual([]);
  });

  test('workspace dialog remains readable in dark mode', async ({ page }) => {
    await mockAuthenticatedSession(page, { theme: 'dark' });
    await mockBusinessObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/business-objects');

    await expect(page.locator('html')).toHaveClass(/dark/);
    await page.getByRole('button', { name: 'New definition' }).click();
    const dialog = page.locator('[data-slot="dialog-content"]');
    await expect(dialog).toBeVisible();
    await expectDarkReadableContrast(dialog.getByLabel('Object key'));
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
  });

  test('fields editor scrolls inside the workspace dialog without document scroll', async ({
    page,
  }) => {
    await mockAuthenticatedSession(page, { theme: 'dark' });
    await mockBusinessObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('/business-objects');
    await page.getByRole('button', { name: 'New definition' }).click();
    const dialog = page.locator('[data-slot="dialog-content"]');
    await dialog.getByLabel('Name', { exact: true }).fill('Customer');
    await dialog.getByRole('button', { name: 'Start definition' }).click();
    await dialog.getByRole('tab', { name: 'Fields' }).click();
    for (let index = 0; index < 4; index += 1) {
      await dialog.getByRole('button', { name: 'Add field' }).click();
    }

    await expect(dialog.locator('[data-slot="managed-dialog-window"]')).toHaveAttribute(
      'data-dialog-preset',
      'windowed',
    );
    const dialogBody = dialog.locator('[data-slot="dialog-body"]');
    await expect
      .poll(() => dialogBody.evaluate((element) => element.scrollHeight > element.clientHeight))
      .toBe(true);
    await dialogBody.evaluate((element) => element.scrollTo({ top: element.scrollHeight }));
    await expect.poll(() => dialogBody.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
    await expect(dialog.getByRole('heading', { name: 'Customer' })).toBeVisible();
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
  });

  test('definition creation errors stay contextual without document scroll', async ({ page }) => {
    await mockAuthenticatedSession(page, { theme: 'dark' });
    await mockBusinessObjectDefinitionApi(page, {
      createDefinitionFailure: {
        status: 409,
        body: {
          type: 'urn:axis:problem:business-objects.objectKeyAlreadyExists',
          title: 'Conflict',
          status: 409,
          detail: 'An object definition with this key already exists in the current workspace.',
          code: 'businessObjects.objectKeyAlreadyExists',
        },
      },
    });

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/business-objects');

    await page.getByRole('button', { name: 'New definition' }).click();
    const dialog = page.locator('[data-slot="dialog-content"]');
    await dialog.getByLabel('Name', { exact: true }).fill('Application');
    await dialog.getByRole('button', { name: 'Start definition' }).click();

    const alert = dialog.getByRole('alert');
    await expect(alert).toContainText('Unable to update business object');
    await expect(alert).toContainText(
      'An object definition with this key already exists in the current workspace.',
    );
    await expect(alert).toHaveClass(/text-destructive/);
    await expect(dialog.getByLabel('Name', { exact: true })).toHaveAttribute(
      'aria-invalid',
      'false',
    );
    await expect(alert).not.toContainText('Something went wrong, please try again');
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
  });

  test('field validation stays contextual to editor inputs', async ({ page }) => {
    await mockAuthenticatedSession(page, { theme: 'dark' });
    await mockBusinessObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/business-objects');

    await page.getByRole('button', { name: 'New definition' }).click();
    const dialog = page.locator('[data-slot="dialog-content"]');
    await dialog.getByLabel('Name', { exact: true }).fill('Customer');
    await dialog.getByRole('button', { name: 'Start definition' }).click();
    await dialog.getByRole('tab', { name: 'Fields' }).click();

    await dialog.getByRole('button', { name: 'Add field' }).click();
    await dialog.getByLabel('Field key').fill('temporary');
    await dialog.getByLabel('Field key').clear();
    await dialog.getByLabel('Label', { exact: true }).fill('Temporary');
    await dialog.getByLabel('Label', { exact: true }).clear();
    await dialog.getByRole('button', { name: 'Save changes' }).click();

    await expect(dialog.getByText('Field keys are required.')).toBeVisible();
    await expect(dialog.getByText('Field labels are required.')).toBeVisible();
    await expect(dialog.getByLabel('Field key')).toHaveAttribute('aria-invalid', 'true');
    await expect(dialog.getByLabel('Label', { exact: true })).toHaveAttribute(
      'aria-invalid',
      'true',
    );
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
  });
});
