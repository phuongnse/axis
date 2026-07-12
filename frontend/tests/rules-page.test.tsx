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

    expect(screen.getByRole('region', { name: 'Built-in field rules' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Rules' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'New rule' })).toBeInTheDocument();

    const catalog = screen.getByRole('region', { name: 'Built-in field rules' });
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
    expect(within(requiredRow).getByText('Built-in')).toHaveAttribute('data-variant', 'outline');
    expect(within(requiredRow).getByText('Date and time')).toHaveAttribute(
      'data-variant',
      'outline',
    );
    expect(within(requiredRow).getByText('Field')).toHaveAttribute('data-variant', 'outline');
    expect(within(catalog).getByText('Decimal precision')).toBeInTheDocument();
    expect(within(catalog).getByText('Date and time range')).toBeInTheDocument();
    expect(within(catalog).getByText('Text format')).toBeInTheDocument();
    expect(within(catalog).getByText('Choice selection count')).toBeInTheDocument();
    expect(within(catalog).getByText('Credit threshold')).toBeInTheDocument();
    expect(within(catalog).getByText('Draft')).toHaveAttribute('data-variant', 'outline');
    expect(within(catalog).queryByText('Validation')).not.toBeInTheDocument();
    expect(within(catalog).queryByText('field.required')).not.toBeInTheDocument();
    expect(within(catalog).queryByText(/Single-select options/)).not.toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(within(catalog).getByRole('button', { name: 'Filters' }));
    await user.click(screen.getByRole('button', { name: 'Add condition' }));
    await user.click(screen.getByTestId('fields'));
    await user.click(await screen.findByRole('option', { name: 'Status' }));
    await user.click(screen.getByTestId('value-editor'));
    await user.click(screen.getByRole('checkbox', { name: 'Built-in' }));
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
      if (url.endsWith('/rules') && init?.method === 'POST')
        return Promise.resolve(jsonResponse(created, 201));
      if (url.endsWith('/rules/high_credit_value')) return Promise.resolve(jsonResponse(created));
      return Promise.resolve(jsonResponse(ruleDefinitions));
    });

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });
    await user.click(await screen.findByRole('button', { name: 'New rule' }));
    expect(await screen.findByRole('heading', { name: 'New workspace rule' })).toBeInTheDocument();

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
    await user.click(screen.getByLabelText('Context'));
    await user.click(await screen.findByRole('option', { name: 'Decimal field value' }));
    expect(screen.getByLabelText('Context')).toHaveTextContent('Decimal field value');
    await user.click(screen.getByRole('button', { name: 'Create draft' }));

    expect(await screen.findByRole('heading', { name: 'High credit value' })).toBeInTheDocument();
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

    const editorName = await screen.findByLabelText('Name');
    await user.clear(editorName);
    await user.type(editorName, 'Updated credit value');
    await user.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.getByRole('heading', { name: 'Discard unsaved changes?' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Keep editing' }));
    expect(screen.getByLabelText('Name')).toHaveValue('Updated credit value');
  });

  it('shows a retryable error state when the catalog cannot load', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse({ title: 'Unavailable' }, 500))
      .mockResolvedValueOnce(jsonResponse(ruleDefinitions));

    await renderWithRouter(<RulesPage />, { path: '/rules', authenticatedPath: 'rules' });

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load rules');
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('region', { name: 'Built-in field rules' })).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(2);
  });
});
