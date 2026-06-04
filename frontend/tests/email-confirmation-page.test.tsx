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
    expect(
      screen.getByText(
        /If an account exists for this email, you will receive a verification link shortly./,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(/Sent to alex@example.com/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /resend email/i })).toBeInTheDocument();
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
});
