import { Buffer } from 'node:buffer';
import { expect, type Locator, type Page, test } from '@playwright/test';
import type { components } from '../src/lib/api-types';

type CreateRuleRequest = components['schemas']['CreateRuleDefinitionRequest'];
type RuleDetail = components['schemas']['RuleDefinitionDetailDto'];
type RuleExpressionLanguage = components['schemas']['RuleExpressionLanguageDto'];
type RuleVersion = components['schemas']['RuleDefinitionVersionDto'];
type SaveRuleRequest = components['schemas']['SaveRuleDefinitionDraftRequest'];

const profile = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'rules@example.com',
  fullName: 'Rules User',
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
const now = '2026-07-10T00:00:00Z';
const systemRule = (definitionKey: string, name: string, targetTypeKeys: string[]) => ({
  definitionKey,
  name,
  description: `${name} validation.`,
  origin: 'System',
  scope: 'Field',
  outcomeKind: 'Validation',
  status: 'Published',
  expressionLanguageVersion: 1,
  latestPublishedVersion: 1,
  applicability: { targetTypeKeys, configurationConstraints: {} },
  parameters: [],
});
const systemRules = [
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
  systemRule('field.decimal_precision', 'Decimal precision', ['Decimal']),
  systemRule('field.date_range', 'Date range', ['Date']),
  systemRule('field.datetime_range', 'Date and time range', ['DateTime']),
  systemRule('field.text_length', 'Text length', ['Text']),
  systemRule('field.text_pattern', 'Text pattern', ['Text']),
  systemRule('field.text_format', 'Text format', ['Text']),
  systemRule('field.choice_selection_count', 'Choice selection count', ['Choice']),
];
const contextSchemas = [
  {
    contextKey: 'business_objects.field.decimal',
    version: 1,
    scope: 'Field',
    displayName: 'Decimal field value',
    fields: [
      { path: 'field.value', displayName: 'Field value', type: 'Decimal', allowMultiple: false },
    ],
    targetTypeKey: 'Decimal',
    configuration: {},
  },
];
const comparableTypes = ['Integer', 'Decimal', 'Date', 'DateTime'] as const;
const expressionLanguage: RuleExpressionLanguage = {
  version: 1,
  operators: [
    {
      operator: 'Equal',
      leftShapes: ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'].map((type) => ({
        type,
        cardinality: 'Any',
      })),
      rightShapes: ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'].map((type) => ({
        type,
        cardinality: 'Any',
      })),
      requiresMatchingTypes: true,
    },
    {
      operator: 'GreaterThan',
      leftShapes: comparableTypes.map((type) => ({ type, cardinality: 'Scalar' })),
      rightShapes: comparableTypes.map((type) => ({ type, cardinality: 'Scalar' })),
      requiresMatchingTypes: true,
    },
  ],
  functions: [
    {
      function: 'IsBlank',
      parameters: [
        {
          acceptedTypes: ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'],
          cardinality: 'Any',
        },
      ],
      returnType: 'Boolean',
      returnCardinality: 'Scalar',
    },
  ],
  limits: {
    maxDepth: 12,
    maxNodes: 200,
    maxFunctionCalls: 50,
    maxParameters: 100,
    maxExecutionSteps: 1000,
  },
};

function systemDetail(definitionKey: string): RuleDetail | null {
  const definition = systemRules.find((candidate) => candidate.definitionKey === definitionKey);
  if (!definition) return null;
  return {
    ...definition,
    revision: null,
    contextKey: null,
    contextSchemaVersion: null,
    condition: {
      nodeId: 'required_check',
      predicateOperator: 'Equal',
      left: {
        kind: 'Function',
        function: 'IsBlank',
        arguments: [{ kind: 'Context', reference: 'field.value', arguments: [] }],
      },
      right: {
        kind: 'Literal',
        literal: { type: 'Boolean', values: ['true'] },
        arguments: [],
      },
      children: [],
    },
    outcome: {
      kind: 'Validation',
      violationCode: `${definitionKey}.failed`,
      severity: 'Error',
      message: `${definition.name} validation failed.`,
    },
    versions: [],
    createdAt: null,
    updatedAt: null,
    archivedAt: null,
  };
}

interface CapturedRequest {
  method: string;
  path: string;
  body?: unknown;
}

function base64UrlJson(value: unknown): string {
  return Buffer.from(JSON.stringify(value), 'utf8').toString('base64url');
}

function accessToken(): string {
  return [
    base64UrlJson({ alg: 'none', typ: 'JWT' }),
    base64UrlJson({ sub: profile.id, email: profile.email, name: profile.fullName }),
    'signature',
  ].join('.');
}

function deriveRuleKey(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
}

function summary(detail: RuleDetail) {
  return {
    definitionKey: detail.definitionKey,
    name: detail.name,
    description: detail.description,
    origin: detail.origin,
    scope: detail.scope,
    outcomeKind: detail.outcomeKind,
    status: detail.status,
    revision: detail.revision,
    latestPublishedVersion: detail.latestPublishedVersion,
    contextKey: detail.contextKey,
    contextSchemaVersion: detail.contextSchemaVersion,
    applicability: null,
    parameters: detail.parameters,
    updatedAt: detail.updatedAt,
  };
}

async function mockAuthenticatedSession(page: Page): Promise<void> {
  await page.addInitScript(() => {
    window.__AXIS_DISABLE_DEVTOOLS__ = true;
    localStorage.setItem('axis.language', 'en');
    localStorage.setItem('axis.theme', 'light');
  });
  await page.route('**/connect/authorize**', async (route) => {
    const requestUrl = new URL(route.request().url());
    const callbackUrl = new URL('/callback', requestUrl.origin);
    callbackUrl.searchParams.set('code', 'rules-code');
    callbackUrl.searchParams.set('state', requestUrl.searchParams.get('state') ?? '');
    await route.fulfill({ status: 302, headers: { location: callbackUrl.toString() } });
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
}

async function mockRulesApi(page: Page): Promise<CapturedRequest[]> {
  let detail: RuleDetail | null = null;
  const requests: CapturedRequest[] = [];

  await page.route('**/api/rules**', async (route) => {
    const request = route.request();
    const method = request.method();
    const path = new URL(request.url()).pathname;
    const captured: CapturedRequest = { method, path };
    requests.push(captured);

    if (method === 'GET' && path === '/api/rules/context-schemas') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(contextSchemas),
      });
      return;
    }

    if (method === 'GET' && path === '/api/rules/expression-language') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(expressionLanguage),
      });
      return;
    }

    if (method === 'GET' && path === '/api/rules') {
      const items = detail ? [...systemRules, summary(detail)] : systemRules;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items, totalCount: items.length, page: 1, pageSize: 100 }),
      });
      return;
    }

    if (method === 'GET' && path.startsWith('/api/rules/field.')) {
      const definition = systemDetail(path.slice('/api/rules/'.length));
      await route.fulfill({
        status: definition ? 200 : 404,
        contentType: 'application/json',
        body: JSON.stringify(definition ?? {}),
      });
      return;
    }

    if (method === 'POST' && path === '/api/rules') {
      const body = request.postDataJSON() as CreateRuleRequest;
      captured.body = body;
      detail = {
        definitionKey: deriveRuleKey(body.name ?? ''),
        name: body.name,
        description: body.description,
        origin: 'Workspace',
        scope: body.scope,
        outcomeKind: body.outcomeKind,
        status: 'Draft',
        expressionLanguageVersion: 1,
        revision: 1,
        latestPublishedVersion: null,
        contextKey: body.contextKey,
        contextSchemaVersion: body.contextSchemaVersion,
        parameters: [],
        versions: [],
        createdAt: now,
        updatedAt: now,
        archivedAt: null,
      };
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify(detail),
      });
      return;
    }

    if (!detail || path !== `/api/rules/${detail.definitionKey}`) {
      const action = detail ? path.replace(`/api/rules/${detail.definitionKey}/`, '') : '';
      if (!detail || !['draft', 'simulate', 'publish', 'archive'].includes(action)) {
        await route.fulfill({ status: 404, body: '{}' });
        return;
      }

      captured.body = request.postDataJSON();
      if (method === 'PUT' && action === 'draft') {
        const body = captured.body as SaveRuleRequest;
        detail = {
          ...detail,
          ...body,
          status: 'Draft',
          revision: (detail.revision ?? 0) + 1,
          updatedAt: now,
        };
      } else if (method === 'POST' && action === 'simulate') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            definitionKey: detail.definitionKey,
            definitionVersion: null,
            isMatch: true,
            outcome: detail.outcome,
            diagnostics: [{ nodeId: 'root', isMatch: true }],
            correlationId: 'rules-browser-test',
          }),
        });
        return;
      } else if (method === 'POST' && action === 'publish') {
        const versionNumber = (detail.latestPublishedVersion ?? 0) + 1;
        const version: RuleVersion = {
          version: versionNumber,
          name: detail.name,
          description: detail.description,
          scope: detail.scope,
          outcomeKind: detail.outcomeKind,
          expressionLanguageVersion: detail.expressionLanguageVersion,
          contextKey: detail.contextKey,
          contextSchemaVersion: detail.contextSchemaVersion,
          parameters: detail.parameters,
          condition: detail.condition,
          outcome: detail.outcome,
          publishedByUserId: profile.id,
          publishedAt: now,
        };
        detail = {
          ...detail,
          status: 'Published',
          revision: (detail.revision ?? 0) + 1,
          latestPublishedVersion: versionNumber,
          versions: [...(detail.versions ?? []), version],
          updatedAt: now,
        };
      } else if (method === 'POST' && action === 'draft') {
        detail = {
          ...detail,
          status: 'Draft',
          revision: (detail.revision ?? 0) + 1,
          updatedAt: now,
        };
      } else if (method === 'POST' && action === 'archive') {
        detail = {
          ...detail,
          status: 'Archived',
          revision: (detail.revision ?? 0) + 1,
          archivedAt: now,
          updatedAt: now,
        };
      }

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(detail),
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(detail),
    });
  });

  return requests;
}

async function expectNoDocumentOverflow(page: Page): Promise<void> {
  await expect
    .poll(() =>
      page.evaluate(() => ({
        horizontal: document.documentElement.scrollWidth <= window.innerWidth + 1,
        vertical: document.documentElement.scrollHeight <= window.innerHeight + 1,
      })),
    )
    .toEqual({ horizontal: true, vertical: true });
}

async function expectDockAboveFooter(page: Page, expectedWidth?: number): Promise<Locator> {
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

  if (expectedWidth === undefined) {
    expect(dockBox.width).toBeGreaterThanOrEqual(160);
    expect(dockBox.width).toBeLessThanOrEqual(256);
  } else {
    expect(dockBox.width).toBeCloseTo(expectedWidth, 0);
  }
  expect(hostBox.x + hostBox.width - (dockBox.x + dockBox.width)).toBeCloseTo(12, 0);
  const footerGap = footerBox.y - (dockBox.y + dockBox.height);
  expect(footerGap).toBeGreaterThanOrEqual(8);
  expect(footerGap).toBeLessThanOrEqual(12);
  return dock;
}

async function expectTableColumnsAligned(table: Locator): Promise<void> {
  const columns = await table.evaluate((root) => {
    const headerCells = [...root.querySelectorAll('[data-slot="table-header"] th')];
    const bodyCells = [...root.querySelectorAll('[data-slot="table-body"] tr:first-child td')];
    return headerCells.map((header, index) => {
      const body = bodyCells[index];
      const label = header.querySelector('[data-slot="data-table-column-label"]');
      const content = body?.querySelector('[data-slot="data-table-cell-content"]');
      const value = content?.querySelector('[data-slot="rule-table-value"]');
      if (!body || !label || !content) {
        throw new Error(`Data table column ${index} is missing a geometry anchor`);
      }
      return {
        headerLeft: header.getBoundingClientRect().left,
        bodyLeft: body.getBoundingClientRect().left,
        labelLeft: label.getBoundingClientRect().left,
        contentLeft: content.getBoundingClientRect().left,
        valueLeft: value?.getBoundingClientRect().left,
        verticalAlign: getComputedStyle(body).verticalAlign,
      };
    });
  });

  expect(columns.length).toBeGreaterThan(0);
  for (const column of columns) {
    expect(Math.abs(column.bodyLeft - column.headerLeft)).toBeLessThanOrEqual(1);
    expect(Math.abs(column.contentLeft - column.labelLeft)).toBeLessThanOrEqual(1);
    expect(column.valueLeft).toBeDefined();
    expect(Math.abs((column.valueLeft ?? 0) - column.labelLeft)).toBeLessThanOrEqual(1);
    expect(column.verticalAlign).toBe('top');
  }
}

test('workspace rule authoring supports simulation, immutable revisions, and archive', async ({
  page,
}, testInfo) => {
  testInfo.setTimeout(60_000);
  const pageErrors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') pageErrors.push(message.text());
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));

  await mockAuthenticatedSession(page);
  const requests = await mockRulesApi(page);
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto('/rules');

  const catalog = page.getByRole('region', { name: 'Rules catalog' });
  const toolbarActions = catalog.locator('[data-slot="data-table-toolbar-actions"]');
  await expect(toolbarActions.getByRole('button', { name: 'New rule' })).toBeVisible();
  await expect(catalog.getByRole('columnheader', { name: /Actions/ })).toHaveCount(0);
  const search = catalog.getByLabel('Search rules');
  await search.fill('date and time range');
  await expect(catalog.getByText('Date and time range', { exact: true })).toBeVisible();
  await expect(catalog.getByText('Required value', { exact: true })).toHaveCount(0);
  await search.clear();
  await expect(catalog.getByText('Required value', { exact: true })).toBeVisible();
  await expect(catalog.getByRole('columnheader', { name: /Origin/ })).toBeVisible();
  await expect(catalog.getByRole('columnheader', { name: /Status/ })).toBeVisible();
  const requiredRow = catalog
    .getByText('Required value', { exact: true })
    .locator('xpath=ancestor::tr');
  await expect(requiredRow.getByText('Built-in', { exact: true })).toBeVisible();
  await expect(requiredRow.getByText('Published', { exact: true })).toBeVisible();
  await expectTableColumnsAligned(catalog);

  const requiredRuleLink = requiredRow.getByRole('button', {
    name: 'Required value',
    exact: true,
  });
  const linkSpacing = await requiredRuleLink.evaluate((element) => {
    const style = window.getComputedStyle(element);
    return {
      height: element.getBoundingClientRect().height,
      paddingInlineStart: style.paddingInlineStart,
      paddingInlineEnd: style.paddingInlineEnd,
    };
  });
  expect(linkSpacing.paddingInlineStart).toBe('0px');
  expect(linkSpacing.paddingInlineEnd).toBe('0px');
  expect(linkSpacing.height).toBeLessThanOrEqual(24);

  await requiredRuleLink.click();
  const systemDetails = page.getByRole('dialog', { name: 'Required value' });
  const systemWindow = systemDetails.locator('[data-slot="managed-dialog-window"]');
  await expect(systemDetails).toBeVisible();
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
  const expandedLayerBox = await page
    .locator('[data-slot="managed-window-expanded-layer"]')
    .boundingBox();
  if (!expandedLayerBox) throw new Error('Managed window work area did not render');
  await expect
    .poll(async () => {
      const box = await systemWindow.boundingBox();
      return box
        ? {
            width: Math.round(box.width),
            height: Math.round(box.height),
            x: Math.round(box.x),
            y: Math.round(box.y),
          }
        : null;
    })
    .toEqual({
      width: Math.round(expandedLayerBox.width),
      height: Math.round(expandedLayerBox.height),
      x: Math.round(expandedLayerBox.x),
      y: Math.round(expandedLayerBox.y),
    });

  await systemDetails.getByRole('button', { name: 'Restore dialog size' }).click();
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'large');
  const expectedLargeRect = {
    width: Math.round(expandedLayerBox.width * 0.5),
    height: Math.round(expandedLayerBox.height * 0.75),
    x: Math.round(expandedLayerBox.x + expandedLayerBox.width * 0.25),
    y: Math.round(expandedLayerBox.y + expandedLayerBox.height * 0.125),
  };
  await expect
    .poll(async () => {
      const box = await systemWindow.boundingBox();
      return box
        ? {
            width: Math.round(box.width),
            height: Math.round(box.height),
            x: Math.round(box.x),
            y: Math.round(box.y),
          }
        : null;
    })
    .toEqual(expectedLargeRect);

  const backgroundNewRuleButton = catalog.getByRole('button', { name: 'New rule', exact: true });
  await expect(backgroundNewRuleButton).toBeVisible();
  await backgroundNewRuleButton.click({ timeout: 10_000 });
  const backgroundCreateDialog = page.getByRole('dialog', { name: 'New workspace rule' });
  await expect(backgroundCreateDialog).toBeVisible();
  await backgroundCreateDialog.getByRole('button', { name: 'Close dialog' }).click();
  await expect(backgroundCreateDialog).toBeHidden();

  const initialDialogBox = await systemWindow.boundingBox();
  if (!initialDialogBox) throw new Error('Managed dialog did not render a bounding box');

  const managedHeader = systemWindow.locator('[data-slot="managed-dialog-header"]');
  const headerBox = await managedHeader.boundingBox();
  if (!headerBox) throw new Error('Managed dialog header did not render a bounding box');
  await page.mouse.move(headerBox.x + 24, headerBox.y + 24);
  await page.mouse.down();
  await page.mouse.move(headerBox.x + 104, headerBox.y + 64, { steps: 5 });
  await page.mouse.up();
  const draggedDialogBox = await systemWindow.boundingBox();
  expect(draggedDialogBox?.x ?? 0).toBeGreaterThan(initialDialogBox.x + 40);
  expect(draggedDialogBox?.y ?? 0).toBeGreaterThan(initialDialogBox.y + 20);

  await systemDetails.getByRole('button', { name: 'Reset dialog' }).click();
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
  await systemDetails.getByRole('button', { name: 'Restore dialog size' }).click();
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'large');
  const resetDialogBox = await systemWindow.boundingBox();
  expect(resetDialogBox?.x).toBeCloseTo(expandedLayerBox.x + expandedLayerBox.width * 0.25, 1);
  expect(resetDialogBox?.y).toBeCloseTo(expandedLayerBox.y + expandedLayerBox.height * 0.125, 1);

  if (!resetDialogBox) throw new Error('Managed dialog disappeared before resizing');
  await page.mouse.move(
    resetDialogBox.x + resetDialogBox.width - 2,
    resetDialogBox.y + resetDialogBox.height - 2,
  );
  await page.mouse.down();
  await page.mouse.move(resetDialogBox.x + 360, resetDialogBox.y + 240, { steps: 5 });
  await page.mouse.up();
  const minimumDialogBox = await systemWindow.boundingBox();
  expect(minimumDialogBox?.width).toBeGreaterThanOrEqual(expandedLayerBox.width / 2 - 1);
  expect(minimumDialogBox?.height).toBeGreaterThanOrEqual(expandedLayerBox.height / 2 - 1);

  if (!minimumDialogBox) throw new Error('Managed dialog disappeared at its minimum size');
  await page.mouse.move(
    minimumDialogBox.x + minimumDialogBox.width - 2,
    minimumDialogBox.y + minimumDialogBox.height - 2,
  );
  await page.mouse.down();
  await page.mouse.move(
    minimumDialogBox.x + minimumDialogBox.width + 120,
    minimumDialogBox.y + minimumDialogBox.height + 80,
    { steps: 5 },
  );
  await page.mouse.up();
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'custom');
  const resizedDialogBox = await systemWindow.boundingBox();
  expect(resizedDialogBox?.width ?? 0).toBeGreaterThan(minimumDialogBox.width + 100);
  expect(resizedDialogBox?.height ?? 0).toBeGreaterThan(minimumDialogBox.height + 60);
  if (!resizedDialogBox) throw new Error('Managed dialog disappeared before docking');

  await systemDetails.getByRole('button', { name: 'Minimize dialog' }).click();
  let dock = await expectDockAboveFooter(page, 256);
  await expect(dock).toHaveAttribute('data-dialog-preset', 'custom');
  await expect(dock).toContainText('Required value');
  await expect(systemDetails).toBeHidden();
  await expect
    .poll(async () => {
      const box = await page.locator('[data-slot="managed-window-expanded-layer"]').boundingBox();
      return box
        ? {
            width: Math.round(box.width),
            height: Math.round(box.height),
            x: Math.round(box.x),
            y: Math.round(box.y),
          }
        : null;
    })
    .toEqual({
      width: Math.round(expandedLayerBox.width),
      height: Math.round(expandedLayerBox.height),
      x: Math.round(expandedLayerBox.x),
      y: Math.round(expandedLayerBox.y),
    });
  await page.keyboard.press('Escape');
  await expect(dock).toBeVisible();
  await catalog.getByRole('button', { name: 'Filters', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Add condition' })).toBeVisible();
  await page.keyboard.press('Escape');

  await catalog.getByRole('button', { name: 'Numeric range', exact: true }).click();
  const numericDetails = page.getByRole('dialog', { name: 'Numeric range' });
  await expect(numericDetails).toBeVisible();
  await expect(dock).toContainText('Required value');
  await expect(dock).not.toContainText('Numeric range');
  await dock.getByRole('button', { name: 'Restore dialog' }).click();
  await expect(systemDetails).toBeVisible();
  await expect(numericDetails).toBeVisible();
  const windowsMenu = page.getByRole('button', { name: 'Windows (2)' });
  await windowsMenu.click();
  await expect(page.getByRole('menuitem', { name: /Required value/ })).toBeVisible();
  const numericWindowItem = page.getByRole('menuitem', { name: /Numeric range/ });
  await expect(numericWindowItem).toBeVisible();
  await numericWindowItem.click();
  await numericDetails.getByRole('button', { name: 'Close dialog' }).click();
  await expect(numericDetails).toBeHidden();
  await expect
    .poll(async () => {
      const box = await systemWindow.boundingBox();
      return box
        ? {
            width: Math.round(box.width),
            height: Math.round(box.height),
            x: Math.round(box.x),
            y: Math.round(box.y),
          }
        : null;
    })
    .toEqual({
      width: Math.round(resizedDialogBox.width),
      height: Math.round(resizedDialogBox.height),
      x: Math.round(resizedDialogBox.x),
      y: Math.round(resizedDialogBox.y),
    });

  await managedHeader.dblclick({ position: { x: 24, y: 24 } });
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
  const maximizedDialogBox = await systemWindow.boundingBox();
  expect(maximizedDialogBox?.x).toBeCloseTo(expandedLayerBox.x, 0);
  expect(maximizedDialogBox?.y).toBeCloseTo(expandedLayerBox.y, 0);
  expect(maximizedDialogBox?.width).toBeCloseTo(expandedLayerBox.width, 0);
  expect(maximizedDialogBox?.height).toBeCloseTo(expandedLayerBox.height, 0);
  const managedHostBox = await page.locator('[data-slot="managed-window-host"]').boundingBox();
  if (!managedHostBox) throw new Error('Managed window host did not render a bounding box');
  expect((maximizedDialogBox?.y ?? 0) + (maximizedDialogBox?.height ?? 0)).toBeCloseTo(
    managedHostBox.y + managedHostBox.height,
    0,
  );
  await systemDetails.getByRole('button', { name: 'Minimize dialog' }).click();
  dock = await expectDockAboveFooter(page, 256);
  await expect(dock).toHaveAttribute('data-dialog-preset', 'fullscreen');
  await dock.getByRole('button', { name: 'Restore dialog' }).click();
  await expect(systemDetails).toBeVisible();
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
  const restoredMaximizedBox = await systemWindow.boundingBox();
  expect(restoredMaximizedBox).toEqual(maximizedDialogBox);
  await managedHeader.dblclick({ position: { x: 24, y: 24 } });
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'custom');
  await expect
    .poll(async () => {
      const box = await systemWindow.boundingBox();
      return box
        ? {
            width: Math.round(box.width),
            height: Math.round(box.height),
            x: Math.round(box.x),
            y: Math.round(box.y),
          }
        : null;
    })
    .toEqual({
      width: Math.round(resizedDialogBox.width),
      height: Math.round(resizedDialogBox.height),
      x: Math.round(resizedDialogBox.x),
      y: Math.round(resizedDialogBox.y),
    });
  const systemBadges = systemDetails.locator(
    '[data-slot="managed-dialog-header"] [data-slot="badge"]',
  );
  await expect(systemBadges).toHaveCount(2);
  await expect(systemBadges.nth(0)).toHaveText('Built-in');
  await expect(systemBadges.nth(1)).toHaveText('Published');
  await expect(systemDetails.getByRole('heading', { name: 'Rule logic' })).toBeVisible();
  await expect(systemDetails).toContainText('Is blank');
  await expect(systemDetails).toContainText('Field value');
  await expect(systemDetails.getByRole('button', { name: 'Archive', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(systemDetails).toBeHidden();

  await catalog.getByRole('button', { name: 'Filters', exact: true }).click();
  await expectNoDocumentOverflow(page);
  await page.getByRole('button', { name: 'Add condition' }).click();
  const filterField = page.getByTestId('fields');
  const filterOperator = page.getByTestId('operators');
  const filterValue = page.getByTestId('value-editor');
  const removeCondition = page.getByRole('button', { name: 'Remove condition', exact: true });
  const [fieldBox, operatorBox, valueBox, removeBox] = await Promise.all([
    filterField.boundingBox(),
    filterOperator.boundingBox(),
    filterValue.boundingBox(),
    removeCondition.boundingBox(),
  ]);
  if (!fieldBox || !operatorBox || !valueBox || !removeBox) {
    throw new Error('Filter condition controls did not render bounding boxes');
  }
  expect(operatorBox.x - (fieldBox.x + fieldBox.width)).toBeLessThanOrEqual(12);
  expect(valueBox.x - (operatorBox.x + operatorBox.width)).toBeLessThanOrEqual(12);
  expect(removeBox.x - (valueBox.x + valueBox.width)).toBeLessThanOrEqual(12);

  await filterField.click();
  const selectContent = page.locator('[data-slot="select-content"][data-open]');
  await expect(selectContent).toBeVisible();
  await expect
    .poll(async () => {
      const [triggerBox, contentBox] = await Promise.all([
        filterField.boundingBox(),
        selectContent.boundingBox(),
      ]);
      if (!triggerBox || !contentBox) return false;
      return (
        contentBox.y >= triggerBox.y + triggerBox.height &&
        Math.abs(contentBox.x - triggerBox.x) <= 1
      );
    })
    .toBe(true);
  await page.getByRole('option', { name: 'Origin', exact: true }).click();
  await page.getByTestId('value-editor').click();
  await page.getByRole('option', { name: 'Built-in', exact: true }).click();
  await expect(page.getByTestId('value-editor')).toContainText('Built-in');
  await page.keyboard.press('Escape');
  await page.keyboard.press('Escape');
  await expect(catalog.getByRole('button', { name: 'Filters', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  );

  await catalog.getByRole('button', { name: 'Columns', exact: true }).click();
  const originColumn = page.getByRole('menuitemcheckbox', { name: 'Origin', exact: true });
  await originColumn.click();
  await expect(catalog.getByRole('columnheader', { name: /Origin/ })).toHaveCount(0);
  await expect(catalog.getByRole('button', { name: 'Clear filters' })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(catalog.getByRole('button', { name: 'Columns', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  );

  await catalog.getByRole('button', { name: 'Filters', exact: true }).click();
  await page.getByRole('button', { name: 'Add condition' }).click();
  await page.getByTestId('fields').click();
  await expect(page.getByRole('option', { name: 'Origin', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await page.keyboard.press('Escape');
  await expect(catalog.getByRole('button', { name: 'Filters', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  );

  await catalog.getByRole('button', { name: 'Columns', exact: true }).click();
  await page.getByRole('menuitemcheckbox', { name: 'Origin', exact: true }).click();
  await expect(catalog.getByRole('columnheader', { name: /Origin/ })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(catalog.getByRole('button', { name: 'Columns', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  );
  await expect(page.getByRole('menuitemcheckbox', { name: 'Origin', exact: true })).toBeHidden();

  const catalogHeader = catalog.getByRole('columnheader', { name: /Rule/ });
  const catalogViewport = catalog.locator('[data-slot="data-table-viewport"]');
  await expect
    .poll(() =>
      catalogViewport.evaluate((element) => element.scrollWidth <= element.clientWidth + 1),
    )
    .toBe(true);
  await testInfo.attach('rules-table-desktop', {
    body: await page.screenshot(),
    contentType: 'image/png',
  });
  const headerBoxBeforeScroll = await catalogHeader.boundingBox();
  const viewportBox = await catalogViewport.boundingBox();
  if (!headerBoxBeforeScroll || !viewportBox) {
    throw new Error('Rules catalog header or viewport did not render a bounding box');
  }
  expect(headerBoxBeforeScroll.y).toBeLessThanOrEqual(viewportBox.y + 1);
  await catalogViewport.evaluate((element) => element.scrollTo({ top: element.scrollHeight }));
  const headerBoxAfterScroll = await catalogHeader.boundingBox();
  if (!headerBoxAfterScroll) throw new Error('Rules catalog header disappeared after scrolling');
  expect(headerBoxAfterScroll.y).toBeCloseTo(headerBoxBeforeScroll.y, 0);

  await page.getByRole('button', { name: 'New rule' }).click();
  const createDialog = page.getByRole('dialog', { name: 'New workspace rule' });
  await expect(createDialog.getByRole('heading', { name: 'Definition' })).toBeVisible();
  await expect(createDialog.getByRole('heading', { name: 'Parameters' })).toBeHidden();
  await createDialog.getByLabel('Name').fill('Credit threshold');
  await createDialog.getByLabel('Description').fill('Flags credit values above a threshold.');
  await createDialog.getByLabel('Context').click();
  await page.getByRole('option', { name: 'Decimal field value' }).click();
  await createDialog.getByRole('button', { name: 'Create draft' }).click();

  const editorDialog = page.getByRole('dialog', { name: 'Credit threshold' });
  await expect(editorDialog.getByRole('heading', { name: 'Credit threshold' })).toBeVisible();
  await expect(editorDialog.getByRole('heading', { name: 'Definition' })).toBeVisible();
  await expect(editorDialog.getByText('Stable key: credit_threshold')).toBeVisible();
  await expect(editorDialog.getByRole('heading', { name: 'Parameters' })).toBeVisible();
  const editorViewport = editorDialog.locator('[data-slot="dialog-body"]');
  await expect
    .poll(() => editorViewport.evaluate((element) => element.scrollHeight > element.clientHeight))
    .toBe(true);
  await editorViewport.evaluate((element) => element.scrollTo({ top: element.scrollHeight }));
  await expect
    .poll(() => editorViewport.evaluate((element) => element.scrollTop))
    .toBeGreaterThan(0);

  await editorDialog.getByRole('button', { name: 'Add parameter' }).click();
  await editorDialog.getByLabel('Key').fill('threshold');
  await editorDialog.getByLabel('Type').click();
  await page.getByRole('option', { name: 'Decimal' }).click();
  await expect(editorDialog.getByLabel('Type')).toContainText('Decimal');
  await editorDialog.getByLabel('Allowed values').fill('100, 200');
  await editorDialog.getByLabel('Operator').click();
  const greaterThanOption = page.getByRole('option', { name: 'Greater than', exact: true });
  await expect(greaterThanOption).toBeVisible();
  await greaterThanOption.click();
  await expect(editorDialog.getByLabel('Operator')).toContainText('Greater than');
  await editorDialog.getByLabel('Right operand').click();
  await page.getByRole('option', { name: 'Parameter' }).click();
  await editorDialog.getByLabel('Parameter', { exact: true }).click();
  await page.getByRole('option', { name: 'threshold' }).click();
  await editorDialog.getByLabel('Violation code').fill('credit.threshold.exceeded');
  await editorDialog.getByLabel('Message').fill('Credit value exceeds the configured threshold.');
  await editorDialog.getByLabel('Field value').fill('150');
  await editorDialog.getByLabel('Parameter: threshold').fill('100');
  await editorDialog.getByRole('button', { name: 'Save and simulate' }).click();
  await expect(editorDialog.getByText('Condition matched')).toBeVisible();
  await expect(editorDialog.getByLabel('Field value')).toHaveValue('150');
  await expect(editorDialog.getByLabel('Parameter: threshold')).toHaveValue('100');
  await testInfo.attach('rules-authoring', {
    body: await page.screenshot(),
    contentType: 'image/png',
  });

  const firstSave = requests.find(
    (request) => request.method === 'PUT' && request.path === '/api/rules/credit_threshold/draft',
  );
  expect(firstSave?.body).toMatchObject({
    parameters: [
      {
        key: 'threshold',
        type: 'Decimal',
        isRequired: true,
        allowMultiple: false,
        allowedValues: ['100', '200'],
      },
    ],
    condition: {
      children: [
        {
          predicateOperator: 'GreaterThan',
          left: { kind: 'Context', reference: 'field.value' },
          right: { kind: 'Parameter', reference: 'threshold' },
        },
      ],
    },
  });

  await editorDialog.getByRole('button', { name: 'Publish version' }).click();
  let publishDialog = page.locator('[data-slot="alert-dialog-content"]');
  await expect(publishDialog.getByRole('heading', { name: 'Publish this rule?' })).toBeVisible();
  await expect(publishDialog).toContainText('Version 1 will be immutable.');
  await publishDialog.getByRole('button', { name: 'Publish version' }).click();
  await expect(editorDialog.getByText('Published', { exact: true })).toBeVisible();
  await expect(editorDialog.getByText('Version 1')).toBeVisible();

  await editorDialog.getByRole('button', { name: 'Start revision' }).click();
  await expect(editorDialog.getByText('Draft', { exact: true })).toBeVisible();
  await editorDialog.getByLabel('Message').fill('Credit value exceeds the approved threshold.');
  await editorDialog.getByRole('button', { name: 'Publish version' }).click();
  publishDialog = page.locator('[data-slot="alert-dialog-content"]');
  await expect(publishDialog).toContainText('Version 2 will be immutable.');
  await publishDialog.getByRole('button', { name: 'Publish version' }).click();
  await expect(editorDialog.getByText('Version 2')).toBeVisible();

  await editorDialog.getByRole('button', { name: 'Archive', exact: true }).click();
  const archiveDialog = page.locator('[data-slot="alert-dialog-content"]');
  await expect(archiveDialog.getByRole('heading', { name: 'Archive this rule?' })).toBeVisible();
  await expect(archiveDialog).toContainText('Published versions already in use remain resolvable.');
  await archiveDialog.getByRole('button', { name: 'Archive', exact: true }).click();
  await expect(editorDialog.getByText('Archived', { exact: true })).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  await expectNoDocumentOverflow(page);
  const editorWindow = editorDialog.locator('[data-slot="managed-dialog-window"]');
  await expect(editorWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
  const dialogBox = await editorWindow.boundingBox();
  expect(dialogBox?.x ?? -1).toBeGreaterThanOrEqual(-1);
  expect((dialogBox?.x ?? 0) + (dialogBox?.width ?? 0)).toBeLessThanOrEqual(391);
  await editorWindow
    .locator('[data-slot="managed-dialog-header"]')
    .dblclick({ position: { x: 24, y: 24 } });
  await expect(editorWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
  await expect(editorDialog.getByRole('button', { name: 'Restore dialog size' })).toBeDisabled();
  await editorDialog.getByRole('button', { name: 'Minimize dialog' }).click();
  const editorDock = await expectDockAboveFooter(page);
  await expect(editorDock).toHaveAttribute('data-dialog-preset', 'fullscreen');
  await expect(editorDock).toContainText('Credit threshold');
  await expect(editorDialog).toBeHidden();
  await page.keyboard.press('Escape');

  await catalog.getByRole('button', { name: 'Required value', exact: true }).click();
  await expect(systemDetails).toBeVisible();
  await systemDetails.getByRole('button', { name: 'Minimize dialog' }).click();
  const mobileDock = await expectDockAboveFooter(page);
  await expect(mobileDock).toContainText('Required value');
  const overflowMenu = page.getByRole('button', { name: '1 more windows' });
  await expect(overflowMenu).toBeVisible();
  await overflowMenu.click();
  await page.getByRole('menuitem', { name: /Credit threshold/ }).click();
  await expect(editorDialog).toBeVisible();
  await expect(editorWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
  await editorDialog.getByRole('button', { name: 'Close dialog' }).click();
  await expect(editorDialog).toBeHidden();
  await mobileDock.getByRole('button', { name: 'Close dialog' }).click();
  await expect(mobileDock).toBeHidden();
  await expect(catalog).toBeVisible();
  await expectNoDocumentOverflow(page);
  await catalog.getByRole('button', { name: 'Filters', exact: true }).click();
  await expectNoDocumentOverflow(page);
  await page.keyboard.press('Escape');
  const catalogHorizontalViewport = catalog.locator('[data-slot="data-table-viewport"]');
  await expect(catalog.locator('[data-slot="table"]')).toHaveCount(1);
  await expect
    .poll(() =>
      catalogHorizontalViewport.evaluate((element) => ({
        hasHorizontalOverflow: element.scrollWidth > element.clientWidth,
        contained: element.getBoundingClientRect().right <= window.innerWidth + 1,
      })),
    )
    .toEqual({ hasHorizontalOverflow: true, contained: true });
  await catalogHorizontalViewport.evaluate((element) => element.scrollTo({ left: 120 }));
  await expect
    .poll(() => catalogHorizontalViewport.evaluate((element) => element.scrollLeft))
    .toBeGreaterThan(0);
  await expectTableColumnsAligned(catalog);
  await catalogViewport.evaluate((element) => element.scrollTo({ top: 0 }));
  await catalogHorizontalViewport.evaluate((element) => element.scrollTo({ left: 0 }));
  await testInfo.attach('rules-table-mobile', {
    body: await page.screenshot(),
    contentType: 'image/png',
  });
  expect(requests.map((request) => `${request.method} ${request.path}`)).toEqual(
    expect.arrayContaining([
      'POST /api/rules',
      'POST /api/rules/credit_threshold/simulate',
      'POST /api/rules/credit_threshold/publish',
      'POST /api/rules/credit_threshold/draft',
      'POST /api/rules/credit_threshold/archive',
    ]),
  );
  expect(pageErrors).toEqual([]);
});
