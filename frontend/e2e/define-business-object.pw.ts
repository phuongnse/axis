import { expect, type Locator, type Page, type TestInfo, test } from '@playwright/test';

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
const secondDefinitionId = '77777777-7777-4777-8777-777777777777';
const fieldId = '44444444-4444-4444-8444-444444444444';
const versionId = '55555555-5555-4555-8555-555555555555';
const bindingId = '66666666-6666-4666-8666-666666666666';
const now = '2026-07-07T00:00:00Z';
const builtInRule = (definitionKey: string, name: string, targetTypeKeys: string[]) => ({
  definitionKey,
  name,
  description: `${name} validation.`,
  origin: 'BuiltIn',
  status: 'Active',
  expressionLanguageVersion: 1,
  latestVersion: 1,
  activeVersion: 1,
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
    builtInRule('field.required', 'Required value', [
      'Text',
      'Integer',
      'Decimal',
      'Date',
      'DateTime',
      'Boolean',
      'Choice',
    ]),
    builtInRule('field.numeric_range', 'Numeric range', ['Integer', 'Decimal']),
    builtInRule('field.date_range', 'Date range', ['Date']),
    builtInRule('field.datetime_range', 'Date and time range', ['DateTime']),
    builtInRule('field.text_length', 'Text length', ['Text']),
    builtInRule('field.text_pattern', 'Text pattern', ['Text']),
    builtInRule('field.text_format', 'Text format', ['Text']),
    builtInRule('field.decimal_precision', 'Decimal precision', ['Decimal']),
    builtInRule('field.choice_selection_count', 'Choice selection count', ['Choice']),
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
  bindingId: string;
  bindingRevision?: number;
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
  canStartCreate?: boolean;
  createDefinitionFailure?: {
    status: number;
    body: unknown;
  };
  initialDefinitions?: BusinessObjectDefinitionDetail[];
}

interface MockBusinessObjectDefinitionRequest {
  method: string;
  path: string;
  body?: unknown;
}

type BusinessObjectDefinitionRequests = (() => string[]) & {
  details: () => readonly MockBusinessObjectDefinitionRequest[];
};

function unpublishedDetail({
  id = definitionId,
  name,
  objectKey,
  revision,
  fields = [],
}: {
  id?: string;
  name: string;
  objectKey: string;
  revision: number;
  fields?: BusinessObjectFieldRequest[];
}) {
  return {
    id,
    workspaceId: profile.workspaceId,
    name,
    objectKey,
    status: 'Unpublished',
    revision,
    latestPublishedVersionNumber: null as number | null,
    createdAt: now,
    updatedAt: now,
    actions: { canSave: true, canPublish: true },
    fields: fields.map((field, index) => ({
      id: index === 0 ? fieldId : `44444444-4444-4444-8444-${String(index).padStart(12, '0')}`,
      order: index,
      ...field,
    })),
    latestPublishedVersion: null as {
      id: string;
      versionNumber: number;
      publishedBySubject: { kind: 'Human'; subjectId: string };
      publishedAt: string;
      fields: unknown[];
    } | null,
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
      id: definition.id,
      name: definition.name,
      objectKey: definition.objectKey,
      revision: definition.revision,
      fields,
    }),
    status: 'Published',
    actions: { canSave: false, canPublish: false },
    latestPublishedVersionNumber: 1,
    latestPublishedVersion: {
      id: versionId,
      versionNumber: 1,
      publishedBySubject: { kind: 'Human', subjectId: profile.id },
      publishedAt: now,
      fields: fields.map((field, index) => ({
        id: index === 0 ? fieldId : `44444444-4444-4444-8444-${String(index).padStart(12, '0')}`,
        ...field,
        order: index,
      })),
    },
  };
}

async function mockAuthenticatedSession(
  page: Page,
  options: { language?: 'en' | 'vi'; theme?: TestTheme } = {},
): Promise<void> {
  const language = options.language ?? 'en';
  const theme = options.theme ?? 'light';
  const sessionProfile = { ...profile, language, theme };

  await page.addInitScript(
    ({ selectedLanguage, selectedTheme }) => {
      (window as Window & { __AXIS_DISABLE_DEVTOOLS__?: boolean }).__AXIS_DISABLE_DEVTOOLS__ = true;
      localStorage.setItem('axis.language', selectedLanguage);
      localStorage.setItem('axis.theme', selectedTheme);
    },
    { selectedLanguage: language, selectedTheme: theme },
  );

  await page.route('**/api/auth/session', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        authenticated: true,
        csrfToken: 'objects-csrf-token',
        user: {
          userId: sessionProfile.id,
          workspaceId: sessionProfile.workspaceId,
          email: sessionProfile.email,
          name: sessionProfile.fullName,
        },
      }),
    });
  });

  await page.route('**/api/users/me', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(sessionProfile),
    });
  });

  await page.route('**/api/workspace-context/eligible', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          workspaceId: sessionProfile.workspaceId,
          name: 'Test workspace',
          slug: 'test-workspace',
          type: 'Organization',
          organizationId: '33333333-3333-4333-8333-333333333333',
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
        availableContributionIds: [
          'identity.memberships',
          'identity.service-identities',
          'authorization.product-roles',
          'businessObjects.definitions',
          'rules.fieldDefinitions',
          'solutions.management',
        ],
      }),
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
  const definitions = new Map(
    (options.initialDefinitions ?? []).map((definition) => [definition.id, definition]),
  );
  const requests: MockBusinessObjectDefinitionRequest[] = [];

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

  await page.route('**/api/rule-bindings', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const requestEntry: MockBusinessObjectDefinitionRequest = { method, path: url.pathname };
    requests.push(requestEntry);

    if (method === 'POST') {
      requestEntry.body = request.postDataJSON();
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({ id: bindingId, revision: 1 }),
      });
      return;
    }

    await route.fulfill({ status: 404, body: `${method} ${url.pathname}` });
  });

  await page.route('**/api/business-object-definitions**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const requestEntry: MockBusinessObjectDefinitionRequest = { method, path: url.pathname };
    requests.push(requestEntry);

    if (method === 'GET' && url.pathname === '/api/business-object-definitions/actions') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ canStartCreate: options.canStartCreate ?? true }),
      });
      return;
    }

    if (method === 'GET' && url.pathname === '/api/business-object-definitions') {
      const candidates = [...definitions.values()].map((definition) => ({
        id: definition.id,
        name: definition.name,
        objectKey: definition.objectKey,
        status: definition.status,
        revision: definition.revision,
        latestPublishedVersionNumber: definition.latestPublishedVersionNumber,
        updatedAt: definition.updatedAt,
      }));
      const query = url.searchParams.get('query')?.trim().toLocaleLowerCase();
      const items = query
        ? candidates.filter((definition) =>
            [definition.name, definition.objectKey].some((value) =>
              value.toLocaleLowerCase().includes(query),
            ),
          )
        : candidates;

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items, totalCount: items.length, page: 1, pageSize: 20 }),
      });
      return;
    }

    const detailMatch = url.pathname.match(/^\/api\/business-object-definitions\/([^/]+)$/);
    if (method === 'GET' && detailMatch) {
      const definition = definitions.get(detailMatch[1]);
      if (!definition) {
        await route.fulfill({ status: 404, body: `${method} ${url.pathname}` });
        return;
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(definition),
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
      const definition = unpublishedDetail({
        name: body.name,
        objectKey: deriveObjectKey(body.name),
        revision: 1,
      });
      definitions.set(definition.id, definition);
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(definition),
      });
      return;
    }

    const saveMatch = url.pathname.match(
      /^\/api\/business-object-definitions\/([^/]+)\/unpublished$/,
    );
    if (method === 'PUT' && saveMatch) {
      const currentDefinition = definitions.get(saveMatch[1]);
      if (!currentDefinition) {
        await route.fulfill({ status: 404, body: `${method} ${url.pathname}` });
        return;
      }
      const body = request.postDataJSON() as BusinessObjectDefinitionRequest;
      requestEntry.body = body;
      const definition = unpublishedDetail({
        id: currentDefinition.id,
        name: body.name,
        objectKey: currentDefinition.objectKey,
        revision: currentDefinition.revision + 1,
        fields: body.fields?.map((field) => ({
          ...field,
          rules: field.rules?.map((rule) => ({ ...rule, bindingRevision: 1 })),
        })),
      });
      definitions.set(definition.id, definition);
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(definition),
      });
      return;
    }

    const publishMatch = url.pathname.match(
      /^\/api\/business-object-definitions\/([^/]+)\/publish$/,
    );
    if (method === 'POST' && publishMatch) {
      const currentDefinition = definitions.get(publishMatch[1]);
      if (!currentDefinition) {
        await route.fulfill({ status: 404, body: `${method} ${url.pathname}` });
        return;
      }
      const definition = publishedDetail(currentDefinition);
      definitions.set(definition.id, definition);
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(definition),
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

function seededDefinitions(count: number): BusinessObjectDefinitionDetail[] {
  return Array.from({ length: count }, (_, index) =>
    unpublishedDetail({
      id: index === 0 ? definitionId : `80000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
      name: index === 0 ? 'Customer' : `Definition ${String(index + 1).padStart(2, '0')}`,
      objectKey: index === 0 ? 'customer' : `definition_${index + 1}`,
      revision: 1,
    }),
  );
}

async function attachGoldenScreenshot(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await page.evaluate(() => {
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
    document
      .querySelector<HTMLElement>('[data-slot="module-navigation-items"] [aria-current="page"]')
      ?.scrollIntoView({ block: 'nearest', inline: 'center' });
  });
  await page.mouse.move(1, 1);
  await testInfo.attach(name, {
    body: await page.screenshot({ animations: 'disabled', caret: 'hide', scale: 'css' }),
    contentType: 'image/png',
  });
}

async function expectDataTableFitsHorizontally(page: Page): Promise<void> {
  const viewport = page.locator('[data-slot="data-table-viewport"]');
  await expect
    .poll(() => viewport.evaluate((element) => element.scrollWidth <= element.clientWidth))
    .toBe(true);
}

async function expectActiveModuleNavigationItemIsRevealed(page: Page): Promise<void> {
  const activeItem = page.locator('[data-slot="module-navigation-items"] [aria-current="page"]');
  await expect
    .poll(() =>
      activeItem.evaluate((element) => {
        const viewport = element.closest('[data-slot="module-navigation-items"]');
        if (!viewport) return false;
        const itemBox = element.getBoundingClientRect();
        const viewportBox = viewport.getBoundingClientRect();
        return itemBox.left >= viewportBox.left && itemBox.right <= viewportBox.right;
      }),
    )
    .toBe(true);
}

async function expectDataTableScrollsInternally(
  page: Page,
  options: { horizontally?: boolean } = {},
): Promise<void> {
  const viewport = page.locator('[data-slot="data-table-viewport"]');
  await expect(viewport).toBeVisible();
  await expect
    .poll(() => viewport.evaluate((element) => element.scrollHeight > element.clientHeight))
    .toBe(true);
  await viewport.evaluate((element) => element.scrollTo({ top: element.scrollHeight }));
  await expect.poll(() => viewport.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);

  if (options.horizontally) {
    await expect
      .poll(() => viewport.evaluate((element) => element.scrollWidth > element.clientWidth))
      .toBe(true);
    await viewport.evaluate((element) => element.scrollTo({ left: element.scrollWidth }));
    await expect.poll(() => viewport.evaluate((element) => element.scrollLeft)).toBeGreaterThan(0);
  }

  await viewport.evaluate((element) => element.scrollTo({ left: 0, top: 0 }));
}

async function expectReducedMotion(locator: Locator): Promise<void> {
  await expect
    .poll(() =>
      locator.evaluate((root) => {
        const milliseconds = (value: string) =>
          value.split(',').map((part) => {
            const duration = part.trim();
            return duration.endsWith('ms')
              ? Number.parseFloat(duration)
              : Number.parseFloat(duration) * 1000;
          });
        const elements = [root, ...root.querySelectorAll('*')];
        return Math.max(
          ...elements.flatMap((element) => {
            const style = getComputedStyle(element);
            return [
              ...milliseconds(style.animationDuration),
              ...milliseconds(style.transitionDuration),
            ];
          }),
        );
      }),
    )
    .toBeLessThanOrEqual(0.1);
}

test.describe('define business object', () => {
  test('denied create affordance blocks toolbar and deep-link launch', async ({ page }) => {
    await mockAuthenticatedSession(page);
    await mockBusinessObjectDefinitionApi(page, { canStartCreate: false });

    await page.goto('/business-objects?dialog=create');

    await expect(page).toHaveURL(/\/business-objects\?page=1$/);
    await expect(page.getByRole('button', { name: 'New definition' })).toHaveCount(0);
    await expect(page.getByRole('dialog', { name: 'Define business object' })).toHaveCount(0);
  });

  test('AT-004 resource workspace visual matrix stays touch-safe and motion-safe', async ({
    page,
  }, testInfo) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await mockAuthenticatedSession(page, { language: 'vi', theme: 'light' });
    await mockBusinessObjectDefinitionApi(page, { initialDefinitions: seededDefinitions(20) });
    await page.route('**/api/users/me/preferences/theme', async (route) => {
      const theme = JSON.parse(route.request().postData() ?? '{}').theme ?? 'light';
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ theme }),
      });
    });

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('/business-objects');

    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    await expect(
      page.getByRole('heading', { name: 'Business objects', exact: true }),
    ).toBeVisible();
    const description = page.getByText('Định nghĩa contract dữ liệu dùng lại trong workspace.', {
      exact: false,
    });
    await expect(description).toBeVisible();
    const catalog = page.getByRole('region', { name: 'Danh sách định nghĩa' });
    const toolbar = catalog.locator('[data-slot="data-table-toolbar"]');
    await expect(toolbar).toBeVisible();
    await expect(toolbar.getByLabel('Tìm business object')).toBeVisible();
    await expect(toolbar.locator('[data-slot="data-table-toolbar-actions"]')).toBeVisible();
    await expectDataTableScrollsInternally(page);
    await expectDataTableFitsHorizontally(page);
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
    await attachGoldenScreenshot(page, testInfo, 'business-objects-light-desktop-vi');

    await page.setViewportSize({ width: 390, height: 844 });
    const newDefinition = page.getByRole('button', { name: 'Định nghĩa mới' });
    await expect(newDefinition).toBeVisible();
    const actionBox = await newDefinition.boundingBox();
    expect(actionBox?.width ?? 0).toBeGreaterThanOrEqual(44);
    expect(actionBox?.height ?? 0).toBeGreaterThanOrEqual(44);
    await newDefinition.focus();
    await expect(newDefinition).toBeFocused();
    await expect(page.getByRole('link', { name: 'Business objects', exact: true })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expect
      .poll(() => description.evaluate((element) => element.scrollWidth <= element.clientWidth))
      .toBe(true);
    await expect
      .poll(() => toolbar.evaluate((element) => element.scrollWidth <= element.clientWidth))
      .toBe(true);
    await expectDataTableScrollsInternally(page, { horizontally: true });
    await expectActiveModuleNavigationItemIsRevealed(page);
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
    await attachGoldenScreenshot(page, testInfo, 'business-objects-light-compact-vi');

    const rulesLink = page.getByRole('link', { name: 'Rules' });
    const restingBackground = await rulesLink.evaluate(
      (element) => getComputedStyle(element).backgroundColor,
    );
    await rulesLink.hover();
    await expect
      .poll(() => rulesLink.evaluate((element) => getComputedStyle(element).backgroundColor))
      .not.toBe(restingBackground);
    await expectReducedMotion(rulesLink);

    await page.getByRole('button', { name: 'Menu tài khoản' }).click();
    const accountMenu = page.locator('[data-slot="popover-content"][aria-label="Menu tài khoản"]');
    await expect(accountMenu).toBeVisible();
    await expectReducedMotion(accountMenu);
    await page.getByRole('button', { name: 'Tối' }).click();
    await expect(page.locator('html')).toHaveClass(/dark/);
    await page.keyboard.press('Escape');
    await expect(newDefinition).toBeVisible();
    await expect(page.getByRole('link', { name: 'Business objects', exact: true })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expectNoPageOverflow(page);
    await attachGoldenScreenshot(page, testInfo, 'business-objects-dark-compact-vi');

    await page.setViewportSize({ width: 1280, height: 720 });
    await expectDataTableScrollsInternally(page);
    await expectDataTableFitsHorizontally(page);
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
    await attachGoldenScreenshot(page, testInfo, 'business-objects-dark-desktop-vi');
  });

  test('AT-005 resource workspace integrates independent managed definition windows', async ({
    page,
  }) => {
    await mockAuthenticatedSession(page);
    await mockBusinessObjectDefinitionApi(page, {
      initialDefinitions: [
        unpublishedDetail({ name: 'Customer', objectKey: 'customer', revision: 1 }),
        unpublishedDetail({
          id: secondDefinitionId,
          name: 'Order',
          objectKey: 'order',
          revision: 1,
        }),
      ],
    });

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto('/business-objects');
    const catalog = page.getByRole('region', { name: 'Definitions' });
    await catalog.getByRole('button', { name: 'Customer', exact: true }).click();
    const customerDialog = page.getByRole('dialog', { name: 'Customer' });
    await expect(customerDialog).toBeVisible();
    await customerDialog.getByRole('button', { name: 'Minimize dialog' }).click();
    await expect(customerDialog).toBeHidden();

    await catalog.getByRole('button', { name: 'Order', exact: true }).click();
    const orderDialog = page.getByRole('dialog', { name: 'Order' });
    await expect(orderDialog).toBeVisible();
    await page.getByRole('button', { name: 'Windows (2)' }).click();
    await page.getByRole('menuitem', { name: /Customer/ }).click();
    await expect(customerDialog).toBeVisible();

    const customerWindow = page.locator(
      `[data-slot="managed-dialog-window"][data-window-id="business-objects:${definitionId}"]`,
    );
    const orderWindow = page.locator(
      `[data-slot="managed-dialog-window"][data-window-id="business-objects:${secondDefinitionId}"]`,
    );
    const [customerBox, orderBox] = await Promise.all([
      customerWindow.boundingBox(),
      orderWindow.boundingBox(),
    ]);
    if (!customerBox || !orderBox) throw new Error('Managed definition window geometry missing');
    expect(
      Math.min(customerBox.x + customerBox.width, orderBox.x + orderBox.width) -
        Math.max(customerBox.x, orderBox.x),
    ).toBeGreaterThan(0);
    expect(
      Math.min(customerBox.y + customerBox.height, orderBox.y + orderBox.height) -
        Math.max(customerBox.y, orderBox.y),
    ).toBeGreaterThan(0);
    await expect(customerWindow).toHaveAttribute('data-active', 'true');

    await page.getByRole('button', { name: 'Windows (2)' }).click();
    await page.getByRole('menuitem', { name: /Order/ }).click();
    await expect(orderWindow).toHaveAttribute('data-active', 'true');
    await orderWindow.getByLabel('Name', { exact: true }).focus();
    await page.keyboard.press('Shift+Tab');
    expect(await orderWindow.evaluate((window) => window.contains(document.activeElement))).toBe(
      true,
    );

    await orderWindow.getByRole('button', { name: 'Minimize dialog' }).click();
    const orderDock = page.locator(
      `[data-slot="managed-window-dock"][data-window-id="business-objects:${secondDefinitionId}"]`,
    );
    await expect(orderDock).toBeVisible();
    await orderDock.locator('[data-action="restore"]').click();
    await expect(orderWindow).toBeVisible();
    await orderWindow.getByLabel('Name', { exact: true }).focus();
    await page.keyboard.press('Escape');
    await expect(orderWindow).toBeHidden();
    await expect(customerWindow).toBeVisible();

    const customerName = customerWindow.getByLabel('Name', { exact: true });
    await customerName.fill('Customer draft');
    await expect(customerName).toHaveValue('Customer draft');
    await customerWindow.getByRole('button', { name: 'Minimize dialog' }).click();
    await page.getByRole('link', { name: 'Rules' }).click();
    await expect(page).toHaveURL(/\/rules\?page=1$/);
    await expect(page.getByRole('heading', { name: 'Rules', exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'Business objects', exact: true }).click();
    await expect(page).toHaveURL(/\/business-objects\?page=1$/);

    await page.getByRole('button', { name: 'Windows (1)' }).click();
    const customerMenuItem = page.getByRole('menuitem', { name: /Customer/ });
    await expect(
      customerMenuItem.locator('[data-slot="managed-window-dirty-indicator"]'),
    ).toBeVisible();
    await customerMenuItem.click();
    await expect(customerWindow).toBeVisible();
    await expect(customerName).toHaveValue('Customer draft');
    await customerName.focus();
    await page.keyboard.press('Escape');

    const discardDialog = page.getByRole('alertdialog', { name: 'Discard unsaved changes?' });
    await expect(discardDialog).toBeVisible();
    expect(await discardDialog.evaluate((dialog) => dialog.contains(document.activeElement))).toBe(
      true,
    );
    await page.keyboard.press('Escape');
    await expect(discardDialog).toBeHidden();
    await expect(customerWindow).toBeVisible();
    await expect(customerName).toHaveValue('Customer draft');
    expect(await customerWindow.evaluate((window) => window.contains(document.activeElement))).toBe(
      true,
    );
    await expectNoPageOverflow(page);
  });

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
    await expect(page).toHaveURL(/\/rules\?page=1$/);
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

  test('business object catalog search is server-driven and shareable', async ({ page }) => {
    await mockAuthenticatedSession(page);
    await mockBusinessObjectDefinitionApi(page);
    await page.goto('/business-objects');

    await page.getByRole('button', { name: 'New definition' }).click();
    let dialog = page.getByRole('dialog', { name: 'Define business object' });
    await dialog.getByLabel('Name', { exact: true }).fill('Customer');
    await dialog.getByRole('button', { name: 'Start definition' }).click();
    dialog = page.getByRole('dialog', { name: 'Customer' });
    await dialog.getByRole('button', { name: 'Close dialog' }).click();

    const catalog = page.getByRole('region', { name: 'Definitions' });
    const search = catalog.getByLabel('Search business objects');
    await search.fill('customer');
    await expect.poll(() => new URL(page.url()).searchParams.get('query')).toBe('customer');
    await expect(catalog.getByRole('button', { name: 'Customer', exact: true })).toBeVisible();

    await search.fill('missing');
    await expect(catalog.getByText('No matching rows')).toBeVisible();
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
    const publishReview = page.locator('[data-slot="alert-dialog-content"]');
    await expect(
      publishReview.getByRole('heading', { name: 'Publish this definition?' }),
    ).toBeVisible();
    await expect(publishReview).toContainText(
      'Publishing creates an immutable version that future records will use.',
    );
    await expect(publishReview).toContainText('Object keycustomer');
    await expect(publishReview).toContainText('Fields1');
    await publishReview.getByRole('button', { name: 'Publish', exact: true }).click();

    const publishedDetails = dialog.locator('[data-slot="business-object-read-only-details"]');
    await expect(publishedDetails).toBeVisible();
    await expect(publishedDetails.getByText('customer', { exact: true })).toBeVisible();
    await expect(dialog.getByRole('tab')).toHaveText(['General', 'Fields']);
    await dialog.getByRole('tab', { name: 'Fields' }).click();
    await expect(publishedDetails.getByRole('heading', { name: 'Name' })).toBeVisible();
    await expect(publishedDetails.getByText('Text', { exact: true })).toBeVisible();
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
  }) => {
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
            .find((request) => request.method === 'POST' && request.path === '/api/rule-bindings')
            ?.body,
      )
      .toMatchObject({
        definitionKey: 'field.required',
        definitionVersion: 1,
        targetType: 'business-object-field',
        targetId: 'application.status',
        useCaseOrTrigger: 'field-validation',
        inputMappings: {
          value: { kind: 'Context', contextKey: 'record.value', literalValues: [] },
        },
      });
    await expect
      .poll(
        () =>
          objectRequests
            .details()
            .find(
              (request) =>
                request.method === 'PUT' &&
                request.path === `/api/business-object-definitions/${definitionId}/unpublished`,
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
            rules: [{ bindingId }],
          },
        ],
      });
    const savedDefinition = objectRequests
      .details()
      .find(
        (request) =>
          request.method === 'PUT' &&
          request.path === `/api/business-object-definitions/${definitionId}/unpublished`,
      )?.body as BusinessObjectDefinitionRequest;
    expect(savedDefinition.fields?.[0]?.rules).toEqual([{ bindingId }]);
    await expect(dialog.getByRole('button', { name: 'Publish', exact: true })).toBeEnabled();

    await dialog.getByRole('button', { name: 'Publish', exact: true }).click();
    const publishReview = page.locator('[data-slot="alert-dialog-content"]');
    await expect(
      publishReview.getByRole('heading', { name: 'Publish this definition?' }),
    ).toBeVisible();
    await expect(publishReview).toContainText('Fields1');
    await expect(publishReview).toContainText('Field rules1');
    await publishReview.getByRole('button', { name: 'Publish', exact: true }).click();

    const publishedDetails = dialog.locator('[data-slot="business-object-read-only-details"]');
    await expect(publishedDetails).toBeVisible();
    await expect(dialog.getByRole('tab')).toHaveText(['General', 'Fields']);
    await dialog.getByRole('tab', { name: 'Fields' }).click();
    await expect(publishedDetails.getByRole('heading', { name: 'Status' })).toBeVisible();
    await expect(publishedDetails.getByText('Choice', { exact: true })).toBeVisible();
    await expect(publishedDetails.getByText('Single', { exact: true })).toBeVisible();
    await expect(publishedDetails.getByText('Approved', { exact: false })).toBeVisible();
    await expect(publishedDetails.getByText(bindingId, { exact: true })).toBeVisible();
    await expect(publishedDetails).toContainText('Binding revision: 1');
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
