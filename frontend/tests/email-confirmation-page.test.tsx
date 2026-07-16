import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { EmailConfirmationPage } from '@/features/auth/components/EmailConfirmationPage';
import { saveRegistrationContext } from '@/features/auth/registration-context';
import { renderWithRouter } from './render-with-router';

describe('EmailConfirmationPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    saveRegistrationContext({
      email: 'alex@example.com',
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  it('shows confirmation copy and resend link', async () => {
    await renderWithRouter(<EmailConfirmationPage />, { path: '/register/confirmation' });

    expect(screen.getByRole('heading', { name: /check your email/i })).toBeInTheDocument();
    const confirmationCopy = screen.getByText(
      /If an account exists for this email, you will receive a verification link shortly./,
    );
    expect(confirmationCopy).toBeInTheDocument();
    expect(confirmationCopy.closest('[role="alert"]')).toHaveClass(
      'border-info/25',
      'bg-info/10',
      'text-info',
    );
    expect(screen.getByText(/Sent to alex@example.com/)).toBeInTheDocument();
    expect(screen.getByText("Didn't receive it?")).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /resend email/i })).toHaveClass('h-8');
    expect(
      screen.queryByRole('link', { name: /register another account/i }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to registration/i })).toHaveAttribute(
      'href',
      '/register',
    );
  });

  it('shows success banner after resend succeeds', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 204,
      text: () => Promise.resolve(''),
    } as unknown as Response);

    await renderWithRouter(<EmailConfirmationPage />, { path: '/register/confirmation' });
    await user.click(screen.getByRole('button', { name: /resend email/i }));

    expect(await screen.findByText('Verification email sent')).toBeInTheDocument();
  });

  it('updates resend rate-limit notices when language changes after failure', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 429,
      statusText: 'Too Many Requests',
      json: () =>
        Promise.resolve({
          code: 'identity.emailVerification.resendRateLimited',
          detail: 'Please wait before trying again.',
        }),
    } as unknown as Response);

    await renderWithRouter(<EmailConfirmationPage />, { path: '/register/confirmation' });
    await user.click(screen.getByRole('button', { name: /resend email/i }));

    expect(await screen.findByText('Too many requests. Try again shortly.')).toBeInTheDocument();
    expect(screen.queryByText('Please wait before trying again.')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Preferences' }));
    await user.click(screen.getByRole('button', { name: 'Vietnamese' }));

    expect(await screen.findByText('Quá nhiều yêu cầu. Hãy thử lại sau.')).toBeInTheDocument();
    expect(screen.queryByText('Too many requests. Try again shortly.')).not.toBeInTheDocument();
  });
});
