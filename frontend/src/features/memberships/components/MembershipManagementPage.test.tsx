import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  inviteWorkspaceMember,
  resendWorkspaceInvitation,
  revokeWorkspaceInvitation,
} from '../api';
import { MembershipManagementPage } from './MembershipManagementPage';

const api = vi.hoisted(() => ({
  list: vi.fn(),
  invite: vi.fn(),
  resend: vi.fn(),
  revoke: vi.fn(),
}));

vi.mock('../api', () => ({
  workspaceInvitationKeys: { all: ['workspace-invitations'] },
  workspaceInvitationsQueryOptions: () => ({
    queryKey: ['workspace-invitations', 'list', 1, 20],
    queryFn: api.list,
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
      <MembershipManagementPage />
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

  it('invites with the accessible form and refreshes the authoritative list', async () => {
    const user = userEvent.setup();
    const queryClient = renderPage();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    const email = screen.getByRole('textbox', { name: 'Recipient email' });
    await user.type(email, '  member@example.com  ');
    await user.click(screen.getByRole('button', { name: 'Invite member' }));

    await waitFor(() =>
      expect(inviteWorkspaceMember).toHaveBeenCalledWith(
        {
          email: 'member@example.com',
          requestedRole: 'Member',
        },
        expect.anything(),
      ),
    );
    expect(await screen.findByText('Invitation outcome confirmed')).toBeInTheDocument();
    expect(email).toHaveValue('');
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['workspace-invitations'] });
  });

  it('keeps malformed email feedback field-local without mutating the server', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByRole('textbox', { name: 'Recipient email' }), 'not-an-email');
    await user.click(screen.getByRole('button', { name: 'Invite member' }));

    expect(screen.getByText('Enter a valid recipient email address.')).toBeInTheDocument();
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

    expect(await screen.findByRole('cell', { name: 'member@example.com' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Resend' }));
    await waitFor(() =>
      expect(resendWorkspaceInvitation).toHaveBeenCalledWith('invitation-1', {
        expectedRevision: 2,
      }),
    );
    expect(await screen.findByText('New invitation link queued')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Revoke' }));
    await waitFor(() =>
      expect(revokeWorkspaceInvitation).toHaveBeenCalledWith('invitation-1', {
        expectedRevision: 2,
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
