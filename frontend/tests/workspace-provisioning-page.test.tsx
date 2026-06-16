import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { WorkspaceProvisioningPage } from '@/features/auth/components/WorkspaceProvisioningPage';
import { saveRegistrationContext } from '@/features/auth/registration-context';
import { renderWithRouter } from './render-with-router';

describe('WorkspaceProvisioningPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    saveRegistrationContext({
      email: 'alex@example.com',
      TenantName: 'Acme Corp',
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  it('shows provisioning headline and checklist steps', async () => {
    vi.mocked(fetch).mockResolvedValue({
      ok: true,
      status: 200,
      text: () =>
        Promise.resolve(
          JSON.stringify({
            tenantId: '00000000-0000-0000-0000-000000000001',
            TenantStatus: 'Provisioning',
            isReady: false,
            modules: [
              { module: 'DataModeling', status: 'Pending', attemptCount: 0, lastError: null },
            ],
          }),
        ),
    } as unknown as Response);

    await renderWithRouter(<WorkspaceProvisioningPage />, {
      path: '/provisioning?token=poll-token',
    });

    expect(await screen.findByText('Creating shared account')).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: /Setting up "Acme Corp" shared account/i }),
    ).toBeInTheDocument();
    expect(screen.getByText('Assigning admin role')).toBeInTheDocument();
    expect(screen.getByText('Opening shared account')).toBeInTheDocument();
  });

  it('shows failed state when provisioning retries are exhausted', async () => {
    vi.mocked(fetch).mockResolvedValue({
      ok: true,
      status: 200,
      text: () =>
        Promise.resolve(
          JSON.stringify({
            tenantId: '00000000-0000-0000-0000-000000000001',
            TenantStatus: 'ProvisioningFailed',
            isReady: false,
            modules: [
              { module: 'DataModeling', status: 'Failed', attemptCount: 3, lastError: 'timeout' },
            ],
          }),
        ),
    } as unknown as Response);

    await renderWithRouter(<WorkspaceProvisioningPage />, {
      path: '/provisioning?token=poll-token',
    });

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /setup failed/i })).toBeInTheDocument();
    });
    expect(screen.getByText(/Provisioning failed after 3 attempts/i)).toBeInTheDocument();
  });
});
