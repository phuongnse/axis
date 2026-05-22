import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { LoginPage } from '../src/features/auth/components/LoginPage';
import { renderWithRouter } from './render-with-router';

vi.mock('@/features/auth/pkce', () => ({
  createPkceSession: () => ({ state: 's', verifier: 'v' }),
  buildAuthorizeUrl: async () => '/connect/authorize?test=1',
}));

describe('LoginPage', () => {
  it('shows inline validation when fields are empty', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<LoginPage />, { path: '/login' });

    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByText('Email address is required')).toBeInTheDocument();
    expect(screen.getByText('Password is required')).toBeInTheDocument();
  });

  it('renders wireframe labels and forgot password link', async () => {
    await renderWithRouter(<LoginPage />, { path: '/login' });

    expect(screen.getByLabelText('Email address')).toBeInTheDocument();
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /forgot password/i })).toHaveAttribute(
      'href',
      '/forgot-password',
    );
    expect(screen.getByText('⬡ Axis')).toBeInTheDocument();
  });
});
