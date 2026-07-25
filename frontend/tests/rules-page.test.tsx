import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RulesPage } from '@/features/rules';
import type {
  RuleConditionNode,
  RuleExpressionDisplayNode,
  RuleOperand,
} from '@/features/rules/api';
import { renderWithRouter } from './render-with-router';

function jsonResponse(data: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(JSON.stringify(data)),
    json: () => Promise.resolve(data),
  } as unknown as Response;
}

function documentation(displayName: string, summary = `${displayName} reference.`) {
  return {
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
  };
}

const systemRule = (
  definitionKey: string,
  name: string,
  description: string,
  targetTypeKeys: string[],
  parameters: object[] = [],
) => ({
  definitionKey,
  name,
  description,
  origin: 'System',
  scope: 'Field',
  outcomeKind: 'Validation',
  status: 'Published',
  latestPublishedVersion: 1,
  applicability: { targetTypeKeys, configurationConstraints: {} },
  parameters,
  documentation: {
    locales: {
      en: {
        displayName: name,
        summary: description,
        usage:
          definitionKey === 'field.required'
            ? 'Ready to use—no setup required.'
            : 'Configure the rule parameters.',
        examples: [definitionKey],
      },
      vi: {
        displayName: name,
        summary: description,
        usage:
          definitionKey === 'field.required'
            ? 'Sẵn sàng sử dụng—không cần cấu hình.'
            : 'Cấu hình parameters của rule.',
        examples: [definitionKey],
      },
    },
  },
});

const ruleDefinitions = {
  items: [
    systemRule(
      'field.required',
      'Required value',
      'Require records to provide a value for the field.',
      ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean', 'Choice'],
    ),
    systemRule('field.numeric_range', 'Numeric range', 'Limits numeric values.', [
      'Integer',
      'Decimal',
    ]),
    systemRule('field.decimal_precision', 'Decimal precision', 'Limits precision.', ['Decimal']),
    systemRule('field.date_range', 'Date range', 'Limits dates.', ['Date']),
    systemRule('field.datetime_range', 'Date and time range', 'Limits instants.', ['DateTime']),
    systemRule('field.text_length', 'Text length', 'Limits text length.', ['Text']),
    systemRule('field.text_pattern', 'Text pattern', 'Matches a pattern.', ['Text']),
    systemRule('field.text_format', 'Text format', 'Requires a known format.', ['Text']),
    systemRule('field.choice_selection_count', 'Choice selection count', 'Limits selections.', [
      'Choice',
    ]),
    {
      definitionKey: 'credit_threshold',
      name: 'Credit threshold',
      description: 'Flags values above the workspace threshold.',
      origin: 'Workspace',
      scope: 'Field',
      outcomeKind: 'Validation',
      status: 'Draft',
      revision: 2,
      contextKey: 'business_objects.field.decimal',
      contextSchemaVersion: 1,
      parameters: [],
    },
  ],
  totalCount: 10,
  page: 1,
  pageSize: 100,
};

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
        documentation: documentation('Field value', 'The decimal field value supplied at runtime.'),
      },
    ],
  },
];

const expressionLanguage = {
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
      documentation: documentation('Same value', 'Checks whether both values are the same.'),
    },
    {
      operator: 'IsNull',
      leftShapes: [{ type: 'Text', cardinality: 'Any' }],
      rightShapes: [],
      requiresMatchingTypes: false,
      documentation: documentation('Is empty'),
    },
  ],
  functions: [
    {
      function: 'IsBlank',
      parameters: [{ acceptedTypes: ['Text'], cardinality: 'Any' }],
      returnType: 'Boolean',
      returnCardinality: 'Scalar',
      documentation: documentation('Is blank'),
    },
    {
      function: 'Length',
      parameters: [{ acceptedTypes: ['Text'], cardinality: 'Scalar' }],
      returnType: 'Integer',
      returnCardinality: 'Scalar',
      documentation: documentation('Length', 'Returns the number of characters in text.'),
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
  operandKinds: ['Context', 'Parameter', 'Literal', 'Function'].map((kind) => ({
    kind,
    documentation: documentation(kind),
  })),
  valueTypes: ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'].map((type) => ({
    type,
    documentation: documentation(type),
  })),
  cardinalities: ['Scalar', 'Multiple', 'Any'].map((cardinality) => ({
    cardinality,
    documentation: documentation(cardinality),
  })),
  limitDefinitions: [
    { key: 'maxDepth', value: 12, documentation: documentation('Maximum nesting depth') },
    { key: 'maxNodes', value: 200, documentation: documentation('Maximum condition nodes') },
    {
      key: 'maxExecutionSteps',
      value: 1000,
      documentation: documentation('Maximum evaluation steps'),
    },
  ],
  limits: { maxDepth: 12, maxNodes: 200, maxExecutionSteps: 1000 },
};

function expressionGuideResponse(init?: RequestInit) {
  const request = init?.body ? JSON.parse(String(init.body)) : {};
  const query = String(request.query ?? '').toLowerCase();
  const text = (value: string, match?: string) => {
    const start = match ? value.toLowerCase().indexOf(match.toLowerCase()) : -1;
    return {
      text: value,
      segments:
        start < 0
          ? [{ text: value, isMatch: false }]
          : [
              ...(start > 0 ? [{ text: value.slice(0, start), isMatch: false }] : []),
              { text: value.slice(start, start + (match?.length ?? 0)), isMatch: true },
              ...(start + (match?.length ?? 0) < value.length
                ? [{ text: value.slice(start + (match?.length ?? 0)), isMatch: false }]
                : []),
            ],
    };
  };
  const item = (
    referenceKind: string,
    referenceKey: string,
    displayName: string,
    summary: string,
    detail?: string,
    match?: string,
  ) => ({
    referenceKind,
    referenceKey,
    displayName: text(displayName, match),
    summary: text(summary),
    usage: text(`Use ${displayName} in a compatible expression.`),
    examples: [text(referenceKind === 'Context' ? '@context.field.value' : referenceKey)],
    detail: detail ? text(detail) : undefined,
  });
  const context = item(
    'Context',
    'field.value',
    'Field value',
    'The value supplied when the rule runs.',
    '@context.field.value',
  );
  const isBlank = item(
    'Function',
    'IsBlank',
    'Is blank',
    'Returns true when a value is absent or empty.',
    'IsBlank(Text · Any) → Boolean · Scalar',
    query === 'blnk' ? 'blank' : undefined,
  );
  const length = item(
    'Function',
    'Length',
    'Length',
    'Returns the number of characters in text.',
    'Length(Text · Scalar) → Integer · Scalar',
    query === 'lenght' ? 'Length' : undefined,
  );
  const equal = item(
    'PredicateOperator',
    'Equal',
    'Same value',
    'Checks whether both values are the same.',
  );
  const any = item('LogicalOperator', 'Any', 'Or', 'Matches when one connected branch matches.');
  const isNotNull = item(
    'PredicateOperator',
    'IsNotNull',
    'Is not empty',
    'Checks whether a value is present.',
  );
  const sections =
    query === 'not-a-reference'
      ? []
      : query === 'blnk'
        ? [{ key: 'functions', title: 'Functions', description: 'Functions.', items: [isBlank] }]
        : query === 'lenght'
          ? [{ key: 'functions', title: 'Functions', description: 'Functions.', items: [length] }]
          : [
              {
                key: 'context',
                title: 'Current context',
                description: 'Context values.',
                items: [context],
              },
              {
                key: 'operators',
                title: 'Operators',
                description: 'Operators.',
                items: [any, equal, isNotNull],
              },
              {
                key: 'functions',
                title: 'Functions',
                description: 'Functions.',
                items: [isBlank, length],
              },
            ];
  return jsonResponse({
    expressionLanguageVersion: 1,
    totalResults: sections.reduce((total, section) => total + section.items.length, 0),
    sections,
  });
}

function systemDetail(definitionKey: string) {
  const summary = ruleDefinitions.items.find(
    (definition) => definition.definitionKey === definitionKey,
  );
  if (!summary) throw new Error(`Missing test rule ${definitionKey}`);
  return {
    ...summary,
    expressionLanguageVersion: 1,
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
      right: { kind: 'Literal', literal: { type: 'Boolean', values: ['true'] }, arguments: [] },
      children: [],
    },
    outcome: {
      kind: 'Validation',
      violationCode: 'field.value.required',
      severity: 'Error',
      message: 'A value is required.',
    },
    versions: [],
    createdAt: null,
    updatedAt: null,
    archivedAt: null,
  };
}

function workspaceRuleDetail(overrides: Record<string, unknown> = {}) {
  return {
    ...ruleDefinitions.items[9],
    expressionLanguageVersion: 1,
    condition: {
      nodeId: 'credit_threshold_check',
      predicateOperator: 'Equal',
      left: { kind: 'Context', reference: 'field.value', arguments: [] },
      right: {
        kind: 'Literal',
        literal: { type: 'Decimal', values: ['100'] },
        arguments: [],
      },
      children: [],
    },
    outcome: {
      kind: 'Validation',
      violationCode: 'credit.threshold.exceeded',
      severity: 'Error',
      message: 'Value exceeds the credit threshold.',
    },
    versions: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    archivedAt: null,
    ...overrides,
  };
}

function expressionAssistResponse(init?: RequestInit) {
  const request = init?.body ? JSON.parse(String(init.body)) : {};
  const syntax = request.syntax as string | null | undefined;
  const cursor = Math.min(request.cursorOffset ?? syntax?.length ?? 0, syntax?.length ?? 0);
  const prefix = syntax?.slice(0, cursor).match(/@?[A-Za-z][A-Za-z0-9.]*$/)?.[0] ?? '';
  const condition =
    request.condition ??
    (syntax?.trim()
      ? syntax.includes('Length(')
        ? {
            nodeId: 'syntax-1',
            predicateOperator: 'Equal',
            left: {
              kind: 'Function',
              function: 'Length',
              arguments: [{ kind: 'Context', reference: 'field.value', arguments: [] }],
            },
            right: {
              kind: 'Literal',
              literal: { type: 'Integer', values: ['5'] },
              arguments: [],
            },
            children: [],
          }
        : {
            nodeId: 'syntax-1',
            predicateOperator: syntax.includes('GreaterThan') ? 'GreaterThan' : 'Equal',
            left: { kind: 'Context', reference: 'field.value', arguments: [] },
            right: syntax.includes('threshold')
              ? { kind: 'Parameter', reference: 'threshold', arguments: [] }
              : {
                  kind: 'Literal',
                  literal: { type: 'Decimal', values: ['100'] },
                  arguments: [],
                },
            children: [],
          }
      : null);
  const canonicalSyntax =
    syntax ??
    (condition
      ? condition.left?.function === 'IsBlank'
        ? 'IsBlank(@context.field.value) Equal Boolean("true")'
        : '@context.field.value Equal Decimal("100")'
      : '');
  return jsonResponse({
    syntax: canonicalSyntax,
    condition,
    display: condition ? displayCondition(condition) : null,
    diagnostics: condition
      ? []
      : [
          {
            code: 'rules.expression.required',
            message: 'Expression is required.',
            start: 0,
            length: 1,
          },
        ],
    completions:
      syntax === null
        ? []
        : expressionCompletions.map((completion) => ({
            ...completion,
            replacementStart: cursor - prefix.length,
            replacementLength: prefix.length,
          })),
  });
}

const expressionCompletions = [
  {
    label: '@context.field.value',
    insertText: '@context.field.value',
    cursorOffset: 20,
    replacementStart: 0,
    replacementLength: 0,
    referenceKind: 'Context',
    referenceKey: 'field.value',
    summary: 'The field value.',
  },
  {
    label: 'Equal',
    insertText: 'Equal',
    cursorOffset: 5,
    replacementStart: 0,
    replacementLength: 0,
    referenceKind: 'PredicateOperator',
    referenceKey: 'Equal',
    summary: 'Checks whether both values are the same.',
  },
  {
    label: 'Length',
    insertText: 'Length()',
    cursorOffset: 7,
    replacementStart: 0,
    replacementLength: 0,
    referenceKind: 'Function',
    referenceKey: 'Length',
    summary: 'Returns the number of characters in text.',
  },
  {
    label: 'Integer',
    insertText: 'Integer("")',
    cursorOffset: 9,
    replacementStart: 0,
    replacementLength: 0,
    referenceKind: 'ValueType',
    referenceKey: 'Integer',
    summary: 'A whole number.',
  },
];

function displayCondition(node: RuleConditionNode): RuleExpressionDisplayNode {
  const children = node.children ?? [];
  if (node.logicalOperator) {
    const headings = {
      All: 'and',
      Any: 'or',
      Not: 'not',
    };
    return {
      nodeId: node.nodeId,
      tokens: [
        {
          text: headings[node.logicalOperator],
          referenceKind: 'LogicalOperator',
          referenceKey: node.logicalOperator,
        },
      ],
      children: children.map(displayCondition),
    };
  }
  return {
    nodeId: node.nodeId,
    tokens: displayConditionTokens(node),
    children: [],
  };
}

function displayConditionTokens(
  node: RuleConditionNode,
): NonNullable<RuleExpressionDisplayNode['tokens']> {
  if (node.logicalOperator) {
    if (node.logicalOperator === 'Not') {
      return [
        {
          text: 'not',
          referenceKind: 'LogicalOperator',
          referenceKey: 'Not',
        },
        ...(node.children?.[0] ? displayConditionTokens(node.children[0]) : []),
      ];
    }
    return (node.children ?? []).flatMap((child, index) => [
      ...(index > 0 && node.logicalOperator === 'Any' ? [{ text: ',' }] : []),
      ...(index > 0
        ? [
            {
              text: node.logicalOperator === 'All' ? 'and' : 'or',
              referenceKind: 'LogicalOperator' as const,
              referenceKey: node.logicalOperator,
            },
          ]
        : []),
      ...displayConditionTokens(child),
    ]);
  }
  if (
    node.predicateOperator === 'Equal' &&
    node.left?.kind === 'Function' &&
    node.left.function === 'IsBlank' &&
    node.right?.literal?.type === 'Boolean' &&
    node.right.literal.values?.[0]?.toLowerCase() === 'true'
  ) {
    return [
      ...displayOperand(node.left.arguments?.[0]),
      {
        text: 'is blank',
        referenceKind: 'Function',
        referenceKey: 'IsBlank',
      },
    ];
  }
  if (node.predicateOperator === 'IsNull' || node.predicateOperator === 'IsNotNull') {
    const parameter = node.left?.kind === 'Parameter';
    return [
      ...displayOperand(node.left),
      {
        text:
          node.predicateOperator === 'IsNull'
            ? parameter
              ? 'is not provided'
              : 'has no value'
            : parameter
              ? 'is provided'
              : 'has a value',
        referenceKind: 'PredicateOperator',
        referenceKey: node.predicateOperator,
      },
    ];
  }
  return [
    ...displayOperand(node.left),
    {
      text:
        node.predicateOperator === 'Equal'
          ? 'equals'
          : node.predicateOperator === 'LessThan'
            ? 'is less than'
            : node.predicateOperator === 'GreaterThan'
              ? 'is greater than'
              : node.predicateOperator,
      referenceKind: 'PredicateOperator',
      referenceKey: node.predicateOperator,
    },
    ...displayOperand(node.right),
  ];
}

function displayOperand(
  operand: RuleOperand | null | undefined,
): NonNullable<RuleExpressionDisplayNode['tokens']> {
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
  if (operand.kind === 'Context')
    return [
      {
        text: 'Field value',
        referenceKind: 'Context',
        referenceKey: operand.reference,
      },
    ];
  if (operand.kind === 'Parameter')
    return [
      {
        text: operand.reference,
        referenceKind: 'Parameter',
        referenceKey: operand.reference,
      },
    ];
  return [
    {
      text: (operand.literal?.values ?? []).join(', '),
      referenceKind: 'Literal',
      referenceKey: operand.literal?.type,
      isCode: true,
    },
  ];
}

describe('RulesPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows a scalable system and workspace catalog without field-only noise', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(ruleDefinitions));

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });

    expect(screen.getByRole('region', { name: 'Rules catalog' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Rules' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'New rule' })).toBeInTheDocument();

    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    const toolbarActions = catalog.querySelector('[data-slot="data-table-toolbar-actions"]');
    expect(toolbarActions).not.toBeNull();
    expect(within(toolbarActions as HTMLElement).getByRole('button', { name: 'New rule' })).toBe(
      screen.getByRole('button', { name: 'New rule' }),
    );
    expect(
      within(catalog).queryByRole('columnheader', { name: /Actions/ }),
    ).not.toBeInTheDocument();
    expect(
      await within(catalog).findByRole('button', { name: 'Credit threshold' }),
    ).toBeInTheDocument();
    const catalogViewport = catalog.querySelector('[data-slot="data-table-viewport"]');
    const catalogHeader = within(catalog).getByRole('columnheader', { name: /Rule/ });
    expect(catalogViewport).not.toBeNull();
    expect(catalog).toContainElement(catalogHeader);
    const requiredRow = within(catalog).getByText('Required value').closest('tr');
    if (!requiredRow) throw new Error('Required rule row was not rendered');
    expect(catalogViewport).toContainElement(requiredRow);
    expect(
      within(requiredRow).getByText('Require records to provide a value for the field.'),
    ).toHaveClass('text-xs');
    expect(requiredRow.querySelectorAll('[data-slot="rule-table-value"]')).toHaveLength(5);
    expect(within(requiredRow).getByText('Built-in')).toHaveClass('bg-info/10', 'text-info');
    expect(within(requiredRow).getByText('Published')).toHaveClass('text-success');
    expect(within(requiredRow).getByText(/Date and time/)).toBeInTheDocument();
    expect(within(requiredRow).getByText('Field')).toBeInTheDocument();
    expect(within(catalog).getByRole('columnheader', { name: /Origin/ })).toBeInTheDocument();
    expect(within(catalog).getByRole('columnheader', { name: /Status/ })).toBeInTheDocument();
    expect(within(catalog).getByText('Decimal precision')).toBeInTheDocument();
    expect(within(catalog).getByText('Date and time range')).toBeInTheDocument();
    expect(within(catalog).getByText('Text format')).toBeInTheDocument();
    expect(within(catalog).getByText('Choice selection count')).toBeInTheDocument();
    expect(within(catalog).getByText('Credit threshold')).toBeInTheDocument();
    const workspaceRow = within(catalog).getByText('Credit threshold').closest('tr');
    if (!workspaceRow) throw new Error('Workspace rule row was not rendered');
    expect(within(workspaceRow).getByText('Workspace')).toHaveClass(
      'bg-primary/10',
      'text-primary',
    );
    expect(within(catalog).getByText('Draft')).toHaveAttribute('data-variant', 'secondary');
    expect(within(catalog).queryByText('Validation')).not.toBeInTheDocument();
    expect(within(catalog).queryByText('field.required')).not.toBeInTheDocument();
    expect(within(catalog).queryByText(/Single-select options/)).not.toBeInTheDocument();

    const user = userEvent.setup();
    expect(within(catalog).queryByRole('button', { name: 'Filters' })).not.toBeInTheDocument();
    await user.type(within(catalog).getByRole('textbox', { name: 'Search rules' }), 'numeric');

    await waitFor(() =>
      expect(
        vi.mocked(fetch).mock.calls.some(([input]) => input.toString().includes('query=numeric')),
      ).toBe(true),
    );
  });

  it('opens details from Rule column links for system and workspace records', async () => {
    const user = userEvent.setup();
    const workspaceDetail = {
      ...ruleDefinitions.items[9],
      condition: null,
      outcome: null,
      versions: [],
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      archivedAt: null,
    };
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.includes('/rules/context-schemas')) {
        return Promise.resolve(jsonResponse(contextSchemas));
      }
      if (url.endsWith('/rules/credit_threshold')) {
        return Promise.resolve(jsonResponse(workspaceDetail));
      }
      if (url.endsWith('/rules/field.required')) {
        return Promise.resolve(jsonResponse(systemDetail('field.required')));
      }
      if (url.endsWith('/rules/expression-language')) {
        return Promise.resolve(jsonResponse(expressionLanguage));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });

    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    const systemRuleLink = await within(catalog).findByRole('button', {
      name: 'Required value',
    });
    const workspaceRuleLink = within(catalog).getByRole('button', {
      name: 'Credit threshold',
    });
    expect(systemRuleLink).toHaveClass('h-auto', 'p-0');
    expect(workspaceRuleLink).toHaveClass('h-auto', 'p-0');

    await user.click(systemRuleLink);
    const systemDetails = await screen.findByRole('dialog', { name: 'Required value' });
    const dialogWindow = systemDetails.querySelector('[data-slot="managed-dialog-window"]');
    expect(dialogWindow).toHaveAttribute('data-dialog-preset', 'windowed');
    expect(within(systemDetails).getByRole('button', { name: 'Reset dialog' })).toBeEnabled();
    const minimizeButton = within(systemDetails).getByRole('button', {
      name: 'Minimize dialog',
    });
    expect(minimizeButton).toBeEnabled();
    expect(minimizeButton.querySelector('svg')).toHaveClass('lucide-minus');
    expect(within(systemDetails).getByRole('button', { name: 'Maximize dialog' })).toBeEnabled();
    expect(within(systemDetails).getByRole('button', { name: 'Close dialog' })).toBeEnabled();
    const systemDetailsFooter = systemDetails.querySelector('[data-slot="managed-dialog-footer"]');
    expect(systemDetailsFooter).not.toBeNull();
    expect(
      within(systemDetailsFooter as HTMLElement).getByRole('button', { name: 'Close' }),
    ).toBeEnabled();
    expect(
      within(systemDetailsFooter as HTMLElement).queryByRole('button', { name: 'Cancel' }),
    ).not.toBeInTheDocument();

    const managedHeader = systemDetails.querySelector('[data-slot="managed-dialog-header"]');
    expect(managedHeader).not.toBeNull();
    await user.dblClick(managedHeader as HTMLElement);
    expect(dialogWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
    expect(
      within(systemDetails).getByRole('button', { name: 'Restore dialog size' }),
    ).toBeEnabled();
    await user.dblClick(managedHeader as HTMLElement);
    expect(dialogWindow).toHaveAttribute('data-dialog-preset', 'windowed');

    const maximizeButton = within(systemDetails).getByRole('button', {
      name: 'Maximize dialog',
    });
    await user.dblClick(maximizeButton);
    expect(dialogWindow).toHaveAttribute('data-dialog-preset', 'windowed');

    await user.click(minimizeButton);
    const dock = document.querySelector('[data-slot="managed-window-dock"]');
    expect(dock).not.toBeNull();
    expect(dock).toHaveAttribute('data-dialog-preset', 'windowed');
    expect(screen.queryByRole('dialog', { name: 'Required value' })).not.toBeInTheDocument();
    const restoreWindowedButton = within(dock as HTMLElement).getByRole('button', {
      name: 'Restore dialog',
    });
    expect(dock?.querySelector('[data-action="restore"]')).toHaveFocus();

    await user.keyboard('{Escape}');
    expect(document.querySelector('[data-slot="managed-window-dock"]')).toBeInTheDocument();
    expect(within(catalog).getByRole('textbox', { name: 'Search rules' })).toBeEnabled();

    await user.click(restoreWindowedButton);
    const restoredWindowed = await screen.findByRole('dialog', { name: 'Required value' });
    const restoredWindowedWindow = restoredWindowed.querySelector(
      '[data-slot="managed-dialog-window"]',
    );
    expect(restoredWindowedWindow).toHaveAttribute('data-dialog-preset', 'windowed');
    expect(minimizeButton).toHaveFocus();

    await user.click(within(restoredWindowed).getByRole('button', { name: 'Maximize dialog' }));
    expect(restoredWindowedWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
    await user.click(within(restoredWindowed).getByRole('button', { name: 'Minimize dialog' }));
    const fullscreenDock = document.querySelector('[data-slot="managed-window-dock"]');
    expect(fullscreenDock).toHaveAttribute('data-dialog-preset', 'fullscreen');
    await user.click(
      within(fullscreenDock as HTMLElement).getByRole('button', { name: 'Restore dialog' }),
    );
    const restoredFullscreen = await screen.findByRole('dialog', { name: 'Required value' });
    expect(restoredFullscreen.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
      'data-dialog-preset',
      'fullscreen',
    );
    const restoreSizeButton = within(restoredFullscreen).getByRole('button', {
      name: 'Restore dialog size',
    });
    expect(restoreSizeButton).toBeEnabled();
    await user.click(restoreSizeButton);
    expect(restoredFullscreen.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
      'data-dialog-preset',
      'windowed',
    );
    expect(
      within(restoredFullscreen).getByRole('button', { name: 'Maximize dialog' }),
    ).toBeEnabled();
    expect(
      within(restoredFullscreen).getByRole('heading', {
        name: 'What this rule does',
      }),
    ).toBeInTheDocument();
    expect(
      within(restoredFullscreen).getByRole('heading', {
        name: 'Where this rule applies',
      }),
    ).toBeInTheDocument();
    const restoredHeader = restoredFullscreen.querySelector('[data-slot="managed-dialog-header"]');
    expect(restoredHeader).not.toBeNull();
    expect(
      Array.from(restoredHeader?.querySelectorAll('[data-slot="badge"]') ?? [], (badge) =>
        badge.textContent?.trim(),
      ),
    ).toEqual(['Built-in', 'Published']);
    expect(
      within(systemDetails).queryByRole('button', { name: 'Archive' }),
    ).not.toBeInTheDocument();
    expect(
      vi
        .mocked(fetch)
        .mock.calls.some(([input]) => input.toString().endsWith('/rules/field.required')),
    ).toBe(true);

    await user.click(
      within(systemDetailsFooter as HTMLElement).getByRole('button', { name: 'Close' }),
    );
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await user.click(workspaceRuleLink);
    const workspaceDetails = await screen.findByRole('dialog', { name: 'Credit threshold' });
    expect(workspaceDetails.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
      'data-dialog-preset',
      'windowed',
    );
    expect(within(workspaceDetails).queryByText('Unsaved changes')).not.toBeInTheDocument();
    expect(within(workspaceDetails).getByRole('button', { name: 'Maximize dialog' })).toBeEnabled();
    await waitFor(() =>
      expect(
        vi
          .mocked(fetch)
          .mock.calls.some(([input]) => input.toString().endsWith('/rules/credit_threshold')),
      ).toBe(true),
    );
    const workspaceFooter = workspaceDetails.querySelector('[data-slot="managed-dialog-footer"]');
    expect(workspaceFooter).not.toBeNull();
    expect(
      within(workspaceFooter as HTMLElement).getByRole('button', { name: 'Cancel' }),
    ).toBeEnabled();
    expect(
      within(workspaceFooter as HTMLElement).queryByRole('button', { name: 'Close' }),
    ).not.toBeInTheDocument();
  });

  it('renders system rule details with a scannable business-first hierarchy', async () => {
    const user = userEvent.setup();
    const detail = systemDetail('field.required');
    detail.applicability = {
      ...detail.applicability,
      targetTypeKeys: ['Choice', 'Text', 'Boolean'],
    };
    detail.parameters = [{ key: 'format', type: 'Text', isRequired: true }];
    detail.condition = {
      nodeId: 'root',
      logicalOperator: 'Any',
      children: [
        {
          nodeId: 'required_group',
          logicalOperator: 'All',
          children: [detail.condition],
        },
        {
          nodeId: 'not_empty_group',
          logicalOperator: 'Not',
          children: [
            {
              nodeId: 'empty_group',
              logicalOperator: 'All',
              children: [
                {
                  nodeId: 'empty_check',
                  predicateOperator: 'IsNull',
                  left: { kind: 'Context', reference: 'field.value', arguments: [] },
                  children: [],
                },
              ],
            },
          ],
        },
      ],
    };
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.endsWith('/rules/expression-language/guide'))
        return Promise.resolve(expressionGuideResponse(init));
      if (url.endsWith('/rules/field.required')) {
        return Promise.resolve(jsonResponse(detail));
      }
      if (url.endsWith('/rules/expression-language')) {
        return Promise.resolve(jsonResponse(expressionLanguage));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });

    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    await user.click(await within(catalog).findByRole('button', { name: 'Required value' }));
    const details = await screen.findByRole('dialog', { name: 'Required value' });
    const header = details.querySelector('[data-slot="managed-dialog-header"]');
    expect(header).not.toBeNull();
    expect(
      within(header as HTMLElement).queryByText(
        'Require records to provide a value for the field.',
      ),
    ).not.toBeInTheDocument();
    const headerBadges = header?.querySelectorAll('[data-slot="badge"]') ?? [];
    expect(Array.from(headerBadges, (badge) => badge.textContent?.trim())).toEqual([
      'Built-in',
      'Published',
    ]);
    expect(headerBadges[0]).toHaveAttribute('data-variant', 'outline');
    expect(headerBadges[0]).toHaveClass('bg-info/10', 'text-info');
    expect(within(details).queryByText('Read-only')).not.toBeInTheDocument();

    const summary = within(details).getByText('Require records to provide a value for the field.');
    expect(summary).toHaveAttribute('data-slot', 'system-rule-summary');

    const behaviorSection = within(details).getByRole('region', {
      name: 'What this rule does',
    });
    const applicabilitySection = within(details).getByRole('region', {
      name: 'Where this rule applies',
    });
    const parametersSection = within(details).getByRole('region', { name: 'Parameters' });

    expect(behaviorSection).toHaveTextContent('When');
    expect(behaviorSection).toHaveTextContent('Field value is blank');
    expect(behaviorSection).not.toHaveTextContent('Expression syntax');
    expect(behaviorSection).toHaveTextContent('Then');
    expect(within(behaviorSection).getByText('A value is required.')).toHaveAttribute(
      'data-slot',
      'system-rule-outcome',
    );
    expect(within(behaviorSection).getByText('Effect:')).toBeVisible();
    expect(within(behaviorSection).getByText('Blocks the action')).toBeVisible();
    expect(behaviorSection.querySelectorAll('[data-slot="badge"]')).toHaveLength(0);
    expect(behaviorSection).not.toHaveTextContent('Validation');
    expect(behaviorSection).not.toHaveTextContent('Severity');
    expect(behaviorSection).not.toHaveTextContent('otherwise');
    expect(behaviorSection.querySelector('[data-slot="system-rule-behavior-flow"]')).toHaveClass(
      'space-y-0',
    );
    expect(behaviorSection.querySelectorAll('[data-slot="rule-timeline-item"]')).toHaveLength(2);
    expect(behaviorSection.querySelectorAll('[data-slot="rule-timeline-marker"]')).toHaveLength(2);
    expect(behaviorSection.querySelector('[data-slot="rule-timeline-line"]')).toBeInTheDocument();
    expect(behaviorSection.querySelectorAll('[data-slot="rule-condition-group"]')).toHaveLength(4);
    expect(
      behaviorSection.querySelector('[data-slot="rule-condition-group"][data-operator="Any"]'),
    ).toBeInTheDocument();
    expect(
      behaviorSection.querySelector('[data-slot="rule-condition-group"][data-operator="All"]'),
    ).toBeInTheDocument();
    expect(
      behaviorSection.querySelector('[data-slot="rule-condition-group"][data-operator="Not"]'),
    ).toBeInTheDocument();
    expect(
      behaviorSection.querySelectorAll('[data-slot="rule-condition-serial-rail"]'),
    ).toHaveLength(2);
    expect(
      behaviorSection.querySelectorAll('[data-slot="rule-condition-parallel-rail"]'),
    ).toHaveLength(2);
    expect(behaviorSection.querySelectorAll('[data-slot="rule-condition-inversion"]')).toHaveLength(
      1,
    );
    expect(within(behaviorSection).getByRole('button', { name: 'or' })).toBeVisible();
    expect(within(behaviorSection).getAllByRole('button', { name: 'and' })).toHaveLength(2);
    expect(within(behaviorSection).getByRole('button', { name: 'not' })).toBeVisible();
    expect(within(behaviorSection).queryByText(/^(and|or|not)$/i)).not.toBeInTheDocument();
    expect(behaviorSection).not.toHaveTextContent('Any');
    expect(behaviorSection).not.toHaveTextContent('All');
    expect(
      within(behaviorSection).queryByRole('button', { name: 'Expression guide' }),
    ).not.toBeInTheDocument();
    expect(
      vi
        .mocked(fetch)
        .mock.calls.some(([input]) =>
          input.toString().endsWith('/rules/expression-language/guide'),
        ),
    ).toBe(false);

    const keyword = within(behaviorSection).getByRole('button', { name: 'is blank' });
    expect(keyword).toHaveClass('underline', 'decoration-dotted');
    expect(keyword.querySelector('svg')).not.toBeInTheDocument();
    expect(
      within(behaviorSection)
        .getAllByRole('button', { name: 'Field value' })[0]
        .querySelector('span'),
    ).toHaveClass('font-mono', 'text-xs');
    await user.click(keyword);
    const reference = await screen.findByRole('dialog', { name: 'Rule expression guide' });
    expect(within(reference).getByRole('heading', { name: 'Functions' })).toBeVisible();
    await waitFor(() =>
      expect(document.activeElement).toHaveAttribute(
        'id',
        'rule-expression-guide-item-Function-IsBlank',
      ),
    );
    const selectedReference = document.getElementById(
      'rule-expression-guide-item-Function-IsBlank',
    );
    expect(selectedReference).toBeVisible();
    expect(selectedReference).toHaveAttribute('aria-current', 'true');
    expect(
      within(selectedReference as HTMLElement).getByRole('heading', { name: 'is blank' }),
    ).toBeVisible();
    expect(
      within(selectedReference as HTMLElement).queryByText('Used here'),
    ).not.toBeInTheDocument();
    expect(
      within(selectedReference as HTMLElement).queryByText(
        'IsBlank(@context.field.value) Equal Boolean("true")',
      ),
    ).not.toBeInTheDocument();
    expect(within(selectedReference as HTMLElement).getByText('How to use')).toBeVisible();
    expect(
      within(selectedReference as HTMLElement).queryByText('Reference'),
    ).not.toBeInTheDocument();
    expect(
      within(selectedReference as HTMLElement).queryByText('Examples'),
    ).not.toBeInTheDocument();
    expect(within(reference).queryByRole('button', { name: 'Insert' })).not.toBeInTheDocument();
    const search = within(reference).getByRole('searchbox', { name: 'Search expression guide' });
    await user.type(search, 'blnk');
    await waitFor(() =>
      expect(
        document
          .getElementById('rule-expression-guide-item-Function-IsBlank')
          ?.querySelector('mark'),
      ).toHaveTextContent('blank'),
    );
    expect(within(reference).getByText('Matches: 1 · “blnk”')).toBeVisible();
    expect(reference.querySelector('mark')).toHaveClass('bg-primary', 'text-primary-foreground');
    expect(
      vi
        .mocked(fetch)
        .mock.calls.some(([input]) =>
          input.toString().endsWith('/rules/expression-language/guide'),
        ),
    ).toBe(true);
    await user.click(within(reference).getByRole('button', { name: 'Close' }));
    await user.click(within(behaviorSection).getAllByRole('button', { name: 'Field value' })[0]);
    const contextDocument = await screen.findByRole('dialog', { name: 'Rule expression guide' });
    await waitFor(() =>
      expect(document.activeElement).toHaveAttribute(
        'id',
        'rule-expression-guide-item-Context-field.value',
      ),
    );
    const selectedContextReference = document.getElementById(
      'rule-expression-guide-item-Context-field.value',
    );
    expect(selectedContextReference).not.toBeNull();
    expect(
      within(selectedContextReference as HTMLElement).queryByText('@context.field.value'),
    ).not.toBeInTheDocument();
    expect(
      within(selectedContextReference as HTMLElement).queryByText('Examples'),
    ).not.toBeInTheDocument();
    await user.click(within(contextDocument).getByRole('button', { name: 'Close' }));
    const orConnector = within(behaviorSection).getByRole('button', { name: 'or' });
    await user.click(orConnector);
    const operatorDocument = await screen.findByRole('dialog', { name: 'Rule expression guide' });
    const selectedOperatorReference = document.getElementById(
      'rule-expression-guide-item-LogicalOperator-Any',
    );
    expect(selectedOperatorReference).toBeVisible();
    expect(
      within(selectedOperatorReference as HTMLElement).getByRole('heading', { name: 'or' }),
    ).toBeVisible();
    await user.click(within(operatorDocument).getByRole('button', { name: 'Close' }));

    expect(applicabilitySection).toHaveTextContent('Applies to a single field value.');
    expect(applicabilitySection).toHaveTextContent('Supported field types');
    expect(
      Array.from(applicabilitySection.querySelectorAll('[data-slot="badge"]'), (badge) =>
        badge.textContent?.trim(),
      ),
    ).toEqual(['Text', 'Boolean', 'Choice']);
    expect(applicabilitySection).toHaveTextContent('Ready to use—no setup required.');
    expect(
      Array.from(parametersSection.querySelectorAll('[data-slot="badge"]'), (badge) =>
        badge.textContent?.trim(),
      ),
    ).toEqual(['Text', 'Required']);
    expect(parametersSection).not.toHaveTextContent('Text · Required');

    const detailsRoot = details.querySelector('[data-slot="system-rule-details"]');
    expect(detailsRoot).toHaveClass('@container/system-rule-details');
    expect(
      applicabilitySection.querySelector('[data-slot="system-rule-applicability-grid"]'),
    ).toHaveClass('grid', '@md/system-rule-details:grid-cols-2');
    expect(detailsRoot?.querySelector('.sm\\:grid-cols-3')).not.toBeInTheDocument();
    expect(detailsRoot?.querySelector('.xl\\:grid-cols-3')).not.toBeInTheDocument();

    expect(within(details).getByRole('heading', { name: 'Version and references' })).toBeVisible();
    expect(
      within(details).queryByRole('button', { name: /Technical details/ }),
    ).not.toBeInTheDocument();
    expect(within(details).queryByText('Outcome')).not.toBeInTheDocument();
    expect(within(details).queryByText('Severity')).not.toBeInTheDocument();
    expect(within(details).getByText('Published version')).toBeVisible();
    expect(within(details).getByText('Expression language')).toBeVisible();
    expect(within(details).getByText('Violation code')).toBeVisible();
    expect(within(details).getByText('field.value.required')).toBeVisible();
  });

  it('does not reconstruct natural language when the server projection is missing', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation(async (input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist')) {
        const response = expressionAssistResponse(init);
        const payload = await response.json();
        return jsonResponse({ ...payload, display: null });
      }
      if (url.endsWith('/rules/field.required')) {
        return jsonResponse(systemDetail('field.required'));
      }
      if (url.endsWith('/rules/expression-language')) {
        return jsonResponse(expressionLanguage);
      }
      return jsonResponse(ruleDefinitions);
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });

    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    await user.click(await within(catalog).findByRole('button', { name: 'Required value' }));
    const details = await screen.findByRole('dialog', { name: 'Required value' });
    const behavior = details.querySelector('[data-slot="system-rule-behavior"]');
    expect(behavior).not.toBeNull();
    expect(await within(behavior as HTMLElement).findByRole('alert')).toHaveTextContent(
      'Unable to load rules',
    );
    expect(behavior).not.toHaveTextContent('@context.');
    expect(behavior).not.toHaveTextContent('IsBlank');
  });

  it('shows a published workspace rule as semantic read-only details', async () => {
    const user = userEvent.setup();
    const detail = workspaceRuleDetail({
      status: 'Published',
      latestPublishedVersion: 1,
      revision: 3,
      parameters: [
        {
          key: 'threshold',
          type: 'Decimal',
          isRequired: true,
          allowMultiple: false,
          allowedValues: [],
        },
      ],
      condition: {
        nodeId: 'credit_threshold_check',
        predicateOperator: 'GreaterThan',
        left: { kind: 'Context', reference: 'field.value', arguments: [] },
        right: { kind: 'Parameter', reference: 'threshold', arguments: [] },
        children: [],
      },
      versions: [{ version: 1, publishedAt: '2026-01-02T00:00:00Z' }],
    });
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.includes('/rules/context-schemas')) {
        return Promise.resolve(jsonResponse(contextSchemas));
      }
      if (url.endsWith('/rules/expression-language')) {
        return Promise.resolve(jsonResponse(expressionLanguage));
      }
      if (url.endsWith('/rules/credit_threshold')) {
        return Promise.resolve(jsonResponse(detail));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    await user.click(await within(catalog).findByRole('button', { name: 'Credit threshold' }));

    const dialog = await screen.findByRole('dialog', { name: 'Credit threshold' });
    const header = dialog.querySelector('[data-slot="managed-dialog-header"]');
    expect(header).not.toBeNull();
    expect(
      Array.from(header?.querySelectorAll('[data-slot="badge"]') ?? [], (badge) =>
        badge.textContent?.trim(),
      ),
    ).toEqual(['Workspace', 'Published']);

    const details = dialog.querySelector('[data-slot="workspace-rule-details"]');
    expect(details).not.toBeNull();
    expect(within(details as HTMLElement).getByText('When')).toBeVisible();
    expect(within(details as HTMLElement).getByText('Then')).toBeVisible();
    expect(
      within(details as HTMLElement).getByText('Value exceeds the credit threshold.'),
    ).toBeVisible();
    expect(within(details as HTMLElement).getByText('Blocks the action')).toBeVisible();
    expect(within(details as HTMLElement).getByText('Decimal field value')).toBeVisible();
    const parameterToken = within(details as HTMLElement).getByRole('button', {
      name: 'threshold',
    });
    expect(parameterToken.querySelector('span')).toHaveClass('font-mono', 'text-xs');
    expect(
      within(within(details as HTMLElement).getByRole('region', { name: 'Parameters' })).getByText(
        'threshold',
      ),
    ).toBeVisible();
    expect(within(details as HTMLElement).getAllByText('Version 1')).toHaveLength(3);
    const immutable = within(details as HTMLElement).getByText('Immutable');
    expect(immutable).not.toHaveAttribute('data-slot', 'badge');
    expect(
      within(dialog).queryByRole('button', { name: 'Expression guide' }),
    ).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('combobox')).not.toBeInTheDocument();
    expect(
      vi
        .mocked(fetch)
        .mock.calls.some(([input]) => input.toString().endsWith('/rules/expression-language')),
    ).toBe(false);

    const footer = dialog.querySelector('[data-slot="managed-dialog-footer"]');
    expect(footer).not.toBeNull();
    expect(
      within(footer as HTMLElement).getByRole('button', { name: 'Start revision' }),
    ).toBeEnabled();
    expect(within(footer as HTMLElement).getByRole('button', { name: 'Archive' })).toBeEnabled();
    expect(within(footer as HTMLElement).getByRole('button', { name: 'Close' })).toBeEnabled();
  });

  it('opens a contextual phrase without mixing sibling condition details', async () => {
    const user = userEvent.setup();
    const maximum = { kind: 'Parameter' as const, reference: 'max', arguments: [] };
    const detail = workspaceRuleDetail({
      status: 'Published',
      latestPublishedVersion: 1,
      revision: 3,
      parameters: [
        {
          key: 'max',
          type: 'Decimal',
          isRequired: false,
          allowMultiple: false,
          allowedValues: [],
        },
      ],
      condition: {
        nodeId: 'maximum',
        logicalOperator: 'All',
        children: [
          {
            nodeId: 'maximum-set',
            predicateOperator: 'IsNotNull',
            left: maximum,
            right: null,
            children: [],
          },
          {
            nodeId: 'above-maximum',
            predicateOperator: 'GreaterThan',
            left: { kind: 'Context', reference: 'field.value', arguments: [] },
            right: maximum,
            children: [],
          },
        ],
      },
      versions: [{ version: 1, publishedAt: '2026-01-02T00:00:00Z' }],
    });
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.includes('/rules/expression-language/guide')) {
        return Promise.resolve(expressionGuideResponse(init));
      }
      if (url.includes('/rules/context-schemas')) {
        return Promise.resolve(jsonResponse(contextSchemas));
      }
      if (url.endsWith('/rules/credit_threshold')) {
        return Promise.resolve(jsonResponse(detail));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    await user.click(await within(catalog).findByRole('button', { name: 'Credit threshold' }));
    const dialog = await screen.findByRole('dialog', { name: 'Credit threshold' });
    await user.click(within(dialog).getByRole('button', { name: 'is provided' }));

    const guide = await screen.findByRole('dialog', { name: 'Rule expression guide' });
    const selected = document.getElementById(
      'rule-expression-guide-item-PredicateOperator-IsNotNull',
    );
    expect(selected).toBeVisible();
    expect(
      within(selected as HTMLElement).getByRole('heading', { name: 'is provided' }),
    ).toBeVisible();
    expect(within(selected as HTMLElement).queryByText('Used here')).not.toBeInTheDocument();
    expect(
      within(selected as HTMLElement).queryByText('@parameters.max IsNotNull'),
    ).not.toBeInTheDocument();
    expect(within(selected as HTMLElement).getByText('How to use')).toBeVisible();
    expect(within(selected as HTMLElement).queryByText('Reference')).not.toBeInTheDocument();
    expect(within(selected as HTMLElement).queryByText('Is not empty')).not.toBeInTheDocument();
    expect(guide).not.toHaveTextContent(
      'All(@parameters.max IsNotNull, @context.field.value GreaterThan @parameters.max)',
    );
  });

  it('reviews rule behavior and impact before publishing', async () => {
    const user = userEvent.setup();
    const detail = workspaceRuleDetail();
    const saved = { ...detail, revision: 3 };
    const published = {
      ...saved,
      status: 'Published',
      latestPublishedVersion: 1,
      versions: [{ version: 1, publishedAt: '2026-01-02T00:00:00Z' }],
    };
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.includes('/rules/context-schemas')) {
        return Promise.resolve(jsonResponse(contextSchemas));
      }
      if (url.endsWith('/rules/expression-language')) {
        return Promise.resolve(jsonResponse(expressionLanguage));
      }
      if (url.endsWith('/rules/credit_threshold/draft') && init?.method === 'PUT') {
        return Promise.resolve(jsonResponse(saved));
      }
      if (url.endsWith('/rules/credit_threshold/publish') && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(published));
      }
      if (url.endsWith('/rules/credit_threshold')) {
        return Promise.resolve(jsonResponse(detail));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    await user.click(await within(catalog).findByRole('button', { name: 'Credit threshold' }));
    const editor = await screen.findByRole('dialog', { name: 'Credit threshold' });
    await user.click(await within(editor).findByRole('button', { name: 'Publish version' }));

    const review = await screen.findByRole('alertdialog', { name: 'Publish this rule?' });
    expect(review).toHaveTextContent('Version 1 will be immutable.');
    expect(review).toHaveTextContent('Field');
    expect(review).toHaveTextContent('Decimal field value');
    expect(review).toHaveTextContent('When');
    expect(review).toHaveTextContent('Then');
    expect(review).toHaveTextContent('Value exceeds the credit threshold.');
    expect(review).toHaveTextContent('Blocks the action');
    expect(
      vi
        .mocked(fetch)
        .mock.calls.some(
          ([input, init]) =>
            input.toString().endsWith('/rules/credit_threshold/publish') && init?.method === 'POST',
        ),
    ).toBe(false);

    await user.click(within(review).getByRole('button', { name: 'Publish version' }));
    await waitFor(() =>
      expect(
        vi
          .mocked(fetch)
          .mock.calls.some(
            ([input, init]) =>
              input.toString().endsWith('/rules/credit_threshold/publish') &&
              init?.method === 'POST',
          ),
      ).toBe(true),
    );
  });

  it.each([
    ['Info', 'Provides information without blocking'],
    ['Warning', 'Shows a warning without blocking'],
  ])('describes %s impact in user terms', async (severity, effect) => {
    const user = userEvent.setup();
    const detail = systemDetail('field.required');
    detail.applicability = { ...detail.applicability, targetTypeKeys: [] };
    detail.outcome = { ...detail.outcome, severity };
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.endsWith('/rules/field.required')) {
        return Promise.resolve(jsonResponse(detail));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });

    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    await user.click(await within(catalog).findByRole('button', { name: 'Required value' }));
    const details = await screen.findByRole('dialog', { name: 'Required value' });
    const behaviorSection = within(details).getByRole('region', {
      name: 'What this rule does',
    });
    expect(within(behaviorSection).getByText(effect)).toBeVisible();
    expect(within(details).getByText('Field type applicability unavailable')).toBeVisible();
    expect(within(details).queryByText('Context unavailable')).not.toBeInTheDocument();
  });

  it('creates a workspace draft from a registered context', async () => {
    const user = userEvent.setup();
    const created = {
      definitionKey: 'high_credit_value',
      name: 'High credit value',
      description: 'Flags high credit values.',
      origin: 'Workspace',
      scope: 'Field',
      outcomeKind: 'Validation',
      status: 'Draft',
      revision: 1,
      contextKey: 'business_objects.field.decimal',
      contextSchemaVersion: 1,
      parameters: [],
      versions: [],
    };
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.includes('/rules/context-schemas'))
        return Promise.resolve(jsonResponse(contextSchemas));
      if (url.endsWith('/rules/expression-language'))
        return Promise.resolve(jsonResponse(expressionLanguage));
      if (url.endsWith('/rules') && init?.method === 'POST')
        return Promise.resolve(jsonResponse(created, 201));
      if (url.endsWith('/rules/high_credit_value')) return Promise.resolve(jsonResponse(created));
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(await screen.findByRole('button', { name: 'New rule' }));
    const createDialog = await screen.findByRole('dialog', { name: 'New workspace rule' });
    expect(createDialog.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
      'data-dialog-preset',
      'windowed',
    );
    const createFooter = createDialog.querySelector('[data-slot="managed-dialog-footer"]');
    expect(createFooter).not.toBeNull();
    expect(
      within(createFooter as HTMLElement).getByRole('button', { name: 'Cancel' }),
    ).toBeEnabled();
    expect(
      within(createFooter as HTMLElement).queryByRole('button', { name: 'Close' }),
    ).not.toBeInTheDocument();
    expect(within(createDialog).getByRole('button', { name: 'Reset dialog' })).toBeEnabled();
    expect(within(createDialog).getByRole('heading', { name: 'Definition' })).toBeInTheDocument();
    expect(
      within(createDialog).queryByRole('heading', { name: 'Parameters' }),
    ).not.toBeInTheDocument();
    expect(
      within(createDialog).queryByRole('heading', { name: 'When this rule matches' }),
    ).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('Name'), 'High credit value');
    await user.type(screen.getByLabelText('Description'), 'Flags high credit values.');
    await waitFor(() =>
      expect(
        vi
          .mocked(fetch)
          .mock.calls.some(([input]) => input.toString().includes('/rules/context-schemas')),
      ).toBe(true),
    );
    await waitFor(() => expect(screen.getByLabelText('Context')).toBeEnabled());
    expect(screen.getByLabelText('Scope')).toHaveTextContent('Field');
    expect(screen.getByText('Applies to a single field value.')).toBeInTheDocument();
    await user.click(screen.getByLabelText('Scope'));
    await user.click(await screen.findByRole('option', { name: 'Field' }));
    expect(screen.getByLabelText('Scope')).toHaveTextContent('Field');
    expect(screen.queryByRole('option', { name: 'Object' })).not.toBeInTheDocument();
    await user.click(screen.getByLabelText('Context'));
    await user.click(await screen.findByRole('option', { name: 'Decimal field value' }));
    expect(screen.getByLabelText('Context')).toHaveTextContent('Decimal field value');
    await user.click(screen.getByRole('button', { name: 'Create draft' }));

    const editorDialog = await screen.findByRole('dialog', { name: 'High credit value' });
    expect(within(editorDialog).getByRole('heading', { name: 'Definition' })).toBeInTheDocument();
    expect(within(editorDialog).getByLabelText('Name')).toHaveValue('High credit value');
    expect(within(editorDialog).getByText('Stable key: high_credit_value')).toBeInTheDocument();
    expect(within(editorDialog).getByRole('heading', { name: 'Parameters' })).toBeInTheDocument();
    expect(
      within(editorDialog).getByRole('heading', { name: 'When this rule matches' }),
    ).toBeInTheDocument();
    expect(within(editorDialog).getByRole('heading', { name: 'Simulation' })).toBeInTheDocument();
    const post = vi
      .mocked(fetch)
      .mock.calls.find(
        ([input, init]) => input.toString().endsWith('/api/rules') && init?.method === 'POST',
      );
    expect(post).toBeDefined();
    expect(JSON.parse(post?.[1]?.body as string)).toMatchObject({
      name: 'High credit value',
      scope: 'Field',
      contextKey: 'business_objects.field.decimal',
      contextSchemaVersion: 1,
      outcomeKind: 'Validation',
    });
  });

  it('keeps workspace draft edits when dialog close is cancelled', async () => {
    const user = userEvent.setup();
    const workspaceDetail = {
      ...ruleDefinitions.items[9],
      expressionLanguageVersion: 1,
      condition: null,
      outcome: null,
      versions: [],
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      archivedAt: null,
    };
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.includes('/rules/context-schemas')) {
        return Promise.resolve(jsonResponse(contextSchemas));
      }
      if (url.endsWith('/rules/expression-language')) {
        return Promise.resolve(jsonResponse(expressionLanguage));
      }
      if (url.endsWith('/rules/credit_threshold')) {
        return Promise.resolve(jsonResponse(workspaceDetail));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(await screen.findByRole('button', { name: 'Credit threshold' }));
    const editorDialog = await screen.findByRole('dialog', { name: 'Credit threshold' });
    const editorName = within(editorDialog).getByLabelText('Name');
    await user.clear(editorName);
    await user.type(editorName, 'Updated credit value');
    await user.click(within(editorDialog).getByRole('button', { name: 'Close dialog' }));

    expect(screen.getByRole('heading', { name: 'Discard unsaved changes?' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Keep editing' }));
    expect(within(editorDialog).getByLabelText('Name')).toHaveValue('Updated credit value');
  });

  it('authors syntax with server guidance and saves the expression contract', async () => {
    const user = userEvent.setup();
    const textSchema = {
      contextKey: 'business_objects.field.text',
      version: 1,
      scope: 'Field',
      displayName: 'Text field value',
      fields: [
        { path: 'field.value', displayName: 'Field value', type: 'Text', allowMultiple: false },
      ],
    };
    const workspaceDetail = {
      ...ruleDefinitions.items[9],
      expressionLanguageVersion: 1,
      contextKey: textSchema.contextKey,
      contextSchemaVersion: 1,
      condition: {
        nodeId: 'root',
        logicalOperator: 'All',
        children: [
          {
            nodeId: 'predicate',
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
      outcome: {
        kind: 'Validation',
        violationCode: 'credit.threshold.exceeded',
        severity: 'Error',
        message: 'Value is invalid.',
      },
      versions: [],
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      archivedAt: null,
    };
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.endsWith('/rules/expression-language/guide'))
        return Promise.resolve(expressionGuideResponse(init));
      if (url.endsWith('/rules/context-schemas'))
        return Promise.resolve(jsonResponse([textSchema]));
      if (url.endsWith('/rules/expression-language')) {
        return Promise.resolve(jsonResponse(expressionLanguage));
      }
      if (url.endsWith('/rules/credit_threshold/draft') && init?.method === 'PUT') {
        return Promise.resolve(jsonResponse(workspaceDetail));
      }
      if (url.endsWith('/rules/credit_threshold')) {
        return Promise.resolve(jsonResponse(workspaceDetail));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    await user.click(await within(catalog).findByRole('button', { name: 'Credit threshold' }));
    const editor = await screen.findByRole('dialog', { name: 'Credit threshold' });
    const expression = (
      await within(editor).findByRole('heading', { name: 'When this rule matches' })
    ).closest('section');
    if (!expression) throw new Error('Expression section was not rendered');

    await user.click(within(expression).getByRole('button', { name: 'Expression guide' }));
    const expressionGuide = await screen.findByRole('dialog', { name: 'Rule expression guide' });
    const guideSearch = within(expressionGuide).getByRole('searchbox', {
      name: 'Search expression guide',
    });
    await user.type(guideSearch, 'lenght');
    expect((await within(expressionGuide).findAllByText('Length'))[0]).toBeVisible();
    await waitFor(() => expect(expressionGuide.querySelector('mark')).toHaveTextContent('Length'));
    await user.clear(guideSearch);
    expect(
      within(expressionGuide).queryByText('Length(Text · Scalar) → Integer · Scalar'),
    ).not.toBeInTheDocument();
    expect(within(expressionGuide).queryByText('Reference')).not.toBeInTheDocument();
    expect((await within(expressionGuide).findAllByText('Same value'))[0]).toBeVisible();
    expect(
      within(expressionGuide).getByText('Checks whether both values are the same.'),
    ).toBeVisible();
    const lengthGuideItem = document.getElementById('rule-expression-guide-item-Function-Length');
    expect(lengthGuideItem).not.toBeNull();
    expect(within(lengthGuideItem as HTMLElement).getByText('Examples')).toBeVisible();
    await user.click(within(expressionGuide).getByRole('button', { name: 'Close' }));

    const syntax = within(expression).getByLabelText('Expression syntax');
    await user.clear(syntax);
    await user.type(syntax, 'Len');
    const suggestions = await screen.findByRole('listbox', { name: 'Expression suggestions' });
    expect(within(suggestions).getByRole('option', { name: /Length/ })).toBeVisible();
    await user.keyboard('{Enter}');
    expect(syntax).toHaveValue('Length()');

    await user.clear(syntax);
    await user.type(syntax, 'Length(@context.field.value) Equal Integer("5")');
    await waitFor(() => expect(within(expression).getByText('What this means')).toBeVisible());
    await user.click(within(editor).getByRole('button', { name: 'Save draft' }));

    await waitFor(() => {
      const save = vi
        .mocked(fetch)
        .mock.calls.find(
          ([input, init]) =>
            input.toString().endsWith('/rules/credit_threshold/draft') && init?.method === 'PUT',
        );
      expect(save).toBeDefined();
      expect(JSON.parse(save?.[1]?.body as string)).toMatchObject({
        expressionSyntax: 'Length(@context.field.value) Equal Integer("5")',
      });
      expect(JSON.parse(save?.[1]?.body as string)).not.toHaveProperty('condition');
    });
  });

  it('shows an error state when rule contexts cannot load for creation', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.includes('/rules/context-schemas')) {
        return Promise.resolve(jsonResponse({ title: 'Unavailable' }, 500));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(await screen.findByRole('button', { name: 'New rule' }));

    const createDialog = await screen.findByRole('dialog', { name: 'New workspace rule' });
    expect(await within(createDialog).findByRole('alert')).toHaveTextContent(
      'Unable to load rules',
    );
    expect(within(createDialog).queryByLabelText('Name')).not.toBeInTheDocument();
  });

  it('shows an empty state when no rule context is eligible for creation', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.includes('/rules/context-schemas')) return Promise.resolve(jsonResponse([]));
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(await screen.findByRole('button', { name: 'New rule' }));

    const createDialog = await screen.findByRole('dialog', { name: 'New workspace rule' });
    expect(await within(createDialog).findByText('Context unavailable')).toBeInTheDocument();
    expect(createDialog).toHaveTextContent(
      'No consumer has registered a context for this scope yet.',
    );
    expect(within(createDialog).queryByLabelText('Name')).not.toBeInTheDocument();
  });

  it('keeps minimized record identity stable while multiple windows overlap', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/expression-language/assist'))
        return Promise.resolve(expressionAssistResponse(init));
      if (url.includes('/rules/context-schemas')) {
        return Promise.resolve(jsonResponse(contextSchemas));
      }
      if (url.endsWith('/rules/field.required')) {
        return Promise.resolve(jsonResponse(systemDetail('field.required')));
      }
      if (url.endsWith('/rules/field.numeric_range')) {
        return Promise.resolve(jsonResponse(systemDetail('field.numeric_range')));
      }
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    const catalog = screen.getByRole('region', { name: 'Rules catalog' });
    await user.click(await within(catalog).findByRole('button', { name: 'Required value' }));
    const requiredWindow = await screen.findByRole('dialog', { name: 'Required value' });
    await user.click(within(requiredWindow).getByRole('button', { name: 'Minimize dialog' }));

    const requiredDock = document.querySelector<HTMLElement>('[data-slot="managed-window-dock"]');
    expect(requiredDock).toHaveTextContent('Required value');
    await user.click(within(catalog).getByRole('button', { name: 'Numeric range' }));
    expect(await screen.findByRole('dialog', { name: 'Numeric range' })).toBeInTheDocument();
    expect(requiredDock).toHaveTextContent('Required value');
    expect(requiredDock).not.toHaveTextContent('Numeric range');

    await user.click(
      within(requiredDock as HTMLElement).getByRole('button', { name: 'Restore dialog' }),
    );
    const windowElements = document.querySelectorAll('[data-slot="managed-dialog-window"]');
    expect(windowElements).toHaveLength(2);
    expect(document.querySelector('[data-window-id="rules:field.required"]')).toHaveAttribute(
      'data-active',
      'true',
    );
    const windowsButton = screen.getByRole('button', { name: 'Windows (2)' });
    await user.click(windowsButton);
    expect(await screen.findByRole('menuitem', { name: /Required value/ })).toBeInTheDocument();
    await user.click(screen.getByRole('menuitem', { name: /Numeric range/ }));
    expect(document.querySelector('[data-window-id="rules:field.numeric_range"]')).toHaveAttribute(
      'data-active',
      'true',
    );

    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Numeric range' })).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('dialog', { name: 'Required value' })).toBeInTheDocument();
    expect(document.querySelector('[data-window-id="rules:field.required"]')).toHaveAttribute(
      'data-active',
      'true',
    );
    expect(screen.getByRole('button', { name: 'Windows (1)' })).toBeInTheDocument();
  });

  it('shows a retryable error state when the catalog cannot load', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse({ title: 'Unavailable' }, 500))
      .mockResolvedValueOnce(jsonResponse(ruleDefinitions));

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load rules');
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('region', { name: 'Rules catalog' })).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(2);
  });
});
