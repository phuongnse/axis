import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { revokeProductRole } from '../api';
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
      <ProductRoleAssignmentsPage />
    </QueryClientProvider>,
  );
}

describe('ProductRoleAssignmentsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.list.mockResolvedValue(management());
    api.revoke.mockResolvedValue({ ...management().assignments[0], isActive: false, revision: 5 });
  });

  it('renders server-owned role presentation and revokes with clean subject and revision', async () => {
    const user = userEvent.setup();
    renderPage();
    expect(await screen.findByText('Case reviewer')).toBeInTheDocument();
    expect(screen.getByText('Reviews submitted cases.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Revoke role' }));
    await user.click(screen.getByRole('button', { name: 'Revoke role' }));
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
  });

  it('keeps empty authoritative state distinct from loading and error', async () => {
    api.list.mockResolvedValue({ subjects: [], roles: [], assignments: [] });
    renderPage();
    expect(await screen.findByText('Assignment unavailable')).toBeInTheDocument();
    expect(
      screen.getByText('No active human or service subjects are available.'),
    ).toBeInTheDocument();
    expect(screen.getByText('No active product-role assignments.')).toBeInTheDocument();
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
