import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/features/auth/auth-store';
import { RulesPage } from '@/features/rules';
import { renderWithRouter } from './render-with-router';

function jsonResponse(data: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(JSON.stringify(data)),
    json: () => Promise.resolve(data),
  } as unknown as Response;
}

function documentation(displayName: string, summary: string) {
  return {
    locales: {
      en: {
        displayName,
        summary,
        usage: `Use ${displayName} in a rule condition.`,
        examples: [displayName],
      },
    },
  };
}

const systemSummary = {
  definitionKey: 'field.required',
  name: 'Required value',
  description: 'Require a value.',
  origin: 'BuiltIn',
  status: 'Active',
  latestVersion: 1,
  activeVersion: 1,
  inputs: [
    {
      key: 'value',
      label: 'Value',
      types: ['Integer', 'Decimal', 'Date'],
      isRequired: false,
      allowMultiple: false,
      allowedValues: [],
    },
  ],
  output: { type: 'Boolean', cardinality: 'Scalar' },
  documentation: documentation('Required value', 'Require a value.'),
};

const workspaceSummary = {
  definitionKey: 'credit_threshold',
  name: 'Credit threshold',
  description: 'Compare a value with a workspace threshold.',
  origin: 'Workspace',
  status: 'Draft',
  revision: 2,
  inputs: [
    {
      key: 'value',
      label: 'Value',
      types: ['Decimal'],
      isRequired: true,
      allowMultiple: false,
      allowedValues: [],
    },
    {
      key: 'threshold',
      label: 'Threshold',
      types: ['Decimal'],
      isRequired: true,
      allowMultiple: false,
      allowedValues: [],
    },
  ],
  output: { type: 'Boolean', cardinality: 'Scalar' },
  documentation: documentation('Credit threshold', 'Compare a value with a workspace threshold.'),
};

const ruleDefinitions = {
  items: [systemSummary, workspaceSummary],
  totalCount: 2,
  page: 1,
  pageSize: 100,
};

function requiredDetail() {
  return {
    ...systemSummary,
    expressionLanguageVersion: 1,
    revision: null,
    condition: requiredCondition(),
    versions: [],
    createdAt: null,
    updatedAt: null,
    archivedAt: null,
  };
}

function workspaceDetail(overrides: Record<string, unknown> = {}) {
  return {
    ...workspaceSummary,
    expressionLanguageVersion: 1,
    condition: thresholdCondition(),
    versions: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    archivedAt: null,
    ...overrides,
  };
}

function requiredCondition() {
  return {
    nodeId: 'required',
    predicateOperator: 'IsNotNull',
    left: { kind: 'Input', reference: 'value', arguments: [] },
    children: [],
  };
}

function thresholdCondition() {
  return {
    nodeId: 'threshold',
    predicateOperator: 'GreaterThan',
    left: { kind: 'Input', reference: 'value', arguments: [] },
    right: { kind: 'Input', reference: 'threshold', arguments: [] },
    children: [],
  };
}

function expressionLanguage() {
  const types = ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'];
  const reference = (displayName: string) => documentation(displayName, `${displayName} help.`);
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
        documentation: reference('Equals'),
      },
      {
        operator: 'GreaterThan',
        leftShapes: shapes(['Integer', 'Decimal', 'Date', 'DateTime'], 'Scalar'),
        rightShapes: shapes(['Integer', 'Decimal', 'Date', 'DateTime'], 'Scalar'),
        requiresMatchingTypes: true,
        documentation: reference('Greater than'),
      },
      {
        operator: 'IsNotNull',
        leftShapes: shapes(types, 'Any'),
        rightShapes: [],
        requiresMatchingTypes: false,
        documentation: reference('Is not empty'),
      },
    ],
    functions: [
      {
        function: 'Length',
        parameters: [{ acceptedTypes: ['Text'], cardinality: 'Scalar' }],
        returnType: 'Integer',
        returnCardinality: 'Scalar',
        documentation: reference('Length'),
      },
    ],
    logicalOperators: ['All', 'Any', 'Not'].map((operator) => ({
      operator,
      minimumChildren: 1,
      maximumChildren: operator === 'Not' ? 1 : null,
      documentation: reference(operator),
    })),
    operandKinds: [
      ['Input', 'Rule input'],
      ['Literal', 'Literal value'],
      ['Function', 'Function'],
    ].map(([kind, displayName]) => ({ kind, documentation: reference(displayName) })),
    valueTypes: types.map((type) => ({ type, documentation: reference(type) })),
    cardinalities: ['Scalar', 'Multiple', 'Any'].map((cardinality) => ({
      cardinality,
      documentation: reference(cardinality),
    })),
    limits: { maxDepth: 12 },
  };
}

function conditionProjectionResponse(init?: RequestInit) {
  const request = init?.body ? JSON.parse(String(init.body)) : {};
  const condition = request.condition ?? thresholdCondition();
  const isRequired = condition.nodeId === 'required';
  return jsonResponse({
    condition,
    display: {
      nodeId: condition.nodeId,
      tokens: isRequired
        ? [
            { text: 'Value', referenceKind: 'Input', referenceKey: 'value' },
            { text: 'is provided', referenceKind: 'PredicateOperator', referenceKey: 'IsNotNull' },
          ]
        : [
            { text: 'Value', referenceKind: 'Input', referenceKey: 'value' },
            {
              text: 'is greater than',
              referenceKind: 'PredicateOperator',
              referenceKey: 'GreaterThan',
            },
            { text: 'Threshold', referenceKind: 'Input', referenceKey: 'threshold' },
          ],
      children: [],
    },
  });
}

function respondForRules(input: RequestInfo | URL, init?: RequestInit): Response {
  const url = input.toString();
  if (url.endsWith('/rules/condition/project')) return conditionProjectionResponse(init);
  if (url.endsWith('/rules/authoring/project')) {
    const body = init?.body ? JSON.parse(String(init.body)) : {};
    const condition = body.source?.ast ?? thresholdCondition();
    return jsonResponse({
      condition,
      formattedDsl: body.source?.text ?? 'value > threshold',
      diagnostics: [],
      isValid: true,
    });
  }
  if (url.endsWith('/rules/authoring/complete')) {
    return jsonResponse([
      { label: 'Threshold', insertText: 'threshold', kind: 'Input', start: 8, length: 2 },
    ]);
  }
  if (url.endsWith('/rules/credit_threshold/draft/simulate')) {
    return jsonResponse({
      definitionKey: 'credit_threshold',
      definitionVersion: null,
      isMatch: false,
      diagnostics: [],
    });
  }
  if (url.endsWith('/rules/expression-language')) return jsonResponse(expressionLanguage());
  if (url.includes('/rules/field.required/bindings?version=1')) {
    return jsonResponse([
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
    ]);
  }
  if (url.endsWith('/rule-bindings/88888888-8888-4888-8888-888888888888')) {
    if (init?.method === 'DELETE') return jsonResponse(null, 204);
    if (init?.method === 'PUT') {
      const body = init.body ? JSON.parse(String(init.body)) : {};
      return jsonResponse({
        id: '88888888-8888-4888-8888-888888888888',
        ...body,
        revision: 2,
      });
    }
    return jsonResponse({
      id: '88888888-8888-4888-8888-888888888888',
      revision: 1,
      definitionKey: 'field.required',
      definitionVersion: 1,
      targetType: 'business-object-field',
      targetId: 'customer.status',
      useCaseOrTrigger: 'field-validation',
      priority: 0,
      enabled: true,
      failureBehavior: 'FailClosed',
      inputMappings: {
        value: { kind: 'Context', contextKey: 'record.value', literalValues: [] },
      },
    });
  }
  if (url.endsWith('/rules/field.required')) return jsonResponse(requiredDetail());
  if (url.endsWith('/rules/credit_threshold')) return jsonResponse(workspaceDetail());
  return jsonResponse(ruleDefinitions);
}

describe('RulesPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    useAuthStore.getState().setBrowserSession({
      authenticated: false,
      csrfToken: 'test-csrf-token',
    });
  });

  afterEach(() => {
    useAuthStore.getState().clearSession();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('renders one catalog shape for built-in and workspace rules', async () => {
    vi.mocked(fetch).mockImplementation((input) => Promise.resolve(respondForRules(input)));

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });

    const catalog = await screen.findByRole('region', { name: 'Rules catalog' });
    expect(within(catalog).getByRole('button', { name: 'Required value' })).toBeInTheDocument();
    expect(within(catalog).getByRole('button', { name: 'Credit threshold' })).toBeInTheDocument();
    expect(within(catalog).getByText('Value, Threshold')).toBeInTheDocument();
    expect(within(catalog).getByRole('columnheader', { name: 'Inputs' })).toBeInTheDocument();
    expect(within(catalog).queryByText('Field')).not.toBeInTheDocument();
    expect(within(catalog).queryByText('Validation')).not.toBeInTheDocument();
  });

  it('shows the platform input-logic-output contract for a built-in rule', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input, init) =>
      Promise.resolve(respondForRules(input, init)),
    );

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    const catalog = await screen.findByRole('region', { name: 'Rules catalog' });
    await user.click(within(catalog).getByRole('button', { name: 'Required value' }));

    const details = await screen.findByRole('dialog', { name: 'Required value' });
    expect(
      within(details)
        .getAllByRole('tab')
        .map((tab) => tab.textContent),
    ).toEqual(['General', 'Rule behavior', 'Usage', 'System info']);
    expect(within(details).getByText('Require a value.')).toBeVisible();
    await user.click(within(details).getByRole('tab', { name: 'Rule behavior' }));
    const behavior = details.querySelector<HTMLElement>('[data-slot="rule-behavior-summary"]');
    expect(behavior).not.toBeNull();
    const steps = behavior?.querySelector<HTMLElement>('[data-slot="rule-behavior-steps"]');
    expect(steps).not.toBeNull();
    expect(
      within(steps as HTMLElement)
        .getAllByRole('heading')
        .map((heading) => heading.textContent),
    ).toEqual(['Inputs', 'Logic', 'Outputs']);
    expect((steps as HTMLElement).querySelectorAll('li')).toHaveLength(3);
    expect(
      (steps as HTMLElement).querySelectorAll('[data-slot="rule-behavior-rail-node"]'),
    ).toHaveLength(3);
    expect(
      (steps as HTMLElement).querySelectorAll('[data-slot="rule-behavior-rail-connector"]'),
    ).toHaveLength(3);
    expect((steps as HTMLElement).querySelectorAll('svg')).toHaveLength(3);
    expect(details).not.toHaveTextContent(
      'The declared inputs, canonical logic, and outputs for this rule.',
    );
    expect(details).toHaveTextContent('Value');
    const inputContract = behavior?.querySelector<HTMLElement>('[data-slot="rule-input-contract"]');
    expect(within(inputContract as HTMLElement).getByText('Optional')).toBeInTheDocument();
    expect(within(inputContract as HTMLElement).getByText('Accepted types')).toBeInTheDocument();
    for (const type of ['Integer', 'Decimal', 'Date']) {
      expect(
        within(inputContract as HTMLElement)
          .getByText(type)
          .closest('[data-slot="badge"]'),
      ).not.toBeNull();
    }
    expect(
      within(inputContract as HTMLElement).queryByText('May be absent'),
    ).not.toBeInTheDocument();
    expect(
      within(inputContract as HTMLElement).queryByText('Single value'),
    ).not.toBeInTheDocument();
    expect(details).toHaveTextContent('Value is provided');
    expect(within(details).queryByRole('button', { name: 'Value' })).not.toBeInTheDocument();
    expect(
      within(details).queryByRole('button', { name: 'Expression guide' }),
    ).not.toBeInTheDocument();
    expect(within(details).queryByText('Match')).not.toBeInTheDocument();
    expect(details).not.toHaveTextContent('isMatch');
    expect(details).toHaveTextContent('Boolean');
    expect(details).not.toHaveTextContent('Scalar');
    expect(details).not.toHaveTextContent('Output contract:');
    expect(within(details).queryByText('What this rule does')).not.toBeInTheDocument();
    await user.click(within(details).getByRole('tab', { name: 'Usage' }));
    expect(await within(details).findByText('customer.status')).toBeVisible();
    expect(within(details).getByText('field-validation')).toBeVisible();
    expect(
      within(details).queryByRole('button', { name: 'Where this rule applies' }),
    ).not.toBeInTheDocument();
    await user.click(within(details).getByRole('tab', { name: 'System info' }));
    expect(within(details).getByText('field.required')).toBeVisible();
    expect(within(details).getByText('Expression language')).toBeVisible();
    const footer = details.querySelector('[data-slot="managed-dialog-footer"]');
    expect(footer).not.toBeNull();
    expect(within(footer as HTMLElement).getAllByRole('button')).toHaveLength(1);
    expect(within(footer as HTMLElement).getByRole('button', { name: 'Close' })).toBeVisible();
  });

  it('edits a binding with its current revision', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input, init) =>
      Promise.resolve(respondForRules(input, init)),
    );
    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(
      within(await screen.findByRole('region', { name: 'Rules catalog' })).getByRole('button', {
        name: 'Required value',
      }),
    );
    const dialog = await screen.findByRole('dialog', { name: 'Required value' });
    await user.click(within(dialog).getByRole('tab', { name: 'Usage' }));
    await user.click(await within(dialog).findByRole('button', { name: 'Edit binding' }));
    const editor = await screen.findByRole('dialog', { name: 'Edit binding' });
    expect(within(editor).getByLabelText('Rule key')).toHaveValue('field.required');
    expect(within(editor).getByLabelText('Rule version')).toHaveValue(1);
    expect(within(editor).getByLabelText('Target type')).toHaveValue('business-object-field');
    expect(within(editor).getByLabelText('Target ID')).toHaveValue('customer.status');
    expect(within(editor).getByLabelText('Use case or trigger')).toHaveValue('field-validation');
    expect(within(editor).getByRole('checkbox', { name: 'Enabled' })).toBeChecked();
    expect(within(editor).getByLabelText('value')).toHaveTextContent('Context');
    expect(within(editor).getByLabelText('Context key')).toHaveValue('record.value');
    await user.clear(within(editor).getByLabelText('Priority'));
    await user.type(within(editor).getByLabelText('Priority'), '4');
    await user.click(within(editor).getByRole('button', { name: 'Save binding' }));

    await waitFor(() => {
      const request = vi
        .mocked(fetch)
        .mock.calls.find(
          ([input, init]) =>
            input.toString().endsWith('/rule-bindings/88888888-8888-4888-8888-888888888888') &&
            init?.method === 'PUT',
        );
      expect(request).toBeDefined();
      expect(JSON.parse(String(request?.[1]?.body))).toEqual({
        expectedRevision: 1,
        definitionKey: 'field.required',
        definitionVersion: 1,
        targetType: 'business-object-field',
        targetId: 'customer.status',
        useCaseOrTrigger: 'field-validation',
        inputMappings: {
          value: { kind: 'Context', contextKey: 'record.value', literalValues: [] },
        },
        priority: 4,
        enabled: true,
        failureBehavior: 'FailClosed',
      });
    });
    expect(screen.queryByRole('dialog', { name: 'Edit binding' })).not.toBeInTheDocument();
  });

  it('uses the visible revision for binding enable, delete, and stale recovery', async () => {
    const user = userEvent.setup();
    let bindingReads = 0;
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (
        url.endsWith('/rule-bindings/88888888-8888-4888-8888-888888888888') &&
        (!init?.method || init.method === 'GET')
      ) {
        bindingReads += 1;
        return Promise.resolve(
          jsonResponse({
            id: '88888888-8888-4888-8888-888888888888',
            revision: bindingReads === 1 ? 1 : 2,
            definitionKey: 'field.required',
            definitionVersion: 1,
            targetType: 'business-object-field',
            targetId: 'customer.status',
            useCaseOrTrigger: 'field-validation',
            priority: bindingReads === 1 ? 0 : 7,
            enabled: true,
            failureBehavior: 'FailClosed',
            inputMappings: {
              value: { kind: 'Context', contextKey: 'record.value', literalValues: [] },
            },
          }),
        );
      }
      if (
        url.endsWith('/rule-bindings/88888888-8888-4888-8888-888888888888') &&
        init?.method === 'PUT'
      ) {
        return Promise.resolve(jsonResponse({ title: 'Revision conflict' }, 409));
      }
      return Promise.resolve(respondForRules(input, init));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(
      within(await screen.findByRole('region', { name: 'Rules catalog' })).getByRole('button', {
        name: 'Required value',
      }),
    );
    const detail = await screen.findByRole('dialog', { name: 'Required value' });
    await user.click(within(detail).getByRole('tab', { name: 'Usage' }));
    await user.click(await within(detail).findByRole('button', { name: 'Edit binding' }));
    const editor = await screen.findByRole('dialog', { name: 'Edit binding' });
    await user.clear(within(editor).getByLabelText('Priority'));
    await user.type(within(editor).getByLabelText('Priority'), '4');
    await user.click(within(editor).getByRole('button', { name: 'Save binding' }));

    expect(
      await within(editor).findByText('This binding changed. Refresh it and try again.'),
    ).toBeVisible();
    expect(within(editor).getByLabelText('Priority')).toHaveValue(4);
    await user.click(within(editor).getByRole('button', { name: 'Refresh binding' }));
    await waitFor(() => expect(within(editor).getByLabelText('Priority')).toHaveValue(7));
    expect(bindingReads).toBeGreaterThanOrEqual(2);

    await user.click(within(editor).getByRole('button', { name: 'Cancel' }));
    await user.click(within(detail).getByRole('button', { name: 'Disable binding' }));
    await waitFor(() => {
      const toggleRequest = vi
        .mocked(fetch)
        .mock.calls.find(
          ([input, init]) =>
            input.toString().endsWith('/rule-bindings/88888888-8888-4888-8888-888888888888') &&
            init?.method === 'PUT' &&
            JSON.parse(String(init.body)).enabled === false,
        );
      expect(JSON.parse(String(toggleRequest?.[1]?.body))).toMatchObject({
        expectedRevision: 1,
        definitionKey: 'field.required',
        definitionVersion: 1,
        enabled: false,
      });
    });

    await user.click(within(detail).getByRole('button', { name: 'Remove binding' }));
    const confirmation = await screen.findByRole('alertdialog', { name: 'Remove this binding?' });
    await user.click(within(confirmation).getByRole('button', { name: 'Remove binding' }));
    await waitFor(() => {
      const deleteRequest = vi
        .mocked(fetch)
        .mock.calls.find(
          ([input, init]) =>
            input.toString().endsWith('/rule-bindings/88888888-8888-4888-8888-888888888888') &&
            init?.method === 'DELETE',
        );
      expect(JSON.parse(String(deleteRequest?.[1]?.body))).toEqual({ expectedRevision: 1 });
    });
  });

  it('keeps invalid DSL edits, applies server completion ranges, and guards dirty close', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/authoring/project') && init?.body) {
        const body = JSON.parse(String(init.body));
        if (body.source?.text === 'invalid expression') {
          return Promise.resolve(
            jsonResponse({
              condition: null,
              formattedDsl: 'server replacement must not win',
              diagnostics: [{ code: 'syntax', message: 'Invalid expression', start: 0, length: 7 }],
              isValid: false,
            }),
          );
        }
      }
      return Promise.resolve(respondForRules(input, init));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(
      within(await screen.findByRole('region', { name: 'Rules catalog' })).getByRole('button', {
        name: 'Credit threshold',
      }),
    );
    const editor = await screen.findByRole('dialog', { name: 'Credit threshold' });
    await user.click(within(editor).getByRole('tab', { name: 'Rule behavior' }));
    const dsl = await within(editor).findByLabelText('Expression syntax');
    await waitFor(() => expect(dsl).toHaveValue('value > threshold'));
    await user.clear(dsl);
    await user.type(dsl, 'value > th');
    await user.click(within(editor).getByRole('button', { name: 'Show suggestions' }));
    const suggestions = await within(editor).findByRole('listbox');
    await user.click(within(suggestions).getByRole('option', { name: 'Threshold' }));
    expect(dsl).toHaveValue('value > threshold');

    await user.clear(dsl);
    await user.type(dsl, 'invalid expression');
    await user.tab();
    expect(await within(editor).findByText('Invalid expression')).toBeVisible();
    expect(dsl).toHaveValue('invalid expression');
    expect(editor.querySelector('[data-slot="rule-expression"]')).toHaveTextContent(
      'Value is greater than Threshold',
    );

    await user.click(within(editor).getByRole('button', { name: 'Close dialog' }));
    const discard = await screen.findByRole('alertdialog', { name: 'Discard unsaved changes?' });
    await user.click(within(discard).getByRole('button', { name: 'Keep editing' }));
    expect(editor).toBeVisible();
    expect(dsl).toHaveValue('invalid expression');
  });

  it('simulates typed samples and confirms independent version activation lifecycle', async () => {
    const user = userEvent.setup();
    const requests: Array<{ method?: string; url: string; body?: Record<string, unknown> }> = [];
    let simulationCalls = 0;
    let currentDetail = workspaceDetail({
      status: 'Active',
      revision: 2,
      latestVersion: 1,
      activeVersion: 1,
      versions: [{ version: 1, name: 'Credit threshold' }],
      actions: {
        canEditDraft: true,
        canCreateVersion: true,
        canActivateVersion: false,
        canDeactivate: true,
        canArchive: true,
      },
    });
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      const body = init?.body ? JSON.parse(String(init.body)) : undefined;
      if (init?.method) requests.push({ method: init.method, url, body });
      if (url.endsWith('/rules/credit_threshold') && (!init?.method || init.method === 'GET')) {
        return Promise.resolve(jsonResponse(currentDetail));
      }
      if (url.endsWith('/rules/credit_threshold/versions/1/simulate')) {
        simulationCalls += 1;
        return Promise.resolve(
          simulationCalls === 1
            ? jsonResponse({
                definitionKey: 'credit_threshold',
                definitionVersion: 1,
                isMatch: true,
                diagnostics: [{ nodeId: 'threshold', isMatch: true }],
              })
            : jsonResponse({ title: 'Invalid sample' }, 400),
        );
      }
      if (url.endsWith('/rules/credit_threshold/versions') && init?.method === 'POST') {
        currentDetail = workspaceDetail({
          ...currentDetail,
          status: 'Active',
          revision: 3,
          latestVersion: 2,
          activeVersion: 1,
          versions: [
            { version: 1, name: 'Credit threshold' },
            { version: 2, name: 'Credit threshold' },
          ],
          actions: {
            canEditDraft: true,
            canCreateVersion: true,
            canActivateVersion: true,
            canDeactivate: true,
            canArchive: true,
          },
        });
        return Promise.resolve(jsonResponse(currentDetail));
      }
      if (url.endsWith('/rules/credit_threshold/active-version') && init?.method === 'PUT') {
        currentDetail = workspaceDetail({
          ...currentDetail,
          status: 'Active',
          revision: 4,
          activeVersion: 2,
          actions: {
            canEditDraft: true,
            canCreateVersion: true,
            canActivateVersion: false,
            canDeactivate: true,
            canArchive: true,
          },
        });
        return Promise.resolve(jsonResponse(currentDetail));
      }
      if (url.endsWith('/rules/credit_threshold/active-version') && init?.method === 'DELETE') {
        currentDetail = workspaceDetail({
          ...currentDetail,
          status: 'Inactive',
          revision: 5,
          activeVersion: null,
          actions: {
            canEditDraft: true,
            canCreateVersion: true,
            canActivateVersion: true,
            canDeactivate: false,
            canArchive: true,
          },
        });
        return Promise.resolve(jsonResponse(currentDetail));
      }
      return Promise.resolve(respondForRules(input, init));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(
      within(await screen.findByRole('region', { name: 'Rules catalog' })).getByRole('button', {
        name: 'Credit threshold',
      }),
    );
    const editor = await screen.findByRole('dialog', { name: 'Credit threshold' });
    await user.type(within(editor).getByLabelText('Value'), '15');
    await user.type(within(editor).getByLabelText('Threshold'), '10');
    await user.click(within(editor).getByRole('button', { name: 'Run simulation' }));
    expect(await within(editor).findByText('Condition matched')).toBeVisible();
    expect(within(editor).getAllByText(/Version 1/).length).toBeGreaterThanOrEqual(1);
    const simulationRequest = requests.find((request) =>
      request.url.endsWith('/versions/1/simulate'),
    );
    expect(simulationRequest?.body).toEqual({
      inputs: {
        value: { type: 'Decimal', values: ['15'] },
        threshold: { type: 'Decimal', values: ['10'] },
      },
    });

    await user.click(within(editor).getByRole('button', { name: 'Run simulation' }));
    expect(
      await within(editor).findByText(
        'The sample values could not be evaluated. Correct them and try again.',
      ),
    ).toBeVisible();

    await user.click(within(editor).getByRole('button', { name: 'Create version' }));
    let confirmation = await screen.findByRole('alertdialog', {
      name: 'Create immutable version?',
    });
    await user.click(within(confirmation).getByRole('button', { name: 'Create version' }));
    await waitFor(() =>
      expect(
        requests.some(
          (request) =>
            request.url.endsWith('/rules/credit_threshold/versions') &&
            request.method === 'POST' &&
            request.body?.expectedRevision === 2,
        ),
      ).toBe(true),
    );

    await within(editor).findByText('Version 2');
    await user.click(within(editor).getByRole('button', { name: 'Activate version' }));
    confirmation = await screen.findByRole('alertdialog', { name: 'Activate this version?' });
    await user.click(within(confirmation).getByRole('button', { name: 'Activate version' }));
    await waitFor(() =>
      expect(
        requests.some(
          (request) =>
            request.url.endsWith('/rules/credit_threshold/active-version') &&
            request.method === 'PUT' &&
            request.body?.version === 2 &&
            request.body?.expectedRevision === 3,
        ),
      ).toBe(true),
    );

    await user.click(await within(editor).findByRole('button', { name: 'Deactivate' }));
    confirmation = await screen.findByRole('alertdialog', { name: 'Deactivate this rule?' });
    await user.click(within(confirmation).getByRole('button', { name: 'Deactivate' }));
    await waitFor(() =>
      expect(
        requests.some(
          (request) =>
            request.url.endsWith('/rules/credit_threshold/active-version') &&
            request.method === 'DELETE' &&
            request.body?.expectedRevision === 4,
        ),
      ).toBe(true),
    );
    expect(await within(editor).findByText('Inactive')).toBeVisible();
  });

  it('edits a workspace rule with inputs and condition only', async () => {
    const user = userEvent.setup();
    const saved = workspaceDetail({ name: 'Updated threshold', revision: 3 });
    vi.mocked(fetch).mockImplementation((input, init) => {
      const url = input.toString();
      if (url.endsWith('/rules/credit_threshold/draft') && init?.method === 'PUT') {
        return Promise.resolve(jsonResponse(saved));
      }
      return Promise.resolve(respondForRules(input, init));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    const catalog = await screen.findByRole('region', { name: 'Rules catalog' });
    await user.click(within(catalog).getByRole('button', { name: 'Credit threshold' }));
    const editor = await screen.findByRole('dialog', { name: 'Credit threshold' });
    await user.clear(within(editor).getByLabelText('Name'));
    await user.type(within(editor).getByLabelText('Name'), 'Updated threshold');
    await user.click(within(editor).getByRole('tab', { name: 'Rule behavior' }));
    expect(
      within(editor)
        .getAllByRole('heading', { level: 3 })
        .map((heading) => heading.textContent),
    ).toEqual(['Inputs', 'Logic', 'What this means', 'Outputs', 'Simulation']);
    expect(editor.querySelector('[data-slot="rule-behavior-summary"]')).toBeNull();
    expect(within(editor).queryByLabelText('Expression')).not.toBeInTheDocument();
    expect(within(editor).getByLabelText('What to check')).toBeInTheDocument();
    expect(within(editor).getByLabelText('How to compare')).toBeInTheDocument();
    expect(within(editor).getByRole('button', { name: 'Expression guide' })).toBeInTheDocument();
    expect(within(editor).queryByText('Match')).not.toBeInTheDocument();
    expect(editor.querySelector('[data-slot="rule-output-summary"]')).toHaveTextContent('Boolean');
    expect(within(editor).queryByText('isMatch')).not.toBeInTheDocument();
    await user.click(within(editor).getByRole('button', { name: 'Save draft' }));

    await waitFor(() => {
      const request = vi
        .mocked(fetch)
        .mock.calls.find(
          ([input, init]) =>
            input.toString().endsWith('/rules/credit_threshold/draft') && init?.method === 'PUT',
        );
      expect(request).toBeDefined();
      const body = JSON.parse(String(request?.[1]?.body));
      expect(body).toMatchObject({
        name: 'Updated threshold',
        inputs: [
          { label: 'Value', types: ['Decimal'] },
          { label: 'Threshold', types: ['Decimal'] },
        ],
      });
      expect(body.condition).toMatchObject({ predicateOperator: 'GreaterThan' });
      expect(body).not.toHaveProperty('scope');
      expect(body).not.toHaveProperty('outcome');
      expect(body).not.toHaveProperty('parameters');
    });
  });

  it('creates a workspace rule using the same input and condition contract', async () => {
    const user = userEvent.setup();
    const created = workspaceDetail({ definitionKey: 'new_rule', name: 'New rule', revision: 1 });
    const requests: Array<{ path: string; init?: RequestInit }> = [];
    vi.mocked(fetch).mockImplementation((input, init) => {
      requests.push({ path: input.toString(), init });
      if (input.toString().endsWith('/rules') && init?.method === 'POST') {
        return Promise.resolve(jsonResponse(created, 201));
      }
      if (input.toString().endsWith('/rules/new_rule/draft') && init?.method === 'PUT') {
        return Promise.resolve(jsonResponse(created));
      }
      return Promise.resolve(respondForRules(input, init));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(await screen.findByRole('button', { name: 'New rule' }));
    const editor = await screen.findByRole('dialog', { name: 'New workspace rule' });
    await user.type(within(editor).getByLabelText('Name'), 'New rule');
    await user.type(within(editor).getByLabelText('Description'), 'A new rule.');
    await user.click(within(editor).getByRole('tab', { name: 'Rule behavior' }));
    await user.click(within(editor).getByRole('button', { name: 'Add input' }));
    await user.click(within(editor).getByRole('button', { name: 'Add input' }));
    const inputLabels = within(editor).getAllByLabelText('Input name');
    await user.type(inputLabels[0], 'Value');
    await user.type(inputLabels[1], 'Threshold');
    await user.click(within(editor).getByRole('button', { name: 'Add condition' }));
    await user.click(within(editor).getByRole('button', { name: 'Save draft' }));

    await waitFor(() =>
      expect(
        requests.some(({ path, init }) => path.endsWith('/rules') && init?.method === 'POST'),
      ).toBe(true),
    );
    const post = requests.find(
      ({ path, init }) => path.endsWith('/rules') && init?.method === 'POST',
    );
    expect(JSON.parse(String(post?.init?.body))).toEqual({
      name: 'New rule',
      description: 'A new rule.',
    });
    const draft = await waitFor(() =>
      requests.find(
        ({ path, init }) => path.endsWith('/rules/new_rule/draft') && init?.method === 'PUT',
      ),
    );
    expect(JSON.parse(String(draft?.init?.body))).toMatchObject({
      inputs: [{ label: 'Value' }, { label: 'Threshold' }],
      condition: expect.any(Object),
    });
  });

  it('builds functions and logical groups from the server language contract', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input, init) =>
      Promise.resolve(respondForRules(input, init)),
    );

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(await screen.findByRole('button', { name: 'New rule' }));
    const editor = await screen.findByRole('dialog', { name: 'New workspace rule' });
    await user.click(within(editor).getByRole('tab', { name: 'Rule behavior' }));
    await user.click(within(editor).getByRole('button', { name: 'Add input' }));
    await user.type(within(editor).getByLabelText('Input name'), 'Customer name');
    await user.click(within(editor).getByRole('button', { name: 'Add condition' }));
    await user.click(within(editor).getByLabelText('What to check'));
    await user.click(await screen.findByRole('option', { name: 'A calculated value' }));

    expect(within(editor).getByLabelText('What to check: Choose a calculation')).toHaveTextContent(
      'Length',
    );
    await user.click(within(editor).getByRole('button', { name: 'Add group' }));
    expect(within(editor).getAllByLabelText('How these conditions work together')).toHaveLength(2);
  });

  it('keeps managed window identity stable when one rule is minimized', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input, init) =>
      Promise.resolve(respondForRules(input, init)),
    );

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    const catalog = await screen.findByRole('region', { name: 'Rules catalog' });
    await user.click(within(catalog).getByRole('button', { name: 'Required value' }));
    const details = await screen.findByRole('dialog', { name: 'Required value' });
    await user.click(within(details).getByRole('tab', { name: 'Rule behavior' }));
    await user.click(within(details).getByRole('button', { name: 'Minimize dialog' }));
    const dock = document.querySelector('[data-slot="managed-window-dock"]');
    expect(dock).toHaveTextContent('Required value');
    await user.click(within(dock as HTMLElement).getByRole('button', { name: 'Restore dialog' }));
    expect(within(details).getByRole('tab', { name: 'Rule behavior' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(within(details).getByRole('heading', { name: 'Inputs' })).toBeVisible();
    await user.click(within(details).getByRole('button', { name: 'Minimize dialog' }));
    await user.click(within(catalog).getByRole('button', { name: 'Credit threshold' }));
    expect(await screen.findByRole('dialog', { name: 'Credit threshold' })).toBeInTheDocument();
    expect(dock).toHaveTextContent('Required value');
    expect(dock).not.toHaveTextContent('Credit threshold');
  });

  it('shows a retryable catalog error', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse({ title: 'Unavailable' }, 500))
      .mockImplementationOnce((input) => Promise.resolve(respondForRules(input)));

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load rules');
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('region', { name: 'Rules catalog' })).toBeInTheDocument();
  });
});
