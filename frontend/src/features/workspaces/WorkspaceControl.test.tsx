import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  beginWorkspaceTransition,
  confirmWorkspaceTransition,
  createOrganizationWorkspace,
  listEligibleWorkspaces,
  recoverWorkspaceTransition,
} from './api';
import { WorkspaceControl } from './WorkspaceControl';

vi.mock('./api', () => ({
  workspaceKeys: { all: ['workspaces'], eligible: ['workspaces', 'eligible'] },
  beginWorkspaceTransition: vi.fn(),
  confirmWorkspaceTransition: vi.fn(),
  createOrganizationIdempotencyKey: vi.fn(() => 'organization-request-key'),
  createOrganizationWorkspace: vi.fn(),
  listEligibleWorkspaces: vi.fn(),
  recoverWorkspaceTransition: vi.fn(),
}));

const personalWorkspace = {
  workspaceId: '11111111-1111-4111-8111-111111111111',
  name: 'Personal workspace',
  slug: 'personal-workspace',
  type: 'Personal' as const,
  organizationId: null,
  isCurrent: true,
};
const organizationWorkspace = {
  workspaceId: '22222222-2222-4222-8222-222222222222',
  name: 'Acme Operations',
  slug: 'acme-operations',
  type: 'Organization' as const,
  organizationId: '33333333-3333-4333-8333-333333333333',
  isCurrent: false,
};
const completedTransition = {
  transitionId: '44444444-4444-4444-8444-444444444444',
  status: 'Completed' as const,
  expiresAt: '2026-08-06T12:00:00Z',
  authoritativeWorkspaceId: organizationWorkspace.workspaceId,
};

function TestBoundary({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      {children}
    </QueryClientProvider>
  );
}

function renderControl(onWorkspaceChanged = vi.fn(async () => undefined)) {
  render(
    <TestBoundary>
      <WorkspaceControl onWorkspaceChanged={onWorkspaceChanged} />
    </TestBoundary>,
  );
  return onWorkspaceChanged;
}

describe('WorkspaceControl', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listEligibleWorkspaces).mockResolvedValue([personalWorkspace, organizationWorkspace]);
    vi.mocked(beginWorkspaceTransition).mockResolvedValue({
      ...completedTransition,
      status: 'Pending',
      authoritativeWorkspaceId: null,
    });
    vi.mocked(confirmWorkspaceTransition).mockResolvedValue(completedTransition);
    vi.mocked(recoverWorkspaceTransition).mockResolvedValue(completedTransition);
  });

  it('groups eligible choices, marks current, and prevents a competing switch while pending', async () => {
    const user = userEvent.setup();
    let releaseBegin: (() => void) | undefined;
    vi.mocked(beginWorkspaceTransition).mockImplementation(
      () =>
        new Promise((resolve) => {
          releaseBegin = () =>
            resolve({
              ...completedTransition,
              status: 'Pending',
              authoritativeWorkspaceId: null,
            });
        }),
    );
    const onWorkspaceChanged = renderControl();

    const control = await screen.findByRole('button', { name: 'Workspace control' });
    await waitFor(() => expect(control).toHaveTextContent('Personal workspace'));
    await user.click(control);

    expect(screen.getByRole('region', { name: 'Personal' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Organizations' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Personal workspace' })).toHaveAttribute(
      'aria-current',
      'page',
    );

    await user.click(screen.getByRole('button', { name: 'Acme Operations' }));
    expect(control).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByText('Switching Workspace...')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Personal workspace' })).toBeDisabled();

    releaseBegin?.();
    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledOnce());
    expect(beginWorkspaceTransition).toHaveBeenCalledWith(organizationWorkspace.workspaceId);
    expect(confirmWorkspaceTransition).toHaveBeenCalledOnce();
  });

  it('keeps validation field-local and separates creation from entering the Workspace', async () => {
    const user = userEvent.setup();
    vi.mocked(createOrganizationWorkspace).mockResolvedValue({
      organizationName: 'Acme Operations',
      workspaceId: organizationWorkspace.workspaceId,
      workspaceName: 'Acme Operations',
    });
    const onWorkspaceChanged = renderControl();

    await user.click(await screen.findByRole('button', { name: 'Workspace control' }));
    await user.click(screen.getByRole('button', { name: 'Create Organization' }));
    const name = screen.getByRole('textbox', { name: 'Organization name' });
    await user.type(name, 'A');
    await user.click(screen.getByRole('button', { name: 'Create Organization' }));

    expect(
      screen.getByText('Enter an Organization name between 2 and 100 characters.'),
    ).toBeInTheDocument();
    expect(createOrganizationWorkspace).not.toHaveBeenCalled();

    await user.clear(name);
    await user.type(name, '  Acme Operations  ');
    await user.click(screen.getByRole('button', { name: 'Create Organization' }));

    expect(
      await screen.findByRole('heading', { name: 'Organization created' }),
    ).toBeInTheDocument();
    expect(screen.getByText(/current Workspace stays active/i)).toBeInTheDocument();
    expect(onWorkspaceChanged).not.toHaveBeenCalled();
    expect(createOrganizationWorkspace).toHaveBeenCalledWith(
      { name: 'Acme Operations' },
      'organization-request-key',
    );

    await user.click(screen.getByRole('button', { name: 'Enter Workspace' }));
    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledOnce());
  });

  it('recovers a lost confirmation response from durable completion', async () => {
    const user = userEvent.setup();
    vi.mocked(confirmWorkspaceTransition).mockRejectedValue(new TypeError('Lost response'));
    const onWorkspaceChanged = renderControl();

    await user.click(await screen.findByRole('button', { name: 'Workspace control' }));
    await user.click(screen.getByRole('button', { name: 'Acme Operations' }));

    await waitFor(() => expect(recoverWorkspaceTransition).toHaveBeenCalledOnce());
    expect(onWorkspaceChanged).toHaveBeenCalledOnce();
  });

  it('clears Workspace-scoped client state when confirmation and recovery both fail', async () => {
    const user = userEvent.setup();
    vi.mocked(confirmWorkspaceTransition).mockRejectedValue(new TypeError('Lost response'));
    vi.mocked(recoverWorkspaceTransition).mockRejectedValue(new TypeError('Recovery unavailable'));
    const onWorkspaceChanged = renderControl();

    await user.click(await screen.findByRole('button', { name: 'Workspace control' }));
    await user.click(screen.getByRole('button', { name: 'Acme Operations' }));

    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledOnce());
    expect(await screen.findByText(/Workspace did not change/i)).toBeInTheDocument();
  });

  it('refreshes the authoritative source session after compensation and keeps recovery choices open', async () => {
    const user = userEvent.setup();
    vi.mocked(confirmWorkspaceTransition).mockResolvedValue({
      ...completedTransition,
      status: 'Compensated',
      authoritativeWorkspaceId: personalWorkspace.workspaceId,
    });
    const onWorkspaceChanged = renderControl();

    await user.click(await screen.findByRole('button', { name: 'Workspace control' }));
    await user.click(screen.getByRole('button', { name: 'Acme Operations' }));

    await waitFor(() => expect(onWorkspaceChanged).toHaveBeenCalledOnce());
    expect(await screen.findByText(/Workspace did not change/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Acme Operations' })).toBeEnabled();
  });

  it('shows a non-disclosing recovery state when eligibility cannot be loaded', async () => {
    const user = userEvent.setup();
    vi.mocked(listEligibleWorkspaces).mockRejectedValue(new TypeError('Unavailable'));
    renderControl();

    await user.click(screen.getByRole('button', { name: 'Workspace control' }));

    expect(await screen.findByText(/Eligible Workspaces are unavailable/i)).toBeInTheDocument();
    expect(screen.queryByText('Acme Operations')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
