import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { completePostVerifyPkceFlow } from '@/features/auth/api';
import { VerifyEmailPage, verifySuccessHoldMs } from '@/features/auth/components/VerifyEmailPage';
import { changeSiteLanguage } from '@/features/preferences';
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
    completePostVerifyPkceFlow: vi.fn(() => Promise.resolve(true)),
  };
});

describe('VerifyEmailPage', () => {
  beforeEach(async () => {
    await changeSiteLanguage('en', { persist: false });
    vi.stubGlobal('fetch', vi.fn());
    navigateMock.mockReset();
    vi.mocked(completePostVerifyPkceFlow).mockClear();
    vi.mocked(completePostVerifyPkceFlow).mockResolvedValue(true);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows a readable verified state before starting PKCE after user verification', async () => {
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

    const { unmount } = await renderWithRouter(<VerifyEmailPage />, {
      path: '/auth/verify?token=valid-token',
    });

    expect(await screen.findByRole('heading', { name: /email verified/i })).toBeInTheDocument();
    expect(
      screen.getByText(
        "Your email is verified and your account is ready. Continue now, or we'll take you to the dashboard in a few seconds.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /continue to dashboard/i })).toBeInTheDocument();
    expect(verifySuccessHoldMs).toBeGreaterThanOrEqual(5_000);
    expect(completePostVerifyPkceFlow).not.toHaveBeenCalled();
    expect(navigateMock).not.toHaveBeenCalled();
    unmount();
  });

  it('lets the user continue manually before the automatic dashboard handoff', async () => {
    const user = userEvent.setup();
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

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=manual-token' });

    const continueButton = await screen.findByRole('button', {
      name: /continue to dashboard/i,
    });
    expect(completePostVerifyPkceFlow).not.toHaveBeenCalled();

    await user.click(continueButton);

    await waitFor(() => {
      expect(completePostVerifyPkceFlow).toHaveBeenCalledWith();
    });
    expect(completePostVerifyPkceFlow).toHaveBeenCalledTimes(1);
    expect(continueButton).toBeDisabled();
    expect(continueButton).toHaveTextContent('Opening dashboard...');
    expect(navigateMock).toHaveBeenCalledWith({ to: '/dashboard', replace: true });
  });

  it('localizes the verified handoff state in the selected language', async () => {
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
    await changeSiteLanguage('vi', { persist: false });

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=valid-token' });

    expect(
      await screen.findByRole('heading', { name: 'Email đã được xác minh' }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'Email của bạn đã được xác minh và tài khoản đã sẵn sàng. Bạn có thể tiếp tục ngay hoặc chúng tôi sẽ tự đưa bạn đến bảng điều khiển sau vài giây.',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Tiếp tục đến bảng điều khiển' }),
    ).toBeInTheDocument();
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

    expect(await screen.findByRole('heading', { name: /email verified/i })).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it('shows expired message when verification token expired', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 422,
      statusText: 'Unprocessable Entity',
      json: () =>
        Promise.resolve({
          code: 'identity.emailVerification.expiredToken',
          detail: 'Do not parse this backend fallback.',
        }),
    } as unknown as Response);

    await renderWithRouter(<VerifyEmailPage />, { path: '/auth/verify?token=expired-token' });

    expect(
      await screen.findByRole('heading', { name: /verification link expired/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Use the email that received the verification link.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Email address')).toBeRequired();
    expect(screen.getByRole('button', { name: /resend verification email/i })).toHaveClass('h-9');
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
          code: 'identity.emailVerification.alreadyUsedToken',
          detail: 'Do not parse this backend fallback.',
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
          code: 'common.rateLimited',
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
