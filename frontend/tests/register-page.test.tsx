import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { RegisterPage } from '../src/features/auth/components/RegisterPage';
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

const LEGAL_VERSIONS = {
  termsVersion: '2026-05-01',
  privacyVersion: '2026-05-01',
};

function mockLegalVersionsFetch() {
  vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/api/legal/versions')) {
      return Promise.resolve({
        ok: true,
        status: 200,
        text: () => Promise.resolve(JSON.stringify(LEGAL_VERSIONS)),
      } as unknown as Response);
    }
    return Promise.reject(new Error(`Unexpected fetch: ${url}`));
  });
}

async function fillRegisterForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Full name'), 'Alex Brown');
  await user.type(screen.getByLabelText('Email address'), 'alex@example.com');
  await user.type(screen.getByLabelText('Password'), 'maple river sunrise');
  await user.type(screen.getByLabelText('Confirm password'), 'maple river sunrise');
  await user.click(screen.getByRole('checkbox', { name: /terms of service/i }));
}

describe('RegisterPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    navigateMock.mockReset();
    sessionStorage.clear();
    mockLegalVersionsFetch();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows inline validation errors when form is empty', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<RegisterPage />, { path: '/register' });

    expect(screen.getByText('This name will appear on your account.')).toBeInTheDocument();
    expect(
      screen.getByText('We will send a verification link to this address.'),
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(screen.getByText('Full name is required')).toBeInTheDocument();
    expect(screen.getByText('Email address is required')).toBeInTheDocument();
    expect(screen.getByText('Password is required')).toBeInTheDocument();
    expect(screen.getByText('Password confirmation is required')).toBeInTheDocument();
    expect(
      screen.getByText('You must accept the Terms of Service and Privacy Policy'),
    ).toBeInTheDocument();
  });

  it('updates password criteria while typing', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<RegisterPage />, { path: '/register' });

    expect(
      screen.getByRole('listitem', { name: 'Missing: At least 15 characters' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Missing: Hard to guess' })).toBeInTheDocument();

    const passwordInput = screen.getByLabelText('Password');
    await user.type(passwordInput, '1234567890123456790');

    expect(
      screen.getByRole('listitem', { name: 'Met: At least 15 characters' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Missing: Hard to guess' })).toBeInTheDocument();

    await user.clear(passwordInput);
    await user.type(passwordInput, 'maple river sunrise');

    expect(
      screen.getByRole('listitem', { name: 'Met: At least 15 characters' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('listitem', { name: 'Met: Hard to guess' })).toBeInTheDocument();
  });

  it('navigates to confirmation screen after successful submit', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/legal/versions')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          text: () => Promise.resolve(JSON.stringify(LEGAL_VERSIONS)),
        } as unknown as Response);
      }
      if (url.includes('/api/users/register') && init?.method === 'POST') {
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

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await fillRegisterForm(user);
    await user.click(screen.getByRole('button', { name: /create account/i }));

    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith({ to: '/register/confirmation' }),
    );
    const stored = sessionStorage.getItem('axis.registration-context');
    expect(stored).toContain('alex@example.com');
    expect(stored).not.toContain('TenantName');
  });

  it('includes setup token when registering the first Tenant user', async () => {
    const user = userEvent.setup();
    let registerBody: Record<string, unknown> | undefined;
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/legal/versions')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          text: () => Promise.resolve(JSON.stringify(LEGAL_VERSIONS)),
        } as unknown as Response);
      }
      if (url.includes('/api/users/register') && init?.method === 'POST') {
        registerBody = JSON.parse(String(init.body)) as Record<string, unknown>;
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

    await renderWithRouter(<RegisterPage />, { path: '/register?setupToken=setup-token' });

    await fillRegisterForm(user);
    await user.click(screen.getByRole('button', { name: /create account/i }));

    await waitFor(() => {
      expect(registerBody?.tenantSetupToken).toBe('setup-token');
    });
  });

  it('maps backend validation errors to inline field messages', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/legal/versions')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          text: () => Promise.resolve(JSON.stringify(LEGAL_VERSIONS)),
        } as unknown as Response);
      }
      if (url.includes('/api/users/register') && init?.method === 'POST') {
        return Promise.resolve({
          ok: false,
          status: 400,
          statusText: 'Bad Request',
          json: () =>
            Promise.resolve({
              errors: {
                email: ['Email is already registered.'],
              },
            }),
        } as unknown as Response);
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await fillRegisterForm(user);
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(await screen.findByText('Email is already registered.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /create account/i })).toBeEnabled();
  });

  it('shows generic server error when API returns 5xx', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/legal/versions')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          text: () => Promise.resolve(JSON.stringify(LEGAL_VERSIONS)),
        } as unknown as Response);
      }
      if (url.includes('/api/users/register') && init?.method === 'POST') {
        return Promise.resolve({
          ok: false,
          status: 500,
          statusText: 'Internal Server Error',
          json: () => Promise.resolve({ message: 'boom' }),
        } as unknown as Response);
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await fillRegisterForm(user);
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong, please try again',
    );
    expect(screen.getByRole('button', { name: /create account/i })).toBeEnabled();
  });
});
