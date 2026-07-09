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

const fieldRuleDefinitions = [
  {
    definitionKey: 'field.required',
    displayName: 'Required',
    description: 'Future records must provide a value.',
    supportedFieldTypes: ['Text', 'Integer', 'Decimal', 'Date', 'Boolean', 'SingleSelect'],
    parameters: [],
  },
  {
    definitionKey: 'field.text_length',
    displayName: 'Text length',
    description: 'Limit text length with optional bounds.',
    supportedFieldTypes: ['Text'],
    parameters: [
      { key: 'min', type: 'Integer', isRequired: false, allowMultiple: false },
      { key: 'max', type: 'Integer', isRequired: false, allowMultiple: false },
    ],
  },
  {
    definitionKey: 'field.single_select_options',
    displayName: 'Single-select options',
    description: 'Define allowed options.',
    supportedFieldTypes: ['SingleSelect'],
    parameters: [{ key: 'options', type: 'Text', isRequired: true, allowMultiple: true }],
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

  it('loads built-in field rules from the Rules catalog endpoint', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(fieldRuleDefinitions));

    await renderWithRouter(<RulesPage />, { path: '/rules' });

    expect(await screen.findByRole('heading', { name: 'Rules' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Review the system-managed field rules available to business object definitions.',
      ),
    ).toBeInTheDocument();
    const catalog = screen.getByRole('region', { name: 'Built-in field rules' });

    expect(within(catalog).getByText('Required value')).toBeInTheDocument();
    expect(within(catalog).getByText('field.required')).toBeInTheDocument();
    expect(within(catalog).getByText('Text length')).toBeInTheDocument();
    expect(within(catalog).getByText('field.text_length')).toBeInTheDocument();
    expect(within(catalog).getByText('Single-select options')).toBeInTheDocument();
    expect(within(catalog).getByText('options')).toBeInTheDocument();
    expect(screen.getByText('Rule contract')).toBeInTheDocument();
    expect(screen.getByText('Field type coverage')).toBeInTheDocument();

    await waitFor(() =>
      expect(vi.mocked(fetch).mock.calls[0][0]?.toString()).toContain(
        '/api/rules/field-rule-definitions',
      ),
    );
  });

  it('shows a retryable error state when the catalog cannot load', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse({ title: 'Unavailable' }, 500))
      .mockResolvedValueOnce(jsonResponse(fieldRuleDefinitions));

    await renderWithRouter(<RulesPage />, { path: '/rules' });

    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load rules');
    await user.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByText('Built-in field rules')).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(2);
  });
});
