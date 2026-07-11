import { Buffer } from 'node:buffer';
import { expect, type Page, test } from '@playwright/test';
import type { components } from '../src/lib/api-types';

type CreateRuleRequest = components['schemas']['CreateRuleDefinitionRequest'];
type RuleDetail = components['schemas']['RuleDefinitionDetailDto'];
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

    if (method === 'GET' && path === '/api/rules') {
      const items = detail ? [...systemRules, summary(detail)] : systemRules;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items, totalCount: items.length, page: 1, pageSize: 100 }),
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

  const catalog = page.getByRole('region', { name: 'Built-in field rules' });
  const toolbarActions = catalog.locator('[data-slot="data-table-toolbar-actions"]');
  await expect(toolbarActions.getByRole('button', { name: 'New rule' })).toBeVisible();
  await expect(catalog.getByRole('columnheader', { name: /Actions/ })).toHaveCount(0);
  const search = catalog.getByLabel('Search rules');
  await search.fill('date and time range');
  await expect(catalog.getByText('Date and time range', { exact: true })).toBeVisible();
  await expect(catalog.getByText('Required value', { exact: true })).toHaveCount(0);
  await search.clear();

  await catalog.getByRole('button', { name: 'Filters', exact: true }).click();
  await page.getByRole('button', { name: 'Add condition' }).click();
  await page.getByTestId('fields').click();
  await page.getByRole('option', { name: 'Status', exact: true }).click();
  await page.getByTestId('value-editor').click();
  const builtInFilter = page.getByRole('checkbox', { name: 'Built-in', exact: true });
  await builtInFilter.click();
  await expect(builtInFilter).toBeChecked();
  await page.keyboard.press('Escape');
  await page.keyboard.press('Escape');
  await expect(catalog.getByRole('button', { name: 'Filters', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  );

  await catalog.getByRole('button', { name: 'Columns', exact: true }).click();
  const statusColumn = page.getByRole('menuitemcheckbox', { name: 'Status', exact: true });
  await statusColumn.click();
  await expect(catalog.getByRole('columnheader', { name: /Status/ })).toHaveCount(0);
  await expect(catalog.getByRole('button', { name: 'Clear filters' })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(catalog.getByRole('button', { name: 'Columns', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  );

  await catalog.getByRole('button', { name: 'Filters', exact: true }).click();
  await page.getByRole('button', { name: 'Add condition' }).click();
  await page.getByTestId('fields').click();
  await expect(page.getByRole('option', { name: 'Status', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await page.keyboard.press('Escape');
  await expect(catalog.getByRole('button', { name: 'Filters', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  );

  await catalog.getByRole('button', { name: 'Columns', exact: true }).click();
  await page.getByRole('menuitemcheckbox', { name: 'Status', exact: true }).click();
  await expect(catalog.getByRole('columnheader', { name: /Status/ })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(catalog.getByRole('button', { name: 'Columns', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  );
  await expect(page.getByRole('menuitemcheckbox', { name: 'Status', exact: true })).toBeHidden();

  const catalogHeader = catalog.getByRole('columnheader', { name: /Rule/ });
  const catalogViewport = catalog.locator('[data-slot="table-container"]');
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
  expect(headerBoxBeforeScroll.y).toBeGreaterThanOrEqual(viewportBox.y - 1);
  await catalogViewport.evaluate((element) => element.scrollTo({ top: element.scrollHeight }));
  const headerBoxAfterScroll = await catalogHeader.boundingBox();
  if (!headerBoxAfterScroll) throw new Error('Rules catalog header disappeared after scrolling');
  expect(headerBoxAfterScroll.y).toBeCloseTo(headerBoxBeforeScroll.y, 0);

  await page.getByRole('button', { name: 'New rule' }).click();
  const createDialog = page.locator('[data-slot="dialog-content"]');
  await createDialog.getByLabel('Name').fill('Credit threshold');
  await createDialog.getByLabel('Description').fill('Flags credit values above a threshold.');
  await createDialog.getByLabel('Context').click();
  await page.getByRole('option', { name: 'Decimal field value' }).click();
  await createDialog.getByRole('button', { name: 'Create draft' }).click();

  const editorDialog = page.locator('[data-slot="dialog-content"]');
  await expect(editorDialog.getByRole('heading', { name: 'Credit threshold' })).toBeVisible();
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
  await editorDialog.getByLabel('Compare with').click();
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
  const dialogBox = await editorDialog.boundingBox();
  expect(dialogBox?.x ?? -1).toBeGreaterThanOrEqual(-1);
  expect((dialogBox?.x ?? 0) + (dialogBox?.width ?? 0)).toBeLessThanOrEqual(391);
  await editorDialog.locator('[data-slot="dialog-close"]').click();
  await expect(editorDialog).toBeHidden();
  await expect(catalog).toBeVisible();
  await expectNoDocumentOverflow(page);
  await expect
    .poll(() =>
      catalogViewport.evaluate((element) => ({
        hasHorizontalOverflow: element.scrollWidth > element.clientWidth,
        contained: element.getBoundingClientRect().right <= window.innerWidth + 1,
      })),
    )
    .toEqual({ hasHorizontalOverflow: true, contained: true });
  await catalogViewport.evaluate((element) => element.scrollTo({ top: 0, left: 0 }));
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
