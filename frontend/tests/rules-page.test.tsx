import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
});

const ruleDefinitions = {
  items: [
    systemRule('field.required', 'Required value', 'Requires a value.', [
      'Text',
      'Integer',
      'Decimal',
      'Date',
      'DateTime',
      'Boolean',
      'Choice',
    ]),
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
    fields: [{ path: 'field.value', displayName: 'Field value', type: 'Decimal' }],
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
    },
    {
      operator: 'IsNull',
      leftShapes: [{ type: 'Text', cardinality: 'Any' }],
      rightShapes: [],
      requiresMatchingTypes: false,
    },
  ],
  functions: [
    {
      function: 'IsBlank',
      parameters: [{ acceptedTypes: ['Text'], cardinality: 'Any' }],
      returnType: 'Boolean',
      returnCardinality: 'Scalar',
    },
    {
      function: 'Length',
      parameters: [{ acceptedTypes: ['Text'], cardinality: 'Scalar' }],
      returnType: 'Integer',
      returnCardinality: 'Scalar',
    },
  ],
  limits: { maxDepth: 12, maxNodes: 200, maxExecutionSteps: 1000 },
};

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
    await user.click(within(catalog).getByRole('button', { name: 'Filters' }));
    await user.click(screen.getByRole('button', { name: 'Add condition' }));
    await user.click(screen.getByTestId('fields'));
    await user.click(await screen.findByRole('option', { name: 'Origin' }));
    await user.click(screen.getByTestId('value-editor'));
    await user.click(await screen.findByRole('option', { name: 'Built-in' }));
    expect(within(catalog).queryByText('Credit threshold')).not.toBeInTheDocument();
    expect(within(catalog).getAllByText('Built-in')).not.toHaveLength(0);
    await user.keyboard('{Escape}');
    await user.click(within(catalog).getByRole('button', { name: 'Clear filters' }));
    expect(within(catalog).getByText('Credit threshold')).toBeInTheDocument();

    await waitFor(() =>
      expect(vi.mocked(fetch).mock.calls[0][0]?.toString()).toContain(
        '/api/rules?page=1&pageSize=100',
      ),
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
    vi.mocked(fetch).mockImplementation((input) => {
      const url = input.toString();
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
    await user.click(within(catalog).getByRole('button', { name: 'Filters' }));
    expect(screen.getByRole('button', { name: 'Add condition' })).toBeInTheDocument();

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
    vi.mocked(fetch).mockImplementation((input) => {
      const url = input.toString();
      if (url.endsWith('/rules/field.required')) {
        return Promise.resolve(jsonResponse(systemDetail('field.required')));
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
    expect(within(details).queryByRole('region', { name: 'Parameters' })).not.toBeInTheDocument();

    expect(behaviorSection).toHaveTextContent('When');
    expect(behaviorSection).toHaveTextContent('Field value is blank');
    expect(behaviorSection).not.toHaveTextContent('Equals');
    expect(behaviorSection).not.toHaveTextContent('true');
    expect(behaviorSection).toHaveTextContent('Then');
    expect(behaviorSection).toHaveTextContent('Validation');
    expect(behaviorSection).toHaveTextContent('Error');
    expect(behaviorSection).toHaveTextContent('A value is required.');

    expect(applicabilitySection).toHaveTextContent('Applies to a single field value.');
    expect(within(applicabilitySection).getByText('Text')).toHaveAttribute('data-slot', 'badge');
    expect(within(applicabilitySection).getByText('Choice')).toHaveAttribute('data-slot', 'badge');
    expect(applicabilitySection).toHaveTextContent('Ready to use—no setup required.');

    const detailsRoot = details.querySelector('[data-slot="system-rule-details"]');
    expect(detailsRoot).toHaveClass('@container/system-rule-details');
    expect(behaviorSection.querySelector('[data-slot="system-rule-behavior-grid"]')).toHaveClass(
      'grid',
      '@md/system-rule-details:grid-cols-2',
    );
    expect(
      applicabilitySection.querySelector('[data-slot="system-rule-applicability-grid"]'),
    ).toHaveClass('grid', '@md/system-rule-details:grid-cols-2');
    expect(detailsRoot?.querySelector('.sm\\:grid-cols-3')).not.toBeInTheDocument();
    expect(detailsRoot?.querySelector('.xl\\:grid-cols-3')).not.toBeInTheDocument();

    expect(within(details).queryByText('Violation code')).not.toBeInTheDocument();
    await user.click(within(details).getByRole('button', { name: /Technical details/ }));
    expect(within(details).getByText('Published version')).toBeVisible();
    expect(within(details).getByText('Expression language')).toBeVisible();
    expect(within(details).getByText('Violation code')).toBeVisible();
    expect(within(details).getByText('field.value.required')).toBeVisible();
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
      within(createDialog).queryByRole('heading', { name: 'Conditions' }),
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
    expect(within(editorDialog).getByRole('heading', { name: 'Conditions' })).toBeInTheDocument();
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
    vi.mocked(fetch).mockImplementation((input) => {
      const url = input.toString();
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

  it('authors function operands from the server capability contract', async () => {
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
    const conditions = within(editor).getByRole('heading', { name: 'Conditions' }).parentElement
      ?.parentElement;
    if (!conditions) throw new Error('Conditions section was not rendered');

    await user.click(within(conditions).getByLabelText('Left operand'));
    await user.click(await screen.findByRole('option', { name: 'Function' }));
    await user.click(within(conditions).getByLabelText('Function'));
    await user.click(await screen.findByRole('option', { name: 'Length' }));

    expect(within(conditions).getByText('Argument 1')).toBeInTheDocument();
    expect(within(conditions).getByText('Field value')).toBeInTheDocument();
    await user.type(within(conditions).getByLabelText('Value'), '5');
    await user.click(within(editor).getByRole('button', { name: 'Save draft' }));

    await waitFor(() => {
      const save = vi
        .mocked(fetch)
        .mock.calls.find(
          ([input, init]) =>
            input.toString().endsWith('/rules/credit_threshold/draft') && init?.method === 'PUT',
        );
      expect(save).toBeDefined();
      expect(JSON.parse(save?.[1]?.body as string).condition.children[0].left).toMatchObject({
        kind: 'Function',
        function: 'Length',
        arguments: [{ kind: 'Context', reference: 'field.value' }],
      });
    });
  });

  it('shows an error state when rule contexts cannot load for creation', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input) => {
      const url = input.toString();
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
    vi.mocked(fetch).mockImplementation((input) => {
      const url = input.toString();
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
    vi.mocked(fetch).mockImplementation((input) => {
      const url = input.toString();
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
