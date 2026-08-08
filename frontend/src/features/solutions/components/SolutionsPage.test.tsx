import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { installSolutionVersion, publishSolutionVersion } from '../api';
import { SolutionsPage } from './SolutionsPage';

const api = vi.hoisted(() => ({
  versions: vi.fn(),
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
    installations: () => ['solutions', 'installations'],
    operation: (id: string) => ['solutions', 'operation', id],
  },
  solutionVersionsQueryOptions: () => ({
    queryKey: ['solutions', 'versions'],
    queryFn: api.versions,
  }),
  solutionInstallationsQueryOptions: () => ({
    queryKey: ['solutions', 'installations'],
    queryFn: api.installations,
  }),
  solutionOperationQueryOptions: (id: string) => ({
    queryKey: ['solutions', 'operation', id],
    queryFn: api.operation,
    enabled: Boolean(id),
  }),
  publishSolutionVersion: api.publish,
  installSolutionVersion: api.install,
  resumeSolutionOperation: api.resume,
}));

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <SolutionsPage />
    </QueryClientProvider>,
  );
}

describe('SolutionsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.versions.mockResolvedValue([]);
    api.installations.mockResolvedValue([]);
    api.publish.mockResolvedValue({ version: version(), isRetry: false });
    api.install.mockResolvedValue({
      operation: { id: 'operation-1', status: 'Pending', steps: [] },
      isRetry: false,
    });
    api.operation.mockResolvedValue({ id: 'operation-1', status: 'Succeeded', steps: [] });
  });

  it('rejects an oversized local package before upload', async () => {
    const user = userEvent.setup();
    renderPage();
    const file = new File(['x'], 'too-large.axis-solution', {
      type: 'application/vnd.dsse.envelope.v1+json',
    });
    Object.defineProperty(file, 'size', { value: 10 * 1024 * 1024 + 1 });
    await user.upload(screen.getByLabelText('Signed solution package'), file);
    expect(screen.getByText('The signed package must be 10 MiB or smaller.')).toBeInTheDocument();
    expect(publishSolutionVersion).not.toHaveBeenCalled();
  });

  it('publishes safe metadata then installs with an internal idempotency key', async () => {
    const user = userEvent.setup();
    renderPage();
    const file = new File(['raw-package-secret'], 'release.axis-solution', {
      type: 'application/vnd.dsse.envelope.v1+json',
    });
    await user.upload(screen.getByLabelText('Signed solution package'), file);
    await user.click(screen.getByRole('button', { name: 'Publish package' }));
    await user.click(screen.getByRole('button', { name: 'Publish package' }));
    await waitFor(() =>
      expect(publishSolutionVersion).toHaveBeenCalledWith(file, expect.anything()),
    );
    expect(await screen.findByText('Solution version published')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(screen.getByText('cases')).toBeInTheDocument();
    expect(screen.getByText('policy')).toBeInTheDocument();
    expect(screen.getByText('definition')).toBeInTheDocument();
    expect(screen.queryByText('raw-package-secret')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Install version' }));
    await user.click(screen.getByRole('button', { name: 'Install version' }));
    await waitFor(() =>
      expect(installSolutionVersion).toHaveBeenCalledWith('version-1', expect.stringMatching(/.+/)),
    );
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
  });

  it('reopens a durable failed operation from the installation list after reload', async () => {
    const user = userEvent.setup();
    api.installations.mockResolvedValue([
      {
        id: 'installation-1',
        workspaceId: 'workspace-1',
        solutionVersionId: 'version-1',
        operationId: 'operation-reload',
        operationStatus: 'Failed',
        provisioningStatus: 'Failed',
        complianceStatus: 'Compliant',
        components: [],
      },
    ]);
    api.operation.mockResolvedValue({
      id: 'operation-reload',
      status: 'Failed',
      steps: [],
    });

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'View operation' }));

    await waitFor(() => expect(api.operation).toHaveBeenCalled());
    expect(await screen.findByRole('button', { name: 'Resume operation' })).toBeInTheDocument();
  });

  it('shows a revoked partial installation as noncompliant without a resume action', async () => {
    const user = userEvent.setup();
    api.installations.mockResolvedValue([
      {
        id: 'installation-revoked',
        workspaceId: 'workspace-1',
        solutionVersionId: 'version-revoked',
        operationId: 'operation-revoked',
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
      },
    ]);
    api.operation.mockResolvedValue({
      id: 'operation-revoked',
      status: 'Blocked',
      problemCode: 'solutions.package.publisher_untrusted',
      steps: [],
    });

    renderPage();
    expect(await screen.findByText('Noncompliant')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'View operation' }));

    expect(await screen.findByText('Blocked')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Resume operation' })).not.toBeInTheDocument();
  });
});

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
