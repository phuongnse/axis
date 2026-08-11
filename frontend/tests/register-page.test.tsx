import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '../src/features/auth/auth-store';
import { RegisterPage } from '../src/features/auth/components/RegisterPage';
import { changeSiteLanguage } from '../src/features/preferences';
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
    useAuthStore.getState().markBrowserSessionGuest('test-csrf-token');
    mockLegalVersionsFetch();
  });

  afterEach(() => {
    useAuthStore.getState().clearSession();
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
    expect(screen.getByLabelText('Full name')).toBeRequired();
    expect(screen.getByLabelText('Email address')).toBeRequired();
    expect(screen.getByLabelText('Password')).toBeRequired();
    expect(screen.getByLabelText('Confirm password')).toBeRequired();
    expect(screen.getByRole('checkbox', { name: /terms of service/i })).toHaveAttribute(
      'aria-required',
      'true',
    );

    const createAccount = await screen.findByRole('button', { name: /create account/i });
    await user.click(createAccount);

    expect(screen.getByText('Full name is required')).toBeInTheDocument();
    expect(screen.getByText('Email address is required')).toBeInTheDocument();
    expect(screen.getByText('Password is required')).toBeInTheDocument();
    expect(screen.getByText('Password confirmation is required')).toBeInTheDocument();
    expect(
      screen.getByText('You must accept the Terms of Service and Privacy Policy'),
    ).toBeInTheDocument();
  });

  it('offers a sign-in link so registration is not a dead end', async () => {
    await renderWithRouter(<RegisterPage />, { path: '/register' });

    expect(screen.getByRole('link', { name: /sign in/i })).toHaveAttribute('href', '/sign-in');
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

    const metLengthCriteria = screen.getByRole('listitem', {
      name: 'Met: At least 15 characters',
    });
    const missingHardCriteria = screen.getByRole('listitem', { name: 'Missing: Hard to guess' });
    expect(metLengthCriteria).toHaveClass('text-success');
    expect(missingHardCriteria).toHaveClass('text-destructive');

    await user.clear(passwordInput);
    await user.type(passwordInput, 'maple river sunrise');

    expect(screen.getByRole('listitem', { name: 'Met: At least 15 characters' })).toHaveClass(
      'text-success',
    );
    expect(screen.getByRole('listitem', { name: 'Met: Hard to guess' })).toHaveClass(
      'text-success',
    );
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
    expect(stored).not.toContain('WorkspaceName');
  });

  it('shows a specific error when legal versions are unavailable', async () => {
    const user = userEvent.setup();
    let registerAttempted = false;
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/legal/versions')) {
        return Promise.resolve({
          ok: true,
          status: 200,
          text: () => Promise.resolve(JSON.stringify({})),
        } as unknown as Response);
      }
      if (url.includes('/api/users/register') && init?.method === 'POST') {
        registerAttempted = true;
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await fillRegisterForm(user);
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(
      await screen.findByText('Legal document versions are not loaded yet.'),
    ).toBeInTheDocument();
    expect(registerAttempted).toBe(false);
  });

  it('submits passwords with leading and trailing spaces as entered', async () => {
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

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await user.type(screen.getByLabelText('Full name'), 'Alex Brown');
    await user.type(screen.getByLabelText('Email address'), 'alex@example.com');
    await user.type(screen.getByLabelText('Password'), '  maple river sunrise  ');
    await user.type(screen.getByLabelText('Confirm password'), '  maple river sunrise  ');
    await user.click(screen.getByRole('checkbox', { name: /terms of service/i }));
    await user.click(screen.getByRole('button', { name: /create account/i }));

    await waitFor(() => {
      expect(registerBody?.password).toBe('  maple river sunrise  ');
      expect(registerBody?.passwordConfirmation).toBe('  maple river sunrise  ');
    });
  });

  it('submits the current site language as the preferred language', async () => {
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
    await renderWithRouter(<RegisterPage />, { path: '/register' });

    const fullName = screen.getByLabelText('Full name');
    const email = screen.getByLabelText('Email address');
    const passwordInput = screen.getByLabelText('Password', { exact: true });
    const passwordConfirmation = screen.getByLabelText('Confirm password', { exact: true });
    const legalAcceptance = screen.getByRole('checkbox', { name: /terms of service/i });
    const createAccount = screen.getByRole('button', { name: /create account/i });
    await user.type(fullName, 'Alex Brown');
    await user.type(email, 'alex@example.com');
    await user.type(passwordInput, 'maple river sunrise');
    await user.type(passwordConfirmation, 'maple river sunrise');
    await user.click(legalAcceptance);
    await act(() => changeSiteLanguage('vi'));
    await user.click(createAccount);

    await waitFor(() => {
      expect(registerBody?.preferredLanguage).toBe('vi');
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
                email: ['Do not show this backend fallback.'],
              },
              errorCodes: {
                email: ['identity.register.emailAlreadyExists'],
              },
            }),
        } as unknown as Response);
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await fillRegisterForm(user);
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(
      await screen.findByText('An account with this email already exists. Sign in instead.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Do not show this backend fallback.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /create account/i })).toBeEnabled();
  });

  it('maps duplicate email conflicts to the email field', async () => {
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
          status: 409,
          statusText: 'Conflict',
          json: () =>
            Promise.resolve({
              code: 'identity.register.emailAlreadyExists',
              detail: 'Do not show this backend fallback.',
            }),
        } as unknown as Response);
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<RegisterPage />, { path: '/register' });

    await fillRegisterForm(user);
    await user.click(screen.getByRole('button', { name: /create account/i }));

    expect(
      await screen.findByText('An account with this email already exists. Sign in instead.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Email address')).toHaveAttribute('aria-invalid', 'true');
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
