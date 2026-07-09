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
const fieldRuleDefinitions = [
  {
    definitionKey: 'field.required',
    displayName: 'Required',
    description: 'Future records must provide a value.',
    supportedFieldTypes: ['Text', 'Integer', 'Decimal', 'Date', 'Boolean', 'SingleSelect'],
    parameters: [],
  },
  {
    definitionKey: 'field.numeric_range',
    displayName: 'Numeric range',
    description: 'Limit integer or decimal values with optional bounds.',
    supportedFieldTypes: ['Integer', 'Decimal'],
    parameters: [
      { key: 'min', type: 'Decimal', isRequired: false, allowMultiple: false },
      { key: 'max', type: 'Decimal', isRequired: false, allowMultiple: false },
    ],
  },
  {
    definitionKey: 'field.date_range',
    displayName: 'Date range',
    description: 'Limit dates with optional bounds.',
    supportedFieldTypes: ['Date'],
    parameters: [
      { key: 'min', type: 'Date', isRequired: false, allowMultiple: false },
      { key: 'max', type: 'Date', isRequired: false, allowMultiple: false },
    ],
  },
  {
    definitionKey: 'field.text_length',
    displayName: 'Text length',
    description: 'Limit text values with optional bounds.',
    supportedFieldTypes: ['Text'],
    parameters: [
      { key: 'min', type: 'Integer', isRequired: false, allowMultiple: false },
      { key: 'max', type: 'Integer', isRequired: false, allowMultiple: false },
    ],
  },
  {
    definitionKey: 'field.text_pattern',
    displayName: 'Text pattern',
    description: 'Require text values to match a pattern.',
    supportedFieldTypes: ['Text'],
    parameters: [{ key: 'pattern', type: 'Text', isRequired: true, allowMultiple: false }],
  },
  {
    definitionKey: 'field.single_select_options',
    displayName: 'Single-select options',
    description: 'Define allowed options.',
    supportedFieldTypes: ['SingleSelect'],
    parameters: [{ key: 'options', type: 'Text', isRequired: true, allowMultiple: true }],
  },
];

type ObjectFieldType = 'Text' | 'Integer' | 'Decimal' | 'Date' | 'Boolean' | 'SingleSelect';

interface ObjectFieldRuleRequest {
  definitionKey: string;
  parameters?: Record<string, string[]>;
}

interface ObjectFieldRequest {
  fieldKey: string;
  label: string;
  fieldType?: ObjectFieldType;
  rules?: ObjectFieldRuleRequest[];
  order?: number;
}

interface ObjectDefinitionRequest {
  name: string;
  fields?: ObjectFieldRequest[];
}

type TestTheme = 'light' | 'dark';

interface MockObjectDefinitionApiOptions {
  createDefinitionFailure?: {
    status: number;
    body: unknown;
  };
}

interface MockObjectDefinitionRequest {
  method: string;
  path: string;
  body?: unknown;
}

type ObjectDefinitionRequests = (() => string[]) & {
  details: () => readonly MockObjectDefinitionRequest[];
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
  fields?: ObjectFieldRequest[];
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

type ObjectDefinitionDetail = ReturnType<typeof unpublishedDetail>;

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

function publishedDetail(definition: ObjectDefinitionDetail) {
  const fields = definition.fields.map((field) => ({
    fieldKey: field.fieldKey,
    label: field.label,
    order: field.order,
    fieldType: field.fieldType ?? 'Text',
    rules: field.rules ?? [],
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
}

async function mockObjectDefinitionApi(
  page: Page,
  options: MockObjectDefinitionApiOptions = {},
): Promise<ObjectDefinitionRequests> {
  let currentDefinition = unpublishedDetail({
    name: 'Customer',
    objectKey: 'customer',
    revision: 1,
  });
  let hasDefinition = false;
  const requests: MockObjectDefinitionRequest[] = [];

  await page.route('**/api/rules/field-rule-definitions', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(fieldRuleDefinitions),
    });
  });

  await page.route('**/api/object-definitions**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const requestEntry: MockObjectDefinitionRequest = { method, path: url.pathname };
    requests.push(requestEntry);

    if (method === 'GET' && url.pathname === '/api/object-definitions') {
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

    if (method === 'GET' && url.pathname === `/api/object-definitions/${definitionId}`) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(currentDefinition),
      });
      return;
    }

    if (method === 'POST' && url.pathname === '/api/object-definitions') {
      if (options.createDefinitionFailure) {
        await route.fulfill({
          status: options.createDefinitionFailure.status,
          contentType: 'application/problem+json',
          body: JSON.stringify(options.createDefinitionFailure.body),
        });
        return;
      }

      const body = request.postDataJSON() as ObjectDefinitionRequest;
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
      url.pathname === `/api/object-definitions/${definitionId}/unpublished`
    ) {
      const body = request.postDataJSON() as ObjectDefinitionRequest;
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

    if (method === 'POST' && url.pathname === `/api/object-definitions/${definitionId}/publish`) {
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
    requests.map((request) => `${request.method} ${request.path}`)) as ObjectDefinitionRequests;
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

async function expectDisabledActionAffordance(page: Page, actionName: string): Promise<void> {
  await page.getByRole('button', { name: actionName, exact: true }).hover({ force: true });

  await expect
    .poll(async () =>
      page.evaluate((name) => {
        const actionButton = [...document.querySelectorAll('button')].find((button) =>
          button.textContent?.includes(name),
        );
        if (!actionButton) return false;

        const actionRect = actionButton.getBoundingClientRect();
        const hintWrapper = actionButton.closest('[data-disabled-action-hint="true"]');
        const textNode = [...actionButton.childNodes].find(
          (node) => node.nodeType === Node.TEXT_NODE && node.textContent?.includes(name),
        );
        if (!hintWrapper || !textNode) return false;

        const textRange = document.createRange();
        textRange.selectNodeContents(textNode);
        const textRect = textRange.getBoundingClientRect();
        const iconRect = actionButton.querySelector('svg')?.getBoundingClientRect();
        const contentLeft = Math.min(iconRect?.left ?? textRect.left, textRect.left);
        const contentRight = Math.max(iconRect?.right ?? textRect.right, textRect.right);
        const contentCenter = (contentLeft + contentRight) / 2;
        const actionCenter = (actionRect.left + actionRect.right) / 2;
        const trailingHintButton = hintWrapper.querySelector(
          'button[aria-label="Action unavailable in the current state"]',
        );
        const actionStyle = getComputedStyle(actionButton);
        const hintWrapperStyle = getComputedStyle(hintWrapper);

        return (
          trailingHintButton === null &&
          hintWrapperStyle.cursor === 'not-allowed' &&
          Number(actionStyle.opacity) >= 0.99 &&
          Math.abs(contentCenter - actionCenter) <= 2
        );
      }, actionName),
    )
    .toBe(true);
}

async function expectDarkInactiveSurfaceContrast(locator: Locator): Promise<void> {
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
        const surfaceBorder = parseCssColor(elementStyle.borderTopColor);

        if (!pageBackground || !surfaceBackground || !surfaceBorder) return false;

        const compositedBackground = composite(surfaceBackground, pageBackground);
        const compositedBorder = composite(surfaceBorder, pageBackground);

        return (
          distance(compositedBackground, pageBackground) >= 35 &&
          distance(compositedBorder, pageBackground) >= 35
        );
      }),
    )
    .toBe(true);
}

async function expectActionWidthHugsContent(page: Page, actionName: string): Promise<void> {
  await expect
    .poll(async () =>
      page.evaluate((name) => {
        const actionButton = [...document.querySelectorAll('button')].find((button) =>
          button.textContent?.includes(name),
        );
        const textNode = [...(actionButton?.childNodes ?? [])].find(
          (node) => node.nodeType === Node.TEXT_NODE && node.textContent?.includes(name),
        );
        if (!actionButton || !textNode) return false;

        const textRange = document.createRange();
        textRange.selectNodeContents(textNode);
        const textRect = textRange.getBoundingClientRect();
        const iconRect = actionButton.querySelector('svg')?.getBoundingClientRect();
        const contentLeft = Math.min(iconRect?.left ?? textRect.left, textRect.left);
        const contentRight = Math.max(iconRect?.right ?? textRect.right, textRect.right);
        const contentWidth = contentRight - contentLeft;
        const actionWidth = actionButton.getBoundingClientRect().width;

        return actionWidth - contentWidth <= 48;
      }, actionName),
    )
    .toBe(true);
}

async function expectPrimaryAction(page: Page, actionName: string): Promise<void> {
  const actionButton = page.getByRole('button', { name: actionName, exact: true }).first();

  await expect(actionButton).toBeEnabled();
  await expect
    .poll(async () => actionButton.evaluate((button) => button.classList.contains('bg-primary')))
    .toBe(true);
}

test.describe('define business object', () => {
  test('AT-013 browser journey creates, saves, and publishes a definition', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') {
        pageErrors.push(message.text());
      }
    });
    page.on('pageerror', (error) => pageErrors.push(error.message));

    await mockAuthenticatedSession(page);
    const objectRequests = await mockObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1920, height: 940 });
    await page.goto('/objects');

    await expect(page).toHaveURL(/\/objects$/);
    await expect(page.getByRole('banner')).toContainText('Objects');
    await expect(page.getByRole('heading', { name: 'Business objects' })).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Modules' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Business objects' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    const editorForm = page.getByRole('form', { name: 'Define business object' });
    await expect(page.getByLabel('Definitions').getByText('No business objects')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add field' })).toBeDisabled();
    await expectDisabledActionAffordance(page, 'Add field');
    await expectActionWidthHugsContent(page, 'Add field');
    await expect(editorForm.getByRole('button', { name: 'Start definition' })).toBeVisible();
    await expect(editorForm.getByRole('button', { name: 'Publish', exact: true })).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Publish readiness' })).toHaveCount(0);
    await expect(
      page.getByText('Start the definition, then add text fields with stable keys and labels.'),
    ).toBeVisible();
    await expectNoDesktopDocumentScroll(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.getByLabel('Name').fill('Customer');
    await expect(page.getByLabel('Object key')).toHaveValue('customer');
    await expect(page.getByLabel('Object key')).toHaveJSProperty('readOnly', true);
    await expect(page.getByRole('button', { name: 'Managed by the system' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Start definition' }).click();

    await expect(page.getByText('Definition created')).toBeVisible();
    await expect(page.getByText('Not published', { exact: true })).toHaveCount(1);
    await expect(page.getByText('Not published 1', { exact: true })).toHaveCount(0);
    const customerForm = page.getByRole('form', { name: 'Customer' });
    await expect(customerForm.getByRole('button', { name: 'Save changes' })).toBeVisible();
    await expect(customerForm.getByRole('button', { name: 'Publish', exact: true })).toBeDisabled();
    await expectPrimaryAction(page, 'Add field');
    await expectPrimaryAction(page, 'Save changes');

    await page.getByRole('button', { name: 'Add field' }).click();
    await page.getByLabel('Field key').fill('name');
    await page.getByLabel('Label').fill('Name');
    await expectPrimaryAction(page, 'Publish');
    await page.getByRole('button', { name: 'Save changes' }).click();

    await expect(page.getByText('Changes saved')).toBeVisible();
    await expect(page.getByText('Not published', { exact: true })).toHaveCount(1);
    await expect(page.getByText('Not published 2', { exact: true })).toHaveCount(0);
    await page.getByRole('button', { name: 'Publish', exact: true }).click();

    await expect(page.getByText('Published').first()).toBeVisible();
    await expect(customerForm.getByRole('button', { name: 'Publish', exact: true })).toHaveCount(0);
    await expectNoPageOverflow(page);

    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.getByRole('navigation', { name: 'Modules' })).toBeVisible();
    await expect(page.getByText('Published').first()).toBeVisible();
    await expectNoPageOverflow(page);

    expect(objectRequests()).toContain('POST /api/object-definitions');
    expect(objectRequests()).toContain(`PUT /api/object-definitions/${definitionId}/unpublished`);
    expect(objectRequests()).toContain(`POST /api/object-definitions/${definitionId}/publish`);
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
    const objectRequests = await mockObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/objects');
    await page.getByLabel('Name').fill('Application');
    await page.getByRole('button', { name: 'Start definition' }).click();
    await expect(page.getByText('Definition created')).toBeVisible();

    await page.getByRole('button', { name: 'Add field' }).click();
    await page.getByLabel('Field key').fill('status');
    await page.getByLabel('Label').fill('Status');
    await page.getByLabel('Type').selectOption('SingleSelect');

    await expect(page.getByRole('button', { name: 'Publish', exact: true })).toBeDisabled();
    await expect(page.getByText('Single-select fields need at least one option.')).toBeVisible();

    await page.getByLabel('Options').fill('Draft\nSubmitted\nApproved');
    await page.getByRole('checkbox', { name: 'Required' }).click();

    await expect
      .poll(
        () =>
          objectRequests
            .details()
            .find(
              (request) =>
                request.method === 'PUT' &&
                request.path === `/api/object-definitions/${definitionId}/unpublished` &&
                JSON.stringify(request.body).includes('field.required') &&
                JSON.stringify(request.body).includes('field.single_select_options'),
            )?.body,
      )
      .toMatchObject({
        name: 'Application',
        fields: [
          {
            fieldKey: 'status',
            label: 'Status',
            fieldType: 'SingleSelect',
            rules: [
              { definitionKey: 'field.required', parameters: {} },
              {
                definitionKey: 'field.single_select_options',
                parameters: { options: ['Draft', 'Submitted', 'Approved'] },
              },
            ],
          },
        ],
      });
    await expect(page.getByRole('button', { name: 'Publish', exact: true })).toBeEnabled();

    await page.getByRole('button', { name: 'Publish', exact: true }).click();

    await expect(page.getByText('Published').first()).toBeVisible();
    await expect(page.getByLabel('Type')).toHaveValue('SingleSelect');
    await expect(page.getByLabel('Options')).toHaveValue('Draft\nSubmitted\nApproved');
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);

    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.getByText('Published').first()).toBeVisible();
    await expectNoPageOverflow(page);

    expect(objectRequests()).toContain(`POST /api/object-definitions/${definitionId}/publish`);
    expect(pageErrors).toEqual([]);
  });

  test('disabled action affordance remains readable in dark mode', async ({ page }) => {
    await mockAuthenticatedSession(page, { theme: 'dark' });
    await mockObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/objects');

    await expect(page.locator('html')).toHaveClass(/dark/);
    await expect(page.getByRole('button', { name: 'Add field' })).toBeDisabled();
    await expectDisabledActionAffordance(page, 'Add field');
    await expectDarkInactiveSurfaceContrast(page.getByRole('button', { name: 'Add field' }));
    await expectDarkInactiveSurfaceContrast(page.getByLabel('Object key'));
  });

  test('fields editor maximizes inside the app shell without document scroll', async ({ page }) => {
    await mockAuthenticatedSession(page, { theme: 'dark' });
    await mockObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/objects');

    await expect(page.getByRole('button', { name: 'Add field' })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Expand editor' })).toHaveCount(0);

    await page.getByLabel('Name').fill('Customer');
    await page.getByRole('button', { name: 'Start definition' }).click();
    await expect(page.getByText('Definition created')).toBeVisible();
    await expect(
      page.getByText('Fields define the text data this business object captures.'),
    ).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add field' })).toBeEnabled();
    await expect(page.getByRole('group', { name: /field actions/i })).toHaveCount(0);
    await page.getByRole('button', { name: 'Expand editor' }).click();

    await expect(page.getByRole('banner')).toContainText('Objects');
    await expect(page.getByRole('navigation', { name: 'Modules' })).toBeVisible();
    await expect(page.getByRole('form', { name: 'Customer' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Fields' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Definitions' })).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Publish readiness' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Restore layout' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);

    await page.getByRole('button', { name: 'Restore layout' }).click();
    await expect(page.getByRole('region', { name: 'Definitions' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Publish readiness' })).toHaveCount(0);
  });

  test('definition creation errors stay contextual without document scroll', async ({ page }) => {
    await mockAuthenticatedSession(page, { theme: 'dark' });
    await mockObjectDefinitionApi(page, {
      createDefinitionFailure: {
        status: 409,
        body: {
          type: 'urn:axis:problem:objects.objectKeyAlreadyExists',
          title: 'Conflict',
          status: 409,
          detail: 'An object definition with this key already exists in the current workspace.',
          code: 'objects.objectKeyAlreadyExists',
        },
      },
    });

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/objects');

    await page.getByLabel('Name').fill('Application');
    await page.getByRole('button', { name: 'Start definition' }).click();

    const alert = page.getByRole('form', { name: 'Define business object' }).getByRole('alert');
    await expect(alert).toContainText('This definition needs attention');
    await expect(alert).toContainText(
      'An object definition with this key already exists in the current workspace.',
    );
    await expect(alert).toHaveClass(/bg-destructive\/15/);
    await expect(page.getByLabel('Name')).toHaveAttribute('aria-invalid', 'false');
    await expect(alert).not.toContainText('Something went wrong, please try again');
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
  });

  test('field validation stays contextual to editor inputs', async ({ page }) => {
    await mockAuthenticatedSession(page, { theme: 'dark' });
    await mockObjectDefinitionApi(page);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/objects');

    await page.getByLabel('Name').fill('Customer');
    await page.getByRole('button', { name: 'Start definition' }).click();
    await expect(page.getByText('Definition created')).toBeVisible();

    await page.getByRole('button', { name: 'Add field' }).click();
    await page.getByRole('button', { name: 'Save changes' }).click();

    const editorForm = page.getByRole('form', { name: 'Customer' });
    const alert = editorForm
      .getByRole('alert')
      .filter({ hasText: 'This definition needs attention' });
    await expect(alert).toHaveCount(0);
    await expect(editorForm.getByText('Field keys are required.')).toBeVisible();
    await expect(editorForm.getByText('Field labels are required.')).toBeVisible();
    await expect(page.getByLabel('Field key')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByLabel('Label')).toHaveAttribute('aria-invalid', 'true');
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
  });
});
