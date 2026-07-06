import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { CallbackPage } from '@/features/auth/components/CallbackPage';
import { renderWithRouter } from './render-with-router';

function setWindowPath(path: string): void {
  window.history.pushState({}, '', path);
}

describe('CallbackPage', () => {
  beforeEach(() => {
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
      await screen.findByText('Invalid authorization response. Please try signing in again.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /sign-in interrupted/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /sign in/i })).toHaveAttribute('href', '/sign-in');
  });

  it('shows token exchange failure as a recoverable state', async () => {
    setWindowPath('/callback?error=tokenFailed');

    await renderWithRouter(<CallbackPage />, { path: '/callback?error=tokenFailed' });

    await waitFor(() => {
      expect(screen.getByText('Token exchange failed. Please try signing in again.')).toBeVisible();
    });
    expect(screen.getByRole('heading', { name: /sign-in interrupted/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /sign in/i })).toHaveAttribute('href', '/sign-in');
  });
});
