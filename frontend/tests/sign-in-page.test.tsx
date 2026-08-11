import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { completePostSignInFlow } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { SignInPage } from '@/features/auth/components/SignInPage';
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
    completePostSignInFlow: vi.fn(() => Promise.resolve(true)),
  };
});

async function fillSignInForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Email address'), '  alex@example.com  ');
  await user.type(screen.getByLabelText('Password'), '  maple river sunrise  ');
}

describe('SignInPage', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    navigateMock.mockReset();
    vi.mocked(completePostSignInFlow).mockClear();
    useAuthStore.getState().markBrowserSessionGuest('test-csrf-token');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows inline validation errors when form is empty', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    expect(screen.getByLabelText('Email address')).toBeRequired();
    expect(screen.getByLabelText('Password')).toBeRequired();
    const signIn = screen.getByRole('button', { name: /sign in/i });
    await user.click(signIn);

    expect(screen.getByText('Email address is required')).toBeInTheDocument();
    expect(screen.getByText('Password is required')).toBeInTheDocument();
  });

  it('submits trimmed email and exact password then opens the authenticated workspace', async () => {
    const user = userEvent.setup();
    let signInBody: Record<string, unknown> | undefined;
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/sign-in') && init?.method === 'POST') {
        signInBody = JSON.parse(String(init.body)) as Record<string, unknown>;
        return Promise.resolve({
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
      }
      if (url.includes('/api/auth/session') && init?.method === undefined) {
        return Promise.resolve(authenticatedSessionResponse());
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(completePostSignInFlow).toHaveBeenCalledWith());
    expect(navigateMock).toHaveBeenCalledWith({ to: '/dashboard', replace: true });
    const signIn = screen.getByRole('button', { name: 'Sign in' });
    expect(signIn).toBeDisabled();
    expect(signIn).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('status')).toHaveTextContent('Signing in');
    expect(signInBody?.email).toBe('alex@example.com');
    expect(signInBody?.password).toBe('  maple river sunrise  ');
  });

  it('keeps a pending authorization request through a credential error and resumes it after sign-in', async () => {
    const user = userEvent.setup();
    let attempts = 0;
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/sign-in') && init?.method === 'POST') {
        attempts += 1;
        if (attempts === 1) {
          return Promise.resolve({
            ok: false,
            status: 422,
            statusText: 'Unprocessable Entity',
            json: () =>
              Promise.resolve({
                code: 'identity.signIn.invalidCredentials',
                detail: 'Do not show this backend fallback.',
              }),
          } as unknown as Response);
        }

        return Promise.resolve({
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
      }
      if (url.includes('/api/auth/session') && init?.method === undefined) {
        return Promise.resolve(authenticatedSessionResponse());
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<SignInPage />, {
      path: '/sign-in?authorization_request=opaque-request-handle&authorization_client=local-mcp-client',
    });

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to sign in');

    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() =>
      expect(completePostSignInFlow).toHaveBeenCalledWith({
        clientId: 'local-mcp-client',
        requestUri: 'opaque-request-handle',
      }),
    );
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('rejects an incomplete authorization continuation without losing sign-in recovery', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/sign-in') && init?.method === 'POST') {
        return Promise.resolve({
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
      }
      if (url.includes('/api/auth/session') && init?.method === undefined) {
        return Promise.resolve(authenticatedSessionResponse());
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<SignInPage />, {
      path: '/sign-in?authorization_request=opaque-request-handle',
    });

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'This authorization request is no longer valid. Start the MCP connection again.',
    );

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(completePostSignInFlow).not.toHaveBeenCalled());
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeEnabled();
  });

  it('shows generic credential errors without field enumeration', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 422,
      statusText: 'Unprocessable Entity',
      json: () =>
        Promise.resolve({
          code: 'identity.signIn.invalidCredentials',
          detail: 'Do not show this backend fallback.',
        }),
    } as unknown as Response);

    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Unable to sign in');
    expect(alert).toHaveTextContent('Email or password is incorrect.');
    expect(alert).not.toHaveTextContent('Do not show this backend fallback.');
    expect(alert.compareDocumentPosition(screen.getByLabelText('Email address'))).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
    expect(screen.getByRole('button', { name: /sign in/i })).toBeEnabled();
  });

  it('shows verification-required state and resends verification email', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/sign-in') && init?.method === 'POST') {
        return Promise.resolve({
          ok: false,
          status: 422,
          statusText: 'Unprocessable Entity',
          json: () =>
            Promise.resolve({
              code: 'identity.signIn.verificationRequired',
              detail: 'Do not show this backend fallback.',
            }),
        } as unknown as Response);
      }
      if (url.includes('/api/auth/resend-verification') && init?.method === 'POST') {
        return Promise.resolve({
          ok: true,
          status: 204,
          text: () => Promise.resolve(''),
        } as unknown as Response);
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    const verificationNotice = await screen.findByRole('alert');
    expect(verificationNotice).toHaveTextContent('Email not verified');
    expect(verificationNotice).toHaveTextContent('Email verification is required before sign-in.');
    expect(screen.queryByText('Do not show this backend fallback.')).not.toBeInTheDocument();
    expect(within(verificationNotice).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByText("Didn't receive it?")).toBeInTheDocument();
    const resendAction = screen.getByRole('button', {
      name: /resend verification email/i,
    });
    const createAccount = screen.getByRole('link', { name: /create account/i });
    expect(verificationNotice).not.toContainElement(resendAction);
    expect(resendAction).toHaveTextContent(/^Resend email$/);
    for (const action of [resendAction, createAccount]) {
      expect(action).toHaveClass(
        'h-auto',
        'border-0',
        'p-0',
        'text-xs',
        'font-medium',
        'text-primary',
        'hover:underline',
      );
      expect(action).not.toHaveClass('underline', 'h-8', 'text-sm');
    }
    expect(resendAction.querySelector('svg')).not.toBeInTheDocument();
    expect(screen.getByText("Didn't receive it?").parentElement).toHaveClass('gap-x-1', 'text-xs');
    await user.click(resendAction);

    const feedback = await screen.findByRole('status');
    expect(feedback).toHaveTextContent('Verification email sent.');
    expect(feedback).toHaveClass('text-xs', 'text-success');
    expect(feedback).not.toHaveClass('text-sm');
    expect(verificationNotice).not.toContainElement(feedback);
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });

  it.each([
    {
      status: 500,
      statusText: 'Internal Server Error',
      expected: 'Something went wrong, please try again',
      expectedClass: 'text-destructive',
      disabled: false,
    },
    {
      status: 429,
      statusText: 'Too Many Requests',
      expected: 'Too many requests. Try again shortly.',
      expectedClass: 'text-warning',
      disabled: true,
    },
  ])('shows resend $status feedback below the action row', async ({
    status,
    statusText,
    expected,
    expectedClass,
    disabled,
  }) => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/sign-in') && init?.method === 'POST') {
        return Promise.resolve({
          ok: false,
          status: 422,
          statusText: 'Unprocessable Entity',
          json: () => Promise.resolve({ code: 'identity.signIn.verificationRequired' }),
        } as unknown as Response);
      }
      if (url.includes('/api/auth/resend-verification') && init?.method === 'POST') {
        return Promise.resolve({
          ok: false,
          status,
          statusText,
          json: () => Promise.resolve({ detail: statusText }),
        } as unknown as Response);
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<SignInPage />, { path: '/sign-in' });
    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    const verificationNotice = await screen.findByRole('alert');
    const resendAction = screen.getByRole('button', {
      name: /resend verification email/i,
    });
    await user.click(resendAction);

    const feedback = await screen.findByRole('status');
    expect(feedback).toHaveTextContent(expected);
    expect(feedback).toHaveClass('text-xs', expectedClass);
    expect(feedback).not.toHaveClass('text-sm');
    expect(verificationNotice).not.toContainElement(feedback);
    expect(resendAction).toHaveProperty('disabled', disabled);
    expect(screen.getAllByRole('alert')).toHaveLength(1);
  });

  it('shows workspace-unavailable and generic server errors as form alerts', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 422,
      statusText: 'Unprocessable Entity',
      json: () =>
        Promise.resolve({
          code: 'identity.signIn.accountUnavailable',
          detail: 'Do not show this backend fallback.',
        }),
    } as unknown as Response);

    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    const workspaceAlert = await screen.findByRole('alert');
    expect(workspaceAlert).toHaveTextContent('Unable to sign in');
    expect(workspaceAlert).toHaveTextContent('Account is not available for sign-in.');
    expect(workspaceAlert).not.toHaveTextContent('Do not show this backend fallback.');

    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error',
      json: () => Promise.resolve({ detail: 'boom' }),
    } as unknown as Response);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong, please try again'),
    );
    expect(screen.getByRole('button', { name: /sign in/i })).toBeEnabled();
  });

  it('shows rate-limited sign-in wait state and disables submit', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 429,
      statusText: 'Too Many Requests',
      json: () =>
        Promise.resolve({
          detail: 'Please wait before trying again.',
        }),
    } as unknown as Response);

    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Unable to sign in');
    expect(alert).toHaveTextContent('Too many sign-in attempts. Try again shortly.');
    expect(screen.getByRole('button', { name: /sign in/i })).toBeDisabled();
  });
});

function authenticatedSessionResponse(): Response {
  return {
    ok: true,
    status: 200,
    text: () =>
      Promise.resolve(
        JSON.stringify({
          authenticated: true,
          csrfToken: 'authenticated-csrf-token',
          user: {
            userId: 'user-1',
            workspaceId: 'workspace-1',
            email: 'alex@example.com',
            name: 'Alex Morgan',
          },
        }),
      ),
  } as unknown as Response;
}
