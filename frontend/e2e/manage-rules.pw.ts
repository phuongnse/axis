import { Buffer } from 'node:buffer';
import { expect, type Page, test } from '@playwright/test';
import type * as ApiTypes from '../src/lib/api-generated';

type JsonObject = Record<string, unknown>;
type CapturedRequest = { method: string; path: string; body?: JsonObject };
type MockRuleDetail = ApiTypes.RuleDefinitionDetailDto & JsonObject;

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
const input = (key: string, label: string, types: string[], isRequired = true) => ({
  key,
  label,
  types,
  isRequired,
  allowMultiple: false,
  allowedValues: [],
});

const systemRules = [
  [
    'field.required',
    'Required value',
    ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'],
  ],
  ['field.numeric_range', 'Numeric range', ['Integer', 'Decimal']],
  ['field.decimal_precision', 'Decimal precision', ['Decimal']],
  ['field.date_range', 'Date range', ['Date']],
  ['field.datetime_range', 'Date and time range', ['DateTime']],
  ['field.text_length', 'Text length', ['Text']],
  ['field.text_pattern', 'Text pattern', ['Text']],
  ['field.text_format', 'Text format', ['Text']],
  ['field.choice_selection_count', 'Choice selection count', ['Text']],
].map(([definitionKey, name, types]) => ({
  definitionKey,
  name,
  description: `${name} validation.`,
  origin: 'System',
  status: 'Published',
  expressionLanguageVersion: 1,
  latestPublishedVersion: 1,
  inputs:
    definitionKey === 'field.date_range'
      ? [
          input('value', 'Value', ['Date']),
          input('min', 'Minimum', ['Date'], false),
          input('max', 'Maximum', ['Date'], false),
        ]
      : [input('value', 'Value', types as string[], definitionKey !== 'field.required')],
  output: { type: 'Boolean', cardinality: 'Scalar' },
}));

function requiredCondition(): ApiTypes.RuleConditionNodeDto {
  return {
    nodeId: 'required-check',
    logicalOperator: undefined,
    predicateOperator: 'Equal',
    left: {
      kind: 'Function',
      reference: undefined,
      literal: undefined,
      function: 'IsBlank',
      arguments: [{ kind: 'Input', reference: 'value', arguments: [] }],
    },
    right: {
      kind: 'Literal',
      reference: undefined,
      literal: { type: 'Boolean', values: ['false'] },
      arguments: [],
    },
    children: [],
  };
}

function dateRangeCondition(): ApiTypes.RuleConditionNodeDto {
  const inputOperand = (reference: string): ApiTypes.RuleOperandDto => ({
    kind: 'Input',
    reference,
    arguments: [],
  });
  const predicate = (
    nodeId: string,
    predicateOperator: ApiTypes.RulePredicateOperator,
    left: ApiTypes.RuleOperandDto,
    right?: ApiTypes.RuleOperandDto,
  ): ApiTypes.RuleConditionNodeDto => ({
    nodeId,
    logicalOperator: undefined,
    predicateOperator,
    left,
    right,
    children: [],
  });
  const group = (
    nodeId: string,
    logicalOperator: ApiTypes.RuleLogicalOperator,
    children: ApiTypes.RuleConditionNodeDto[],
  ): ApiTypes.RuleConditionNodeDto => ({
    nodeId,
    logicalOperator,
    predicateOperator: undefined,
    left: undefined,
    right: undefined,
    children,
  });

  return group('date-range', 'All', [
    group('date-range-minimum', 'Any', [
      predicate('minimum-absent', 'IsNull', inputOperand('min')),
      predicate(
        'minimum-satisfied',
        'GreaterThanOrEqual',
        inputOperand('value'),
        inputOperand('min'),
      ),
    ]),
    group('date-range-maximum', 'Any', [
      predicate('maximum-absent', 'IsNull', inputOperand('max')),
      predicate('maximum-satisfied', 'LessThanOrEqual', inputOperand('value'), inputOperand('max')),
    ]),
  ]);
}

function systemDetail(definitionKey: string): MockRuleDetail | null {
  const summary = systemRules.find((rule) => rule.definitionKey === definitionKey);
  if (!summary) return null;
  return {
    ...summary,
    revision: null,
    condition:
      definitionKey === 'field.required'
        ? requiredCondition()
        : definitionKey === 'field.date_range'
          ? dateRangeCondition()
          : undefined,
    versions: [],
    createdAt: null,
    updatedAt: null,
    archivedAt: null,
  } as MockRuleDetail;
}

function conditionProjection(
  body: ApiTypes.ProjectRuleConditionRequest,
): ApiTypes.RuleConditionProjectionDto {
  const condition = body.condition ?? {
    nodeId: 'threshold-check',
    logicalOperator: undefined,
    predicateOperator: 'GreaterThan',
    left: { kind: 'Input', reference: 'Value', arguments: [] },
    right: { kind: 'Input', reference: 'Threshold', arguments: [] },
    children: [],
  };
  return {
    condition,
    display: conditionDisplay(condition),
  };
}

function conditionDisplay(
  condition: ApiTypes.RuleConditionNodeDto,
): ApiTypes.RuleExpressionDisplayNodeDto {
  if (condition.logicalOperator) {
    const labels: Record<ApiTypes.RuleLogicalOperator, string> = {
      All: 'All conditions must match',
      Any: 'Any condition may match',
      Not: 'This must not match',
    };
    return {
      nodeId: condition.nodeId,
      tokens: [
        {
          text: labels[condition.logicalOperator],
          referenceKind: 'LogicalOperator',
          referenceKey: condition.logicalOperator,
        },
      ],
      children: (condition.children ?? []).map(conditionDisplay),
    };
  }

  const labels: Record<string, string[]> = {
    'minimum-absent': ['Minimum', 'is not provided'],
    'minimum-satisfied': ['Value', 'is greater than or equal to', 'Minimum'],
    'maximum-absent': ['Maximum', 'is not provided'],
    'maximum-satisfied': ['Value', 'is less than or equal to', 'Maximum'],
  };
  const text = labels[condition.nodeId ?? ''] ?? ['Value', 'greater than', 'Threshold'];
  return {
    nodeId: condition.nodeId,
    tokens: text.map((token, index) => ({
      text: token,
      referenceKind: index % 2 === 0 ? 'Input' : 'PredicateOperator',
      referenceKey: token,
    })),
    children: [],
  };
}

function expressionLanguage() {
  const types = ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'];
  const documentation = (displayName: string) => ({ locales: { en: { displayName } } });
  const shapes = (valueTypes: string[], cardinality: 'Scalar' | 'Multiple' | 'Any') =>
    valueTypes.map((type) => ({ type, cardinality }));
  return {
    version: 1,
    operators: [
      {
        operator: 'Equal',
        leftShapes: shapes(types, 'Any'),
        rightShapes: shapes(types, 'Any'),
        requiresMatchingTypes: true,
        documentation: documentation('Equals'),
      },
      {
        operator: 'IsNotNull',
        leftShapes: shapes(types, 'Any'),
        rightShapes: [],
        requiresMatchingTypes: false,
        documentation: documentation('Is not empty'),
      },
    ],
    functions: [],
    logicalOperators: ['All', 'Any', 'Not'].map((operator) => ({
      operator,
      minimumChildren: 1,
      maximumChildren: operator === 'Not' ? 1 : null,
      documentation: documentation(operator),
    })),
    operandKinds: [
      ['Input', 'Rule input'],
      ['Literal', 'Literal value'],
      ['Function', 'Function'],
    ].map(([kind, displayName]) => ({ kind, documentation: documentation(displayName) })),
    valueTypes: types.map((type) => ({ type, documentation: documentation(type) })),
    cardinalities: ['Scalar', 'Multiple', 'Any'].map((cardinality) => ({
      cardinality,
      documentation: documentation(cardinality),
    })),
    limits: { maxDepth: 12 },
  };
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

async function mockAuthenticatedSession(page: Page): Promise<void> {
  await page.addInitScript(() => {
    (window as Window & { __AXIS_DISABLE_DEVTOOLS__?: boolean }).__AXIS_DISABLE_DEVTOOLS__ = true;
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
  let detail: MockRuleDetail | null = null;
  const requests: CapturedRequest[] = [];

  await page.route('**/api/rules**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const captured: CapturedRequest = { method: request.method(), path };
    requests.push(captured);

    if (request.method() === 'GET' && path === '/api/rules/expression-language') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(expressionLanguage()),
      });
      return;
    }
    if (request.method() === 'POST' && path === '/api/rules/condition/project') {
      captured.body = request.postDataJSON() as JsonObject;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(
          conditionProjection(captured.body as ApiTypes.ProjectRuleConditionRequest),
        ),
      });
      return;
    }
    if (request.method() === 'POST' && path === '/api/rules/expression-language/guide') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ expressionLanguageVersion: 1, totalResults: 0, sections: [] }),
      });
      return;
    }
    if (request.method() === 'GET' && path === '/api/rules') {
      const query = url.searchParams.get('query')?.toLowerCase() ?? '';
      const candidates = detail ? [...systemRules, detail] : systemRules;
      const items = candidates.filter((candidate) =>
        `${candidate.name} ${candidate.description} ${candidate.definitionKey}`
          .toLowerCase()
          .includes(query),
      );
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items, totalCount: items.length, page: 1, pageSize: 20 }),
      });
      return;
    }
    if (
      request.method() === 'GET' &&
      path === '/api/rules/field.required/bindings' &&
      url.searchParams.get('version') === '1'
    ) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            bindingId: '88888888-8888-4888-8888-888888888888',
            definitionKey: 'field.required',
            definitionVersion: 1,
            targetType: 'business-object-field',
            targetId: 'customer.status',
            useCaseOrTrigger: 'field-validation',
            priority: 0,
            enabled: true,
            failureBehavior: 'FailClosed',
          },
        ]),
      });
      return;
    }
    if (request.method() === 'GET' && path.startsWith('/api/rules/field.')) {
      const system = systemDetail(path.slice('/api/rules/'.length));
      await route.fulfill({
        status: system ? 200 : 404,
        contentType: 'application/json',
        body: JSON.stringify(system ?? {}),
      });
      return;
    }
    if (request.method() === 'GET' && detail && path === `/api/rules/${detail.definitionKey}`) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(detail),
      });
      return;
    }
    if (request.method() === 'POST' && path === '/api/rules') {
      const body = request.postDataJSON() as ApiTypes.CreateRuleDefinitionRequest;
      captured.body = body as JsonObject;
      const definitionKey = (body.name ?? '')
        .trim()
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '_');
      detail = {
        definitionKey,
        name: body.name,
        description: body.description,
        origin: 'Workspace',
        status: 'Draft',
        expressionLanguageVersion: 1,
        revision: 1,
        latestPublishedVersion: null,
        inputs: [],
        output: { type: 'Boolean', cardinality: 'Scalar' },
        condition: undefined,
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

    if (
      request.method() === 'PUT' &&
      detail &&
      path === `/api/rules/${detail.definitionKey}/draft`
    ) {
      const body = request.postDataJSON() as ApiTypes.SaveRuleDefinitionDraftRequest;
      captured.body = body as JsonObject;
      detail = {
        ...detail,
        name: body.name,
        description: body.description,
        inputs: body.inputs,
        condition: body.condition,
        revision: (detail.revision ?? 0) + 1,
        updatedAt: now,
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(detail),
      });
      return;
    }

    await route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
  });

  return requests;
}

test('rule catalog exposes inputs and pure system details', async ({ page }) => {
  await mockAuthenticatedSession(page);
  await mockRulesApi(page);
  await page.goto('/rules');

  await expect(page.getByRole('heading', { name: 'Rules' })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'Inputs' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Required value' })).toBeVisible();
  await expect(page.getByText('Value').first()).toBeVisible();
  await expect(page.getByText('Applies to')).toHaveCount(0);

  await page.getByRole('button', { name: 'Required value' }).click();
  const details = page.locator('[data-slot="system-rule-details"]');
  await expect(details.getByRole('tab')).toHaveText([
    'General',
    'Rule behavior',
    'Usage',
    'System info',
  ]);
  const tabScroller = details.locator('[data-slot="managed-dialog-tab-scroll"]');
  await expect
    .poll(() => tabScroller.evaluate((element) => element.scrollWidth <= element.clientWidth))
    .toBe(true);
  await expect(details.getByText('Required value validation.')).toBeVisible();
  await details.getByRole('tab', { name: 'Rule behavior' }).click();
  await expect(details.getByRole('heading', { name: 'Inputs' })).toBeVisible();
  await expect(details.getByRole('heading', { name: 'Logic' })).toBeVisible();
  await expect(details.getByRole('heading', { name: 'Outputs' })).toBeVisible();
  const behavior = details.locator('[data-slot="rule-behavior-summary"]');
  const behaviorSteps = behavior.locator('[data-slot="rule-behavior-steps"]');
  await expect(behavior.locator('[data-slot="rule-behavior-rail-node"] svg')).toHaveCount(3);
  await expect
    .poll(() => behaviorSteps.evaluate((element) => getComputedStyle(element).borderTopWidth))
    .toBe('0px');
  await expect
    .poll(() =>
      behavior.evaluate((element) => {
        const connectors = Array.from(
          element.querySelectorAll<HTMLElement>('[data-slot="rule-behavior-rail-connector"]'),
        );
        const nodes = Array.from(
          element.querySelectorAll<HTMLElement>('[data-slot="rule-behavior-rail-node"]'),
        );
        if (connectors.length !== 3 || nodes.length !== 3) return false;
        const connectorRects = connectors.map((item) => item.getBoundingClientRect());
        const firstRect = nodes[0].getBoundingClientRect();
        const lastRect = nodes.at(-1)?.getBoundingClientRect();
        if (!lastRect) return false;
        const centerX = firstRect.left + firstRect.width / 2;
        return (
          connectorRects.every((rect) => Math.abs(rect.left + rect.width / 2 - centerX) <= 1) &&
          Math.abs(connectorRects[0].top - (firstRect.top + firstRect.height / 2)) <= 1 &&
          Math.abs(connectorRects[0].bottom - connectorRects[1].top) <= 1 &&
          Math.abs(connectorRects[1].bottom - connectorRects[2].top) <= 1 &&
          Math.abs(connectorRects[2].bottom - (lastRect.top + lastRect.height / 2)) <= 1
        );
      }),
    )
    .toBe(true);
  await expect
    .poll(() =>
      behavior.locator('li').evaluateAll((sections) =>
        sections.every((section) => {
          const heading = section.querySelector('h3');
          const content = section.children.item(1);
          if (!heading || !content) return false;
          return (
            Math.abs(heading.getBoundingClientRect().left - content.getBoundingClientRect().left) <=
            1
          );
        }),
      ),
    )
    .toBe(true);
  await expect
    .poll(() => behavior.evaluate((element) => element.scrollWidth <= element.clientWidth))
    .toBe(true);
  await expect
    .poll(async () => (await behavior.boundingBox())?.height ?? Number.POSITIVE_INFINITY)
    .toBeLessThan(260);
  await expect(details).toContainText('Value');
  await expect(behavior.locator('[data-slot="rule-output-summary"]')).toHaveText('Boolean');
  await expect(behavior).not.toContainText('Single value');
  await expect(behavior).not.toContainText('isMatch');
  await expect(behavior).not.toContainText('Scalar');
  await expect(details).toContainText('Published');
  await expect(details).not.toContainText('Validation');
  await details.getByRole('tab', { name: 'Usage' }).click();
  await expect(details.getByText('customer.status')).toBeVisible();
  await expect(details.getByText('field-validation')).toBeVisible();
  const footer = page.locator('[data-slot="managed-dialog-footer"]');
  await expect(footer.getByRole('button')).toHaveCount(1);
  await expect(footer.getByRole('button', { name: 'Close' })).toBeVisible();
  await details.getByRole('tab', { name: 'System info' }).click();
  await expect(details.getByText('field.required')).toBeVisible();
  await expect(details.getByText('Expression language')).toBeVisible();
});

test('date range presents nested logic as a compact boolean expression', async ({ page }) => {
  await mockAuthenticatedSession(page);
  await mockRulesApi(page);
  await page.goto('/rules');

  await page.getByRole('button', { name: 'Date range' }).click();
  const details = page.locator('[data-slot="system-rule-details"]');
  await details.getByRole('tab', { name: 'Rule behavior' }).click();
  const logic = details.locator('[data-slot="rule-behavior-flow"]');

  await expect(logic.getByText('Minimum is not provided')).toBeVisible();
  await expect(logic.getByText('Value is greater than or equal to Minimum')).toBeVisible();
  await expect(logic.getByText('Maximum is not provided')).toBeVisible();
  await expect(logic.getByText('Value is less than or equal to Maximum')).toBeVisible();
  await expect(logic.locator('[data-slot="rule-expression-operator"]')).toHaveText([
    'or',
    'and',
    'or',
  ]);
  await expect(logic.getByRole('button')).toHaveCount(0);
  await expect(logic.locator('[data-slot^="rule-condition-"]')).toHaveCount(0);
  const expression = logic.locator('[data-slot="rule-expression"]');
  await expect
    .poll(async () => (await expression.boundingBox())?.height ?? Number.POSITIVE_INFINITY)
    .toBeLessThan(80);
});

test('workspace rule authoring saves one inputs-condition contract', async ({ page }) => {
  await mockAuthenticatedSession(page);
  const requests = await mockRulesApi(page);
  await page.goto('/rules');
  await page.getByRole('button', { name: 'New rule' }).click();

  await page.getByLabel('Name').fill('Credit threshold');
  await page.getByLabel('Description').fill('Matches values above a threshold.');
  await page.getByRole('tab', { name: 'Rule behavior' }).click();
  await page.getByRole('button', { name: 'Add input' }).click();
  await page.getByRole('button', { name: 'Add input' }).click();
  await page.getByLabel('Input name').nth(0).fill('Value');
  await page.getByLabel('Input name').nth(1).fill('Threshold');
  await expect(page.getByLabel('Expression')).toHaveCount(0);
  await page.getByRole('button', { name: 'Add condition' }).click();
  await page.getByLabel('How to compare').click();
  await page.getByRole('option', { name: 'Equals' }).click();
  await page.getByRole('button', { name: 'Save' }).click();

  await expect
    .poll(() =>
      requests.some((request) => request.method === 'PUT' && request.path.endsWith('/draft')),
    )
    .toBe(true);
  const save = requests.find(
    (request) => request.method === 'PUT' && request.path.endsWith('/draft'),
  );
  const saveBody = save?.body as ApiTypes.SaveRuleDefinitionDraftRequest;
  expect(saveBody.inputs?.map((candidate) => candidate.label)).toEqual(['Value', 'Threshold']);
  expect(saveBody.condition?.predicateOperator).toBe('Equal');
  expect(saveBody.condition?.left?.kind).toBe('Input');
  expect(save?.body).not.toHaveProperty('scope');
  expect(save?.body).not.toHaveProperty('outcome');
  await expect(
    page.getByLabel('Credit threshold').getByText('Draft', { exact: true }),
  ).toBeVisible();
});

test('rule catalog remains usable on a narrow viewport', async ({ page }) => {
  await mockAuthenticatedSession(page);
  await mockRulesApi(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/rules');

  await expect(page.getByRole('heading', { name: 'Rules' })).toBeVisible();
  await expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1))
    .toBe(true);

  await page.getByRole('button', { name: 'Required value' }).click();
  const details = page.locator('[data-slot="system-rule-details"]');
  await details.getByRole('tab', { name: 'Rule behavior' }).click();
  const behavior = details.locator('[data-slot="rule-behavior-summary"]');
  await expect(behavior.getByRole('heading')).toHaveText(['Inputs', 'Logic', 'Outputs']);
  await expect
    .poll(() => behavior.evaluate((element) => element.scrollWidth <= element.clientWidth))
    .toBe(true);
});
