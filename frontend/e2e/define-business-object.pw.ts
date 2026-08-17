import { expect, type Locator, type Page, test } from '@playwright/test';
import { expectCanonicalTestLanguage } from './canonical-test-language';

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
    metadata: {
      revision,
      createdBy: { kind: 'User', subjectId: profile.id, displayName: profile.fullName },
      createdAt: now,
      modifiedBy: { kind: 'User', subjectId: profile.id, displayName: profile.fullName },
      modifiedAt: now,
    },
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
        metadata: definition.metadata,
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

async function expectResourceWorkspaceScreenshot(
  page: Page,
  name: string,
  { canonicalLanguage = true }: { canonicalLanguage?: boolean } = {},
): Promise<void> {
  if (canonicalLanguage) await expectCanonicalTestLanguage(page);
  const workspace = page.locator('[data-slot="resource-workspace"]');
  await expect(workspace).toBeVisible();
  await expect(workspace).toHaveAttribute('data-axis-surface-contract', 'resource-workspace');
  await expect(workspace).toHaveAttribute('data-axis-surface-id', 'business-object-definitions');
  await page.mouse.move(1, 1);
  await page.evaluate(() => {
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
    document
      .querySelector<HTMLElement>('[data-slot="module-navigation-items"] [aria-current="page"]')
      ?.scrollIntoView({ block: 'nearest', inline: 'center' });
  });
  await page.evaluate(() =>
    Promise.allSettled(
      document.getAnimations({ subtree: true }).map((animation) => animation.finished),
    ),
  );
  await expect(page).toHaveScreenshot(`${name}.png`, {
    animations: 'disabled',
    caret: 'hide',
    fullPage: true,
    scale: 'css',
  });
}

async function expectRecordActionAlignedToCellContent(page: Page): Promise<void> {
  const label = page
    .getByRole('region', { name: 'Definitions' })
    .getByRole('button', { name: 'Customer' })
    .locator('[data-slot="data-table-record-action-label"]');
  await expect(label).toBeVisible();
  await expect
    .poll(() =>
      label.evaluate((element) => {
        const content = element.closest('[data-slot="data-table-cell-content"]');
        if (!content) return Number.POSITIVE_INFINITY;
        return Math.abs(
          element.getBoundingClientRect().left - content.getBoundingClientRect().left,
        );
      }),
    )
    .toBeLessThanOrEqual(0.5);
}

async function expectManagedTaskWindowScreenshot(
  page: Page,
  name: string,
  { canonicalLanguage = true }: { canonicalLanguage?: boolean } = {},
): Promise<void> {
  if (canonicalLanguage) await expectCanonicalTestLanguage(page);
  const activeWindow = page.locator('[data-slot="managed-dialog-window"][data-active="true"]');
  await expect(activeWindow).toBeVisible();
  await expect(activeWindow).toHaveAttribute('data-axis-surface-contract', 'managed-task-window');
  await expect(activeWindow).toHaveAttribute('data-axis-surface-id', 'business-object-editor');
  await page.evaluate(() =>
    Promise.allSettled(
      document.getAnimations({ subtree: true }).map((animation) => animation.finished),
    ),
  );
  await page.evaluate(() => {
    document
      .querySelector<HTMLElement>(
        '[data-slot="managed-dialog-window"][data-active="true"] ' +
          '[data-slot="managed-dialog-footer"] [data-slot="dropdown-menu-trigger"]',
      )
      ?.focus({ preventScroll: true });
    window.getSelection()?.removeAllRanges();
    for (const control of document.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>(
      '[data-slot="managed-dialog-window"] input, [data-slot="managed-dialog-window"] textarea',
    )) {
      const end = control.value.length;
      control.setSelectionRange(end, end);
    }
  });
  await page.mouse.move(1, 1);
  await expect(page).toHaveScreenshot(`${name}.png`, {
    animations: 'disabled',
    caret: 'hide',
    fullPage: true,
    scale: 'css',
  });
}

async function expectManagedTaskWindowTextContrast(
  root: Locator,
  minimumTextNodeCount: number,
): Promise<void> {
  await expect
    .poll(async () => {
      const results = await root.evaluate((root) => {
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
          if (!text || !element || element.closest(':disabled,[aria-disabled="true"]')) continue;
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
            ratio:
              (Math.max(foregroundLuminance, backgroundLuminance) + 0.05) /
              (Math.min(foregroundLuminance, backgroundLuminance) + 0.05),
            text: text.join(' '),
          };
        });
      });

      return {
        coverageShortfall: Math.max(0, minimumTextNodeCount - results.length),
        failures: results.filter(({ ratio }) => ratio < 4.5),
      };
    })
    .toEqual({ coverageShortfall: 0, failures: [] });
}

async function expectManagedTaskWindowNonTextContrast(page: Page): Promise<void> {
  const activeWindow = page.locator('[data-slot="managed-dialog-window"][data-active="true"]');
  const windowsTrigger = activeWindow
    .locator('[data-slot="managed-dialog-footer"]')
    .getByRole('button', { name: /Windows \(\d+\)/ });
  const tabBudget = await activeWindow
    .locator(
      'button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), ' +
        'textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
    )
    .evaluateAll(
      (elements) =>
        elements.filter((element) => {
          const bounds = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          return (
            bounds.width > 0 &&
            bounds.height > 0 &&
            style.display !== 'none' &&
            style.visibility !== 'hidden' &&
            !element.closest('[hidden], [aria-hidden="true"], [inert]')
          );
        }).length + 1,
    );
  await activeWindow.getByRole('button', { name: 'Reset dialog' }).focus();
  const restingSwitcherBorder = await windowsTrigger.evaluate(
    (element) => getComputedStyle(element).borderTopColor,
  );
  for (let index = 0; index < tabBudget; index += 1) {
    if (await windowsTrigger.evaluate((element) => document.activeElement === element)) break;
    await page.keyboard.press('Tab');
  }
  await expect(windowsTrigger).toBeFocused();
  await expectVisibleFocusIndicator(windowsTrigger);
  await expect
    .poll(() => windowsTrigger.evaluate((element) => getComputedStyle(element).borderTopColor))
    .not.toBe(restingSwitcherBorder);

  const readSamples = () =>
    page.evaluate(() => {
      type Rgba = [number, number, number, number];
      const canvas = document.createElement('canvas');
      canvas.width = 1;
      canvas.height = 1;
      const context = canvas.getContext('2d', { willReadFrequently: true });
      if (!context) throw new Error('Expected a 2D canvas context');
      const rgba = (color: string): Rgba => {
        context.clearRect(0, 0, 1, 1);
        context.fillStyle = color;
        context.fillRect(0, 0, 1, 1);
        const value = context.getImageData(0, 0, 1, 1).data;
        return [value[0], value[1], value[2], value[3] / 255];
      };
      const composite = (foreground: Rgba, background: Rgba): Rgba => {
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
      const luminance = ([red, green, blue]: Rgba) => {
        const linear = [red, green, blue].map((channel) => {
          const value = channel / 255;
          return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
        });
        return linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722;
      };
      const ratio = (first: Rgba, second: Rgba) => {
        const firstLuminance = luminance(first);
        const secondLuminance = luminance(second);
        return (
          (Math.max(firstLuminance, secondLuminance) + 0.05) /
          (Math.min(firstLuminance, secondLuminance) + 0.05)
        );
      };
      const effectiveBackground = (element: Element | null): Rgba => {
        const layers: Rgba[] = [];
        for (let node = element; node; node = node.parentElement) {
          layers.push(rgba(getComputedStyle(node).backgroundColor));
        }
        let background: Rgba = [255, 255, 255, 1];
        for (const layer of layers.reverse()) background = composite(layer, background);
        return background;
      };
      const samples: Array<{ label: string; ratio: number }> = [];
      const active = document.querySelector<HTMLElement>(
        '[data-slot="managed-dialog-window"][data-active="true"]',
      );
      if (!active) throw new Error('Managed task window is missing');

      for (const [index, button] of [
        ...active.querySelectorAll<HTMLElement>(
          '[data-slot="managed-dialog-header"] [data-slot="button"]:not(:disabled)',
        ),
      ].entries()) {
        const background = effectiveBackground(button);
        samples.push({
          label: `header-control-${index + 1}`,
          ratio: ratio(composite(rgba(getComputedStyle(button).color), background), background),
        });
      }

      const input = active.querySelector<HTMLElement>('[data-slot="input"]:not(:disabled)');
      if (input) {
        const outside = effectiveBackground(input.parentElement);
        samples.push({
          label: 'editable-field-boundary',
          ratio: ratio(composite(rgba(getComputedStyle(input).borderTopColor), outside), outside),
        });
      }

      const trigger = active.querySelector<HTMLElement>(
        '[data-slot="managed-dialog-footer"] [data-slot="dropdown-menu-trigger"]',
      );
      if (!trigger) throw new Error('Managed task window switcher is missing');
      const triggerOutside = effectiveBackground(trigger.parentElement);
      samples.push({
        label: 'focused-window-switcher-border',
        ratio: ratio(
          composite(rgba(getComputedStyle(trigger).borderTopColor), triggerOutside),
          triggerOutside,
        ),
      });

      const dock = document.querySelector<HTMLElement>(
        '[data-slot="managed-window-tray"] [data-slot="managed-window-dock"]',
      );
      if (dock) {
        for (const [index, button] of [
          ...dock.querySelectorAll<HTMLElement>('[data-slot="button"]:not(:disabled)'),
        ].entries()) {
          const background = effectiveBackground(button);
          samples.push({
            label: `dock-control-${index + 1}`,
            ratio: ratio(composite(rgba(getComputedStyle(button).color), background), background),
          });
        }
      }
      return samples;
    });

  await expect
    .poll(async () => (await readSamples()).filter((sample) => sample.ratio < 3))
    .toEqual([]);
  const results = await readSamples();
  expect(results.length).toBeGreaterThanOrEqual(6);

  await windowsTrigger.click();
  const menu = page.getByRole('menu');
  await expect(menu).toBeVisible();
  await expectManagedTaskWindowTextContrast(menu, 3);
  await page.keyboard.press('Escape');
  await expect(menu).toBeHidden();
}

async function expectManagedTaskWindowContrast(page: Page): Promise<void> {
  await expectManagedTaskWindowTextContrast(
    page.locator('[data-slot="managed-dialog-window"][data-active="true"]'),
    10,
  );
  await expectManagedTaskWindowNonTextContrast(page);
}

async function expectManagedTaskWindowTargetGeometry(
  page: Page,
  minimumSize: 32 | 44,
): Promise<void> {
  const targets = page.locator(
    '[data-slot="managed-dialog-window"][data-active="true"] [data-slot="managed-dialog-header"] [data-slot="button"], ' +
      '[data-slot="managed-dialog-window"][data-active="true"] [data-slot="managed-dialog-footer"] [data-slot="button"], ' +
      '[data-slot="managed-dialog-window"][data-active="true"] [data-slot="managed-dialog-footer"] [data-slot="dropdown-menu-trigger"], ' +
      '[data-slot="managed-window-tray"] [data-slot="button"], ' +
      '[data-slot="managed-window-tray"] [data-slot="dropdown-menu-trigger"]',
  );
  const measurements = await targets.evaluateAll((elements) =>
    elements.map((element) => {
      const bounds = element.getBoundingClientRect();
      return {
        height: bounds.height,
        name: element.getAttribute('aria-label') ?? element.textContent?.trim() ?? '',
        width: bounds.width,
      };
    }),
  );
  expect(measurements.length).toBeGreaterThan(0);
  for (const measurement of measurements) {
    expect(measurement.width, `${measurement.name} width`).toBeGreaterThanOrEqual(minimumSize);
    expect(measurement.height, `${measurement.name} height`).toBeGreaterThanOrEqual(minimumSize);
  }
}

async function expectManagedTaskWindowDesktopGeometry(
  page: Page,
  activeWindow: Locator,
): Promise<void> {
  const workArea = page.locator('[data-slot="managed-window-expanded-layer"]');
  const [workAreaBox, initialBox, minimum] = await Promise.all([
    workArea.boundingBox(),
    activeWindow.boundingBox(),
    activeWindow.evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        height: Number.parseFloat(style.minHeight),
        width: Number.parseFloat(style.minWidth),
      };
    }),
  ]);
  if (!workAreaBox || !initialBox)
    throw new Error('Managed task window work-area geometry missing');
  expect(initialBox.width).toBeCloseTo(workAreaBox.width * 0.5, 0);
  expect(initialBox.height).toBeCloseTo(workAreaBox.height * 0.75, 0);
  expect(minimum.width).toBeCloseTo(workAreaBox.width * 0.35, 0);
  expect(minimum.height).toBeCloseTo(workAreaBox.height * 0.5, 0);

  await activeWindow.getByRole('button', { name: 'Maximize dialog' }).click();
  await expect
    .poll(async () => (await activeWindow.boundingBox())?.width)
    .toBeCloseTo(workAreaBox.width, 0);
  await expect
    .poll(async () => (await activeWindow.boundingBox())?.height)
    .toBeCloseTo(workAreaBox.height, 0);
  await expect
    .poll(async () => (await activeWindow.boundingBox())?.x)
    .toBeCloseTo(workAreaBox.x, 0);
  await expect
    .poll(async () => (await activeWindow.boundingBox())?.y)
    .toBeCloseTo(workAreaBox.y, 0);
  await activeWindow.getByRole('button', { name: 'Restore dialog size' }).click();
  await expect.poll(async () => await activeWindow.boundingBox()).toEqual(initialBox);

  const header = activeWindow.locator('[data-slot="managed-dialog-header"]');
  await header.dblclick({ position: { x: 24, y: 24 } });
  await expect(activeWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
  await header.dblclick({ position: { x: 24, y: 24 } });
  await expect.poll(async () => await activeWindow.boundingBox()).toEqual(initialBox);
}

async function expectManagedTaskWindowHeaderControlGeometry(
  activeWindow: Locator,
  mode: 'compact' | 'desktop',
): Promise<void> {
  const header = activeWindow.locator('[data-slot="managed-dialog-header"]');
  const primary = header.locator('[data-slot="managed-dialog-header-primary"]');
  const identity = header.locator('[data-slot="managed-dialog-header-identity"]');
  const controls = header.locator('[data-slot="managed-dialog-header-controls"]');
  const description = header.locator('[data-slot="dialog-description"]');
  const [headerBox, primaryBox, identityBox, controlsBox, descriptionBox] = await Promise.all([
    header.boundingBox(),
    primary.boundingBox(),
    identity.boundingBox(),
    controls.boundingBox(),
    description.boundingBox(),
  ]);
  if (!headerBox || !primaryBox || !identityBox || !controlsBox || !descriptionBox)
    throw new Error('Managed task window header geometry missing');

  await expect
    .poll(() => header.evaluate((element) => getComputedStyle(element).userSelect))
    .toBe('none');

  if (mode === 'desktop') {
    expect(
      Math.abs(identityBox.y + identityBox.height / 2 - (controlsBox.y + controlsBox.height / 2)),
    ).toBeLessThanOrEqual(1);
    expect(controlsBox.x).toBeGreaterThan(identityBox.x);
    return;
  }

  expect(identityBox.y + identityBox.height).toBeLessThanOrEqual(controlsBox.y);
  expect(controlsBox.y + controlsBox.height).toBeLessThanOrEqual(descriptionBox.y);
  expect(
    Math.abs(controlsBox.x + controlsBox.width / 2 - (headerBox.x + headerBox.width / 2)),
  ).toBeLessThanOrEqual(1);
}

async function openOverlappingManagedDefinitionWindows(page: Page): Promise<{
  catalog: Locator;
  customerWindow: Locator;
  definitionTwoWindow: Locator;
}> {
  const catalog = page.getByRole('region', { name: 'Definitions' });
  await catalog.getByRole('button', { name: 'Customer', exact: true }).click();
  const customerWindow = page
    .getByRole('dialog', { name: 'Customer' })
    .locator('[data-slot="managed-dialog-window"]');
  await expect(customerWindow).toBeVisible();

  const header = customerWindow.locator('[data-slot="managed-dialog-header"]');
  const headerBox = await header.boundingBox();
  if (!headerBox) throw new Error('Managed task window header geometry missing');
  await page.mouse.move(headerBox.x + headerBox.width / 2, headerBox.y + headerBox.height / 2);
  await page.mouse.down();
  await page.mouse.move(
    headerBox.x + headerBox.width / 2 + 120,
    headerBox.y + headerBox.height / 2 + 24,
    { steps: 6 },
  );
  await page.mouse.up();

  await catalog.getByRole('button', { name: 'Definition 02', exact: true }).click();
  const definitionTwoWindow = page
    .getByRole('dialog', { name: 'Definition 02' })
    .locator('[data-slot="managed-dialog-window"]');
  await expect(definitionTwoWindow).toBeVisible();
  await expect(definitionTwoWindow).toHaveAttribute('data-active', 'true');

  const [customerBox, definitionTwoBox] = await Promise.all([
    customerWindow.boundingBox(),
    definitionTwoWindow.boundingBox(),
  ]);
  if (!customerBox || !definitionTwoBox) throw new Error('Managed task window geometry missing');
  expect(
    Math.min(customerBox.x + customerBox.width, definitionTwoBox.x + definitionTwoBox.width) -
      Math.max(customerBox.x, definitionTwoBox.x),
  ).toBeGreaterThan(0);
  expect(
    Math.min(customerBox.y + customerBox.height, definitionTwoBox.y + definitionTwoBox.height) -
      Math.max(customerBox.y, definitionTwoBox.y),
  ).toBeGreaterThan(0);

  return { catalog, customerWindow, definitionTwoWindow };
}

async function prepareCompactManagedTaskWindowCandidate(
  page: Page,
  catalog: Locator,
  definitionTwoWindow: Locator,
): Promise<void> {
  await definitionTwoWindow.getByRole('button', { name: 'Minimize dialog' }).click();
  await catalog.getByRole('button', { name: 'Definition 03', exact: true }).click();
  const definitionThreeWindow = page
    .getByRole('dialog', { name: 'Definition 03' })
    .locator('[data-slot="managed-dialog-window"]');
  await definitionThreeWindow.getByRole('button', { name: 'Minimize dialog' }).click();

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(definitionTwoWindow).toBeHidden();
  await expect(page.getByRole('dialog', { name: 'Customer' })).toBeVisible();
  const activeFooter = page.locator(
    '[data-slot="managed-dialog-window"][data-active="true"] [data-slot="managed-dialog-footer"]',
  );
  const activeWindow = page.locator('[data-slot="managed-dialog-window"][data-active="true"]');
  await expectManagedTaskWindowHeaderControlGeometry(activeWindow, 'compact');
  const windowsTrigger = activeFooter.getByRole('button', { name: 'Windows (3)' });
  await expect(windowsTrigger).toBeVisible();
  await expect(
    page.locator('[data-slot="managed-window-tray"]').getByText('+1', { exact: true }),
  ).toBeVisible();
  await expect(page.locator('[data-slot="managed-window-dock"]')).toHaveCount(1);
  await expect(
    page.locator('[data-slot="managed-window-tray"]').getByRole('button', { name: 'Windows (3)' }),
  ).toHaveCount(0);

  const footerButtons = page.locator(
    '[data-slot="managed-dialog-window"][data-active="true"] [data-slot="managed-dialog-footer"] [data-slot="button"], ' +
      '[data-slot="managed-dialog-window"][data-active="true"] [data-slot="managed-dialog-footer"] [data-slot="dropdown-menu-trigger"]',
  );
  const tray = page.locator('[data-slot="managed-window-tray"]');
  const [buttonBoxes, trayBox] = await Promise.all([
    footerButtons.evaluateAll((buttons) =>
      buttons.map((button) => {
        const bounds = button.getBoundingClientRect();
        return { bottom: bounds.bottom, top: bounds.top };
      }),
    ),
    tray.boundingBox(),
  ]);
  if (!trayBox) throw new Error('Managed task window tray geometry missing');
  for (const buttonBox of buttonBoxes) {
    expect(buttonBox.bottom).toBeLessThanOrEqual(trayBox.y);
  }
  const trayTarget = tray.locator('[data-slot="button"]').first();
  await expect
    .poll(() =>
      trayTarget.evaluate((button) => {
        const bounds = button.getBoundingClientRect();
        const topmost = document.elementFromPoint(
          bounds.left + bounds.width / 2,
          bounds.top + bounds.height / 2,
        );
        const tray = button.closest<HTMLElement>('[data-slot="managed-window-tray"]');
        const expandedLayer = document.querySelector<HTMLElement>(
          '[data-slot="managed-window-expanded-layer"]',
        );
        return {
          expandedLayerZIndex: expandedLayer ? getComputedStyle(expandedLayer).zIndex : null,
          trayIsTopmost: Boolean(topmost?.closest('[data-slot="managed-window-tray"]')),
          trayZIndex: tray ? getComputedStyle(tray).zIndex : null,
        };
      }),
    )
    .toEqual({
      expandedLayerZIndex: '40',
      trayIsTopmost: true,
      trayZIndex: '50',
    });
}

async function expectResourceWorkspaceTargetGeometry(
  page: Page,
  minimumSize: 32 | 44,
): Promise<void> {
  const targets = page
    .locator('[data-slot="resource-workspace"]')
    .locator(
      '[data-slot="button"]:not([data-slot="data-table-resizer"]), ' +
        '[data-slot="data-table-record-action"], [data-slot="input-group-control"], ' +
        '[data-slot="select-trigger"]',
    );
  const measurements = await targets.evaluateAll((elements) =>
    elements.map((element) => {
      const bounds = element.getBoundingClientRect();
      return {
        height: bounds.height,
        name:
          element.getAttribute('aria-label') ??
          element.textContent?.trim() ??
          element.getAttribute('data-slot') ??
          '',
        slot: element.getAttribute('data-slot'),
        width: bounds.width,
      };
    }),
  );

  expect(measurements.length).toBeGreaterThanOrEqual(10);
  expect(
    measurements.every(({ height }) => height >= minimumSize - 1),
    `Resource Workspace targets meet the ${minimumSize}px height: ${JSON.stringify(measurements)}`,
  ).toBe(true);
  expect(
    measurements
      .filter(({ slot }) => slot === 'button' || slot === 'data-table-record-action')
      .every(({ width }) => width >= minimumSize - 1),
    `Resource Workspace buttons meet the ${minimumSize}px width: ${JSON.stringify(measurements)}`,
  ).toBe(true);
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
    await expect(viewport).toHaveAttribute('data-horizontal-overflow', 'compact');
    await expect
      .poll(() => viewport.evaluate((element) => element.scrollWidth > element.clientWidth))
      .toBe(true);
    await viewport.evaluate((element) => element.scrollTo({ left: element.scrollWidth }));
    await expect.poll(() => viewport.evaluate((element) => element.scrollLeft)).toBeGreaterThan(0);
  }

  await viewport.evaluate((element) => element.scrollTo({ left: 0, top: 0 }));
}

async function expectDataTableFitsHorizontally(page: Page): Promise<void> {
  const viewport = page.locator('[data-slot="data-table-viewport"]');
  await expect(viewport).toHaveAttribute('data-horizontal-overflow', 'fitted');
  await expect
    .poll(() =>
      viewport.evaluate((element) =>
        JSON.stringify({
          fits: element.scrollWidth <= element.clientWidth + 1,
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
          tableWidth:
            element.querySelector<HTMLElement>('[data-slot="table"]')?.getBoundingClientRect()
              .width ?? 0,
        }),
      ),
    )
    .toContain('"fits":true');
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

function observeUnexpectedRuntimeErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
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

  test('AT-004 canonical EN resource workspace visual matrix stays touch-safe and motion-safe', async ({
    page,
  }) => {
    const runtimeErrors = observeUnexpectedRuntimeErrors(page);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await mockAuthenticatedSession(page, { language: 'en', theme: 'light' });
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

    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.locator('html')).not.toHaveClass(/dark/);
    await expect(
      page.getByRole('heading', { name: 'Business objects', exact: true }),
    ).toBeVisible();
    const description = page.getByText(
      'Define reusable data contracts for this workspace. Unpublished definitions stay editable; published versions are stable.',
    );
    await expect(description).toBeVisible();
    const catalog = page.getByRole('region', { name: 'Definitions' });
    const toolbar = catalog.locator('[data-slot="data-table-toolbar"]');
    await expect(toolbar).toBeVisible();
    await expect(toolbar.getByLabel('Search business objects')).toBeVisible();
    await expect(toolbar.locator('[data-slot="data-table-toolbar-actions"]')).toBeVisible();
    const versionHeader = catalog.getByRole('columnheader', { name: 'Version' });
    const revisionHeader = catalog.getByRole('columnheader', { name: 'Revision' });
    const firstDataRow = catalog.getByRole('row').nth(1);
    const versionCell = firstDataRow.locator('td[data-cell-kind="version"]');
    const revisionCell = firstDataRow.locator('td[data-cell-kind="revision"]');
    const actorCells = firstDataRow.locator('td[data-cell-kind="actor"]');
    const dateTimeCells = firstDataRow.locator('td[data-cell-kind="dateTime"]');
    await expect(versionHeader).toHaveAttribute('data-align', 'start');
    await expect(revisionHeader).toHaveAttribute('data-align', 'start');
    await expect(versionCell).toHaveAttribute('data-align', 'start');
    await expect(versionCell).toHaveCSS('text-align', 'start');
    await expect(versionCell).toHaveCSS('vertical-align', 'middle');
    await expect(firstDataRow).toHaveAttribute('data-row-layout', 'single-line');
    await expect(versionCell).toHaveText('N/A');
    await expect(revisionCell).toHaveText('r1');
    await expect(actorCells).toHaveCount(1);
    await expect(actorCells).toHaveText('Objects User');
    await expect(dateTimeCells).toHaveCount(1);
    for (const dateTimeCell of await dateTimeCells.all()) {
      await expect(dateTimeCell).not.toContainText(now);
    }
    await expectDataTableScrollsInternally(page);
    await expectDataTableFitsHorizontally(page);
    await expectRecordActionAlignedToCellContent(page);
    await expectResourceWorkspaceTargetGeometry(page, 32);
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
    const ariaTree = await page.locator('[data-slot="resource-workspace"]').ariaSnapshot();
    for (const semanticEntry of [
      'heading "Business objects"',
      'region "Definitions"',
      'textbox "Search business objects"',
      'button "New definition"',
      'table',
    ]) {
      expect(ariaTree).toContain(semanticEntry);
    }
    await expectResourceWorkspaceScreenshot(page, 'resource-workspace-light-desktop-en');

    await page.setViewportSize({ width: 390, height: 844 });
    const newDefinition = page.getByRole('button', { name: 'New definition' });
    await expect(newDefinition).toBeVisible();
    const actionBox = await newDefinition.boundingBox();
    expect(actionBox?.width ?? 0).toBeGreaterThanOrEqual(44);
    expect(actionBox?.height ?? 0).toBeGreaterThanOrEqual(44);
    await newDefinition.focus();
    await expect(newDefinition).toBeFocused();
    await expectVisibleFocusIndicator(newDefinition);
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
    await expectResourceWorkspaceTargetGeometry(page, 44);
    await expectActiveModuleNavigationItemIsRevealed(page);
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
    await expectResourceWorkspaceScreenshot(page, 'resource-workspace-light-compact-en');

    const rulesLink = page.getByRole('link', { name: 'Rules' });
    const restingBackground = await rulesLink.evaluate(
      (element) => getComputedStyle(element).backgroundColor,
    );
    await rulesLink.hover();
    await expect
      .poll(() => rulesLink.evaluate((element) => getComputedStyle(element).backgroundColor))
      .not.toBe(restingBackground);
    await expectReducedMotion(rulesLink);

    await page.getByRole('button', { name: /Account menu/ }).click();
    const accountMenu = page.locator('[data-axis-surface-id="account-actions"]');
    await expect(accountMenu).toBeVisible();
    await expectReducedMotion(accountMenu);
    await page.getByRole('button', { name: 'Dark' }).click();
    await expect(page.locator('html')).toHaveClass(/dark/);
    await page.keyboard.press('Escape');
    await expect(newDefinition).toBeVisible();
    await expect(page.getByRole('link', { name: 'Business objects', exact: true })).toHaveAttribute(
      'aria-current',
      'page',
    );
    await expectResourceWorkspaceTargetGeometry(page, 44);
    await expectDarkReadableContrast(description);
    await expectNoPageOverflow(page);
    await expectResourceWorkspaceScreenshot(page, 'resource-workspace-dark-compact-en');

    await page.setViewportSize({ width: 1280, height: 720 });
    await expectDataTableScrollsInternally(page);
    await expectDataTableFitsHorizontally(page);
    await expectResourceWorkspaceTargetGeometry(page, 32);
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
    await expectResourceWorkspaceScreenshot(page, 'resource-workspace-dark-desktop-en');
    expect(runtimeErrors).toEqual([]);
  });

  test('AT-004 Resource Workspace reflows localized collection content at the 320 CSS pixel boundary', async ({
    page,
  }) => {
    const runtimeErrors = observeUnexpectedRuntimeErrors(page);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await mockAuthenticatedSession(page, { language: 'vi', theme: 'light' });
    await mockBusinessObjectDefinitionApi(page, { initialDefinitions: seededDefinitions(20) });
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto('/business-objects', { waitUntil: 'domcontentloaded' });

    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await expect(
      page.getByRole('heading', { name: 'Business objects', exact: true }),
    ).toBeVisible();
    await expect(
      page.getByText(
        'Định nghĩa contract dữ liệu dùng lại trong workspace. Định nghĩa chưa publish còn chỉnh được; bản publish là phiên bản ổn định.',
      ),
    ).toBeVisible();
    await page.addStyleTag({
      content: `
        [data-slot="resource-workspace"],
        [data-slot="resource-workspace"] * {
          letter-spacing: 0.12em !important;
          line-height: 1.5 !important;
          word-spacing: 0.16em !important;
        }
      `,
    });

    await expectDataTableScrollsInternally(page, { horizontally: true });
    await expectResourceWorkspaceTargetGeometry(page, 44);
    await expectActiveModuleNavigationItemIsRevealed(page);
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
    const search = page.getByLabel('Tìm business object');
    await search.focus();
    await expectVisibleFocusIndicator(search);
    await expectResourceWorkspaceScreenshot(page, 'resource-workspace-light-compact-vi-reflow', {
      canonicalLanguage: false,
    });
    expect(runtimeErrors).toEqual([]);
  });

  for (const theme of ['light', 'dark'] as const) {
    test(`AT-004 Managed Task Window ${theme} desktop and compact candidate stays layered and touch-safe`, async ({
      page,
    }) => {
      const runtimeErrors = observeUnexpectedRuntimeErrors(page);
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await mockAuthenticatedSession(page, { language: 'en', theme });
      await mockBusinessObjectDefinitionApi(page, { initialDefinitions: seededDefinitions(3) });
      await page.setViewportSize({ width: 1280, height: 720 });
      await page.goto('/business-objects');

      await expect(page.locator('html')).toHaveAttribute('lang', 'en');
      if (theme === 'dark') await expect(page.locator('html')).toHaveClass(/dark/);
      else await expect(page.locator('html')).not.toHaveClass(/dark/);

      const { catalog, customerWindow, definitionTwoWindow } =
        await openOverlappingManagedDefinitionWindows(page);
      await expect(customerWindow).not.toHaveAttribute('data-active', 'true');
      await expectManagedTaskWindowDesktopGeometry(page, definitionTwoWindow);
      await expectManagedTaskWindowHeaderControlGeometry(definitionTwoWindow, 'desktop');
      await expect(
        definitionTwoWindow
          .locator('[data-slot="managed-dialog-footer"]')
          .getByRole('button', { name: 'Windows (2)' }),
      ).toBeVisible();
      await expect(page.locator('[data-slot="managed-window-tray"]')).toHaveCount(0);
      await expectManagedTaskWindowTargetGeometry(page, 32);
      await expectReducedMotion(definitionTwoWindow);
      const ariaTree = await definitionTwoWindow.ariaSnapshot();
      for (const semanticEntry of [
        'heading "Definition 02"',
        'tab "General"',
        'tab "Fields"',
        'textbox "Name"',
        'button "Windows (2)"',
        'button "Cancel"',
        'button "Save changes"',
        'button "Publish"',
      ]) {
        expect(ariaTree).toContain(semanticEntry);
      }
      await expectManagedTaskWindowContrast(page);
      await expectNoDesktopDocumentScroll(page);
      await expectNoPageOverflow(page);
      await expectManagedTaskWindowScreenshot(page, `managed-task-window-${theme}-desktop-en`);

      await prepareCompactManagedTaskWindowCandidate(page, catalog, definitionTwoWindow);
      await expectManagedTaskWindowTargetGeometry(page, 44);
      await expectManagedTaskWindowContrast(page);
      await expectNoDesktopDocumentScroll(page);
      await expectNoPageOverflow(page);
      await expectManagedTaskWindowScreenshot(page, `managed-task-window-${theme}-compact-en`);
      expect(runtimeErrors).toEqual([]);
    });
  }

  test('AT-004 Managed Task Window reflows VI at the 320 CSS pixel boundary', async ({ page }) => {
    const runtimeErrors = observeUnexpectedRuntimeErrors(page);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await mockAuthenticatedSession(page, { language: 'vi', theme: 'light' });
    await mockBusinessObjectDefinitionApi(page, { initialDefinitions: seededDefinitions(1) });
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.goto('/business-objects');

    const catalog = page.getByRole('region', { name: 'Định nghĩa' });
    await catalog.getByRole('button', { name: 'Customer', exact: true }).click();
    await page.setViewportSize({ width: 320, height: 900 });
    await expect(page.locator('html')).toHaveAttribute('lang', 'vi');
    await page.evaluate(() => {
      for (const element of document.querySelectorAll<HTMLElement>(
        '[data-slot="managed-dialog-window"], [data-slot="managed-dialog-window"] *',
      )) {
        element.style.setProperty('letter-spacing', '0.12em', 'important');
        element.style.setProperty('line-height', '1.5', 'important');
        element.style.setProperty('word-spacing', '0.16em', 'important');
      }
    });

    await expectManagedTaskWindowHeaderControlGeometry(
      page.locator('[data-slot="managed-dialog-window"][data-active="true"]'),
      'compact',
    );
    await expectManagedTaskWindowTargetGeometry(page, 44);
    await expectReducedMotion(page.locator('[data-slot="managed-dialog-window"]'));
    await expectNoDesktopDocumentScroll(page);
    await expectNoPageOverflow(page);
    await expectManagedTaskWindowScreenshot(page, 'managed-task-window-light-compact-vi-reflow', {
      canonicalLanguage: false,
    });
    expect(runtimeErrors).toEqual([]);
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

    await page.getByRole('button', { name: /Account menu/ }).click();
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
