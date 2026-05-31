import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { RegisterCompletePage } from '@/features/auth/components/RegisterCompletePage';
import { renderWithRouter } from './render-with-router';

const SESSION_ID = '11111111-1111-1111-1111-111111111111';

function mockFetch(options: { session?: { email: string; display_name: string } | null } = {}) {
  const session =
    options.session === undefined
      ? { email: 'sso-user@example.com', display_name: 'SSO User' }
      : options.session;

  vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString();

    if (url.includes('/api/auth/external-registration/')) {
      if (session === null) {
        return Promise.resolve({
          ok: false,
          status: 404,
          json: () => Promise.resolve({ detail: 'Not found' }),
        } as unknown as Response);
      }
      return Promise.resolve({
        ok: true,
        status: 200,
        text: () => Promise.resolve(JSON.stringify(session)),
      } as unknown as Response);
    }

    if (url.includes('/api/organizations') && init?.method === 'POST') {
      return Promise.resolve({
        ok: true,
        status: 200,
        text: () =>
          Promise.resolve(
            JSON.stringify({
              message: 'Registration successful. Please check your email to verify your account.',
            }),
          ),
      } as unknown as Response);
    }

    return Promise.reject(new Error(`Unexpected fetch: ${url}`));
  });
}

describe('RegisterCompletePage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    mockFetch();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows an expired-session message when the session is invalid', async () => {
    await renderWithRouter(<RegisterCompletePage sessionId="" invalidSession />, {
      path: '/register/complete',
    });

    expect(
      await screen.findByRole('heading', { name: /registration session expired/i }),
    ).toBeInTheDocument();
  });

  it('shows an expired-session message when the session lookup 404s', async () => {
    mockFetch({ session: null });

    await renderWithRouter(<RegisterCompletePage sessionId={SESSION_ID} />, {
      path: '/register/complete',
    });

    expect(
      await screen.findByRole('heading', { name: /registration session expired/i }),
    ).toBeInTheDocument();
  });

  it('prefills the form from the session and previews the slug', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<RegisterCompletePage sessionId={SESSION_ID} />, {
      path: '/register/complete',
    });

    // Email comes from the provider session and is read-only.
    const emailField = await screen.findByLabelText('Email address');
    expect(emailField).toHaveValue('sso-user@example.com');
    expect(emailField).toBeDisabled();

    // Full name is prefilled from the provider display name.
    await waitFor(() => expect(screen.getByLabelText('Full name')).toHaveValue('SSO User'));

    await user.type(screen.getByLabelText('Organization name'), "O'Brien & Co.");
    await waitFor(() =>
      expect(screen.getByLabelText('Organization slug')).toHaveValue('o-brien-co'),
    );
  });

  it('submits the completion form and shows the check-your-email screen', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<RegisterCompletePage sessionId={SESSION_ID} />, {
      path: '/register/complete',
    });

    await screen.findByLabelText('Organization name');
    await user.type(screen.getByLabelText('Organization name'), 'Acme Corp');
    await user.click(screen.getByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(await screen.findByRole('heading', { name: /check your email/i })).toBeInTheDocument();
  });
});
