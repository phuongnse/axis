import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { completePostVerifyPkceFlow } from '@/features/auth/api';
import { VerifyEmailPage } from '@/features/auth/components/VerifyEmailPage';
import { renderWithRouter } from './render-with-router';

const navigateMock = vi.fn();

vi.mock('@tanstack/react-router', async () => {
  const actual =
    await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router');
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

vi.mock('@/features/auth/api', async () => {
  const actual = await vi.importActual<typeof import('@/features/auth/api')>('@/features/auth/api');
  return {
    ...actual,
    completePostVerifyPkceFlow: vi.fn(() => Promise.resolve()),
  };
});

describe('VerifyEmailPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    navigateMock.mockReset();
    vi.mocked(completePostVerifyPkceFlow).mockClear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('starts PKCE and stores provisioning token after user verification', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      text: () =>
        Promise.resolve(
          JSON.stringify({
            sessionEstablished: true,
            nextStep: 'WorkspaceProvisioning',
          }),
        ),
    } as unknown as Response);

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=valid-token' });

    await waitFor(() => {
      expect(completePostVerifyPkceFlow).toHaveBeenCalledWith('valid-token');
    });
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('navigates to user registration when organization contact is verified', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      text: () =>
        Promise.resolve(
          JSON.stringify({
            sessionEstablished: false,
            nextStep: 'RegisterUser',
            organizationSetupToken: 'setup-token',
          }),
        ),
    } as unknown as Response);

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=org-token' });

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith({
        to: '/register',
        search: { setupToken: 'setup-token' },
      });
    });
    expect(completePostVerifyPkceFlow).not.toHaveBeenCalled();
  });

  it('shows expired message when verification token expired', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 422,
      statusText: 'Unprocessable Entity',
      json: () =>
        Promise.resolve({
          detail: 'This verification link has expired. Please request a new verification email.',
        }),
    } as unknown as Response);

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=expired-token' });

    expect(
      await screen.findByRole('heading', { name: /verification link expired/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Use the email that received the verification link.'),
    ).toBeInTheDocument();
  });
});
