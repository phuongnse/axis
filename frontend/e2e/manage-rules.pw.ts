import { expect, type Page, test } from '@playwright/test';
import type * as ApiTypes from '../src/lib/api-generated';

type JsonObject = Record<string, unknown>;
type CapturedRequest = { method: string; path: string; query?: string; body?: JsonObject };
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

const builtInRules = [
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
  origin: 'BuiltIn',
  status: 'Active',
  expressionLanguageVersion: 1,
  latestVersion: 1,
  activeVersion: 1,
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

function builtInDetail(definitionKey: string): MockRuleDetail | null {
  const summary = builtInRules.find((rule) => rule.definitionKey === definitionKey);
  if (!summary) return null;
  const condition =
    definitionKey === 'field.required'
      ? requiredCondition()
      : definitionKey === 'field.date_range'
        ? dateRangeCondition()
        : undefined;
  return {
    ...summary,
    revision: null,
    condition,
    versions: [
      {
        version: summary.latestVersion,
        name: summary.name,
        description: summary.description,
        expressionLanguageVersion: summary.expressionLanguageVersion,
        inputs: summary.inputs,
        output: summary.output,
        condition,
        createdAt: now,
      },
    ],
    createdAt: null,
    updatedAt: null,
    archivedAt: null,
  } as MockRuleDetail;
}

function conditionDisplay(
  condition: ApiTypes.RuleConditionNodeDto,
): ApiTypes.RuleExpressionDisplayNodeDto {
  if (condition.nodeId === 'date-range') {
    return {
      nodeId: condition.nodeId,
      tokens: [
        {
          text: 'All conditions must match',
          referenceKind: 'LogicalOperator',
          referenceKey: 'All',
        },
      ],
      children: [
        {
          nodeId: 'minimum-bound',
          tokens: [
            { text: 'Value', referenceKind: 'Input', referenceKey: 'value' },
            {
              text: 'is greater than or equal to',
              referenceKind: 'PredicateOperator',
              referenceKey: 'GreaterThanOrEqual',
            },
            { text: 'Minimum', referenceKind: 'Input', referenceKey: 'min' },
            { text: 'when' },
            { text: 'Minimum', referenceKind: 'Input', referenceKey: 'min' },
            {
              text: 'is specified',
              referenceKind: 'PredicateOperator',
              referenceKey: 'IsNotNull',
            },
          ],
          children: [],
        },
        {
          nodeId: 'maximum-bound',
          tokens: [
            { text: 'Value', referenceKind: 'Input', referenceKey: 'value' },
            {
              text: 'is less than or equal to',
              referenceKind: 'PredicateOperator',
              referenceKey: 'LessThanOrEqual',
            },
            { text: 'Maximum', referenceKind: 'Input', referenceKey: 'max' },
            { text: 'when' },
            { text: 'Maximum', referenceKind: 'Input', referenceKey: 'max' },
            {
              text: 'is specified',
              referenceKind: 'PredicateOperator',
              referenceKey: 'IsNotNull',
            },
          ],
          children: [],
        },
      ],
    };
  }

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

  const text = ['Value', 'greater than', 'Threshold'];
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
        csrfToken: 'rules-csrf-token',
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
          workspaceId: profile.workspaceId,
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

async function mockRulesApi(page: Page, canStartCreate = true): Promise<CapturedRequest[]> {
  let detail: MockRuleDetail | null = null;
  let binding: ApiTypes.RuleBindingDto = {
    id: '88888888-8888-4888-8888-888888888888',
    workspaceId: profile.workspaceId,
    definitionKey: 'field.required',
    definitionVersion: 1,
    targetType: 'business-object-field',
    targetId: 'customer.status',
    useCaseOrTrigger: 'field-validation',
    priority: 0,
    enabled: true,
    failureBehavior: 'FailClosed',
    revision: 1,
    createdAt: now,
    updatedAt: now,
  };
  const requests: CapturedRequest[] = [];

  await page.route('**/api/rules**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const captured: CapturedRequest = { method: request.method(), path, query: url.search };
    requests.push(captured);

    if (request.method() === 'GET' && path === '/api/rules/actions') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ canStartCreate }),
      });
      return;
    }

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
      const condition = captured.body.condition as ApiTypes.RuleConditionNodeDto;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ condition, display: conditionDisplay(condition) }),
      });
      return;
    }
    if (request.method() === 'POST' && path === '/api/rules/authoring/project') {
      captured.body = request.postDataJSON() as JsonObject;
      const source = captured.body.source as
        | { ast?: ApiTypes.RuleConditionNodeDto; text?: string }
        | undefined;
      const condition = source?.ast ?? requiredCondition();
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          condition,
          formattedDsl: source?.text ?? 'value == false',
          explanation: conditionDisplay(condition),
          diagnostics: [],
          isValid: true,
        }),
      });
      return;
    }
    if (request.method() === 'POST' && path === '/api/rules/authoring/complete') {
      captured.body = request.postDataJSON() as JsonObject;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          { label: 'value', insertText: 'value', kind: 'Input', start: 0, length: 0 },
        ]),
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
      const candidates = detail ? [...builtInRules, detail] : builtInRules;
      const items = candidates.filter((candidate) =>
        `${candidate.name} ${candidate.description} ${candidate.definitionKey}`
          .toLowerCase()
          .includes(query),
      );
      const sortBy = url.searchParams.get('sortBy');
      if (sortBy === 'Name' || sortBy === 'Origin' || sortBy === 'Status') {
        const direction = url.searchParams.get('sortDirection') === 'Descending' ? -1 : 1;
        const field = sortBy === 'Name' ? 'name' : sortBy.toLowerCase();
        items.sort(
          (left, right) =>
            String(left[field as keyof typeof left]).localeCompare(
              String(right[field as keyof typeof right]),
            ) * direction,
        );
      }
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
            revision: 1,
          },
        ]),
      });
      return;
    }
    if (request.method() === 'GET' && path.startsWith('/api/rules/field.')) {
      const system = builtInDetail(path.slice('/api/rules/'.length));
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
        latestVersion: null,
        activeVersion: null,
        inputs: [],
        output: { type: 'Boolean', cardinality: 'Scalar' },
        condition: undefined,
        versions: [],
        actions: {
          canEditDraft: true,
          canCreateVersion: true,
          canActivateVersion: false,
          canDeactivate: false,
          canArchive: true,
        },
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
      request.method() === 'POST' &&
      detail &&
      path === `/api/rules/${detail.definitionKey}/versions`
    ) {
      detail = {
        ...detail,
        latestVersion: (detail.latestVersion ?? 0) + 1,
        revision: (detail.revision ?? 0) + 1,
        versions: [
          ...(detail.versions ?? []),
          {
            version: (detail.latestVersion ?? 0) + 1,
            name: detail.name,
            description: detail.description,
            expressionLanguageVersion: 1,
            inputs: detail.inputs,
            output: detail.output,
            condition: detail.condition,
            createdAt: now,
          },
        ],
        actions: {
          canEditDraft: true,
          canCreateVersion: true,
          canActivateVersion: true,
          canDeactivate: false,
          canArchive: true,
        },
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(detail),
      });
      return;
    }
    if (
      request.method() === 'PUT' &&
      detail &&
      path === `/api/rules/${detail.definitionKey}/active-version`
    ) {
      detail = {
        ...detail,
        activeVersion: detail.latestVersion,
        status: 'Active',
        revision: (detail.revision ?? 0) + 1,
        actions: {
          canEditDraft: true,
          canCreateVersion: true,
          canActivateVersion: false,
          canDeactivate: true,
          canArchive: true,
        },
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(detail),
      });
      return;
    }
    if (
      request.method() === 'DELETE' &&
      detail &&
      path === `/api/rules/${detail.definitionKey}/active-version`
    ) {
      detail = {
        ...detail,
        activeVersion: null,
        status: 'Inactive',
        revision: (detail.revision ?? 0) + 1,
        actions: {
          canEditDraft: true,
          canCreateVersion: true,
          canActivateVersion: true,
          canDeactivate: false,
          canArchive: true,
        },
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(detail),
      });
      return;
    }
    if (
      request.method() === 'POST' &&
      detail &&
      path === `/api/rules/${detail.definitionKey}/archive`
    ) {
      detail = {
        ...detail,
        activeVersion: null,
        status: 'Archived',
        revision: (detail.revision ?? 0) + 1,
        archivedAt: now,
        actions: {
          canEditDraft: false,
          canCreateVersion: false,
          canActivateVersion: false,
          canDeactivate: false,
          canArchive: false,
        },
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(detail),
      });
      return;
    }
    if (
      request.method() === 'POST' &&
      detail &&
      (path === `/api/rules/${detail.definitionKey}/draft/simulate` ||
        /\/versions\/\d+\/simulate$/.test(path))
    ) {
      captured.body = request.postDataJSON() as JsonObject;
      const sample = captured.body.inputs as Record<string, { values?: string[] }> | undefined;
      const isMatch = sample?.value?.values?.[0] !== 'no';
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          definitionKey: detail.definitionKey,
          definitionVersion: path.includes('/versions/') ? detail.activeVersion : null,
          isMatch,
          diagnostics: [{ nodeId: detail.condition?.nodeId, isMatch }],
          correlationId: 'rules-e2e',
        }),
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

  await page.route('**/api/rule-bindings/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    requests.push({
      method: request.method(),
      path,
      body: request.postDataJSON?.() as JsonObject,
    });
    if (request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(binding),
      });
      return;
    }
    if (request.method() === 'PUT') {
      binding = {
        ...binding,
        ...(request.postDataJSON() as JsonObject),
        revision: (binding.revision ?? 0) + 1,
        updatedAt: now,
      };
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(binding),
      });
      return;
    }
    if (request.method() === 'DELETE') {
      await route.fulfill({ status: 204 });
      return;
    }
    await route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
  });

  return requests;
}

test('denied create affordance blocks rule toolbar and deep-link launch', async ({ page }) => {
  await mockAuthenticatedSession(page);
  await mockRulesApi(page, false);

  await page.goto('/rules?dialog=create');

  await expect(page).toHaveURL(/\/rules\?page=1$/);
  await expect(page.getByRole('button', { name: 'New rule' })).toHaveCount(0);
  await expect(page.getByRole('dialog', { name: 'New workspace rule' })).toHaveCount(0);
});

test('rule catalog exposes inputs and read-only built-in details', async ({ page }) => {
  await mockAuthenticatedSession(page);
  const requests = await mockRulesApi(page);
  await page.goto('/rules');

  await expect(page.getByRole('heading', { name: 'Rules' })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'Inputs' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Required value' })).toBeVisible();
  await expect(page.getByText('Value').first()).toBeVisible();
  await expect(page.getByText('Applies to')).toHaveCount(0);

  const catalog = page.getByRole('region', { name: 'Rules catalog' });
  const firstRuleCell = catalog.getByRole('row').nth(1).locator('td').first();
  await expect(firstRuleCell).toHaveAttribute('data-cell-kind', 'action');
  await expect(firstRuleCell).toContainText('Required value');
  await expect(firstRuleCell).not.toContainText('Required value validation.');
  await expect
    .poll(() => firstRuleCell.evaluate((cell) => getComputedStyle(cell).verticalAlign))
    .toBe('middle');
  await expect(firstRuleCell.locator('..')).toHaveAttribute('data-row-layout', 'single-line');
  const wrappedRuleRow = catalog.getByRole('row').filter({ hasText: 'Date range' }).first();
  const wrappedRuleCell = wrappedRuleRow.locator('td[data-cell-kind="action"]');
  const wrappedInputsCell = wrappedRuleRow.locator('td[data-cell-kind="list"]');
  await expect(wrappedRuleRow).toHaveAttribute('data-row-layout', 'multiline');
  await expect(wrappedRuleCell).toHaveCSS('vertical-align', 'top');
  await expect(wrappedInputsCell).toHaveCSS('vertical-align', 'top');
  await expect
    .poll(() =>
      wrappedRuleRow.evaluate((row) => {
        const actionLabel = row.querySelector<HTMLElement>(
          '[data-slot="data-table-record-action-label"]',
        );
        const wrappedValue = row.querySelector<HTMLElement>(
          'td[data-cell-kind="list"] [data-slot="data-table-value"]',
        );
        if (!actionLabel || !wrappedValue) return Number.POSITIVE_INFINITY;
        return Math.abs(
          actionLabel.getBoundingClientRect().top - wrappedValue.getClientRects()[0].top,
        );
      }),
    )
    .toBeLessThanOrEqual(2);
  await expect(catalog.getByRole('button', { name: 'Origin: Sort ascending' })).toBeVisible();
  await expect(catalog.getByRole('button', { name: 'Status: Sort ascending' })).toBeVisible();
  await expect(catalog.getByRole('button', { name: 'Inputs: Sort ascending' })).toHaveCount(0);
  await catalog.getByRole('button', { name: 'Rule: Sort ascending' }).click();
  await expect(page).toHaveURL(/sortBy=Name&sortDirection=Ascending/);
  await expect(catalog.getByRole('columnheader', { name: 'Rule' })).toHaveAttribute(
    'aria-sort',
    'ascending',
  );
  await expect(catalog.getByRole('row').nth(1)).toContainText('Choice selection count');
  await expect
    .poll(() =>
      requests.some(
        (request) =>
          request.path === '/api/rules' &&
          request.query?.includes('sortBy=Name') &&
          request.query?.includes('sortDirection=Ascending'),
      ),
    )
    .toBe(true);
  await catalog.getByRole('button', { name: 'Rule: Sort descending' }).click();
  await expect(catalog.getByRole('row').nth(1)).toContainText('Text pattern');
  await catalog.getByRole('button', { name: 'Rule: Clear sorting' }).click();
  await expect(catalog.getByRole('row').nth(1)).toContainText('Required value');

  await page.getByRole('button', { name: 'Required value' }).click();
  const details = page.locator('[data-slot="managed-dialog-window"]');
  await expect(details.getByRole('tab')).toHaveText([
    'General',
    'Rule behavior',
    'Test rule',
    'Versions',
    'Usage',
    'System info',
  ]);
  const tabScroller = details.locator('[data-slot="managed-dialog-tab-scroll"]');
  await expect
    .poll(() => tabScroller.evaluate((element) => element.scrollWidth <= element.clientWidth))
    .toBe(true);
  await expect(details.locator('[data-slot="dialog-description"]')).toHaveText(
    'Required value validation.',
  );
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
  await expect(details).toContainText('Active');
  await expect(details).not.toContainText('Validation');
  await details.getByRole('tab', { name: 'Usage' }).click();
  await expect(details.getByText('customer.status')).toBeVisible();
  await expect(details.getByText('field-validation')).toBeVisible();
  const footer = page.locator('[data-slot="managed-dialog-footer"]');
  await expect(footer.getByRole('button', { name: 'Windows (1)' })).toBeVisible();
  const footerActions = footer.locator('[data-slot="managed-dialog-footer-actions"]');
  await expect(footerActions.getByRole('button')).toHaveCount(1);
  await expect(footerActions.getByRole('button', { name: 'Close' })).toBeVisible();
  await details.getByRole('tab', { name: 'System info' }).click();
  await expect(details.getByText('field.required')).toBeVisible();
  await expect(details.getByRole('tabpanel').getByText('Expression language')).toBeVisible();
});

test('built-in date range presents optional bounds as conditional assertions', async ({ page }) => {
  await mockAuthenticatedSession(page);
  await mockRulesApi(page);
  await page.goto('/rules');

  await page.getByRole('button', { name: 'Date range' }).click();
  const details = page.locator('[data-slot="managed-dialog-window"]');
  await details.getByRole('tab', { name: 'Rule behavior' }).click();
  const logic = details.locator('[data-slot="rule-behavior-flow"]');
  const expression = logic.locator('[data-slot="rule-expression"]');

  await expect(expression).toContainText(
    'Value is greater than or equal to Minimum when Minimum is specified',
  );
  await expect(expression).toContainText(
    'Value is less than or equal to Maximum when Maximum is specified',
  );
  await expect(logic.locator('[data-slot="rule-expression-operator"]')).toHaveText(['and']);
  await expect(logic.getByRole('button')).toHaveCount(0);
  await expect(logic.locator('[data-slot^="rule-condition-"]')).toHaveCount(0);
  await expect
    .poll(async () => (await expression.boundingBox())?.height ?? Number.POSITIVE_INFINITY)
    .toBeLessThan(80);
});

test('workspace rule authoring projects, simulates, and manages immutable lifecycle', async ({
  page,
}) => {
  await mockAuthenticatedSession(page);
  const requests = await mockRulesApi(page);
  await page.goto('/rules');
  await page.getByRole('button', { name: 'New rule' }).click();

  await page.getByLabel('Name').fill('Credit threshold');
  await page.getByLabel('Description').fill('Matches values above a threshold.');
  await page.getByRole('tab', { name: 'Rule behavior' }).click();
  await page.getByRole('button', { name: 'Add input' }).click();
  await page.getByRole('button', { name: 'Add input' }).click();
  await page.getByLabel('Key').nth(0).fill('value');
  await page.getByLabel('Key').nth(1).fill('threshold');
  await page.getByLabel('Input name').nth(0).fill('Value');
  await page.getByLabel('Input name').nth(1).fill('Threshold');
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
  expect(saveBody.inputs?.map(({ key, label }) => ({ key, label }))).toEqual([
    { key: 'value', label: 'Value' },
    { key: 'threshold', label: 'Threshold' },
  ]);
  expect(saveBody.condition?.predicateOperator).toBe('Equal');
  expect(saveBody.condition?.left?.kind).toBe('Input');
  expect(saveBody.condition?.left?.reference).toBe('value');
  await expect(
    page.getByLabel('Credit threshold').getByText('Draft', { exact: true }),
  ).toBeVisible();

  const editor = page.locator('[data-slot="managed-dialog-window"]');
  await editor.getByRole('tab', { name: 'Rule behavior' }).click();
  await editor.getByLabel('Expression syntax').fill('value');
  await editor.getByLabel('Expression syntax').blur();
  await editor.getByRole('button', { name: 'Show suggestions' }).click();
  await expect(editor.getByRole('button', { name: 'value' })).toBeVisible();
  await editor.getByRole('button', { name: 'value' }).click();
  await editor.getByRole('button', { name: 'Save draft' }).click();
  await expect(page.getByText('Draft saved').last()).toBeVisible();
  await editor.getByRole('tab', { name: 'Test rule' }).click();
  const simulation = editor.getByRole('region', { name: 'Simulation' });
  await simulation.getByLabel('Value').fill('yes');
  await simulation.getByRole('button', { name: 'Run simulation' }).click();
  await expect(simulation.locator('[data-slot="alert-title"]')).toHaveText('Condition matched');
  await expect(simulation.getByRole('heading', { name: 'Why this matched' })).toBeVisible();
  await expect(simulation.locator('[data-slot="rule-expression"]')).toBeVisible();
  await simulation.getByLabel('Value').fill('no');
  await expect(simulation.locator('[data-slot="alert-title"]')).toHaveCount(0);
  await simulation.getByRole('button', { name: 'Run simulation' }).click();
  await expect(simulation.locator('[data-slot="alert-title"]')).toHaveText('No match');
  await expect(simulation.getByRole('heading', { name: 'Why this did not match' })).toBeVisible();

  await editor.getByRole('tab', { name: 'Versions' }).click();
  await editor.getByRole('button', { name: 'Create version' }).click();
  await page.getByRole('button', { name: 'Create version' }).last().click();
  await expect(
    editor.getByLabel('Version history').getByText('Version 1', { exact: true }).first(),
  ).toBeVisible();
  await editor.getByRole('button', { name: 'Activate version' }).click();
  await page.getByRole('button', { name: 'Activate version' }).last().click();
  await expect(editor.getByText('Active', { exact: true })).toBeVisible();
  await editor.getByRole('button', { name: 'Deactivate' }).click();
  await page.getByRole('button', { name: 'Deactivate' }).last().click();
  await expect(editor.getByText('Inactive', { exact: true })).toBeVisible();
  expect(requests.some((request) => request.path.endsWith('/authoring/project'))).toBe(true);
  expect(requests.some((request) => request.path.endsWith('/authoring/complete'))).toBe(true);
  expect(requests.some((request) => request.path.endsWith('/draft/simulate'))).toBe(true);
  expect(requests.some((request) => request.path.endsWith('/versions'))).toBe(true);
  expect(
    requests.some(
      (request) => request.path.endsWith('/active-version') && request.method === 'PUT',
    ),
  ).toBe(true);
  expect(
    requests.some(
      (request) => request.path.endsWith('/active-version') && request.method === 'DELETE',
    ),
  ).toBe(true);
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
  const details = page.locator('[data-slot="managed-dialog-window"]');
  await details.getByRole('tab', { name: 'Rule behavior' }).click();
  const behavior = details.locator('[data-slot="rule-behavior-summary"]');
  await expect(behavior.getByRole('heading')).toHaveText(['Inputs', 'Logic', 'Outputs']);
  await expect
    .poll(() => behavior.evaluate((element) => element.scrollWidth <= element.clientWidth))
    .toBe(true);
});
