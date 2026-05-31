import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { RegisterPage } from '../src/features/auth/components/RegisterPage';
import { renderWithRouter } from './render-with-router';

interface RegisterResponseConfig {
  ok: boolean;
  status: number;
  statusText?: string;
  body: unknown;
}

/**
 * Routes fetch by URL so the on-mount external-providers query and the register
 * POST are both satisfied deterministically (otherwise they race for a single
 * mockResolvedValueOnce).
 */
function mockFetch(options: { providers?: string[]; register?: RegisterResponseConfig } = {}) {
  const providers = options.providers ?? [];
  vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString();

    if (url.includes('/api/auth/external-providers')) {
      return Promise.resolve({
        ok: true,
        status: 200,
        text: () => Promise.resolve(JSON.stringify({ providers })),
      } as unknown as Response);
    }

    if (url.includes('/api/organizations') && init?.method === 'POST') {
      const register = options.register;
      if (!register) {
        return Promise.reject(new Error('No register response configured'));
      }
      return Promise.resolve({
        ok: register.ok,
        status: register.status,
        statusText: register.statusText,
        text: () => Promise.resolve(JSON.stringify(register.body)),
        json: () => Promise.resolve(register.body),
      } as unknown as Response);
    }

    return Promise.reject(new Error(`Unexpected fetch: ${url}`));
  });
}

async function fillForm(user: ReturnType<typeof userEvent.setup>, orgName = "O'Brien & Co.") {
  await user.type(screen.getByLabelText('Organization name'), orgName);
  await user.type(screen.getByLabelText('Full name'), 'Alex Brown');
  await user.type(screen.getByLabelText('Email address'), 'alex@example.com');
  await user.type(screen.getByLabelText('Password'), 'Passw0rd');
  await user.type(screen.getByLabelText('Confirm password'), 'Passw0rd');
}

describe('RegisterPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    mockFetch();
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
    mockFetch({
      register: {
        ok: true,
        status: 200,
        body: {
          message: 'Registration successful. Please check your email to verify your account.',
        },
      },
    });

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await fillForm(user);
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(await screen.findByRole('heading', { name: /check your email/i })).toBeInTheDocument();
    expect(
      screen.getByText(
        'If an account exists for this email, you will receive a verification link shortly.',
      ),
    ).toBeInTheDocument();
  });

  it('maps backend validation errors to inline field messages', async () => {
    const user = userEvent.setup();
    mockFetch({
      register: {
        ok: false,
        status: 400,
        statusText: 'Bad Request',
        body: {
          errors: {
            org_name: ['Organization name must be between 2 and 100 characters.'],
          },
        },
      },
    });

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await fillForm(user, 'Acme Corp');
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(
      await screen.findByText('Organization name must be between 2 and 100 characters.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /create account/i })).toBeEnabled();
  });

  it('shows generic server error when API returns 5xx', async () => {
    const user = userEvent.setup();
    mockFetch({
      register: {
        ok: false,
        status: 500,
        statusText: 'Internal Server Error',
        body: { message: 'boom' },
      },
    });

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await fillForm(user, 'Acme Corp');
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong, please try again',
    );
    expect(screen.getByRole('button', { name: /create account/i })).toBeEnabled();
  });

  it('renders SSO buttons for configured external providers', async () => {
    mockFetch({ providers: ['google', 'github'] });

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    const googleLink = await screen.findByRole('link', { name: /continue with google/i });
    expect(googleLink).toHaveAttribute('href', '/connect/external/register/google');
    expect(screen.getByRole('link', { name: /continue with github/i })).toHaveAttribute(
      'href',
      '/connect/external/register/github',
    );
  });

  it('does not render SSO buttons when no providers are configured', async () => {
    await renderWithRouter(<RegisterPage />, { path: '/register' });

    // Email/password form is always present.
    expect(await screen.findByLabelText('Organization name')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /continue with/i })).not.toBeInTheDocument();
  });
});
