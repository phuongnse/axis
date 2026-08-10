import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { restoreBrowserSession, signOutUser } from '@/features/auth/api';
import { ApiError } from '@/lib/api';
import {
  acceptWorkspaceInvitation,
  awaitInvitationBootstrap,
  enterAcceptedWorkspace,
  reviewWorkspaceInvitation,
} from '../invitation-acceptance-api';
import { AcceptWorkspaceInvitationPage } from './AcceptWorkspaceInvitationPage';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    ...props
  }: AnchorHTMLAttributes<HTMLAnchorElement> & { children: ReactNode; to: string }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
  useNavigate: () => vi.fn(),
}));

vi.mock('@/features/auth/api', () => ({
  restoreBrowserSession: vi.fn(),
  signOutUser: vi.fn(),
}));

vi.mock('../invitation-acceptance-api', () => ({
  acceptWorkspaceInvitation: vi.fn(),
  awaitInvitationBootstrap: vi.fn(),
  enterAcceptedWorkspace: vi.fn(),
  reviewWorkspaceInvitation: vi.fn(),
}));

describe('AcceptWorkspaceInvitationPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(awaitInvitationBootstrap).mockResolvedValue(true);
    vi.mocked(restoreBrowserSession).mockResolvedValue(true);
    vi.mocked(reviewWorkspaceInvitation).mockResolvedValue({
      invitationId: 'invitation-1',
      workspaceId: 'workspace-1',
      organizationName: 'Acme',
      workspaceName: 'Acme Operations',
      inviterName: 'Ada Admin',
      requestedRole: 'Member',
      expiresAt: '2026-08-13T10:00:00Z',
    });
    vi.mocked(acceptWorkspaceInvitation).mockResolvedValue({
      outcome: 'Accepted',
      workspaceId: 'workspace-1',
      organizationRole: 'Member',
      workspaceRole: 'Member',
    });
    vi.mocked(signOutUser).mockResolvedValue();
    vi.mocked(enterAcceptedWorkspace).mockResolvedValue();
  });

  it('fails closed without invitation metadata when the handoff is unavailable', async () => {
    vi.mocked(awaitInvitationBootstrap).mockResolvedValue(false);

    render(<AcceptWorkspaceInvitationPage />);

    expect(await screen.findByText('Invitation unavailable')).toBeInTheDocument();
    expect(screen.queryByText('Acme')).not.toBeInTheDocument();
    expect(reviewWorkspaceInvitation).not.toHaveBeenCalled();
  });

  it('preserves intent through sign-in or registration without exposing the invitation', async () => {
    vi.mocked(restoreBrowserSession).mockResolvedValue(false);

    render(<AcceptWorkspaceInvitationPage />);

    expect(await screen.findByText('Continue with the invited account')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/sign-in');
    expect(screen.getByRole('link', { name: 'Create account' })).toHaveAttribute(
      'href',
      '/register',
    );
    expect(screen.queryByText('Acme Operations')).not.toBeInTheDocument();
  });

  it('reviews matched invitation facts and confirms acceptance', async () => {
    const user = userEvent.setup();
    render(<AcceptWorkspaceInvitationPage />);

    expect(await screen.findByText('Review Workspace invitation')).toBeInTheDocument();
    expect(screen.getByText('Acme')).toBeInTheDocument();
    expect(screen.getByText('Acme Operations')).toBeInTheDocument();
    expect(screen.getByText('Ada Admin')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Accept invitation' }));

    await waitFor(() => expect(acceptWorkspaceInvitation).toHaveBeenCalledOnce());
    expect(await screen.findByText('Invitation accepted')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Enter Workspace' })).toBeInTheDocument();
  });

  it('keeps the handoff recoverable while switching away from a wrong account', async () => {
    const user = userEvent.setup();
    vi.mocked(reviewWorkspaceInvitation).mockRejectedValue(
      new ApiError(403, { code: 'identity.invitation.accountMismatch' }),
    );
    render(<AcceptWorkspaceInvitationPage />);

    expect(await screen.findByText('Use the invited account')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Use another account' }));

    await waitFor(() => expect(signOutUser).toHaveBeenCalledOnce());
    expect(await screen.findByText('Continue with the invited account')).toBeInTheDocument();
  });
});
