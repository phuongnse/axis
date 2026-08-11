import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import { ManagedWindowProvider } from '@/components/shared/ManagedWindowManager';
import { axisStyles } from '@/theme.generated';
import { assignProductRole, revokeProductRole } from '../api';
import { productRolesManagedWindowRenderers } from '../managed-windows';
import { ProductRoleAssignmentsPage } from './ProductRoleAssignmentsPage';

const api = vi.hoisted(() => ({ list: vi.fn(), assign: vi.fn(), revoke: vi.fn() }));

vi.mock('../api', () => ({
  productRoleQueryKeys: { all: ['product-role-assignments'] },
  productRoleManagementQueryOptions: (language: string) => ({
    queryKey: ['product-role-assignments', language],
    queryFn: api.list,
  }),
  assignProductRole: api.assign,
  revokeProductRole: api.revoke,
}));

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <ManagedWindowProvider renderers={productRolesManagedWindowRenderers}>
        <div className="relative h-dvh w-dvw">
          <ProductRoleAssignmentsPage />
          <ManagedWindowHost />
        </div>
      </ManagedWindowProvider>
    </QueryClientProvider>,
  );
  return queryClient;
}

describe('ProductRoleAssignmentsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.list.mockResolvedValue(management());
    api.assign.mockResolvedValue({ ...management().assignments[0], revision: 5 });
    api.revoke.mockResolvedValue({ ...management().assignments[0], isActive: false, revision: 5 });
  });

  it('composes the shared resource workspace and revokes from a managed assignment window', async () => {
    const user = userEvent.setup();
    renderPage();

    const page = document.querySelector<HTMLElement>('[data-slot="page-layout"]');
    const workspace = document.querySelector<HTMLElement>('[data-slot="resource-workspace"]');
    const content = document.querySelector<HTMLElement>('[data-slot="resource-workspace-content"]');
    const table = await screen.findByRole('region', {
      name: 'Current product-role assignments',
    });
    const assign = await within(table).findByRole('button', { name: 'Assign role' });

    expect(page).toHaveAttribute('data-scroll-mode', 'contained');
    expect(page).toContainElement(workspace);
    expect(workspace?.querySelectorAll('[data-slot="page-header"]')).toHaveLength(1);
    expect(content).toContainElement(table);
    expect(content?.querySelectorAll('[data-slot="data-table"]')).toHaveLength(1);
    expect(within(table).queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument();
    expect(assign).toHaveClass(
      axisStyles.density.minHeight.touchTarget,
      axisStyles.density.minWidth.touchTarget,
      axisStyles.density.minHeight.compactControlAtSmall,
      axisStyles.density.minWidth.compactControlAtSmall,
    );
    expect(within(table).getByText('Reviews submitted cases.')).toBeInTheDocument();

    await user.click(within(table).getByRole('button', { name: 'Alex Nguyen' }));
    const dialog = await screen.findByRole('dialog', { name: 'Alex Nguyen' });
    expect(within(dialog).getByText('Case reviewer')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Revoke role' }));
    const confirmation = await screen.findByRole('alertdialog', {
      name: 'Revoke this exact product role?',
    });
    await user.click(within(confirmation).getByRole('button', { name: 'Revoke role' }));

    await waitFor(() =>
      expect(revokeProductRole).toHaveBeenCalledWith(
        {
          target: { kind: 'Human', subjectId: 'user-1' },
          policyVersionId: 'policy-version-1',
          roleKey: 'case.reviewer',
          expectedRevision: 4,
        },
        expect.stringMatching(/.+/),
      ),
    );
    expect(
      await within(dialog).findByText('The exact product authority was removed immediately.'),
    ).toBeInTheDocument();
  });

  it('assigns the exact server-projected subject and role from a managed window', async () => {
    const user = userEvent.setup();
    const queryClient = renderPage();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const table = await screen.findByRole('region', {
      name: 'Current product-role assignments',
    });
    await user.click(await within(table).findByRole('button', { name: 'Assign role' }));
    const dialog = await screen.findByRole('dialog', { name: 'Assign product role' });

    const nextSubject = within(dialog).getByRole('combobox', { name: 'Active subject' });
    nextSubject.focus();
    await user.keyboard('{Enter}{ArrowDown}{Enter}');
    const nextRole = within(dialog).getByRole('combobox', { name: 'Installed product role' });
    nextRole.focus();
    await user.keyboard('{Enter}{ArrowDown}{Enter}');
    await user.click(within(dialog).getByRole('button', { name: 'Assign role' }));

    await waitFor(() =>
      expect(assignProductRole).toHaveBeenCalledWith(
        {
          target: { kind: 'Human', subjectId: 'user-1' },
          policyVersionId: 'policy-version-1',
          roleKey: 'case.reviewer',
          expectedRevision: 4,
        },
        expect.stringMatching(/.+/),
      ),
    );
    expect(await within(dialog).findByText('Product role assigned')).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['product-role-assignments'] });

    nextSubject.focus();
    await user.keyboard('{Enter}{ArrowDown}{Enter}');
    nextRole.focus();
    await user.keyboard('{Enter}{ArrowDown}{Enter}');
    await user.click(within(dialog).getByRole('button', { name: 'Assign role' }));
    await waitFor(() => expect(assignProductRole).toHaveBeenCalledTimes(2));
    expect(assignProductRole).toHaveBeenLastCalledWith(
      {
        target: { kind: 'Human', subjectId: 'user-1' },
        policyVersionId: 'policy-version-1',
        roleKey: 'case.reviewer',
        expectedRevision: 5,
      },
      expect.stringMatching(/.+/),
    );
  });

  it('preserves a role draft through minimize and guards destructive closure', async () => {
    const user = userEvent.setup();
    renderPage();
    const table = await screen.findByRole('region', {
      name: 'Current product-role assignments',
    });
    await user.click(await within(table).findByRole('button', { name: 'Assign role' }));
    const dialog = await screen.findByRole('dialog', { name: 'Assign product role' });
    const subject = within(dialog).getByRole('combobox', { name: 'Active subject' });
    subject.focus();
    await user.keyboard('{Enter}');
    await user.click(await screen.findByRole('option', { name: /Alex Nguyen/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Minimize dialog' }));

    const dock = document.querySelector<HTMLElement>('[data-slot="managed-window-dock"]');
    await user.click(within(dock as HTMLElement).getByRole('button', { name: 'Restore dialog' }));
    expect(subject).toHaveTextContent('Alex Nguyen');

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    const confirmation = await screen.findByRole('alertdialog', {
      name: 'Discard this assignment draft?',
    });
    await user.click(within(confirmation).getByRole('button', { name: 'Keep editing' }));
    expect(dialog).toBeVisible();

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await user.click(
      within(
        await screen.findByRole('alertdialog', { name: 'Discard this assignment draft?' }),
      ).getByRole('button', { name: 'Discard assignment draft' }),
    );
    await waitFor(() => expect(dialog).not.toBeInTheDocument());
  });

  it('fails closed on query errors and keeps empty authoritative state explicit', async () => {
    const user = userEvent.setup();
    api.list.mockRejectedValueOnce(new TypeError('network unavailable'));
    renderPage();

    const table = await screen.findByRole('region', {
      name: 'Current product-role assignments',
    });
    expect(await within(table).findByText('Unable to load product-role management')).toBeVisible();
    expect(within(table).queryByRole('button', { name: 'Assign role' })).not.toBeInTheDocument();

    api.list.mockResolvedValue({ subjects: [], roles: [], assignments: [] });
    await user.click(within(table).getByRole('button', { name: 'Retry' }));
    expect(await within(table).findByText('No active product-role assignments.')).toBeVisible();
    expect(
      within(table).getByText('No active human or service subjects are available.'),
    ).toBeVisible();
    expect(within(table).queryByRole('button', { name: 'Assign role' })).not.toBeInTheDocument();
  });
});

function management() {
  return {
    subjects: [
      {
        subject: { kind: 'Human', subjectId: 'user-1' },
        displayName: 'Alex Nguyen',
        secondaryLabel: 'alex@example.com',
      },
    ],
    roles: [
      {
        policyVersionId: 'policy-version-1',
        policyKey: 'cases',
        roleKey: 'case.reviewer',
        displayName: 'Case reviewer',
        description: 'Reviews submitted cases.',
      },
    ],
    assignments: [
      {
        workspaceId: 'workspace-1',
        subject: { kind: 'Human', subjectId: 'user-1' },
        policyVersionId: 'policy-version-1',
        roleKey: 'case.reviewer',
        isActive: true,
        revision: 4,
      },
    ],
  };
}
