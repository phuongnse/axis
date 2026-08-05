import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  createMemoryHistory,
  createRootRouteWithContext,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import { ManagedWindowProvider } from '@/components/shared/ManagedWindowManager';
import { useAuthStore } from '@/features/auth/auth-store';
import { businessObjectDefinitionQueryKeys } from '@/features/business-objects/api';
import { BusinessObjectsPage } from '@/features/business-objects/components/BusinessObjectsPage';
import { ruleDefinitionQueryKeys } from '@/features/rules';
import { managedWindowRenderers } from '@/lib/managed-window-registry';
import type { MyRouterContext } from '@/routes/__root';
import { loadBusinessObjectDefinitionsRoute } from '@/routes/_authenticated/business-objects';

const definitionId = '33333333-3333-4333-8333-333333333333';
const fieldId = '55555555-5555-4555-8555-555555555555';
const optionId = '66666666-6666-4666-8666-666666666666';
const ruleId = '77777777-7777-4777-8777-777777777777';
const bindingId = '88888888-8888-4888-8888-888888888888';
const now = '2026-07-07T00:00:00Z';

const fieldRuleDefinitions = {
  items: [
    {
      definitionKey: 'field.required',
      name: 'Required',
      description: 'Future records must provide a value.',
      origin: 'BuiltIn',
      status: 'Active',
      latestVersion: 1,
      activeVersion: 1,
      output: { type: 'Boolean', cardinality: 'Scalar' },
      inputs: [{ key: 'value', types: ['Text'], isRequired: true, allowMultiple: false }],
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 100,
};

describe('BusinessObjectsPage', () => {
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

  it('loads the route data once and reuses both query caches', async () => {
    const page = emptyPage();
    const queryClient = testQueryClient();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      return isRulesRequest(input) ? jsonResponse(fieldRuleDefinitions) : jsonResponse(page);
    });

    await loadBusinessObjectDefinitionsRoute({ queryClient });
    await loadBusinessObjectDefinitionsRoute({ queryClient });

    expect(fetch).toHaveBeenCalledTimes(2);
    expect(
      queryClient.getQueryData(businessObjectDefinitionQueryKeys.list(1, 20, '', 'en')),
    ).toEqual(page);
    expect(
      queryClient.getQueryData(ruleDefinitionQueryKeys.list({ page: 1, pageSize: 100 })),
    ).toEqual(fieldRuleDefinitions);
  });

  it('prefetches a definition and opens its managed window without another detail request', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail();
    let detailRequests = 0;
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === `/api/business-object-definitions/${definitionId}`) {
        detailRequests += 1;
        return jsonResponse(detail);
      }
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      throw new Error(`Unexpected fetch: ${path}`);
    });

    const router = await renderPage();
    const recordButton = await screen.findByRole('button', { name: 'Customer' });
    await user.hover(recordButton);
    await waitFor(() => expect(detailRequests).toBe(1));
    await user.click(recordButton);

    const definitionDialog = await screen.findByRole('dialog', { name: 'Customer' });
    expect(definitionDialog.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
      'data-dialog-preset',
      'windowed',
    );
    expect(screen.getByRole('button', { name: 'Maximize dialog' })).toBeEnabled();
    expect(screen.getByLabelText('Object key')).toHaveValue('customer');
    const editFooter = definitionDialog.querySelector('[data-slot="managed-dialog-footer"]');
    expect(editFooter).not.toBeNull();
    expect(within(editFooter as HTMLElement).getByRole('button', { name: 'Cancel' })).toBeEnabled();
    expect(
      within(editFooter as HTMLElement).queryByRole('button', { name: 'Close' }),
    ).not.toBeInTheDocument();
    expect(router.state.location.search).toEqual({ page: 1 });
    expect(detailRequests).toBe(1);

    const nameInput = screen.getByLabelText('Name');
    await user.clear(nameInput);
    await user.type(nameInput, 'Preferred customer');
    await user.click(screen.getByRole('button', { name: 'Minimize dialog' }));

    const dock = document.querySelector('[data-slot="managed-window-dock"]');
    expect(dock).not.toBeNull();
    expect(within(dock as HTMLElement).getByText('Unsaved changes')).toBeInTheDocument();
    expect(router.state.location.search).toEqual({ page: 1 });
    await user.click(within(dock as HTMLElement).getByRole('button', { name: 'Close dialog' }));
    expect(screen.getByRole('heading', { name: 'Discard unsaved changes?' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Keep editing' }));
    expect(document.querySelector('[data-slot="managed-window-dock"]')).toBeInTheDocument();

    await user.click(recordButton);
    expect(await screen.findByLabelText('Name')).toHaveValue('Preferred customer');
    expect(
      document.querySelectorAll(
        '[data-window-id="business-objects:33333333-3333-4333-8333-333333333333"]',
      ),
    ).toHaveLength(1);
    expect(router.state.location.search).toEqual({ page: 1 });
  });

  it('uses an explicit Close action for a read-only definition', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === `/api/business-object-definitions/${definitionId}`) {
        return jsonResponse(detail);
      }
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage(
      `/business-objects?page=1&dialog=view&recordId=${encodeURIComponent(definitionId)}`,
    );

    const definitionDialog = await screen.findByRole('dialog', { name: 'Customer' });
    const readOnlyDetails = definitionDialog.querySelector(
      '[data-slot="business-object-read-only-details"]',
    );
    expect(readOnlyDetails).not.toBeNull();
    const generalPanel = within(definitionDialog).getByRole('tabpanel');
    expect(within(definitionDialog).getByText('Not published')).toBeVisible();
    expect(within(generalPanel).getByText('customer')).toBeVisible();
    expect(within(generalPanel).getByText('1')).toBeVisible();
    expect(
      within(definitionDialog)
        .getAllByRole('tab')
        .map((tab) => tab.textContent),
    ).toEqual(['General', 'Fields']);
    await user.click(within(definitionDialog).getByRole('tab', { name: 'Fields' }));
    expect(
      within(readOnlyDetails as HTMLElement).getByRole('heading', { name: 'Status' }),
    ).toBeVisible();
    expect(within(readOnlyDetails as HTMLElement).getByText('Single')).toBeVisible();
    expect(within(readOnlyDetails as HTMLElement).getByText('Field rules')).toBeVisible();
    expect(within(readOnlyDetails as HTMLElement).getByText(bindingId)).toBeVisible();
    expect(readOnlyDetails as HTMLElement).toHaveTextContent('Binding revision: 1');
    expect(within(definitionDialog).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(definitionDialog).queryByRole('combobox')).not.toBeInTheDocument();
    expect(within(definitionDialog).getByRole('tablist')).toBeInTheDocument();
    const footer = definitionDialog.querySelector('[data-slot="managed-dialog-footer"]');
    expect(footer).not.toBeNull();
    expect(within(footer as HTMLElement).getByRole('button', { name: 'Close' })).toBeEnabled();
    expect(
      within(footer as HTMLElement).queryByRole('button', { name: 'Cancel' }),
    ).not.toBeInTheDocument();

    await user.click(within(footer as HTMLElement).getByRole('button', { name: 'Close' }));
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Customer' })).not.toBeInTheDocument(),
    );
  });

  it('reviews the immutable contract before publishing a definition', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = requestPath(input);
      const method = init?.method ?? 'GET';
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === '/api/business-object-definitions' && method === 'GET') {
        return jsonResponse(pageWith(detail));
      }
      if (path === `/api/business-object-definitions/${definitionId}` && method === 'GET') {
        return jsonResponse(detail);
      }
      if (
        path === `/api/business-object-definitions/${definitionId}/publish` &&
        method === 'POST'
      ) {
        return jsonResponse(detail);
      }
      throw new Error(`Unexpected fetch: ${method} ${path}`);
    });

    await renderPage(
      `/business-objects?page=1&dialog=edit&recordId=${encodeURIComponent(definitionId)}`,
    );
    const definitionDialog = await screen.findByRole('dialog', { name: 'Customer' });
    await user.click(within(definitionDialog).getByRole('button', { name: 'Publish' }));

    const review = await screen.findByRole('alertdialog', { name: 'Publish this definition?' });
    expect(review).toHaveTextContent(
      'Publishing creates an immutable version that future records will use.',
    );
    expect(review).toHaveTextContent('Customer');
    expect(review).toHaveTextContent('customer');
    expect(review).toHaveTextContent('Fields1');
    expect(review).toHaveTextContent('Field rules1');
    expect(
      vi
        .mocked(fetch)
        .mock.calls.some(
          ([input, init]) =>
            requestPath(input) === `/api/business-object-definitions/${definitionId}/publish` &&
            init?.method === 'POST',
        ),
    ).toBe(false);

    await user.click(within(review).getByRole('button', { name: 'Publish' }));
    await waitFor(() =>
      expect(
        vi
          .mocked(fetch)
          .mock.calls.some(
            ([input, init]) =>
              requestPath(input) === `/api/business-object-definitions/${definitionId}/publish` &&
              init?.method === 'POST',
          ),
      ).toBe(true),
    );
  });

  it('shows publish failures inside the confirmation dialog', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = requestPath(input);
      const method = init?.method ?? 'GET';
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === '/api/business-object-definitions' && method === 'GET') {
        return jsonResponse(pageWith(detail));
      }
      if (path === `/api/business-object-definitions/${definitionId}` && method === 'GET') {
        return jsonResponse(detail);
      }
      if (
        path === `/api/business-object-definitions/${definitionId}/publish` &&
        method === 'POST'
      ) {
        return jsonResponse({ title: 'Publish failed' }, 409);
      }
      throw new Error(`Unexpected fetch: ${method} ${path}`);
    });

    await renderPage(
      `/business-objects?page=1&dialog=edit&recordId=${encodeURIComponent(definitionId)}`,
    );
    const definitionDialog = await screen.findByRole('dialog', { name: 'Customer' });
    await user.click(within(definitionDialog).getByRole('button', { name: 'Publish' }));
    const review = await screen.findByRole('alertdialog', { name: 'Publish this definition?' });
    await user.click(within(review).getByRole('button', { name: 'Publish' }));

    expect(await within(review).findByText('Unable to update business object')).toBeVisible();
    expect(review).toBeVisible();
  });

  it('waits for deep-link detail before opening and consuming the launch intent', async () => {
    const detail = definitionDetail();
    let resolveDetail!: (response: Response) => void;
    const detailResponse = new Promise<Response>((resolve) => {
      resolveDetail = resolve;
    });
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === `/api/business-object-definitions/${definitionId}`) return detailResponse;
      if (path === '/api/business-object-definitions') return jsonResponse(emptyPage());
      throw new Error(`Unexpected fetch: ${path}`);
    });

    const router = await renderPage(
      `/business-objects?page=1&dialog=view&recordId=${encodeURIComponent(definitionId)}`,
    );

    await waitFor(() =>
      expect(
        vi
          .mocked(fetch)
          .mock.calls.some(
            ([input]) => requestPath(input) === `/api/business-object-definitions/${definitionId}`,
          ),
      ).toBe(true),
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(router.state.location.search).toEqual({
      page: 1,
      dialog: 'view',
      recordId: definitionId,
    });

    await act(async () => resolveDetail(jsonResponse(detail)));

    expect(await screen.findByRole('dialog', { name: 'Customer' })).toBeInTheDocument();
    await waitFor(() => expect(router.state.location.search).toEqual({ page: 1 }));
  });

  it('consumes a create launch intent and transitions the window to the created record', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = requestPath(input);
      const method = init?.method ?? 'GET';
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === '/api/business-object-definitions' && method === 'GET') {
        return jsonResponse(emptyPage());
      }
      if (path === '/api/business-object-definitions' && method === 'POST') {
        return jsonResponse(definitionDetail({ fields: [] }), 201);
      }
      throw new Error(`Unexpected fetch: ${method} ${path}`);
    });

    const router = await renderPage('/business-objects?page=1&dialog=create');
    await user.type(await screen.findByLabelText('Name'), 'Customer');
    await user.click(screen.getByRole('button', { name: 'Start definition' }));

    await waitFor(() => expect(router.state.location.search).toEqual({ page: 1 }));
    expect(await screen.findByRole('dialog', { name: 'Customer' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Fields' })).toBeInTheDocument();
  });

  it('round-trips stable field, option, and rule IDs while keeping persisted keys read-only', async () => {
    const user = userEvent.setup();
    const requests: unknown[] = [];
    const detail = definitionDetail();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = requestPath(input);
      const method = init?.method ?? 'GET';
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === '/api/business-object-definitions' && method === 'GET') {
        return jsonResponse(pageWith(detail));
      }
      if (path === `/api/business-object-definitions/${definitionId}` && method === 'GET') {
        return jsonResponse(detail);
      }
      if (
        path === `/api/business-object-definitions/${definitionId}/unpublished` &&
        method === 'PUT'
      ) {
        const body = JSON.parse(String(init?.body));
        requests.push(body);
        return jsonResponse(definitionDetail({ revision: 4, fields: body.fields }));
      }
      throw new Error(`Unexpected fetch: ${method} ${path}`);
    });

    await renderPage(
      `/business-objects?page=1&dialog=edit&recordId=${encodeURIComponent(definitionId)}`,
    );
    await user.click(await screen.findByRole('tab', { name: 'Fields' }));

    const fieldKey = await screen.findByLabelText('Field key');
    const optionKey = screen.getByLabelText('Option key');
    expect(fieldKey).toHaveAttribute('readonly');
    expect(optionKey).toHaveAttribute('readonly');
    await user.clear(screen.getAllByLabelText('Label')[0]);
    await user.type(screen.getAllByLabelText('Label')[0], 'Lifecycle status');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0]).toMatchObject({
      expectedRevision: 3,
      fields: [
        {
          id: fieldId,
          fieldKey: 'status',
          choiceConfiguration: {
            selectionMode: 'Single',
            options: [{ id: optionId, optionKey: 'active', label: 'Active' }],
          },
          rules: [
            {
              id: ruleId,
              bindingId,
            },
          ],
        },
      ],
    });
  });

  it('binds the field value from context and configures only remaining rule inputs as literals', async () => {
    const user = userEvent.setup();
    const bindingRequests: unknown[] = [];
    const detail = definitionDetail({
      fields: [
        {
          id: fieldId,
          fieldKey: 'name',
          label: 'Name',
          order: 0,
          fieldType: 'Text',
          rules: [],
        },
      ],
    });
    const definitions = {
      ...fieldRuleDefinitions,
      items: [
        {
          definitionKey: 'field.minimum-length',
          name: 'Minimum length',
          description: 'Future records must meet the minimum length.',
          origin: 'BuiltIn',
          status: 'Active',
          latestVersion: 1,
          activeVersion: 1,
          output: { type: 'Boolean', cardinality: 'Scalar' },
          inputs: [
            { key: 'value', types: ['Text'], isRequired: true, allowMultiple: false },
            { key: 'minimum', types: ['Integer'], isRequired: true, allowMultiple: false },
          ],
        },
      ],
    };
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = requestPath(input);
      const method = init?.method ?? 'GET';
      if (isRulesRequest(input)) return jsonResponse(definitions);
      if (path === '/api/business-object-definitions' && method === 'GET') {
        return jsonResponse(pageWith(detail));
      }
      if (path === `/api/business-object-definitions/${definitionId}` && method === 'GET') {
        return jsonResponse(detail);
      }
      if (path === '/api/rule-bindings' && method === 'POST') {
        bindingRequests.push(JSON.parse(String(init?.body)));
        return jsonResponse({ id: bindingId });
      }
      if (
        path === `/api/business-object-definitions/${definitionId}/unpublished` &&
        method === 'PUT'
      ) {
        return jsonResponse(detail);
      }
      throw new Error(`Unexpected fetch: ${method} ${path}`);
    });

    await renderPage(
      `/business-objects?page=1&dialog=edit&recordId=${encodeURIComponent(definitionId)}`,
    );
    await user.click(await screen.findByRole('tab', { name: 'Fields' }));
    await user.click(screen.getByRole('combobox', { name: 'Add rule' }));
    await user.click(await screen.findByRole('option', { name: 'Minimum length' }));

    expect(screen.queryByLabelText('Value')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Minimum (Name: Minimum length)'), '5');
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() =>
      expect(bindingRequests).toEqual([
        expect.objectContaining({
          inputMappings: {
            value: { kind: 'Context', contextKey: 'record.value', literalValues: [] },
            minimum: { kind: 'Literal', contextKey: null, literalValues: ['5'] },
          },
        }),
      ]),
    );
  });

  it('does not offer scalar-only rules for multiple-choice fields', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail({
      fields: [
        {
          id: fieldId,
          fieldKey: 'status',
          label: 'Status',
          order: 0,
          fieldType: 'Choice',
          choiceConfiguration: {
            selectionMode: 'Multiple',
            options: [{ id: optionId, optionKey: 'active', label: 'Active', order: 0 }],
          },
          rules: [],
        },
      ],
    });
    const definitions = {
      ...fieldRuleDefinitions,
      items: [
        {
          ...fieldRuleDefinitions.items[0],
          definitionKey: 'field.scalar-required',
          name: 'Scalar required',
        },
        {
          ...fieldRuleDefinitions.items[0],
          definitionKey: 'field.multiple-required',
          name: 'Multiple required',
          inputs: [{ key: 'value', types: ['Text'], isRequired: true, allowMultiple: true }],
        },
      ],
    };
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(definitions);
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      if (path === `/api/business-object-definitions/${definitionId}`) return jsonResponse(detail);
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage(
      `/business-objects?page=1&dialog=edit&recordId=${encodeURIComponent(definitionId)}`,
    );
    await user.click(await screen.findByRole('tab', { name: 'Fields' }));
    await user.click(screen.getByRole('combobox', { name: 'Add rule' }));

    expect(await screen.findByRole('option', { name: 'Multiple required' })).toBeVisible();
    expect(screen.queryByRole('option', { name: 'Scalar required' })).not.toBeInTheDocument();
  });

  it('preflights a changed multiple-choice rule configuration before any mutation', async () => {
    const user = userEvent.setup();
    const requests: string[] = [];
    const detail = definitionDetail({
      fields: [
        {
          id: fieldId,
          fieldKey: 'status',
          label: 'Status',
          order: 0,
          fieldType: 'Choice',
          choiceConfiguration: {
            selectionMode: 'Single',
            options: [{ id: optionId, optionKey: 'active', label: 'Active', order: 0 }],
          },
          rules: [],
        },
      ],
    });
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, _init?: RequestInit) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      if (path === `/api/business-object-definitions/${definitionId}`) return jsonResponse(detail);
      requests.push(path);
      return jsonResponse({});
    });

    await renderPage(
      `/business-objects?page=1&dialog=edit&recordId=${encodeURIComponent(definitionId)}`,
    );
    await user.click(await screen.findByRole('tab', { name: 'Fields' }));
    await user.click(screen.getByRole('combobox', { name: 'Add rule' }));
    await user.click(await screen.findByRole('option', { name: 'Required' }));
    await user.click(screen.getByLabelText('Selection mode'));
    await user.click(await screen.findByRole('option', { name: 'Multiple' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    expect(await screen.findByText(/Required is not compatible/)).toBeVisible();
    expect(requests).toEqual([]);
  });

  it('shows a required literal error before any binding mutation', async () => {
    const user = userEvent.setup();
    const requests: string[] = [];
    const detail = definitionDetail({
      fields: [
        { id: fieldId, fieldKey: 'name', label: 'Name', order: 0, fieldType: 'Text', rules: [] },
      ],
    });
    const definitions = ruleDefinitionsWithMinimum('Minimum characters');
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(definitions);
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      if (path === `/api/business-object-definitions/${definitionId}`) return jsonResponse(detail);
      requests.push(path);
      return jsonResponse({});
    });

    await renderPage(
      `/business-objects?page=1&dialog=edit&recordId=${encodeURIComponent(definitionId)}`,
    );
    await user.click(await screen.findByRole('tab', { name: 'Fields' }));
    await user.click(screen.getByRole('combobox', { name: 'Add rule' }));
    await user.click(await screen.findByRole('option', { name: 'Minimum length' }));
    await user.click(screen.getByRole('button', { name: 'Save changes' }));

    const input = await screen.findByLabelText('Minimum characters (Name: Minimum length)');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(
      screen
        .getAllByRole('alert')
        .some((alert) => alert.textContent?.includes('Enter a value for Minimum characters.')),
    ).toBe(true);
    expect(requests).toEqual([]);
  });

  it('uses server input labels and unique accessible names for repeated rule values', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail({
      fields: [
        { id: fieldId, fieldKey: 'name', label: 'Name', order: 0, fieldType: 'Text', rules: [] },
      ],
    });
    const definitions = {
      ...fieldRuleDefinitions,
      items: ['A', 'B'].map((suffix) => ({
        definitionKey: `field.limit-${suffix.toLowerCase()}`,
        name: `Limit ${suffix}`,
        origin: 'BuiltIn',
        status: 'Active',
        latestVersion: 1,
        activeVersion: 1,
        output: { type: 'Boolean', cardinality: 'Scalar' },
        inputs: [
          { key: 'value', types: ['Text'], isRequired: true, allowMultiple: false },
          {
            key: 'limit',
            label: 'Maximum characters',
            types: ['Integer'],
            isRequired: true,
            allowMultiple: true,
          },
        ],
      })),
    };
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(definitions);
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      if (path === `/api/business-object-definitions/${definitionId}`) return jsonResponse(detail);
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage(
      `/business-objects?page=1&dialog=edit&recordId=${encodeURIComponent(definitionId)}`,
    );
    await user.click(await screen.findByRole('tab', { name: 'Fields' }));
    for (const name of ['Limit A', 'Limit B']) {
      await user.click(screen.getByRole('combobox', { name: 'Add rule' }));
      await user.click(await screen.findByRole('option', { name }));
    }
    for (const button of screen.getAllByRole('button', { name: 'Add value' })) {
      await user.click(button);
    }

    const inputs = [
      screen.getByLabelText('Maximum characters (Name: Limit A)'),
      screen.getByLabelText('Maximum characters (Name: Limit B)'),
      screen.getByLabelText('Maximum characters (Name: Limit A) 2'),
      screen.getByLabelText('Maximum characters (Name: Limit B) 2'),
    ];
    expect(new Set(inputs.map((input) => input.id)).size).toBe(4);
  });
});

function ruleDefinitionsWithMinimum(label: string) {
  return {
    ...fieldRuleDefinitions,
    items: [
      {
        definitionKey: 'field.minimum-length',
        name: 'Minimum length',
        origin: 'BuiltIn',
        status: 'Active',
        latestVersion: 1,
        activeVersion: 1,
        output: { type: 'Boolean', cardinality: 'Scalar' },
        inputs: [
          { key: 'value', types: ['Text'], isRequired: true, allowMultiple: false },
          { key: 'minimum', label, types: ['Integer'], isRequired: true, allowMultiple: false },
        ],
      },
    ],
  };
}

async function renderPage(path = '/business-objects?page=1') {
  const queryClient = testQueryClient();
  const rootRoute = createRootRouteWithContext<MyRouterContext>()();
  const authenticatedRoute = createRoute({
    getParentRoute: () => rootRoute,
    id: '_authenticated',
    component: Outlet,
  });
  const businessObjectsRoute = createRoute({
    getParentRoute: () => authenticatedRoute,
    path: 'business-objects',
    validateSearch: (search: Record<string, unknown>) => ({
      page: Number(search.page) > 0 ? Number(search.page) : 1,
      ...(search.dialog === 'create' || search.dialog === 'edit' || search.dialog === 'view'
        ? { dialog: search.dialog }
        : {}),
      ...(typeof search.recordId === 'string' && search.recordId
        ? { recordId: search.recordId }
        : {}),
    }),
    component: BusinessObjectsPage,
  });
  const router = createRouter({
    routeTree: rootRoute.addChildren([authenticatedRoute.addChildren([businessObjectsRoute])]),
    context: { queryClient },
    history: createMemoryHistory({ initialEntries: [path] }),
  });

  await act(() => router.load());
  render(
    <QueryClientProvider client={queryClient}>
      <ManagedWindowProvider renderers={managedWindowRenderers}>
        <div className="relative h-dvh w-dvw">
          <RouterProvider router={router} />
          <ManagedWindowHost />
        </div>
      </ManagedWindowProvider>
    </QueryClientProvider>,
  );
  return router;
}

function testQueryClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
}

function jsonResponse(data: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(JSON.stringify(data)),
    json: () => Promise.resolve(data),
  } as Response;
}

function requestPath(input: RequestInfo | URL): string {
  const value = typeof input === 'string' ? input : input.toString();
  return new URL(value, 'https://axis.test').pathname;
}

function isRulesRequest(input: RequestInfo | URL): boolean {
  return requestPath(input) === '/api/rules';
}

function emptyPage() {
  return { items: [], totalCount: 0, page: 1, pageSize: 20 };
}

function pageWith(detail: ReturnType<typeof definitionDetail>) {
  return {
    items: [
      {
        id: detail.id,
        name: detail.name,
        objectKey: detail.objectKey,
        status: detail.status,
        revision: detail.revision,
        latestPublishedVersionNumber: null,
        updatedAt: detail.updatedAt,
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 20,
  };
}

function definitionDetail({
  revision = 3,
  fields,
}: {
  revision?: number;
  fields?: unknown[];
} = {}) {
  return {
    id: definitionId,
    workspaceId: '44444444-4444-4444-8444-444444444444',
    name: 'Customer',
    objectKey: 'customer',
    status: 'Unpublished',
    revision,
    latestPublishedVersionNumber: null,
    createdAt: now,
    updatedAt: now,
    fields: fields ?? [
      {
        id: fieldId,
        fieldKey: 'status',
        label: 'Status',
        order: 0,
        fieldType: 'Choice',
        choiceConfiguration: {
          selectionMode: 'Single',
          options: [{ id: optionId, optionKey: 'active', label: 'Active', order: 0 }],
        },
        rules: [
          {
            id: ruleId,
            bindingId,
            bindingRevision: 1,
          },
        ],
      },
    ],
    latestPublishedVersion: null,
  };
}
