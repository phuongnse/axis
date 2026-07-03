import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { completePostSignInPkceFlow } from '@/features/auth/api';
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
    completePostSignInPkceFlow: vi.fn(() => Promise.resolve()),
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
    vi.mocked(completePostSignInPkceFlow).mockClear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows inline validation errors when form is empty', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(screen.getByText('Email address is required')).toBeInTheDocument();
    expect(screen.getByText('Password is required')).toBeInTheDocument();
  });

  it('updates client validation errors when language changes', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(screen.getByText('Email address is required')).toBeInTheDocument();
    expect(screen.getByLabelText('Email address')).toHaveAttribute('aria-invalid', 'true');

    await user.click(screen.getByRole('button', { name: 'Preferences' }));
    await user.click(screen.getByRole('button', { name: /tiếng việt/i }));

    expect(await screen.findByText('Email là bắt buộc')).toBeInTheDocument();
    expect(screen.getByText('Mật khẩu là bắt buộc')).toBeInTheDocument();
    expect(screen.queryByText('Email address is required')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Địa chỉ email')).toHaveAttribute('aria-invalid', 'true');
  });

  it('submits trimmed email and exact password then starts PKCE', async () => {
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
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(completePostSignInPkceFlow).toHaveBeenCalledWith());
    expect(signInBody?.email).toBe('alex@example.com');
    expect(signInBody?.password).toBe('  maple river sunrise  ');
  });

  it('shows generic credential errors without field enumeration', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 422,
      statusText: 'Unprocessable Entity',
      json: () =>
        Promise.resolve({
          detail: 'Email or password is incorrect.',
        }),
    } as unknown as Response);

    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Email or password is incorrect.');
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
              detail: 'Email verification is required before sign-in.',
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

    expect(
      await screen.findByText('Email verification is required before sign-in.'),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /resend verification email/i }));

    expect(await screen.findByText('Verification email sent.')).toBeInTheDocument();
  });

  it('shows workspace-unavailable and generic server errors as form alerts', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 422,
      statusText: 'Unprocessable Entity',
      json: () =>
        Promise.resolve({
          detail: 'Account is not available for sign-in.',
        }),
    } as unknown as Response);

    await renderWithRouter(<SignInPage />, { path: '/sign-in' });

    await fillSignInForm(user);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Account is not available for sign-in.',
    );

    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error',
      json: () => Promise.resolve({ detail: 'boom' }),
    } as unknown as Response);
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong, please try again',
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

    expect(await screen.findByRole('alert')).toHaveTextContent('Please wait before trying again.');
    expect(screen.getByRole('button', { name: /sign in/i })).toBeDisabled();
  });
});
