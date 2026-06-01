import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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

describe('VerifyEmailPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    navigateMock.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('redirects to provisioning after successful verification', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 204,
      text: () => Promise.resolve(''),
    } as unknown as Response);

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=valid-token' });

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith({
        to: '/provisioning',
        search: { token: 'valid-token' },
      });
    });
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
  });
});
