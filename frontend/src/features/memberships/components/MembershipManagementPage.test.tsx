import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import { ManagedWindowProvider } from '@/components/shared/ManagedWindowManager';
import { axisStyles } from '@/theme.generated';
import {
  inviteWorkspaceMember,
  resendWorkspaceInvitation,
  revokeWorkspaceInvitation,
} from '../api';
import { membershipsManagedWindowRenderers } from '../managed-windows';
import { MembershipManagementPage } from './MembershipManagementPage';

const api = vi.hoisted(() => ({
  list: vi.fn(),
  invite: vi.fn(),
  resend: vi.fn(),
  revoke: vi.fn(),
}));

vi.mock('../api', () => ({
  workspaceInvitationKeys: { all: ['workspace-invitations'] },
  workspaceInvitationsQueryOptions: (page = 1, pageSize = 20) => ({
    queryKey: ['workspace-invitations', 'list', page, pageSize],
    queryFn: () => api.list(page, pageSize),
  }),
  inviteWorkspaceMember: api.invite,
  resendWorkspaceInvitation: api.resend,
  revokeWorkspaceInvitation: api.revoke,
}));

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <ManagedWindowProvider renderers={membershipsManagedWindowRenderers}>
        <div className="relative h-dvh w-dvw">
          <MembershipManagementPage />
          <ManagedWindowHost />
        </div>
      </ManagedWindowProvider>
    </QueryClientProvider>,
  );
  return queryClient;
}

describe('MembershipManagementPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    api.invite.mockResolvedValue({
      outcome: 'Created',
      requestedRole: 'Member',
      invitation: pendingInvitation(),
    });
    api.resend.mockResolvedValue({ ...pendingInvitation(), revision: 3 });
    api.revoke.mockResolvedValue({ ...pendingInvitation(), status: 'Revoked', revision: 3 });
  });

  it('composes the shared resource workspace and launches the invitation task in a managed window', async () => {
    const user = userEvent.setup();
    api.list.mockResolvedValue({
      items: [pendingInvitation()],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    renderPage();

    const page = document.querySelector<HTMLElement>('[data-slot="page-layout"]');
    const workspace = document.querySelector<HTMLElement>('[data-slot="resource-workspace"]');
    const content = document.querySelector<HTMLElement>('[data-slot="resource-workspace-content"]');
    const table = await screen.findByRole('region', { name: 'Workspace invitation outcomes' });
    const invite = await within(table).findByRole('button', { name: 'Invite member' });

    expect(page).toHaveAttribute('data-scroll-mode', 'contained');
    expect(page).toContainElement(workspace);
    expect(workspace?.querySelectorAll('[data-slot="page-header"]')).toHaveLength(1);
    expect(content).toContainElement(table);
    expect(content?.querySelectorAll('[data-slot="data-table"]')).toHaveLength(1);
    expect(within(table).queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument();
    expect(invite).toHaveClass(
      axisStyles.density.minHeight.touchTarget,
      axisStyles.density.minWidth.touchTarget,
      axisStyles.density.minHeight.compactControlAtSmall,
      axisStyles.density.minWidth.compactControlAtSmall,
    );

    await user.click(invite);
    expect(await screen.findByRole('dialog', { name: 'Invite member' })).toBeVisible();
  });

  it('invites with the accessible form and refreshes the authoritative list', async () => {
    const user = userEvent.setup();
    const queryClient = renderPage();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const table = await screen.findByRole('region', { name: 'Workspace invitation outcomes' });
    await user.click(await within(table).findByRole('button', { name: 'Invite member' }));
    const dialog = await screen.findByRole('dialog', { name: 'Invite member' });
    const email = within(dialog).getByRole('textbox', { name: 'Recipient email' });
    await user.type(email, '  member@example.com  ');
    await user.click(within(dialog).getByRole('button', { name: 'Invite member' }));

    await waitFor(() =>
      expect(inviteWorkspaceMember).toHaveBeenCalledWith(
        {
          email: 'member@example.com',
          requestedRole: 'Member',
        },
        expect.anything(),
      ),
    );
    expect(await within(dialog).findByText('Invitation outcome confirmed')).toBeInTheDocument();
    expect(email).toHaveValue('');
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['workspace-invitations'] });
  });

  it('preserves an invitation draft through minimize and guards destructive closure', async () => {
    const user = userEvent.setup();
    renderPage();

    const table = await screen.findByRole('region', { name: 'Workspace invitation outcomes' });
    await user.click(await within(table).findByRole('button', { name: 'Invite member' }));
    const dialog = await screen.findByRole('dialog', { name: 'Invite member' });
    const email = within(dialog).getByRole('textbox', { name: 'Recipient email' });
    await user.type(email, 'member@example.com');
    await user.click(within(dialog).getByRole('button', { name: 'Minimize dialog' }));

    const dock = document.querySelector<HTMLElement>('[data-slot="managed-window-dock"]');
    await user.click(within(dock as HTMLElement).getByRole('button', { name: 'Restore dialog' }));
    expect(email).toHaveValue('member@example.com');

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    const confirmation = await screen.findByRole('alertdialog', {
      name: 'Discard this invitation?',
    });
    await user.click(within(confirmation).getByRole('button', { name: 'Keep editing' }));
    expect(dialog).toBeVisible();
    expect(email).toHaveValue('member@example.com');

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await user.click(
      within(
        await screen.findByRole('alertdialog', { name: 'Discard this invitation?' }),
      ).getByRole('button', { name: 'Discard invitation' }),
    );
    await waitFor(() => expect(dialog).not.toBeInTheDocument());
  });

  it('keeps malformed email feedback field-local without mutating the server', async () => {
    const user = userEvent.setup();
    renderPage();

    const table = await screen.findByRole('region', { name: 'Workspace invitation outcomes' });
    await user.click(await within(table).findByRole('button', { name: 'Invite member' }));
    const dialog = await screen.findByRole('dialog', { name: 'Invite member' });
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Recipient email' }),
      'not-an-email',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Invite member' }));

    expect(within(dialog).getByText('Enter a valid recipient email address.')).toBeInTheDocument();
    expect(inviteWorkspaceMember).not.toHaveBeenCalled();
  });

  it('offers recovery actions for a pending invitation with failed delivery', async () => {
    const user = userEvent.setup();
    api.list.mockResolvedValue({
      items: [{ ...pendingInvitation(), deliveryStatus: 'Failed' }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    renderPage();

    const table = await screen.findByRole('region', { name: 'Workspace invitation outcomes' });
    expect(await within(table).findByText('Delivery failed')).toHaveAttribute(
      'data-status-state',
      'critical',
    );
    await user.click(await within(table).findByRole('button', { name: 'member@example.com' }));
    const dialog = await screen.findByRole('dialog', { name: 'member@example.com' });
    await user.click(within(dialog).getByRole('button', { name: 'Resend' }));
    await waitFor(() =>
      expect(resendWorkspaceInvitation).toHaveBeenCalledWith('invitation-1', {
        expectedRevision: 2,
      }),
    );
    expect(await screen.findByText('New invitation link queued')).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'Revoke' }));
    const confirmation = await screen.findByRole('alertdialog', {
      name: 'Revoke this invitation?',
    });
    await user.click(within(confirmation).getByRole('button', { name: 'Revoke' }));
    await waitFor(() =>
      expect(revokeWorkspaceInvitation).toHaveBeenCalledWith('invitation-1', {
        expectedRevision: 3,
      }),
    );
  });

  it('shows a retryable non-secret state when the list cannot be loaded', async () => {
    const user = userEvent.setup();
    api.list.mockRejectedValueOnce(new TypeError('network unavailable'));
    renderPage();

    expect(await screen.findByText('Unable to load invitations')).toBeInTheDocument();
    api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    await user.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByText('No Workspace invitations yet.')).toBeInTheDocument();
    expect(api.list).toHaveBeenCalledTimes(2);
  });

  it('requests and displays a later invitation page through accessible pagination', async () => {
    const user = userEvent.setup();
    api.list.mockImplementation((page: number) =>
      Promise.resolve({
        items: [{ ...pendingInvitation(), recipientEmail: `member-${page}@example.com` }],
        page,
        pageSize: 20,
        totalCount: 21,
      }),
    );
    renderPage();

    expect(await screen.findByRole('cell', { name: 'member-1@example.com' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Next page' }));

    expect(await screen.findByRole('cell', { name: 'member-2@example.com' })).toBeInTheDocument();
    expect(api.list).toHaveBeenLastCalledWith(2, 20);
  });
});

function pendingInvitation() {
  return {
    invitationId: 'invitation-1',
    recipientEmail: 'member@example.com',
    requestedRole: 'Member',
    status: 'Pending',
    deliveryStatus: 'Pending',
    createdAt: '2026-08-06T10:00:00Z',
    expiresAt: '2026-08-13T10:00:00Z',
    revision: 2,
  };
}
