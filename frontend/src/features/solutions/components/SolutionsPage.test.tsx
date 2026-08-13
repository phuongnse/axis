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
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import { ManagedWindowProvider } from '@/components/shared/ManagedWindowManager';
import { ApiError } from '@/lib/api';
import type { MyRouterContext } from '@/routes/__root';
import { installSolutionVersion, publishSolutionVersion } from '../api';
import { solutionsManagedWindowRenderers } from '../managed-windows';
import { SolutionsPage } from './SolutionsPage';

const api = vi.hoisted(() => ({
  versions: vi.fn(),
  version: vi.fn(),
  installations: vi.fn(),
  operation: vi.fn(),
  publish: vi.fn(),
  install: vi.fn(),
  resume: vi.fn(),
}));

vi.mock('../api', () => ({
  solutionPackageMaxBytes: 10 * 1024 * 1024,
  solutionQueryKeys: {
    all: ['solutions'],
    versions: () => ['solutions', 'versions'],
    version: (id: string) => ['solutions', 'versions', id],
    installations: () => ['solutions', 'installations'],
    operation: (id: string) => ['solutions', 'operation', id],
  },
  solutionVersionsQueryOptions: () => ({
    queryKey: ['solutions', 'versions'],
    queryFn: api.versions,
  }),
  solutionVersionQueryOptions: (id: string) => ({
    queryKey: ['solutions', 'versions', id],
    queryFn: () => api.version(id),
    enabled: Boolean(id),
  }),
  solutionInstallationsQueryOptions: () => ({
    queryKey: ['solutions', 'installations'],
    queryFn: api.installations,
  }),
  solutionOperationQueryOptions: (id: string) => ({
    queryKey: ['solutions', 'operation', id],
    queryFn: () => api.operation(id),
    enabled: Boolean(id),
  }),
  publishSolutionVersion: api.publish,
  installSolutionVersion: api.install,
  resumeSolutionOperation: api.resume,
}));

describe('SolutionsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.versions.mockResolvedValue([]);
    api.version.mockResolvedValue(version());
    api.installations.mockResolvedValue([]);
    api.publish.mockReset().mockResolvedValue({ version: version(), isRetry: false });
    api.install.mockResolvedValue({
      operation: {
        id: 'operation-1',
        installationId: 'installation-1',
        status: 'Pending',
        steps: [],
      },
      isRetry: false,
    });
    api.operation.mockResolvedValue({
      id: 'operation-1',
      installationId: 'installation-1',
      status: 'Succeeded',
      steps: [],
    });
  });

  it('composes one solution collection with its publish action in the table toolbar', async () => {
    api.versions.mockResolvedValue([version()]);
    api.installations.mockResolvedValue([installation()]);

    await renderPage();

    const collection = await screen.findByRole('region', { name: 'Solution versions' });
    const surface = document.querySelector('[data-axis-surface-id="solution-delivery"]');
    expect(surface).toHaveAttribute('data-axis-surface-contract', 'resource-workspace');
    expect(screen.getAllByRole('table')).toHaveLength(1);
    expect(screen.queryByRole('tab')).not.toBeInTheDocument();
    expect(within(collection).getByRole('button', { name: 'Publish package' })).toBeVisible();
    expect(within(collection).getByText('Installed')).toBeVisible();
    expect(within(collection).getByText('Compliant')).toBeVisible();
  });

  it('rejects an oversized local package before upload', async () => {
    const user = userEvent.setup();
    await renderPage();
    await user.click(screen.getByRole('button', { name: 'Publish package' }));
    const file = new File(['x'], 'too-large.axis-solution', {
      type: 'application/vnd.dsse.envelope.v1+json',
    });
    Object.defineProperty(file, 'size', { value: 10 * 1024 * 1024 + 1 });

    await user.upload(await screen.findByLabelText('Signed solution package'), file);

    expect(screen.getByText('The signed package must be 10 MiB or smaller.')).toBeInTheDocument();
    expect(publishSolutionVersion).not.toHaveBeenCalled();
  });

  it('publishes safe metadata then installs from a focused release window', async () => {
    const user = userEvent.setup();
    await renderPage();
    await user.click(screen.getByRole('button', { name: 'Publish package' }));
    const file = new File(['raw-package-secret'], 'release.axis-solution', {
      type: 'application/vnd.dsse.envelope.v1+json',
    });
    await user.upload(await screen.findByLabelText('Signed solution package'), file);
    await user.click(
      within(activeManagedWindow()).getByRole('button', { name: 'Publish package' }),
    );
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Publish package' }),
    );

    await waitFor(() => expect(publishSolutionVersion).toHaveBeenCalled());
    expect(await screen.findByText('Solution version published')).toBeInTheDocument();
    expect(screen.getByText('policy')).toBeInTheDocument();
    expect(screen.getByText('definition')).toBeInTheDocument();
    expect(screen.queryByText('raw-package-secret')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'View release' }));
    await user.click(await screen.findByRole('button', { name: 'Install version' }));
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Install version' }),
    );

    await waitFor(() =>
      expect(installSolutionVersion).toHaveBeenCalledWith('version-1', expect.stringMatching(/.+/)),
    );
    expect(await screen.findByText('Installation operation ready')).toBeInTheDocument();
  });

  it('retains the local package and retries after publisher trust recovers', async () => {
    const user = userEvent.setup();
    const file = new File(['raw-package-secret'], 'retry.axis-solution', {
      type: 'application/vnd.dsse.envelope.v1+json',
    });
    api.publish
      .mockRejectedValueOnce(
        new ApiError(409, {
          code: 'solutions.package.publisher_untrusted',
          detail: 'publisher-private-diagnostic',
        }),
      )
      .mockResolvedValueOnce({ version: version(), isRetry: false });

    await renderPage();
    await user.click(screen.getByRole('button', { name: 'Publish package' }));
    await user.upload(await screen.findByLabelText('Signed solution package'), file);
    await user.click(
      within(activeManagedWindow()).getByRole('button', { name: 'Publish package' }),
    );
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Publish package' }),
    );

    expect(await screen.findByText('Publisher trust unavailable')).toBeInTheDocument();
    expect(screen.getByText('Selected retry.axis-solution (18 B)')).toBeInTheDocument();
    expect(screen.queryByText('publisher-private-diagnostic')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(publishSolutionVersion).toHaveBeenCalledTimes(2));
    expect(await screen.findByText('Solution version published')).toBeInTheDocument();
  });

  it('keeps a generic publish conflict mapped to solution changed', async () => {
    const user = userEvent.setup();
    api.publish.mockRejectedValueOnce(
      new ApiError(409, {
        code: 'solutions.version.identity_conflict',
        detail: 'conflicting-package-private-diagnostic',
      }),
    );

    await renderPage();
    await user.click(screen.getByRole('button', { name: 'Publish package' }));
    await user.upload(
      await screen.findByLabelText('Signed solution package'),
      new File(['package'], 'conflict.axis-solution', {
        type: 'application/vnd.dsse.envelope.v1+json',
      }),
    );
    await user.click(
      within(activeManagedWindow()).getByRole('button', { name: 'Publish package' }),
    );
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Publish package' }),
    );

    expect(await screen.findByText('Solution changed')).toBeInTheDocument();
    expect(
      screen.getByText('Refresh the authoritative state before retrying.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('conflicting-package-private-diagnostic')).not.toBeInTheDocument();
  });

  it('reopens and resumes a durable failed installation after reload', async () => {
    const user = userEvent.setup();
    api.versions.mockResolvedValue([version()]);
    api.installations.mockResolvedValue([
      installation({ operationStatus: 'Failed', provisioningStatus: 'Failed' }),
    ]);
    api.operation.mockResolvedValue({
      id: 'operation-reload',
      installationId: 'installation-1',
      status: 'Failed',
      steps: [],
    });
    api.resume.mockResolvedValue({
      id: 'operation-reload',
      installationId: 'installation-1',
      status: 'Pending',
      steps: [],
    });

    await renderPage();
    await user.click(
      await screen.findByRole('button', { name: 'View installation for cases 1.0.0' }),
    );

    expect(
      await screen.findByRole('heading', { name: 'Installation · cases 1.0.0' }),
    ).toBeVisible();
    await waitFor(() => expect(api.operation).toHaveBeenCalledWith('operation-reload'));
    await user.click(await screen.findByRole('button', { name: 'Resume operation' }));
    await waitFor(() => expect(api.resume).toHaveBeenCalled());
    expect(await screen.findByText('Resume accepted')).toBeInTheDocument();
  });

  it('shows a revoked partial installation as noncompliant without a resume action', async () => {
    const user = userEvent.setup();
    api.versions.mockResolvedValue([version()]);
    api.installations.mockResolvedValue([
      installation({
        operationStatus: 'Blocked',
        provisioningStatus: 'Failed',
        complianceStatus: 'Noncompliant',
        components: [
          {
            type: 'authorization.policy.v1',
            key: 'policy',
            sha256: 'policy-hash',
            status: 'Confirmed',
          },
          {
            type: 'business-object.definition.v1',
            key: 'definition',
            sha256: 'definition-hash',
            status: 'Failed',
            problemCode: 'solutions.package.publisher_untrusted',
          },
        ],
      }),
    ]);
    api.operation.mockResolvedValue({
      id: 'operation-reload',
      installationId: 'installation-1',
      status: 'Blocked',
      problemCode: 'solutions.package.publisher_untrusted',
      steps: [],
    });

    await renderPage();
    expect(await screen.findByText('Noncompliant')).toHaveAttribute(
      'data-status-state',
      'critical',
    );
    await user.click(screen.getByRole('button', { name: 'View installation for cases 1.0.0' }));

    expect(within(activeManagedWindow()).getByText('Blocked')).toHaveAttribute(
      'data-status-state',
      'caution',
    );
    for (const failedState of within(activeManagedWindow()).getAllByText('Failed')) {
      expect(failedState).toHaveAttribute('data-status-state', 'critical');
    }
    expect(screen.queryByRole('button', { name: 'Resume operation' })).not.toBeInTheDocument();
  });

  it('opens a release from a shareable route intent and consumes the intent', async () => {
    const router = await renderPage('/solutions?dialog=release&versionId=version-1');

    expect(await screen.findByRole('heading', { name: 'cases 1.0.0' })).toBeVisible();
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty('dialog'));
  });
});

async function renderPage(path = '/solutions') {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: Number.POSITIVE_INFINITY },
      mutations: { retry: false },
    },
  });
  await Promise.all([
    queryClient.ensureQueryData({ queryKey: ['solutions', 'versions'], queryFn: api.versions }),
    queryClient.ensureQueryData({
      queryKey: ['solutions', 'installations'],
      queryFn: api.installations,
    }),
  ]);
  const rootRoute = createRootRouteWithContext<MyRouterContext>()({ component: Outlet });
  const authenticatedRoute = createRoute({
    getParentRoute: () => rootRoute,
    id: '_authenticated',
    component: Outlet,
  });
  const solutionsRoute = createRoute({
    getParentRoute: () => authenticatedRoute,
    path: 'solutions',
    validateSearch: (search: Record<string, unknown>) => ({
      ...(typeof search.query === 'string' && search.query ? { query: search.query } : {}),
      ...(search.dialog === 'publish' ||
      search.dialog === 'release' ||
      search.dialog === 'installation'
        ? { dialog: search.dialog }
        : {}),
      ...(typeof search.versionId === 'string' && search.versionId
        ? { versionId: search.versionId }
        : {}),
      ...(typeof search.installationId === 'string' && search.installationId
        ? { installationId: search.installationId }
        : {}),
    }),
    component: SolutionsPage,
  });
  const router = createRouter({
    routeTree: rootRoute.addChildren([authenticatedRoute.addChildren([solutionsRoute])]),
    context: { queryClient },
    history: createMemoryHistory({ initialEntries: [path] }),
  });

  await act(() => router.load());
  await act(async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <ManagedWindowProvider renderers={solutionsManagedWindowRenderers}>
          <div className="relative h-dvh w-dvw">
            <RouterProvider router={router} />
            <ManagedWindowHost />
          </div>
        </ManagedWindowProvider>
      </QueryClientProvider>,
    );
  });
  return router;
}

function version() {
  return {
    id: 'version-1',
    solutionKey: 'cases',
    solutionVersion: '1.0.0',
    packageSha256: 'package-hash',
    axisOpenApiSha256: 'api-hash',
    publisherId: 'publisher',
    publisherKeyId: 'key-1',
    trustStatus: 'Trusted' as const,
    sourceRevision: 'abc123',
    publishedAt: '2026-08-12T08:00:00Z',
    components: [
      {
        type: 'authorization.policy.v1',
        key: 'policy',
        sha256: 'policy-hash',
        dependsOn: [],
      },
      {
        type: 'business-object.definition.v1',
        key: 'definition',
        sha256: 'definition-hash',
        dependsOn: [{ type: 'authorization.policy.v1', key: 'policy' }],
      },
    ],
  };
}

function installation(overrides: Record<string, unknown> = {}) {
  return {
    id: 'installation-1',
    workspaceId: 'workspace-1',
    solutionVersionId: 'version-1',
    operationId: 'operation-reload',
    operationStatus: 'Succeeded' as const,
    provisioningStatus: 'Installed' as const,
    complianceStatus: 'Compliant' as const,
    components: [],
    updatedAt: '2026-08-12T09:00:00Z',
    ...overrides,
  };
}

function activeManagedWindow(): HTMLElement {
  const windows = document.querySelectorAll<HTMLElement>(
    '[data-axis-surface-id="solution-delivery-windows"]',
  );
  const active = windows.item(windows.length - 1);
  if (!active) throw new Error('Expected an active solution managed window.');
  return active;
}
