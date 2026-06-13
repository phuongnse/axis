import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { DashboardOverview } from '../src/features/dashboard/components/DashboardOverview';
import { renderWithRouter } from './render-with-router';

const profileWithWorkspace = {
  id: '9fc0f6c1-24f6-4e66-a50f-3f742ad10b1a',
  email: 'admin@example.com',
  firstName: 'Admin',
  lastName: 'User',
  fullName: 'Admin User',
  avatarUrl: null,
  isActive: true,
  orgId: '3cde5c59-d18b-464f-a021-5fce16b01118',
  permissions: ['organization:settings:read'],
};

const organizationSettings = {
  organizationId: '3cde5c59-d18b-464f-a021-5fce16b01118',
  name: 'Northwind Labs',
  slug: 'northwind-labs',
  logoUrl: null,
  planName: 'Business',
  status: 'Active',
  createdAt: '2026-06-01T00:00:00Z',
  timeZoneId: 'Asia/Saigon',
  defaultLanguage: 'en',
  scheduledHardDeleteAt: null,
  usage: {
    workflowsUsed: 7,
    workflowsLimit: 20,
    executionsUsedThisMonth: 1250,
    executionsPerMonthLimit: 5000,
    usersUsed: 4,
    usersLimit: 10,
  },
};

function jsonResponse(data: unknown): Response {
  return {
    ok: true,
    status: 200,
    text: () => Promise.resolve(JSON.stringify(data)),
  } as unknown as Response;
}

describe('DashboardOverview', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('renders current workspace data from the API instead of demo data', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.includes('/api/users/me')) {
        return Promise.resolve(jsonResponse(profileWithWorkspace));
      }

      if (url.includes('/api/organizations/current/settings')) {
        return Promise.resolve(jsonResponse(organizationSettings));
      }

      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<DashboardOverview />, { path: '/dashboard' });

    expect(await screen.findByRole('heading', { name: 'Northwind Labs' })).toBeInTheDocument();
    expect(screen.getByText('Business')).toBeInTheDocument();
    expect(screen.getByText('Users')).toBeInTheDocument();
    expect(screen.getByText('Workflows')).toBeInTheDocument();
    expect(screen.getByText('Executions this month')).toBeInTheDocument();
    expect(screen.getByText('1,250')).toBeInTheDocument();

    expect(screen.queryByText('Acme Corp')).not.toBeInTheDocument();
    expect(screen.queryByText('Order Processing')).not.toBeInTheDocument();
    expect(screen.queryByText('42 models')).not.toBeInTheDocument();
  });

  it('does not request organization settings when the account has no workspace', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.includes('/api/users/me')) {
        return Promise.resolve(
          jsonResponse({
            ...profileWithWorkspace,
            orgId: null,
            permissions: [],
          }),
        );
      }

      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<DashboardOverview />, { path: '/dashboard' });

    expect(await screen.findByText(/not linked to a workspace yet/i)).toBeInTheDocument();
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  });
});
