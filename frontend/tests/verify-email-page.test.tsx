import { screen, waitFor } from '@testing-library/react';
import { StrictMode } from 'react';
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

  it('starts PKCE after user verification', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      text: () =>
        Promise.resolve(
          JSON.stringify({
            sessionEstablished: true,
            nextStep: 'Dashboard',
          }),
        ),
    } as unknown as Response);

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=valid-token' });

    await waitFor(() => {
      expect(completePostVerifyPkceFlow).toHaveBeenCalledWith();
    });
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('submits a verification token only once under React StrictMode', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      text: () =>
        Promise.resolve(
          JSON.stringify({
            sessionEstablished: true,
            nextStep: 'Dashboard',
          }),
        ),
    } as unknown as Response);

    await renderWithRouter(
      <StrictMode>
        <VerifyEmailPage />
      </StrictMode>,
      { path: '/auth/verify?token=strict-token' },
    );

    await waitFor(() => {
      expect(completePostVerifyPkceFlow).toHaveBeenCalledWith();
    });
    expect(fetch).toHaveBeenCalledTimes(1);
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
    expect(screen.getByRole('button', { name: /resend verification email/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to registration/i })).toHaveAttribute(
      'href',
      '/register',
    );
  });

  it('shows invalid message when verification token is missing', async () => {
    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify' });

    expect(
      await screen.findByRole('heading', { name: /invalid verification link/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to registration/i })).toBeInTheDocument();
  });

  it('shows already-used message when verification token was consumed', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 422,
      statusText: 'Unprocessable Entity',
      json: () =>
        Promise.resolve({
          detail: 'This link has already been used. Please sign in.',
        }),
    } as unknown as Response);

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=used-token' });

    expect(await screen.findByRole('heading', { name: /already verified/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /sign in/i })).toBeInTheDocument();
  });

  it('shows rate-limited message when verification retries are throttled', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 429,
      statusText: 'Too Many Requests',
      json: () =>
        Promise.resolve({
          detail: 'Please wait before trying again.',
        }),
    } as unknown as Response);

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=limited-token' });

    expect(await screen.findByRole('heading', { name: /please wait/i })).toBeInTheDocument();
    expect(screen.getByLabelText('Email address')).toBeDisabled();
    expect(screen.getByRole('button', { name: /resend verification email/i })).toBeDisabled();
    expect(screen.getByRole('link', { name: /back to registration/i })).toHaveAttribute(
      'href',
      '/register',
    );
  });
});
