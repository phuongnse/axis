import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { DashboardOverview } from '../src/features/dashboard/components/DashboardOverview';
import { renderWithRouter } from './render-with-router';

const profile = {
  id: '9fc0f6c1-24f6-4e66-a50f-3f742ad10b1a',
  email: 'admin@example.com',
  fullName: 'Admin User',
  isActive: true,
  workspaceId: '3cde5c59-d18b-464f-a021-5fce16b01118',
  workspaces: [
    {
      id: '3cde5c59-d18b-464f-a021-5fce16b01118',
      name: 'Admin User',
      slug: 'admin-user',
      type: 'Personal',
      isCurrent: true,
    },
  ],
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

  it('renders the verified account dashboard from the current user API', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.includes('/api/users/me')) {
        return Promise.resolve(jsonResponse(profile));
      }

      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<DashboardOverview />, { path: '/dashboard' });

    expect(await screen.findByRole('heading', { name: 'Admin User' })).toBeInTheDocument();
    expect(screen.getByText('Account ready')).toBeInTheDocument();
    expect(screen.getAllByText('admin@example.com')).toHaveLength(2);
    expect(screen.getByText('Personal')).toBeInTheDocument();

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  });

  it('shows a retryable error state when the current user API fails', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error',
      json: () => Promise.resolve({ detail: 'boom' }),
    } as unknown as Response);

    await renderWithRouter(<DashboardOverview />, { path: '/dashboard' });

    expect(
      await screen.findByRole('heading', { name: /unable to load account/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  });
});
