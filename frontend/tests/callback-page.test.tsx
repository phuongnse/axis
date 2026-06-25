import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { exchangeAuthorizationCode } from '@/features/auth/api';
import { CallbackPage } from '@/features/auth/components/CallbackPage';
import { renderWithRouter } from './render-with-router';

const navigateMock = vi.fn();

function setWindowPath(path: string): void {
  window.history.pushState({}, '', path);
}

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
    exchangeAuthorizationCode: vi.fn(() => Promise.resolve('access-token')),
  };
});

describe('CallbackPage', () => {
  beforeEach(() => {
    navigateMock.mockReset();
    vi.mocked(exchangeAuthorizationCode).mockClear();
    sessionStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    sessionStorage.clear();
    setWindowPath('/');
  });

  it('shows invalid state when PKCE state is missing', async () => {
    setWindowPath('/callback?code=auth-code&state=missing');
    await renderWithRouter(<CallbackPage />, { path: '/callback?code=auth-code&state=missing' });

    expect(
      await screen.findByText('Invalid authorization response. Please try registering again.'),
    ).toBeInTheDocument();
    expect(exchangeAuthorizationCode).not.toHaveBeenCalled();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('exchanges the authorization code and opens the dashboard', async () => {
    sessionStorage.setItem('pkce_verifier', 'verifier');
    sessionStorage.setItem('pkce_state', 'state');
    setWindowPath('/callback?code=auth-code&state=state');

    await renderWithRouter(<CallbackPage />, { path: '/callback?code=auth-code&state=state' });

    expect(screen.getByText('Completing sign-in...')).toBeInTheDocument();
    await waitFor(() => {
      expect(exchangeAuthorizationCode).toHaveBeenCalledWith('auth-code');
    });
    expect(navigateMock).toHaveBeenCalledWith({ to: '/dashboard', replace: true });
  });
});
