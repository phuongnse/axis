import { QueryClient } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { objectDefinitionQueryKeys } from '@/features/objects/api';
import { BusinessObjectsPage } from '@/features/objects/components/BusinessObjectsPage';
import { loadObjectDefinitionsRoute } from '@/routes/_authenticated/objects';
import { renderWithRouter } from './render-with-router';

const definitionId = '33333333-3333-4333-8333-333333333333';
const workspaceId = '44444444-4444-4444-8444-444444444444';
const fieldId = '55555555-5555-4555-8555-555555555555';
const versionId = '66666666-6666-4666-8666-666666666666';
const now = '2026-07-07T00:00:00Z';

function jsonResponse(data: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(JSON.stringify(data)),
    json: () => Promise.resolve(data),
  } as unknown as Response;
}

function requestPath(url: string): string {
  return new URL(url, 'https://axis.test').pathname;
}

describe('BusinessObjectsPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('loads the initial definition list through the route loader cache', async () => {
    const page = {
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
    };
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    });

    vi.mocked(fetch).mockResolvedValue(jsonResponse(page));

    await loadObjectDefinitionsRoute({ queryClient });
    await loadObjectDefinitionsRoute({ queryClient });

    expect(fetch).toHaveBeenCalledTimes(1);
    expect(queryClient.getQueryData(objectDefinitionQueryKeys.list(1, 20))).toEqual(page);
  });

  it('prefetches definition detail on list item intent and reuses the cache on selection', async () => {
    const user = userEvent.setup();
    const detail = draftDetail({ name: 'Customer', objectKey: 'customer', draftVersion: 3 });
    let detailRequestCount = 0;

    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = init?.method ?? 'GET';

      if (url.includes('/api/object-definitions?')) {
        return jsonResponse({
          items: [
            {
              id: detail.id,
              name: detail.name,
              objectKey: detail.objectKey,
              status: detail.status,
              draftVersion: detail.draftVersion,
              latestPublishedVersionNumber: null,
              updatedAt: detail.updatedAt,
            },
          ],
          totalCount: 1,
          page: 1,
          pageSize: 20,
        });
      }

      if (method === 'GET' && url.endsWith(`/api/object-definitions/${definitionId}`)) {
        detailRequestCount += 1;
        return jsonResponse(detail);
      }

      return Promise.reject(new Error(`Unexpected fetch: ${method} ${url}`));
    });

    await renderWithRouter(<BusinessObjectsPage />, { path: '/objects' });

    const listItem = await screen.findByRole('button', { name: /Customer/ });
    await user.hover(listItem);

    await waitFor(() => expect(detailRequestCount).toBe(1));
    await user.click(listItem);

    expect(await screen.findByLabelText('Object key')).toHaveValue('customer');
    expect(screen.queryByText('Draft 3')).not.toBeInTheDocument();
    await waitFor(() => expect(detailRequestCount).toBe(1));
  });

  it('renders the definition workflow as a connected timeline', async () => {
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.includes('/api/object-definitions?')) {
        return jsonResponse({ items: [], totalCount: 0, page: 1, pageSize: 20 });
      }

      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<BusinessObjectsPage />, { path: '/objects' });

    const workflow = await screen.findByRole('region', {
      name: 'Business object definition workflow',
    });
    const steps = within(workflow).getAllByRole('listitem');

    expect(steps).toHaveLength(3);
    expect(steps[0]).toHaveClass(
      'grid',
      'grid-cols-[1.75rem_minmax(0,1fr)]',
      'before:top-7',
      'before:bg-border',
    );
    expect(steps[0]).toHaveTextContent('1');
    expect(steps[0]).toHaveTextContent('Create draft identity');
    expect(steps[1]).toHaveTextContent('2');
    expect(steps[1]).toHaveTextContent('Shape the contract');
    expect(steps[2]).toHaveTextContent('3');
    expect(steps[2]).toHaveTextContent('Publish a stable version');
    expect(steps[2]).toHaveClass('last:before:hidden');
    expect(steps[0]).not.toHaveClass('rounded-lg', 'border', 'bg-card');
  });

  it('creates, saves, and publishes a business object definition draft', async () => {
    const user = userEvent.setup();
    const listItems: unknown[] = [];
    const requests: { method: string; url: string; body?: unknown }[] = [];

    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = init?.method ?? 'GET';
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
      requests.push({ method, url, body });

      if (url.includes('/api/object-definitions?')) {
        return jsonResponse({
          items: listItems,
          totalCount: listItems.length,
          page: 1,
          pageSize: 20,
        });
      }

      if (method === 'POST' && url.endsWith('/api/object-definitions')) {
        const detail = draftDetail({
          name: body.name,
          objectKey: deriveObjectKey(body.name),
          draftVersion: 1,
        });
        listItems.splice(0, listItems.length, {
          id: detail.id,
          name: detail.name,
          objectKey: detail.objectKey,
          status: detail.status,
          draftVersion: detail.draftVersion,
          latestPublishedVersionNumber: null,
          updatedAt: detail.updatedAt,
        });
        return jsonResponse(detail, 201);
      }

      if (method === 'PUT' && url.endsWith(`/api/object-definitions/${definitionId}/draft`)) {
        const detail = draftDetail({
          name: body.name,
          objectKey: 'customer',
          draftVersion: 2,
          fields: body.fields,
        });
        listItems.splice(0, listItems.length, {
          id: detail.id,
          name: detail.name,
          objectKey: detail.objectKey,
          status: detail.status,
          draftVersion: detail.draftVersion,
          latestPublishedVersionNumber: null,
          updatedAt: detail.updatedAt,
        });
        return jsonResponse(detail);
      }

      if (method === 'POST' && url.endsWith(`/api/object-definitions/${definitionId}/publish`)) {
        const detail = {
          ...draftDetail({
            name: 'Customer',
            objectKey: 'customer',
            draftVersion: 2,
            fields: [
              {
                id: fieldId,
                fieldKey: 'name',
                label: 'Name',
                order: 0,
              },
            ],
          }),
          status: 'Published',
          latestPublishedVersionNumber: 1,
          latestPublishedVersion: {
            id: versionId,
            versionNumber: 1,
            publishedByUserId: '77777777-7777-4777-8777-777777777777',
            publishedAt: now,
            fields: [
              {
                id: fieldId,
                fieldKey: 'name',
                label: 'Name',
                order: 0,
              },
            ],
          },
        };
        listItems.splice(0, listItems.length, {
          id: detail.id,
          name: detail.name,
          objectKey: detail.objectKey,
          status: detail.status,
          draftVersion: detail.draftVersion,
          latestPublishedVersionNumber: 1,
          updatedAt: detail.updatedAt,
        });
        return jsonResponse(detail);
      }

      return Promise.reject(new Error(`Unexpected fetch: ${method} ${url}`));
    });

    await renderWithRouter(<BusinessObjectsPage />, { path: '/objects' });

    expect(await screen.findAllByText('No business objects')).not.toHaveLength(0);
    expect(
      screen.getByText(
        'Create a draft to define the structure and rules for a reusable business object.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add field' })).toBeDisabled();
    expect(
      within(screen.getByRole('form', { name: 'Define business object' })).getByRole('button', {
        name: 'Create draft',
      }),
    ).toBeEnabled();
    expect(
      within(screen.getByRole('form', { name: 'Define business object' })).queryByRole('button', {
        name: 'Publish',
      }),
    ).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('Name'), 'Customer');
    expect(screen.getByLabelText('Object key')).toHaveValue('customer');
    expect(screen.getByLabelText('Object key')).toHaveAttribute('readonly');
    expect(screen.queryByRole('button', { name: 'Managed by the system' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Create draft' }));

    expect(await screen.findByText('Draft created')).toBeInTheDocument();
    expect(screen.getAllByText('Draft', { exact: true })).toHaveLength(1);
    expect(screen.queryByText('Draft 1')).not.toBeInTheDocument();
    expect(
      screen.getByText('Add at least one field before publishing this draft.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add field' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Add field' })).toHaveClass('bg-primary');
    expect(
      within(screen.getByRole('form', { name: 'Customer' })).getByRole('button', {
        name: 'Save draft',
      }),
    ).toBeEnabled();
    expect(
      within(screen.getByRole('form', { name: 'Customer' })).getByRole('button', {
        name: 'Save draft',
      }),
    ).toHaveClass('bg-primary');
    expect(
      within(screen.getByRole('form', { name: 'Customer' })).getByRole('button', {
        name: 'Publish',
      }),
    ).toBeDisabled();
    expect(
      within(screen.getByRole('form', { name: 'Customer' })).queryByRole('button', {
        name: 'Create draft',
      }),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Add field' }));
    await user.type(screen.getByLabelText('Field key'), 'name');
    await user.type(screen.getByLabelText('Label'), 'Name');
    expect(
      within(screen.getByRole('form', { name: 'Customer' })).getByRole('button', {
        name: 'Publish',
      }),
    ).toHaveClass('bg-primary');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    expect(await screen.findByText('Draft saved')).toBeInTheDocument();
    expect(screen.getAllByText('Draft', { exact: true })).toHaveLength(1);
    expect(screen.queryByText('Draft 2')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Publish' }));

    expect(await screen.findAllByText('Published')).not.toHaveLength(0);
    expect(screen.getAllByText('Version 1')).not.toHaveLength(0);
    expect(screen.getByRole('button', { name: 'Add field' })).not.toHaveClass('bg-primary');
    expect(
      within(screen.getByRole('form', { name: 'Customer' })).queryByRole('button', {
        name: 'Publish',
      }),
    ).not.toBeInTheDocument();

    expect(requests.map((request) => `${request.method} ${requestPath(request.url)}`)).toContain(
      'POST /api/object-definitions',
    );
    expect(requests.map((request) => `${request.method} ${requestPath(request.url)}`)).toContain(
      `PUT /api/object-definitions/${definitionId}/draft`,
    );
    expect(requests.map((request) => `${request.method} ${requestPath(request.url)}`)).toContain(
      `POST /api/object-definitions/${definitionId}/publish`,
    );
    expect(
      requests.find(
        (request) =>
          request.method === 'POST' && requestPath(request.url) === '/api/object-definitions',
      )?.body,
    ).not.toHaveProperty('objectKey');
    const saveDraftRequest = requests.find(
      (request) =>
        request.method === 'PUT' &&
        requestPath(request.url) === `/api/object-definitions/${definitionId}/draft`,
    );
    expect(saveDraftRequest?.body).toMatchObject({ expectedDraftVersion: 1, name: 'Customer' });
    expect(saveDraftRequest?.body).not.toHaveProperty('objectKey');
    expect(
      requests.find(
        (request) =>
          request.method === 'POST' &&
          requestPath(request.url) === `/api/object-definitions/${definitionId}/publish`,
      )?.body,
    ).toEqual({ expectedDraftVersion: 2 });
  });

  it('shows server problem details when draft creation fails', async () => {
    const user = userEvent.setup();

    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = init?.method ?? 'GET';

      if (url.includes('/api/object-definitions?')) {
        return jsonResponse({ items: [], totalCount: 0, page: 1, pageSize: 20 });
      }

      if (method === 'POST' && url.endsWith('/api/object-definitions')) {
        return jsonResponse(
          {
            type: 'urn:axis:problem:objects.objectDefinitionInvalid',
            title: 'One or more validation errors occurred.',
            status: 400,
            errors: {
              name: ['Object names must be unique in this workspace.'],
            },
            code: 'objects.objectDefinitionInvalid',
          },
          400,
        );
      }

      return Promise.reject(new Error(`Unexpected fetch: ${method} ${url}`));
    });

    await renderWithRouter(<BusinessObjectsPage />, { path: '/objects' });

    await user.type(await screen.findByLabelText('Name'), 'Application');
    await user.click(screen.getByRole('button', { name: 'Create draft' }));

    const alertTitle = await within(
      screen.getByRole('form', { name: 'Define business object' }),
    ).findByText('Check required values');
    const alert = alertTitle.closest('[role="alert"]');
    if (!alert) throw new Error('Expected scoped identity alert');

    expect(alert).toHaveClass('bg-destructive/15');
    expect(screen.getByText('Check required values')).toBeInTheDocument();
    expect(
      within(alert).getByText('Object names must be unique in this workspace.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.queryByText('Something went wrong, please try again')).not.toBeInTheDocument();
  });

  it('maximizes and restores the fields editor only after a draft exists', async () => {
    const user = userEvent.setup();

    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = init?.method ?? 'GET';
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;

      if (url.includes('/api/object-definitions?')) {
        return jsonResponse({ items: [], totalCount: 0, page: 1, pageSize: 20 });
      }

      if (method === 'POST' && url.endsWith('/api/object-definitions')) {
        return jsonResponse(
          draftDetail({ name: body.name, objectKey: deriveObjectKey(body.name) }),
          201,
        );
      }

      return Promise.reject(new Error(`Unexpected fetch: ${method} ${url}`));
    });

    await renderWithRouter(<BusinessObjectsPage />, { path: '/objects' });
    await screen.findByText('No business objects');

    expect(screen.getByRole('button', { name: 'Add field' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Expand editor' })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('Name'), 'Customer');
    await user.click(screen.getByRole('button', { name: 'Create draft' }));
    expect(await screen.findByText('Draft created')).toBeInTheDocument();
    expect(
      screen.getByText('Fields define the text data this business object captures.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add field' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Expand editor' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: /field actions/i })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Expand editor' }));

    expect(screen.getByRole('button', { name: 'Restore layout' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('region', { name: 'Fields' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Definitions' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Publish readiness' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Expand editor' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Restore layout' }));
    expect(screen.getByRole('button', { name: 'Expand editor' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Definitions' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Publish readiness' })).not.toBeInTheDocument();
  });

  it('scopes field validation to affected field controls', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = init?.method ?? 'GET';
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;

      if (url.includes('/api/object-definitions?')) {
        return jsonResponse({ items: [], totalCount: 0, page: 1, pageSize: 20 });
      }

      if (method === 'POST' && url.endsWith('/api/object-definitions')) {
        return jsonResponse(
          draftDetail({ name: body.name, objectKey: deriveObjectKey(body.name) }),
          201,
        );
      }

      return Promise.reject(new Error(`Unexpected fetch: ${method} ${url}`));
    });

    await renderWithRouter(<BusinessObjectsPage />, { path: '/objects' });
    await user.type(await screen.findByLabelText('Name'), 'Customer');
    await user.click(screen.getByRole('button', { name: 'Create draft' }));
    await screen.findByText('Draft created');

    await user.click(screen.getByRole('button', { name: 'Add field' }));
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    const editorForm = screen.getByRole('form', { name: 'Customer' });
    const fieldKey = screen.getByLabelText('Field key');
    const label = screen.getByLabelText('Label');
    const editorAlertTitle = within(editorForm).getByText('Check required values');
    const editorAlert = editorAlertTitle.closest('[role="alert"]');
    if (!editorAlert) throw new Error('Expected scoped field validation alert');

    expect(editorAlert).toHaveClass('bg-destructive/15');
    expect(editorAlert).toHaveTextContent(
      'Field keys must start with a lowercase letter and be unique.',
    );
    expect(editorAlert).toHaveTextContent('Field labels are required.');
    expect(fieldKey).toHaveAttribute('aria-invalid', 'true');
    expect(label).toHaveAttribute('aria-invalid', 'true');
  });

  it('marks every field with a duplicated key as invalid', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = init?.method ?? 'GET';
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;

      if (url.includes('/api/object-definitions?')) {
        return jsonResponse({ items: [], totalCount: 0, page: 1, pageSize: 20 });
      }

      if (method === 'POST' && url.endsWith('/api/object-definitions')) {
        return jsonResponse(
          draftDetail({ name: body.name, objectKey: deriveObjectKey(body.name) }),
          201,
        );
      }

      return Promise.reject(new Error(`Unexpected fetch: ${method} ${url}`));
    });

    await renderWithRouter(<BusinessObjectsPage />, { path: '/objects' });
    await user.type(await screen.findByLabelText('Name'), 'Customer');
    await user.click(screen.getByRole('button', { name: 'Create draft' }));
    await screen.findByText('Draft created');

    await user.click(screen.getByRole('button', { name: 'Add field' }));
    await user.click(screen.getByRole('button', { name: 'Add field' }));

    const fieldKeys = screen.getAllByLabelText('Field key');
    const labels = screen.getAllByLabelText('Label');
    await user.type(fieldKeys[0], 'customer_name');
    await user.type(labels[0], 'Customer name');
    await user.type(fieldKeys[1], 'customer_name');
    await user.type(labels[1], 'Duplicate customer name');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    const editorForm = screen.getByRole('form', { name: 'Customer' });
    const editorAlertTitle = within(editorForm).getByText('Check required values');
    const editorAlert = editorAlertTitle.closest('[role="alert"]');
    if (!editorAlert) throw new Error('Expected scoped duplicate-key validation alert');

    expect(editorAlert).toHaveTextContent(
      'Field keys must start with a lowercase letter and be unique.',
    );
    expect(fieldKeys[0]).toHaveAttribute('aria-invalid', 'true');
    expect(fieldKeys[1]).toHaveAttribute('aria-invalid', 'true');
  });

  it('disables publication until draft fields are present', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      const method = init?.method ?? 'GET';
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;

      if (url.includes('/api/object-definitions?')) {
        return jsonResponse({ items: [], totalCount: 0, page: 1, pageSize: 20 });
      }

      if (method === 'POST' && url.endsWith('/api/object-definitions')) {
        return jsonResponse(
          draftDetail({ name: body.name, objectKey: deriveObjectKey(body.name) }),
          201,
        );
      }

      return Promise.reject(new Error(`Unexpected fetch: ${method} ${url}`));
    });

    await renderWithRouter(<BusinessObjectsPage />, { path: '/objects' });
    await user.type(await screen.findByLabelText('Name'), 'Customer');
    expect(screen.getByLabelText('Object key')).toHaveValue('customer');
    await user.click(screen.getByRole('button', { name: 'Create draft' }));
    await screen.findByText('Draft created');

    expect(screen.getByRole('button', { name: 'Publish' })).toBeDisabled();
    expect(
      screen.getByText('Add at least one field before publishing this draft.'),
    ).toBeInTheDocument();
    await waitFor(() => {
      const publishCalls = vi.mocked(fetch).mock.calls.filter(([input, init]) => {
        const url = typeof input === 'string' ? input : input.toString();
        return init?.method === 'POST' && url.includes('/publish');
      });
      expect(publishCalls).toHaveLength(0);
    });
  });
});

function deriveObjectKey(name: string): string {
  return (
    name
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '')
      .replace(/_{2,}/g, '_') || 'object'
  );
}

function draftDetail({
  name,
  objectKey,
  draftVersion = 1,
  fields = [],
}: {
  name: string;
  objectKey: string;
  draftVersion?: number;
  fields?: unknown[];
}) {
  return {
    id: definitionId,
    workspaceId,
    name,
    objectKey,
    status: 'Draft',
    draftVersion,
    latestPublishedVersionNumber: null,
    createdAt: now,
    updatedAt: now,
    fields: fields.map((field, index) => ({
      id: index === 0 ? fieldId : `55555555-5555-4555-8555-${String(index).padStart(12, '0')}`,
      ...field,
    })),
    latestPublishedVersion: null,
  };
}
