import { Buffer } from 'node:buffer';
import { expect, type Locator, type Page, test } from '@playwright/test';
import type { components } from '../src/lib/api-types';

type CreateRuleRequest = components['schemas']['CreateRuleDefinitionRequest'];
type RuleDetail = components['schemas']['RuleDefinitionDetailDto'];
type RuleExpressionLanguage = components['schemas']['RuleExpressionLanguageDto'];
type AssistRuleRequest = components['schemas']['AssistRuleExpressionRequest'];
type RuleExpressionAuthoring = components['schemas']['RuleExpressionAuthoringDto'];
type RuleExpressionCompletion = components['schemas']['RuleExpressionCompletionDto'];
type RuleExpressionDisplay = components['schemas']['RuleExpressionDisplayNodeDto'];
type RuleExpressionDisplayToken = components['schemas']['RuleExpressionDisplayTokenDto'];
type RuleExpressionReferenceKind = components['schemas']['RuleExpressionReferenceKind'];
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
const documentation = (displayName: string, summary = `${displayName} reference.`) => ({
  locales: {
    en: {
      displayName,
      summary,
      usage: `Use ${displayName} in a compatible expression.`,
      examples: [displayName],
    },
    vi: {
      displayName,
      summary: `Tham chiếu ${displayName}.`,
      usage: `Dùng ${displayName} trong biểu thức tương thích.`,
      examples: [displayName],
    },
  },
});
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
      {
        path: 'field.value',
        displayName: 'Field value',
        type: 'Decimal',
        allowMultiple: false,
        documentation: documentation('Field value', 'The decimal field value supplied at runtime.'),
      },
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
      documentation: documentation('Equals', 'Checks whether both values are the same.'),
    },
    {
      operator: 'GreaterThan',
      leftShapes: comparableTypes.map((type) => ({ type, cardinality: 'Scalar' })),
      rightShapes: comparableTypes.map((type) => ({ type, cardinality: 'Scalar' })),
      requiresMatchingTypes: true,
      documentation: documentation('Greater than'),
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
      documentation: documentation('Is blank'),
    },
  ],
  logicalOperators: [
    {
      operator: 'All',
      minimumChildren: 1,
      maximumChildren: null,
      documentation: documentation('All'),
    },
    {
      operator: 'Any',
      minimumChildren: 1,
      maximumChildren: null,
      documentation: documentation('Any'),
    },
    {
      operator: 'Not',
      minimumChildren: 1,
      maximumChildren: 1,
      documentation: documentation('Not'),
    },
  ],
  operandKinds: [
    { kind: 'Context', documentation: documentation('Context value') },
    { kind: 'Parameter', documentation: documentation('Parameter') },
    { kind: 'Literal', documentation: documentation('Literal value') },
    { kind: 'Function', documentation: documentation('Function result') },
  ],
  valueTypes: ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'].map((type) => ({
    type,
    documentation: documentation(type),
  })),
  cardinalities: [
    { cardinality: 'Scalar', documentation: documentation('Single value') },
    { cardinality: 'Multiple', documentation: documentation('Multiple values') },
    { cardinality: 'Any', documentation: documentation('Single or multiple') },
  ],
  limitDefinitions: [
    { key: 'maxDepth', value: 12, documentation: documentation('Maximum nesting depth') },
    { key: 'maxNodes', value: 200, documentation: documentation('Maximum condition nodes') },
    {
      key: 'maxFunctionCalls',
      value: 50,
      documentation: documentation('Maximum function calls'),
    },
    { key: 'maxParameters', value: 100, documentation: documentation('Maximum parameters') },
    {
      key: 'maxExecutionSteps',
      value: 1000,
      documentation: documentation('Maximum evaluation steps'),
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
  const requiredCheck = {
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
  } satisfies NonNullable<RuleDetail['condition']>;
  return {
    ...definition,
    revision: null,
    contextKey: null,
    contextSchemaVersion: null,
    condition:
      definitionKey === 'field.required'
        ? {
            nodeId: 'root',
            logicalOperator: 'Any',
            children: [
              { nodeId: 'required_group', logicalOperator: 'All', children: [requiredCheck] },
              {
                nodeId: 'fallback_group',
                logicalOperator: 'All',
                children: [
                  {
                    nodeId: 'fallback_check',
                    predicateOperator: 'Equal',
                    left: { kind: 'Context', reference: 'field.value', arguments: [] },
                    right: {
                      kind: 'Literal',
                      literal: { type: 'Text', values: ['example'] },
                      arguments: [],
                    },
                    children: [],
                  },
                ],
              },
            ],
          }
        : requiredCheck,
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

function expressionGuide(request: { query?: string | null }) {
  const query = request.query?.trim().toLocaleLowerCase() ?? '';
  const text = (value: string, highlight?: string) => {
    const start = highlight ? value.toLocaleLowerCase().indexOf(highlight) : -1;
    const highlightLength = highlight?.length ?? 0;
    return {
      text: value,
      segments:
        start < 0
          ? [{ text: value, isMatch: false }]
          : [
              ...(start > 0 ? [{ text: value.slice(0, start), isMatch: false }] : []),
              { text: value.slice(start, start + highlightLength), isMatch: true },
              ...(start + highlightLength < value.length
                ? [{ text: value.slice(start + highlightLength), isMatch: false }]
                : []),
            ],
    };
  };
  const item = (
    referenceKind: string,
    referenceKey: string,
    displayName: string,
    summary: string,
    highlight?: string,
  ) => ({
    referenceKind,
    referenceKey,
    displayName: text(displayName, highlight),
    summary: text(summary),
    usage: text(`Use ${displayName} in a compatible expression.`),
    examples: [text(referenceKey)],
  });
  const isBlank = item(
    'Function',
    'IsBlank',
    'Is blank',
    'Returns true when a value is absent or empty.',
    query === 'blnk' ? 'blank' : undefined,
  );
  const sections =
    query === 'blnk'
      ? [{ key: 'functions', title: 'Functions', description: 'Functions.', items: [isBlank] }]
      : [
          {
            key: 'context',
            title: 'Available fields',
            description: 'Context values.',
            items: [
              item(
                'Context',
                'field.value',
                '@context.field.value',
                'The field value supplied when the rule runs.',
              ),
            ],
          },
          {
            key: 'operators',
            title: 'Operators',
            description: 'Comparison operators.',
            items: [
              item(
                'PredicateOperator',
                'GreaterThan',
                'Greater than',
                'The first value is larger than or later than the second.',
              ),
            ],
          },
          {
            key: 'functions',
            title: 'Functions',
            description: 'Expression functions.',
            items: [isBlank],
          },
        ];
  return {
    expressionLanguageVersion: 1,
    totalResults: sections.reduce((total, section) => total + section.items.length, 0),
    sections,
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

function assistExpression(body: AssistRuleRequest): RuleExpressionAuthoring {
  const syntax = body.syntax;
  const condition = body.condition ?? parseExpressionSyntax(syntax ?? '');
  const cursor = Math.min(body.cursorOffset ?? syntax?.length ?? 0, syntax?.length ?? 0);
  const prefix = syntax?.slice(0, cursor).match(/@?[A-Za-z][A-Za-z0-9.]*$/)?.[0] ?? '';
  const parameters = body.parameters ?? [];
  const completions = [
    completion('@context.field.value', '@context.field.value', 'Context', 'field.value'),
    completion('Equal', 'Equal', 'PredicateOperator', 'Equal'),
    completion('GreaterThan', 'GreaterThan', 'PredicateOperator', 'GreaterThan'),
    ...parameters.map((parameter: { key?: string }) =>
      completion(
        `@parameters.${parameter.key ?? ''}`,
        `@parameters.${parameter.key ?? ''}`,
        'Parameter',
        parameter.key ?? '',
      ),
    ),
  ].map((item) => ({
    ...item,
    replacementStart: cursor - prefix.length,
    replacementLength: prefix.length,
  }));
  return {
    syntax: syntax ?? (condition ? formatCondition(condition) : ''),
    condition,
    display: condition ? displayCondition(condition) : null,
    diagnostics: condition
      ? []
      : [
          {
            code: 'rules.expression.required',
            message: 'Expression is required.',
            start: 0,
            length: Math.max(1, syntax?.length ?? 0),
          },
        ],
    completions: syntax === null ? [] : completions,
  };
}

function completion(
  label: string,
  insertText: string,
  referenceKind: RuleExpressionReferenceKind,
  referenceKey: string,
): RuleExpressionCompletion {
  return {
    label,
    insertText,
    cursorOffset: insertText.length,
    replacementStart: 0,
    replacementLength: 0,
    referenceKind,
    referenceKey,
    summary: `${label} reference.`,
  };
}

function parseExpressionSyntax(syntax: string): NonNullable<RuleDetail['condition']> | null {
  const match = syntax
    .trim()
    .match(
      /^@context\.field\.value\s+(Equal|GreaterThan)\s+@parameters\.([A-Za-z][A-Za-z0-9_.]*)$/,
    );
  if (!match) return null;
  return {
    nodeId: 'syntax-1',
    predicateOperator: match[1] as 'Equal' | 'GreaterThan',
    left: { kind: 'Context', reference: 'field.value', arguments: [] },
    right:
      match[2] === 'threshold'
        ? { kind: 'Parameter', reference: 'threshold', arguments: [] }
        : { kind: 'Context', reference: match[2], arguments: [] },
    children: [],
  };
}

function formatCondition(condition: NonNullable<RuleDetail['condition']>): string {
  if (condition.logicalOperator) {
    return `${condition.logicalOperator}(${(condition.children ?? []).map(formatCondition).join(', ')})`;
  }
  return `${formatOperand(condition.left)} ${condition.predicateOperator} ${formatOperand(
    condition.right,
  )}`.trim();
}

function formatOperand(operand: NonNullable<RuleDetail['condition']>['left']): string {
  if (!operand) return '';
  if (operand.kind === 'Function') {
    return `${operand.function}(${(operand.arguments ?? []).map(formatOperand).join(', ')})`;
  }
  if (operand.kind === 'Literal') {
    return `${operand.literal?.type}("${operand.literal?.values?.[0] ?? ''}")`;
  }
  if (!operand.reference) return '';
  return operand.kind === 'Context'
    ? `@context.${operand.reference}`
    : `@parameters.${operand.reference}`;
}

function displayCondition(condition: NonNullable<RuleDetail['condition']>): RuleExpressionDisplay {
  const children = condition.children ?? [];
  if (condition.logicalOperator) {
    const headings = {
      All: 'and',
      Any: 'or',
      Not: 'not',
    };
    return {
      nodeId: condition.nodeId,
      tokens: [
        {
          text: headings[condition.logicalOperator],
          referenceKind: 'LogicalOperator',
          referenceKey: condition.logicalOperator,
        },
      ],
      children: children.map(displayCondition),
    };
  }
  return {
    nodeId: condition.nodeId,
    tokens: displayConditionTokens(condition),
    children: [],
  };
}

function displayConditionTokens(
  condition: NonNullable<RuleDetail['condition']>,
): RuleExpressionDisplayToken[] {
  if (condition.logicalOperator) {
    if (condition.logicalOperator === 'Not') {
      return [
        {
          text: 'not',
          referenceKind: 'LogicalOperator',
          referenceKey: 'Not',
        },
        ...(condition.children?.[0] ? displayConditionTokens(condition.children[0]) : []),
      ];
    }
    return (condition.children ?? []).flatMap((child, index) => [
      ...(index > 0 && condition.logicalOperator === 'Any' ? [{ text: ',' }] : []),
      ...(index > 0
        ? [
            {
              text: condition.logicalOperator === 'All' ? 'and' : 'or',
              referenceKind: 'LogicalOperator' as const,
              referenceKey: condition.logicalOperator,
            },
          ]
        : []),
      ...displayConditionTokens(child),
    ]);
  }
  if (
    condition.predicateOperator === 'Equal' &&
    condition.left?.kind === 'Function' &&
    condition.left.function === 'IsBlank' &&
    condition.right?.literal?.type === 'Boolean' &&
    condition.right.literal.values?.[0]?.toLowerCase() === 'true'
  ) {
    return [
      ...displayOperand(condition.left.arguments?.[0]),
      {
        text: 'is blank',
        referenceKind: 'Function',
        referenceKey: 'IsBlank',
      },
    ];
  }
  if (condition.predicateOperator === 'IsNull' || condition.predicateOperator === 'IsNotNull') {
    const parameter = condition.left?.kind === 'Parameter';
    return [
      ...displayOperand(condition.left),
      {
        text:
          condition.predicateOperator === 'IsNull'
            ? parameter
              ? 'is not provided'
              : 'has no value'
            : parameter
              ? 'is provided'
              : 'has a value',
        referenceKind: 'PredicateOperator',
        referenceKey: condition.predicateOperator,
      },
    ];
  }
  return [
    ...displayOperand(condition.left),
    {
      text:
        condition.predicateOperator === 'Equal'
          ? 'equals'
          : condition.predicateOperator === 'LessThan'
            ? 'is less than'
            : 'is greater than',
      referenceKind: 'PredicateOperator',
      referenceKey: condition.predicateOperator,
    },
    ...displayOperand(condition.right),
  ];
}

function displayOperand(
  operand: NonNullable<RuleDetail['condition']>['left'],
): RuleExpressionDisplayToken[] {
  if (!operand) return [];
  if (operand.kind === 'Function') {
    if (operand.function === 'ToDecimal') return displayOperand(operand.arguments?.[0]);
    return [
      {
        text: operand.function === 'IsBlank' ? 'Is blank' : operand.function,
        referenceKind: 'Function',
        referenceKey: operand.function,
      },
      { text: '(' },
      ...(operand.arguments ?? []).flatMap(displayOperand),
      { text: ')' },
    ];
  }
  if (operand.kind === 'Literal') {
    return [
      {
        text: operand.literal?.values?.join(', ') ?? '',
        referenceKind: 'Literal',
        referenceKey: operand.literal?.type,
        isCode: true,
      },
    ];
  }
  return [
    {
      text: operand.kind === 'Context' ? 'Field value' : operand.reference,
      referenceKind: operand.kind,
      referenceKey: operand.reference,
    },
  ];
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
    const requestUrl = new URL(request.url());
    const path = requestUrl.pathname;
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

    if (method === 'POST' && path === '/api/rules/expression-language/assist') {
      const body = request.postDataJSON() as AssistRuleRequest;
      captured.body = body;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(assistExpression(body)),
      });
      return;
    }

    if (method === 'POST' && path === '/api/rules/expression-language/guide') {
      const body = request.postDataJSON() as { query?: string | null };
      captured.body = body;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(expressionGuide(body)),
      });
      return;
    }

    if (method === 'GET' && path === '/api/rules') {
      const candidates = detail ? [...systemRules, summary(detail)] : systemRules;
      const query = requestUrl.searchParams.get('query')?.trim().toLocaleLowerCase();
      const items = query
        ? candidates.filter((definition) =>
            [definition.name, definition.description, definition.definitionKey]
              .filter(Boolean)
              .join(' ')
              .toLocaleLowerCase()
              .includes(query),
          )
        : candidates;
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
        const condition = parseExpressionSyntax(body.expressionSyntax ?? '');
        detail = {
          ...detail,
          name: body.name,
          description: body.description,
          scope: body.scope,
          contextKey: body.contextKey,
          contextSchemaVersion: body.contextSchemaVersion,
          outcomeKind: body.outcomeKind,
          parameters: body.parameters,
          condition,
          outcome: body.outcome,
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

test('rules catalog search and managed system windows remain usable', async ({ page }) => {
  const pageErrors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') pageErrors.push(message.text());
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));

  await mockAuthenticatedSession(page);
  await mockRulesApi(page);
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto('/rules');

  const catalog = page.getByRole('region', { name: 'Rules catalog' });
  const toolbarActions = catalog.locator('[data-slot="data-table-toolbar-actions"]');
  await expect(toolbarActions.getByRole('button', { name: 'New rule' })).toBeVisible();
  await expect(catalog.getByRole('columnheader', { name: /Actions/ })).toHaveCount(0);
  const search = catalog.getByLabel('Search rules');
  await search.fill('date and time range');
  await expect
    .poll(() => new URL(page.url()).searchParams.get('query'))
    .toBe('date and time range');
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
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'windowed');
  const expandedLayerBox = await page
    .locator('[data-slot="managed-window-expanded-layer"]')
    .boundingBox();
  if (!expandedLayerBox) throw new Error('Managed window work area did not render');
  const expectedWindowedRect = {
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
    .toEqual(expectedWindowedRect);

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
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'windowed');
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
  expect(minimumDialogBox?.width).toBeGreaterThanOrEqual(expandedLayerBox.width * 0.35 - 1);
  expect(minimumDialogBox?.width).toBeLessThan(resetDialogBox.width - 1);
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
  await expect(catalog.getByRole('button', { name: 'Filters', exact: true })).toHaveCount(0);

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
  expect(pageErrors).toEqual([]);
});

test('read-only rule details open the canonical server guide', async ({ page }) => {
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
  await catalog.getByRole('button', { name: 'Required value', exact: true }).click();
  const systemDetails = page.getByRole('dialog', { name: 'Required value' });
  await expect(systemDetails).toBeVisible();
  const systemBadges = systemDetails.locator(
    '[data-slot="managed-dialog-header"] [data-slot="badge"]',
  );
  await expect(systemBadges).toHaveCount(2);
  await expect(systemBadges.nth(0)).toHaveText('Built-in');
  await expect(systemBadges.nth(1)).toHaveText('Published');
  await expect(systemDetails.getByRole('heading', { name: 'What this rule does' })).toBeVisible();
  await expect(
    systemDetails.getByRole('heading', { name: 'Where this rule applies' }),
  ).toBeVisible();
  await expect(systemDetails).toContainText('Field value is blank');
  await expect(systemDetails).not.toContainText('Expression syntax');
  await expect(systemDetails.getByRole('button', { name: 'Expression guide' })).toHaveCount(0);
  expect(
    requests.filter((request) => request.path === '/api/rules/expression-language'),
  ).toHaveLength(0);
  const blankKeyword = systemDetails.getByRole('button', { name: 'is blank' });
  await expect(blankKeyword).toHaveClass(/underline/);
  await expect(blankKeyword.locator('svg')).toHaveCount(0);
  await blankKeyword.click();
  const keywordReference = page.getByRole('dialog', { name: 'Rule expression guide' });
  await expect(keywordReference.getByRole('heading', { name: 'Functions' })).toBeVisible();
  const selectedKeywordReference = keywordReference.locator(
    '#rule-expression-guide-item-Function-IsBlank',
  );
  await expect(selectedKeywordReference).toBeFocused();
  await expect(selectedKeywordReference).toHaveAttribute('aria-current', 'true');
  await expect(selectedKeywordReference.getByRole('heading', { name: 'is blank' })).toBeVisible();
  await expect(
    keywordReference.getByText('IsBlank(@context.field.value) Equal Boolean("true")'),
  ).toHaveCount(0);
  await expect(selectedKeywordReference.getByText('How to use')).toBeVisible();
  await expect(selectedKeywordReference.getByText('Reference', { exact: true })).toHaveCount(0);
  await expect(selectedKeywordReference.getByText('Examples')).toHaveCount(0);
  await expect(keywordReference.getByRole('button', { name: 'Insert' })).toHaveCount(0);
  expect(
    requests.filter((request) => request.path === '/api/rules/expression-language'),
  ).toHaveLength(0);
  await keywordReference.getByRole('button', { name: 'Close' }).click();
  const behaviorFlow = systemDetails.locator('[data-slot="system-rule-behavior-flow"]');
  await expect(behaviorFlow.locator('[data-slot="badge"]')).toHaveCount(0);
  await expect(behaviorFlow.locator('[data-slot="rule-timeline-item"]')).toHaveCount(2);
  await expect(behaviorFlow.locator('[data-slot="rule-timeline-marker"]')).toHaveCount(2);
  await expect(behaviorFlow.locator('[data-slot="rule-timeline-line"]')).toBeVisible();
  await expect(behaviorFlow.locator('[data-slot="rule-timeline-tail"]')).toBeVisible();
  await expect(behaviorFlow.locator('[data-slot="rule-condition-group"]')).toHaveCount(3);
  await expect(
    behaviorFlow.locator('[data-slot="rule-condition-group"][data-operator="Any"]'),
  ).toHaveCount(1);
  await expect(
    behaviorFlow.locator('[data-slot="rule-condition-group"][data-operator="All"]'),
  ).toHaveCount(2);
  await expect(behaviorFlow.locator('[data-slot="rule-condition-parallel-rail"]')).toHaveCount(2);
  await expect(behaviorFlow.locator('[data-slot="rule-condition-serial-rail"]')).toHaveCount(2);
  const orConnector = behaviorFlow.getByRole('button', { name: 'or' });
  await expect(orConnector).toBeVisible();
  await expect(behaviorFlow.getByRole('button', { name: 'and' })).toHaveCount(2);
  await expect(orConnector).toHaveAttribute('title', 'or');
  await orConnector.focus();
  await expect(orConnector).toBeFocused();
  const conditionConnector = await behaviorFlow.evaluate((flow) => {
    const root = flow.querySelector<HTMLElement>(
      '[data-slot="rule-condition-group"][data-operator="Any"]',
    );
    const branches = Array.from(
      root?.querySelectorAll<HTMLElement>(
        ':scope > ul > [data-slot="rule-condition-item"] > [data-slot="rule-condition-parallel-branch"][data-edge="inline-start"]',
      ) ?? [],
    ).map((branch) => branch.getBoundingClientRect());
    const rails = Array.from(
      root?.querySelectorAll<HTMLElement>(':scope > [data-slot="rule-condition-parallel-rail"]') ??
        [],
    ).map((rail) => rail.getBoundingClientRect());
    return {
      branchCenters: branches.map((branch) => ({
        start: branch.x,
        y: branch.y + branch.height / 2,
      })),
      railCenters: rails.map((rail) => ({
        x: rail.x + rail.width / 2,
        start: rail.y,
        end: rail.y + rail.height,
      })),
    };
  });
  expect(
    Math.abs(conditionConnector.branchCenters[0].start - conditionConnector.railCenters[0].x),
  ).toBeLessThanOrEqual(1);
  expect(
    Math.abs(conditionConnector.branchCenters[0].y - conditionConnector.railCenters[0].start),
  ).toBeLessThanOrEqual(1);
  const lastBranchCenter = conditionConnector.branchCenters.at(-1);
  if (!lastBranchCenter) throw new Error('Expected a final connector branch.');
  expect(Math.abs(lastBranchCenter.y - conditionConnector.railCenters[0].end)).toBeLessThanOrEqual(
    1,
  );
  await expect(behaviorFlow.getByText(/^(and|or|not)$/i)).toHaveCount(0);
  await expect(behaviorFlow).not.toContainText('Any');
  await expect(behaviorFlow).not.toContainText('All');
  await expect(behaviorFlow.locator('[data-slot="system-rule-effect"]')).toContainText(
    'Effect: Blocks the action',
  );
  await expect(
    systemDetails.getByRole('heading', { name: 'Version and references' }),
  ).toBeVisible();
  await expect(systemDetails.getByRole('button', { name: /Technical details/ })).toHaveCount(0);
  await expect(systemDetails.getByRole('button', { name: 'Archive', exact: true })).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(systemDetails).toBeHidden();
  expect(pageErrors).toEqual([]);
});

test('rules catalog table remains aligned and configurable', async ({ page }, testInfo) => {
  const pageErrors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') pageErrors.push(message.text());
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));

  await mockAuthenticatedSession(page);
  await mockRulesApi(page);
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto('/rules');

  const catalog = page.getByRole('region', { name: 'Rules catalog' });
  await expect(catalog.getByRole('button', { name: 'Filters', exact: true })).toHaveCount(0);

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
  expect(pageErrors).toEqual([]);
});

test('workspace rule authoring supports simulation and immutable lifecycle', async ({
  page,
}, testInfo) => {
  const pageErrors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') pageErrors.push(message.text());
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));

  await mockAuthenticatedSession(page);
  const requests = await mockRulesApi(page);
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto('/rules');

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
  await editorDialog.getByRole('button', { name: 'Expression guide' }).click();
  const expressionGuide = page.getByRole('dialog', { name: 'Rule expression guide' });
  await expect(expressionGuide.getByRole('heading', { name: 'Operators' })).toBeVisible();
  await expect(expressionGuide.getByRole('heading', { name: 'Functions' })).toBeVisible();
  const guideSearch = expressionGuide.getByRole('searchbox', { name: 'Search expression guide' });
  await guideSearch.fill('blnk');
  const highlightedMatch = expressionGuide.locator('mark').filter({ hasText: 'blank' }).first();
  await expect(highlightedMatch).toBeVisible();
  await expect(highlightedMatch).toHaveClass(/bg-primary/);
  await expect(expressionGuide.getByText('Matches: 1 · “blnk”')).toBeVisible();
  await guideSearch.fill('');
  const availableFields = expressionGuide
    .getByRole('heading', { name: 'Available fields' })
    .locator('..')
    .locator('..');
  await availableFields.getByRole('button', { name: 'Insert' }).click();
  await expressionGuide.getByRole('button', { name: 'Close' }).click();
  await expect(editorDialog.getByLabel('Expression syntax')).toHaveValue('@context.field.value');
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
  const expressionSyntax = editorDialog.getByLabel('Expression syntax');
  await expressionSyntax.fill('@context.field.value Gre');
  const suggestions = page.getByRole('listbox', { name: 'Expression suggestions' });
  await expect(suggestions.getByRole('option', { name: /GreaterThan/ })).toBeVisible();
  await expressionSyntax.press('Enter');
  await expect(expressionSyntax).toHaveValue('@context.field.value GreaterThan');
  await expressionSyntax.pressSequentially(' @parameters.threshold');
  await expect(editorDialog.getByRole('heading', { name: 'What this means' })).toBeVisible();
  await expect(editorDialog).toContainText('Field value is greater than threshold');
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
    expressionSyntax: '@context.field.value GreaterThan @parameters.threshold',
  });
  expect(firstSave?.body).not.toHaveProperty('condition');

  await editorDialog.getByRole('button', { name: 'Publish version' }).click();
  let publishDialog = page.locator('[data-slot="alert-dialog-content"]');
  await expect(publishDialog.getByRole('heading', { name: 'Publish this rule?' })).toBeVisible();
  await expect(publishDialog).toContainText('Version 1 will be immutable.');
  await expect(publishDialog).toContainText('Decimal field value');
  await expect(publishDialog).toContainText('When');
  await expect(publishDialog).toContainText('Then');
  await expect(publishDialog).toContainText('Blocks the action');
  await publishDialog.getByRole('button', { name: 'Publish version' }).click();
  await expect(editorDialog.getByText('Published', { exact: true })).toBeVisible();
  const publishedDetails = editorDialog.locator('[data-slot="workspace-rule-details"]');
  await expect(publishedDetails).toBeVisible();
  await expect(publishedDetails.getByText('When', { exact: true })).toBeVisible();
  await expect(publishedDetails.getByText('Then', { exact: true })).toBeVisible();
  await expect(publishedDetails).toContainText('Blocks the action');
  await expect(publishedDetails.getByText('Version 1').first()).toBeVisible();
  await expect(editorDialog.getByRole('button', { name: 'Expression guide' })).toHaveCount(0);
  await expect(editorDialog.getByLabel('Message')).toHaveCount(0);

  await editorDialog.getByRole('button', { name: 'Start revision' }).click();
  await expect(editorDialog.getByText('Draft', { exact: true })).toBeVisible();
  await editorDialog.getByLabel('Message').fill('Credit value exceeds the approved threshold.');
  await editorDialog.getByRole('button', { name: 'Publish version' }).click();
  publishDialog = page.locator('[data-slot="alert-dialog-content"]');
  await expect(publishDialog).toContainText('Version 2 will be immutable.');
  await publishDialog.getByRole('button', { name: 'Publish version' }).click();
  await expect(editorDialog.getByText('Version 2').first()).toBeVisible();

  await editorDialog.getByRole('button', { name: 'Archive', exact: true }).click();
  const archiveDialog = page.locator('[data-slot="alert-dialog-content"]');
  await expect(archiveDialog.getByRole('heading', { name: 'Archive this rule?' })).toBeVisible();
  await expect(archiveDialog).toContainText('Published versions already in use remain resolvable.');
  await archiveDialog.getByRole('button', { name: 'Archive', exact: true }).click();
  await expect(
    editorDialog
      .locator('[data-slot="managed-dialog-header"] [data-slot="badge"]')
      .filter({ hasText: 'Archived' }),
  ).toBeVisible();

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

test('rules catalog and details stay contained on mobile', async ({ page }, testInfo) => {
  const pageErrors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') pageErrors.push(message.text());
  });
  page.on('pageerror', (error) => pageErrors.push(error.message));

  await mockAuthenticatedSession(page);
  await mockRulesApi(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/rules');

  const catalog = page.getByRole('region', { name: 'Rules catalog' });
  await expectNoDocumentOverflow(page);
  await catalog.getByRole('button', { name: 'Required value', exact: true }).click();
  const systemDetails = page.getByRole('dialog', { name: 'Required value' });
  const systemWindow = systemDetails.locator('[data-slot="managed-dialog-window"]');
  await expect(systemWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
  const dialogBox = await systemWindow.boundingBox();
  expect(dialogBox?.x ?? -1).toBeGreaterThanOrEqual(-1);
  expect((dialogBox?.x ?? 0) + (dialogBox?.width ?? 0)).toBeLessThanOrEqual(391);
  await systemDetails.getByRole('button', { name: 'Minimize dialog' }).click();
  const mobileDock = await expectDockAboveFooter(page);
  await expect(mobileDock).toContainText('Required value');
  await mobileDock.getByRole('button', { name: 'Close dialog' }).click();
  await expect(mobileDock).toBeHidden();
  await expect(catalog).toBeVisible();
  await expectNoDocumentOverflow(page);
  await expect(catalog.getByRole('button', { name: 'Filters', exact: true })).toHaveCount(0);
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
  await catalogHorizontalViewport.evaluate((element) => element.scrollTo({ top: 0 }));
  await catalogHorizontalViewport.evaluate((element) => element.scrollTo({ left: 0 }));
  await testInfo.attach('rules-table-mobile', {
    body: await page.screenshot(),
    contentType: 'image/png',
  });
  expect(pageErrors).toEqual([]);
});
