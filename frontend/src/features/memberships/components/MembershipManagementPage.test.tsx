import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  createMemoryHistory,
  createRootRouteWithContext,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from '@tanstack/react-router';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import { ManagedWindowProvider } from '@/components/shared/ManagedWindowManager';
import { ApiError } from '@/lib/api';
import type { MyRouterContext } from '@/routes/__root';
import { axisStyles } from '@/theme.generated';
import {
  grantWorkspaceProductBuilder,
  inviteWorkspaceMember,
  resendWorkspaceInvitation,
  revokeWorkspaceInvitation,
  revokeWorkspaceProductBuilder,
} from '../api';
import { membershipsManagedWindowRenderers } from '../managed-windows';
import { MembershipManagementPage } from './MembershipManagementPage';

const api = vi.hoisted(() => ({
  list: vi.fn(),
  invite: vi.fn(),
  resend: vi.fn(),
  revoke: vi.fn(),
  productBuilders: vi.fn(),
  grantProductBuilder: vi.fn(),
  revokeProductBuilder: vi.fn(),
}));

vi.mock('../api', () => ({
  workspaceInvitationKeys: { all: ['workspace-invitations'] },
  workspaceProductBuilderKeys: { all: ['workspace-product-builders'] },
  workspaceInvitationsQueryOptions: (
    page = 1,
    pageSize = 20,
    sortBy?: string,
    sortDirection?: string,
  ) => ({
    queryKey: ['workspace-invitations', 'list', page, pageSize, sortBy, sortDirection],
    queryFn: () => api.list(page, pageSize, sortBy, sortDirection),
  }),
  inviteWorkspaceMember: api.invite,
  resendWorkspaceInvitation: api.resend,
  revokeWorkspaceInvitation: api.revoke,
  workspaceProductBuildersQueryOptions: () => ({
    queryKey: ['workspace-product-builders'],
    queryFn: () => api.productBuilders(),
  }),
  grantWorkspaceProductBuilder: api.grantProductBuilder,
  revokeWorkspaceProductBuilder: api.revokeProductBuilder,
}));

function renderPage(path = '/memberships') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const rootRoute = createRootRouteWithContext<MyRouterContext>()();
  const authenticatedRoute = createRoute({
    getParentRoute: () => rootRoute,
    id: '_authenticated',
    component: Outlet,
  });
  const membershipsRoute = createRoute({
    getParentRoute: () => authenticatedRoute,
    path: 'memberships',
    validateSearch: (search: Record<string, unknown>) => {
      const sortBy =
        search.sortBy === 'Email' ||
        search.sortBy === 'Status' ||
        search.sortBy === 'Role' ||
        search.sortBy === 'Expires'
          ? search.sortBy
          : undefined;
      const sortDirection =
        search.sortDirection === 'Ascending' || search.sortDirection === 'Descending'
          ? search.sortDirection
          : undefined;
      return {
        page: Number(search.page) > 0 ? Number(search.page) : 1,
        pageSize: [20, 50, 100].includes(Number(search.pageSize)) ? Number(search.pageSize) : 20,
        ...(sortBy && sortDirection ? { sortBy, sortDirection } : {}),
      };
    },
    component: MembershipManagementPage,
  });
  const router = createRouter({
    routeTree: rootRoute.addChildren([authenticatedRoute.addChildren([membershipsRoute])]),
    context: { queryClient },
    history: createMemoryHistory({ initialEntries: [path] }),
  });
  render(
    <QueryClientProvider client={queryClient}>
      <ManagedWindowProvider renderers={membershipsManagedWindowRenderers}>
        <div className="relative h-dvh w-dvw">
          <RouterProvider router={router} />
          <ManagedWindowHost />
        </div>
      </ManagedWindowProvider>
    </QueryClientProvider>,
  );
  return { queryClient, router };
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
    api.productBuilders.mockResolvedValue([productBuilderMember()]);
    api.grantProductBuilder.mockResolvedValue({
      ...productBuilderMember(),
      isProductBuilder: true,
      membershipRevision: 2,
    });
    api.revokeProductBuilder.mockResolvedValue({
      ...productBuilderMember(),
      isProductBuilder: false,
      membershipRevision: 3,
    });
  });

  it('manages Product Builder independently from the Workspace lifecycle role', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole('tab', { name: 'Members' }));
    const table = await screen.findByRole('region', {
      name: 'Active Workspace member authoring authority',
    });
    expect(within(table).getByText('Workspace member')).toBeVisible();
    expect(within(table).getByText('Not granted')).toBeVisible();

    await user.click(within(table).getByRole('button', { name: 'Builder Member' }));
    const dialog = await screen.findByRole('dialog', { name: 'Builder Member' });
    await user.click(within(dialog).getByRole('button', { name: 'Grant Product Builder' }));
    await waitFor(() =>
      expect(grantWorkspaceProductBuilder).toHaveBeenCalledWith('builder-user', {
        expectedRevision: 1,
      }),
    );
    expect(await within(dialog).findByText('Product Builder granted')).toBeVisible();

    await user.click(within(dialog).getByRole('button', { name: 'Revoke Product Builder' }));
    const confirmation = await screen.findByRole('alertdialog', {
      name: 'Revoke Product Builder?',
    });
    await user.click(within(confirmation).getByRole('button', { name: 'Revoke Product Builder' }));
    await waitFor(() =>
      expect(revokeWorkspaceProductBuilder).toHaveBeenCalledWith('builder-user', {
        expectedRevision: 2,
      }),
    );
  });

  it('refreshes the authoritative member revision before retrying a Product Builder conflict', async () => {
    const user = userEvent.setup();
    api.productBuilders
      .mockResolvedValueOnce([productBuilderMember()])
      .mockResolvedValueOnce([{ ...productBuilderMember(), membershipRevision: 4 }]);
    api.grantProductBuilder
      .mockRejectedValueOnce(new ApiError(409, { code: 'identity.membership.revisionConflict' }))
      .mockResolvedValueOnce({
        ...productBuilderMember(),
        isProductBuilder: true,
        membershipRevision: 5,
      });
    renderPage();

    await user.click(await screen.findByRole('tab', { name: 'Members' }));
    const table = await screen.findByRole('region', {
      name: 'Active Workspace member authoring authority',
    });
    await user.click(within(table).getByRole('button', { name: 'Builder Member' }));
    const dialog = await screen.findByRole('dialog', { name: 'Builder Member' });

    await user.click(within(dialog).getByRole('button', { name: 'Grant Product Builder' }));
    expect(await within(dialog).findByText('Membership changed')).toBeVisible();
    await waitFor(() => expect(api.productBuilders).toHaveBeenCalledTimes(2));

    await user.click(within(dialog).getByRole('button', { name: 'Grant Product Builder' }));
    await waitFor(() => {
      expect(grantWorkspaceProductBuilder).toHaveBeenNthCalledWith(1, 'builder-user', {
        expectedRevision: 1,
      });
      expect(grantWorkspaceProductBuilder).toHaveBeenNthCalledWith(2, 'builder-user', {
        expectedRevision: 4,
      });
    });
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

    const table = await screen.findByRole('region', { name: 'Workspace invitation outcomes' });
    const page = document.querySelector<HTMLElement>('[data-slot="page-layout"]');
    const workspace = document.querySelector<HTMLElement>('[data-slot="resource-workspace"]');
    const content = document.querySelector<HTMLElement>('[data-slot="resource-workspace-content"]');
    const invite = await within(table).findByRole('button', { name: 'Invite member' });

    expect(page).toHaveAttribute('data-scroll-mode', 'contained');
    expect(page).toContainElement(workspace);
    expect(workspace?.querySelectorAll('[data-slot="page-header"]')).toHaveLength(1);
    expect(content).toContainElement(table);
    expect(content?.querySelectorAll('[data-slot="data-table"]')).toHaveLength(1);
    expect(within(table).queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument();
    expect(
      within(table).getByRole('button', { name: 'Recipient email: Sort ascending' }),
    ).toBeVisible();
    expect(within(table).getByRole('button', { name: 'Delivery: Sort ascending' })).toBeVisible();
    expect(invite).toHaveClass(
      axisStyles.density.minHeight.touchTarget,
      axisStyles.density.minWidth.touchTarget,
      axisStyles.density.minHeight.compactControlAtSmall,
      axisStyles.density.minWidth.compactControlAtSmall,
    );

    await user.click(invite);
    expect(await screen.findByRole('dialog', { name: 'Invite member' })).toBeVisible();
  });

  it('sorts invitation delivery on the server and preserves the URL-backed table state', async () => {
    const user = userEvent.setup();
    api.list.mockResolvedValue({
      items: [pendingInvitation()],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    const { router } = renderPage();

    const table = await screen.findByRole('region', { name: 'Workspace invitation outcomes' });
    await user.click(within(table).getByRole('button', { name: 'Delivery: Sort ascending' }));

    await waitFor(() => expect(api.list).toHaveBeenLastCalledWith(1, 20, 'Delivery', 'Ascending'));
    expect(router.state.location.search).toEqual({
      page: 1,
      pageSize: 20,
      sortBy: 'Delivery',
      sortDirection: 'Ascending',
    });
    expect(within(table).getByRole('columnheader', { name: 'Delivery' })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
  });

  it('invites with the accessible form and refreshes the authoritative list', async () => {
    const user = userEvent.setup();
    const { queryClient } = renderPage();
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
    expect(api.list).toHaveBeenLastCalledWith(2, 20, undefined, undefined);
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

function productBuilderMember() {
  return {
    userId: 'builder-user',
    displayName: 'Builder Member',
    email: 'builder@example.com',
    workspaceRole: 'Member',
    isProductBuilder: false,
    membershipRevision: 1,
    canChange: true,
  };
}
