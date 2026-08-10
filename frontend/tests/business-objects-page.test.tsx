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

  it('composes the proving resource workspace from shared page patterns', async () => {
    const detail = definitionDetail();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage();

    const page = document.querySelector<HTMLElement>('[data-slot="page-layout"]');
    const workspace = document.querySelector<HTMLElement>('[data-slot="resource-workspace"]');
    const header = document.querySelector<HTMLElement>('[data-slot="page-header"]');
    const title = await screen.findByRole('heading', { level: 1, name: 'Business objects' });
    const table = screen.getByRole('region', { name: 'Definitions' });
    const recordAction = await within(table).findByRole('button', { name: 'Customer' });
    const createAction = await within(table).findByRole('button', { name: 'New definition' });

    expect(page).toHaveAttribute('data-scroll-mode', 'contained');
    expect(page).toHaveClass(
      'h-full',
      'min-h-0',
      'gap-axis-region',
      'p-axis-page-compact',
      'sm:p-axis-page-default',
      'lg:p-axis-page-wide',
    );
    expect(page?.parentElement).not.toHaveClass(
      'p-axis-page-compact',
      'sm:p-axis-page-default',
      'lg:p-axis-page-wide',
      'font-heading',
    );
    expect(page).toContainElement(workspace);
    expect(header?.parentElement).toBe(workspace);
    expect(title).toHaveAttribute('data-slot', 'page-title');
    expect(title.closest('[data-slot="page-header"]')).toBe(header);
    expect(workspace?.querySelectorAll('[data-slot="page-layout"]')).toHaveLength(0);
    expect(page?.querySelectorAll('[data-slot="page-header"]')).toHaveLength(1);
    expect(page?.querySelectorAll('[data-slot="data-table"]')).toHaveLength(1);
    expect(within(table).queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument();
    expectPageActionSizing(recordAction);
    expectPageActionSizing(createAction);
  });

  it('keeps collection loading and retryable list failure inside the stable page', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail();
    let listReads = 0;
    let resolveInitialList!: (response: Response) => void;
    const initialList = new Promise<Response>((resolve) => {
      resolveInitialList = resolve;
    });
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
      if (path === '/api/business-object-definitions') {
        listReads += 1;
        return listReads === 1 ? initialList : jsonResponse(pageWith(detail));
      }
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage();

    const page = document.querySelector<HTMLElement>('[data-slot="page-layout"]');
    const table = screen.getByRole('region', { name: 'Definitions' });
    expect(table).toHaveAttribute('aria-busy', 'true');
    expect(within(table).getAllByText('Loading rows')).not.toHaveLength(0);

    await act(async () => resolveInitialList(jsonResponse({ title: 'List failed' }, 500)));

    const error = await within(table).findByRole('alert');
    expect(error).toHaveTextContent('Unable to load business objects');
    expect(error).toHaveTextContent('Check the connection and try again.');
    expect(page).toContainElement(error);
    await user.click(within(error).getByRole('button', { name: 'Retry' }));

    expect(await within(table).findByRole('button', { name: 'Customer' })).toBeVisible();
    expect(table).not.toHaveAttribute('aria-busy');
    expect(listReads).toBe(2);
    expect(document.querySelector('[data-slot="page-layout"]')).toBe(page);
  });

  it('keeps the empty collection inside the resource workspace with its authorized next action', async () => {
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
      if (path === '/api/business-object-definitions') return jsonResponse(emptyPage());
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage();

    const workspace = document.querySelector('[data-slot="resource-workspace"]');
    const table = screen.getByRole('region', { name: 'Definitions' });
    expect(workspace).toContainElement(table);
    await waitFor(() => expect(table).not.toHaveAttribute('aria-busy'));
    expect(within(table).getByText('No business objects')).toBeVisible();
    expect(
      within(table).getByText(
        'Start a definition to capture the structure and rules for a reusable business object.',
      ),
    ).toBeVisible();
    expect(within(table).getByRole('button', { name: 'New definition' })).toBeEnabled();
  });

  it('hides create and consumes a denied create deep link', async () => {
    let resolveActions!: (response: Response) => void;
    const actionsResponse = new Promise<Response>((resolve) => {
      resolveActions = resolve;
    });
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === '/api/business-object-definitions/actions') {
        return actionsResponse;
      }
      if (path === '/api/business-object-definitions') return jsonResponse(emptyPage());
      throw new Error(`Unexpected fetch: ${path}`);
    });

    const router = await renderPage('/business-objects?page=1&dialog=create');

    await waitFor(() =>
      expect(
        vi
          .mocked(fetch)
          .mock.calls.some(
            ([input]) => requestPath(input) === '/api/business-object-definitions/actions',
          ),
      ).toBe(true),
    );
    expect(screen.queryByRole('button', { name: 'New definition' })).not.toBeInTheDocument();
    expect(
      screen.queryByRole('dialog', { name: 'Define business object' }),
    ).not.toBeInTheDocument();
    await act(async () => resolveActions(jsonResponse({ canStartCreate: false })));
    await waitFor(() => expect(router.state.location.search).toEqual({ page: 1 }));
    expect(screen.queryByRole('button', { name: 'New definition' })).not.toBeInTheDocument();
    expect(
      screen.queryByRole('dialog', { name: 'Define business object' }),
    ).not.toBeInTheDocument();
  });

  it('keeps a create deep link retryable while authorization is unavailable', async () => {
    const user = userEvent.setup();
    let actionReads = 0;
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === '/api/business-object-definitions/actions') {
        actionReads += 1;
        return actionReads === 1
          ? jsonResponse({ title: 'Unavailable' }, 503)
          : jsonResponse({ canStartCreate: true });
      }
      if (path === '/api/business-object-definitions') return jsonResponse(emptyPage());
      throw new Error(`Unexpected fetch: ${path}`);
    });

    const router = await renderPage('/business-objects?page=1&dialog=create');

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Business object actions are temporarily unavailable');
    expect(router.state.location.search).toEqual({ page: 1, dialog: 'create' });
    const retry = within(notice).getByRole('button', { name: 'Retry' });
    expectPageActionSizing(retry);
    await user.click(retry);
    expect(await screen.findByRole('dialog', { name: 'Define business object' })).toBeVisible();
    await waitFor(() => expect(router.state.location.search).toEqual({ page: 1 }));
  });

  it('uses server actions to deny business object save and publish controls', async () => {
    const deniedDetail = {
      ...definitionDetail(),
      actions: { canSave: false, canPublish: false },
    };
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (path === '/api/business-object-definitions/actions') {
        return jsonResponse({ canStartCreate: true });
      }
      if (path === `/api/business-object-definitions/${definitionId}`) {
        return jsonResponse(deniedDetail);
      }
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(deniedDetail));
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage(
      `/business-objects?page=1&dialog=edit&recordId=${encodeURIComponent(definitionId)}`,
    );

    const dialog = await screen.findByRole('dialog', { name: 'Customer' });
    expect(within(dialog).queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Publish' })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Close' })).toBeEnabled();
  });

  it.each([
    403, 404,
  ])('keeps a %s detail response non-disclosing and non-interactive', async (status) => {
    const detail = definitionDetail();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
      if (path === `/api/business-object-definitions/${definitionId}`) {
        return jsonResponse({ detail: 'Secret authorization detail' }, status);
      }
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Customer' }));

    const dialog = await screen.findByRole('dialog', { name: 'Business object definition' });
    const notice = await within(dialog).findByRole('alert');
    expect(notice).toHaveTextContent('Action unavailable');
    expect(notice).toHaveTextContent('This action is not available.');
    expect(notice).not.toHaveTextContent('Secret authorization detail');
    expect(within(dialog).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Publish' })).not.toBeInTheDocument();
    expect(
      vi.mocked(fetch).mock.calls.some(([, init]) => init?.method && init.method !== 'GET'),
    ).toBe(false);
  });

  it('retries a temporarily unavailable business object detail without exposing controls', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail();
    let detailAvailable = false;
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
      if (path === `/api/business-object-definitions/${definitionId}`) {
        return detailAvailable
          ? jsonResponse(detail)
          : jsonResponse({ detail: 'Private dependency detail' }, 503);
      }
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage();
    const recordButton = await screen.findByRole('button', { name: 'Customer' });
    await act(async () => recordButton.click());

    const dialog = await screen.findByRole('dialog', { name: 'Business object definition' });
    const notice = await within(dialog).findByRole('alert');
    expect(notice).toHaveTextContent('Business object temporarily unavailable');
    expect(notice).not.toHaveTextContent('Private dependency detail');
    expect(within(dialog).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument();
    detailAvailable = true;
    await user.click(within(notice).getByRole('button', { name: 'Retry' }));
    expect(await within(dialog).findByLabelText('Name')).toHaveValue('Customer');
    expect(within(dialog).getByRole('button', { name: 'Save changes' })).toBeDisabled();
  });

  it.each([
    [403, 'This action is not available.'],
    [404, 'This action is not available.'],
    [503, 'Authorization is temporarily unavailable. Try again.'],
  ])('maps a %s save failure to safe copy', async (status, expectedMessage) => {
    const user = userEvent.setup();
    const detail = definitionDetail();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      if (path === `/api/business-object-definitions/${definitionId}`) {
        return jsonResponse(detail);
      }
      if (
        path === `/api/business-object-definitions/${definitionId}/unpublished` &&
        init?.method === 'PUT'
      ) {
        return jsonResponse({ detail: 'Private mutation detail' }, status);
      }
      throw new Error(`Unexpected fetch: ${init?.method ?? 'GET'} ${path}`);
    });

    await renderPage();
    await user.click(await screen.findByRole('button', { name: 'Customer' }));
    const dialog = await screen.findByRole('dialog', { name: 'Customer' });
    const name = within(dialog).getByLabelText('Name');
    await user.clear(name);
    await user.type(name, 'Updated customer');
    await user.click(within(dialog).getByRole('button', { name: 'Save changes' }));

    const notice = await within(dialog).findByRole('alert');
    expect(notice).toHaveTextContent(expectedMessage);
    expect(notice).not.toHaveTextContent('Private mutation detail');
  });

  it('keeps form labels scoped to each concurrent definition window', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
      if (path === '/api/business-object-definitions') return jsonResponse(pageWith(detail));
      if (path === `/api/business-object-definitions/${definitionId}`) {
        return jsonResponse(detail);
      }
      throw new Error(`Unexpected fetch: ${path}`);
    });

    await renderPage();
    await user.click(await screen.findByRole('button', { name: 'Customer' }));
    await user.click(screen.getByRole('button', { name: 'New definition' }));

    const editDialog = await screen.findByRole('dialog', { name: 'Customer' });
    const createDialog = await screen.findByRole('dialog', { name: 'Define business object' });
    const inputs = (
      [
        [editDialog, 'Name'],
        [editDialog, 'Object key'],
        [createDialog, 'Name'],
        [createDialog, 'Object key'],
      ] as const
    ).map(([dialog, label]) => {
      const input = within(dialog).getByLabelText(label);
      const labelElement = within(dialog).getByText(label, { selector: 'label' });
      expect(dialog).toContainElement(input);
      expect(labelElement).toHaveAttribute('for', input.id);
      return input;
    });

    expect(new Set(inputs.map((input) => input.id)).size).toBe(inputs.length);
  });

  it('prefetches a definition and opens its managed window without another detail request', async () => {
    const user = userEvent.setup();
    const detail = definitionDetail();
    let detailRequests = 0;
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const path = requestPath(input);
      if (isRulesRequest(input)) return jsonResponse(fieldRuleDefinitions);
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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
      if (isDefinitionActionsRequest(input)) return allowedDefinitionActions();
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

function isDefinitionActionsRequest(input: RequestInfo | URL): boolean {
  return requestPath(input) === '/api/business-object-definitions/actions';
}

function allowedDefinitionActions() {
  return jsonResponse({ canStartCreate: true });
}

function emptyPage() {
  return { items: [], totalCount: 0, page: 1, pageSize: 20 };
}

function expectPageActionSizing(action: HTMLElement) {
  expect(action).toHaveAttribute('data-slot', 'button');
  expect(action).toHaveClass(
    'min-h-axis-touch-target',
    'min-w-axis-touch-target',
    'sm:min-h-axis-compact-control',
    'sm:min-w-axis-compact-control',
  );
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
    actions: { canSave: true, canPublish: true },
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
