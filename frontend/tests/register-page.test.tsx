import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { RegisterPage } from '../src/features/auth/components/RegisterPage';
import { renderWithRouter } from './render-with-router';

describe('RegisterPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows inline validation errors when form is empty', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(await screen.findByText('Organization name is required')).toBeInTheDocument();
    expect(screen.getByText('Full name is required')).toBeInTheDocument();
    expect(screen.getByText('Email address is required')).toBeInTheDocument();
    expect(screen.getByText('Password is required')).toBeInTheDocument();
    expect(screen.getByText('Password confirmation is required')).toBeInTheDocument();
  });

  it('shows confirmation screen after successful submit', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      text: () =>
        Promise.resolve(
          JSON.stringify({
            message: 'Registration successful. Please check your email to verify your account.',
          }),
        ),
    } as unknown as Response);

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await user.type(screen.getByLabelText('Organization name'), "O'Brien & Co.");
    await user.type(screen.getByLabelText('Full name'), 'Alex Brown');
    await user.type(screen.getByLabelText('Email address'), 'alex@example.com');
    await user.type(screen.getByLabelText('Password'), 'Passw0rd');
    await user.type(screen.getByLabelText('Confirm password'), 'Passw0rd');
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(await screen.findByRole('heading', { name: /check your email/i })).toBeInTheDocument();
    expect(
      screen.getByText('If an account exists for this email, you will receive a verification link shortly.'),
    ).toBeInTheDocument();
  });

  it('shows generic server error when API returns 5xx', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error',
      json: () => Promise.resolve({ message: 'boom' }),
    } as unknown as Response);

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await user.type(screen.getByLabelText('Organization name'), 'Acme Corp');
    await user.type(screen.getByLabelText('Full name'), 'Alex Brown');
    await user.type(screen.getByLabelText('Email address'), 'alex@example.com');
    await user.type(screen.getByLabelText('Password'), 'Passw0rd');
    await user.type(screen.getByLabelText('Confirm password'), 'Passw0rd');
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong, please try again',
    );
    expect(screen.getByRole('button', { name: /create account/i })).toBeEnabled();
  });
});
