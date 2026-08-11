import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AccountSurface } from '@/components/shared/AccountSurface';
import { transientItemHighlight } from '@/components/shared/interactionStates';
import { useAuthStore } from '@/features/auth/auth-store';
import { getCurrentUserProfile } from '@/features/dashboard/api';
import { createOrganizationWorkspace, listEligibleWorkspaces } from './api';
import {
  type WorkspaceChangeResult,
  type WorkspaceContextState,
  WorkspaceControl,
} from './WorkspaceControl';

vi.mock('@/features/dashboard/api', () => ({
  dashboardQueryKeys: {
    all: ['dashboard'] as const,
    currentUser: () => ['dashboard', 'current-user'] as const,
  },
  getCurrentUserProfile: vi.fn(),
}));

vi.mock('./api', () => ({
  workspaceKeys: { all: ['workspaces'], eligible: ['workspaces', 'eligible'] },
  createOrganizationIdempotencyKey: vi.fn(() => 'organization-request-key'),
  createOrganizationWorkspace: vi.fn(),
  listEligibleWorkspaces: vi.fn(),
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
const idleContext: WorkspaceContextState = {
  failure: null,
  phase: 'idle',
  targetWorkspaceId: null,
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

function renderControl(
  onWorkspaceChange = vi.fn(async () => 'entered' as WorkspaceChangeResult),
  contextState: WorkspaceContextState = idleContext,
  onRetryContext = vi.fn(async () => undefined),
) {
  const view = render(
    <TestBoundary>
      <WorkspaceControl
        contextState={contextState}
        onRetryContext={onRetryContext}
        onWorkspaceChange={onWorkspaceChange}
      />
    </TestBoundary>,
  );
  return { ...view, onRetryContext, onWorkspaceChange };
}

describe('WorkspaceControl', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getCurrentUserProfile).mockResolvedValue({
      id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      email: 'ada@example.com',
      fullName: 'Ada Lovelace',
      isActive: true,
      language: 'en',
      theme: 'light',
      workspaceId: personalWorkspace.workspaceId,
      workspaces: [
        {
          id: personalWorkspace.workspaceId,
          name: personalWorkspace.name,
          slug: personalWorkspace.slug,
          type: personalWorkspace.type,
          isCurrent: true,
        },
      ],
    });
    useAuthStore.getState().setBrowserSession({
      authenticated: true,
      csrfToken: 'csrf-token',
      user: {
        userId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        workspaceId: personalWorkspace.workspaceId,
        email: 'ada@example.com',
        name: 'Ada Lovelace',
      },
    });
    vi.mocked(listEligibleWorkspaces).mockResolvedValue([personalWorkspace, organizationWorkspace]);
  });

  it('presents one flat Workspace choice set, marks current, and prevents a competing switch while pending', async () => {
    const user = userEvent.setup();
    const onWorkspaceChange = vi.fn(async () => 'entered' as WorkspaceChangeResult);
    const view = renderControl(onWorkspaceChange);

    const workspaceSection = screen.getByRole('region', { name: 'Workspace' });
    await screen.findByRole('button', { name: 'Personal workspace' });
    expect(screen.getByRole('region', { name: 'Eligible Workspaces' })).toBeInTheDocument();
    expect(screen.queryByText('Choose Workspace')).not.toBeInTheDocument();
    expect(screen.queryByText('Personal Workspace')).not.toBeInTheDocument();
    expect(screen.queryByText('Organization Workspaces')).not.toBeInTheDocument();
    const currentWorkspace = screen.getByRole('button', { name: 'Personal workspace' });
    expect(currentWorkspace).toHaveAttribute('aria-current', 'page');
    expect(currentWorkspace).toHaveClass('bg-secondary', 'disabled:opacity-100');
    expect(currentWorkspace.querySelector('.lucide-user-round')).not.toBeNull();
    expect(currentWorkspace.querySelector('.lucide-check')).toBeNull();
    const organizationChoice = screen.getByRole('button', { name: 'Acme Operations' });
    expect(organizationChoice.querySelector('.lucide-building-2')).not.toBeNull();
    expect(organizationChoice).toHaveClass(...transientItemHighlight.split(' '));

    await user.click(organizationChoice);
    expect(onWorkspaceChange).toHaveBeenCalledWith(organizationWorkspace);
    view.rerender(
      <TestBoundary>
        <WorkspaceControl
          contextState={{
            failure: null,
            phase: 'switching',
            targetWorkspaceId: organizationWorkspace.workspaceId,
          }}
          onRetryContext={vi.fn(async () => undefined)}
          onWorkspaceChange={onWorkspaceChange}
        />
      </TestBoundary>,
    );
    expect(workspaceSection).toHaveAttribute('aria-busy', 'true');
    expect(await screen.findByText('Switching Workspace...')).toHaveClass('sr-only');
    const visualStatus = workspaceSection.querySelector('[role="status"]:not(.sr-only)');
    expect(visualStatus?.closest('[data-slot="option-item-icon"]')).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Personal workspace' })).toBeDisabled();
  });

  it('does not flash loading feedback when eligible Workspaces resolve quickly', async () => {
    renderControl();

    expect(screen.queryByText('Loading eligible Workspaces...')).not.toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Personal workspace' })).toBeInTheDocument();
    expect(screen.queryByText('Loading eligible Workspaces...')).not.toBeInTheDocument();
  });

  it('keeps validation field-local and separates creation from entering the Workspace', async () => {
    const user = userEvent.setup();
    vi.mocked(createOrganizationWorkspace).mockResolvedValue({
      organizationName: 'Acme Operations',
      workspaceId: organizationWorkspace.workspaceId,
      workspaceName: 'Acme Operations',
    });
    const { onWorkspaceChange } = renderControl();

    await screen.findByRole('button', { name: 'Personal workspace' });
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
    expect(onWorkspaceChange).not.toHaveBeenCalled();
    expect(createOrganizationWorkspace).toHaveBeenCalledWith(
      { name: 'Acme Operations' },
      'organization-request-key',
    );

    await user.click(screen.getByRole('button', { name: 'Enter Workspace' }));
    await waitFor(() => expect(onWorkspaceChange).toHaveBeenCalledOnce());
  });

  it('keeps organization creation available inside the account menu', async () => {
    const user = userEvent.setup();
    render(
      <TestBoundary>
        <AccountSurface
          surfaceId="account-actions"
          identity={{
            displayName: 'Ada Lovelace',
            initials: 'AL',
            secondaryLabel: 'ada@example.com',
            triggerKind: 'person',
            triggerLabel: 'Ada Lovelace',
          }}
          onSignOut={vi.fn()}
          preferenceControls={null}
          workspace={
            <WorkspaceControl
              contextState={idleContext}
              onRetryContext={vi.fn(async () => undefined)}
              onWorkspaceChange={vi.fn(async () => 'entered' as WorkspaceChangeResult)}
            />
          }
        />
      </TestBoundary>,
    );

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(await screen.findByRole('button', { name: 'Create Organization' }));

    expect(screen.getByRole('dialog', { name: 'Create Organization' })).toBeVisible();
  });

  it('renders an unknown outcome in place and delegates canonical retry', async () => {
    const user = userEvent.setup();
    const onRetryContext = vi.fn(async () => undefined);
    renderControl(
      vi.fn(async () => 'unknown'),
      {
        failure: 'outcome-unknown',
        phase: 'failed',
        targetWorkspaceId: organizationWorkspace.workspaceId,
      },
      onRetryContext,
    );

    await screen.findByRole('button', { name: 'Personal workspace' });
    expect(screen.getByText(/could not be confirmed/i)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Retry refresh' }));

    expect(onRetryContext).toHaveBeenCalledOnce();
  });

  it('keeps recovery choices available when the authoritative Workspace did not change', async () => {
    renderControl(
      vi.fn(async () => 'not-entered'),
      {
        failure: 'switch-failed',
        phase: 'failed',
        targetWorkspaceId: organizationWorkspace.workspaceId,
      },
    );

    await screen.findByRole('button', { name: 'Personal workspace' });
    expect(screen.getByText(/Workspace did not change/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Acme Operations' })).toBeEnabled();
  });

  it('keeps refresh recovery inside the account view', async () => {
    const user = userEvent.setup();
    const onRetryContext = vi.fn(async () => undefined);
    renderControl(
      vi.fn(async () => 'unknown'),
      {
        failure: 'refresh-failed',
        phase: 'failed',
        targetWorkspaceId: organizationWorkspace.workspaceId,
      },
      onRetryContext,
    );

    await screen.findByRole('button', { name: 'Personal workspace' });
    expect(document.body).toHaveTextContent(/new Workspace context could not be loaded/i);
    await user.click(screen.getByRole('button', { name: 'Retry refresh' }));

    expect(onRetryContext).toHaveBeenCalledOnce();
  });

  it('shows a non-disclosing recovery state when eligibility cannot be loaded', async () => {
    vi.mocked(listEligibleWorkspaces).mockRejectedValue(new TypeError('Unavailable'));
    renderControl();

    expect(await screen.findByText(/Eligible Workspaces are unavailable/i)).toBeInTheDocument();
    expect(screen.queryByText('Acme Operations')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
