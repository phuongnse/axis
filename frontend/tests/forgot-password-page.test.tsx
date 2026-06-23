import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ForgotPasswordPage } from '@/features/auth/components/ForgotPasswordPage';
import { renderWithRouter } from './render-with-router';

describe('ForgotPasswordPage', () => {
  it('renders disabled reset form and sign-in return link', async () => {
    await renderWithRouter(<ForgotPasswordPage />, { path: '/forgot-password' });

    expect(screen.getByRole('heading', { name: /reset your password/i })).toBeInTheDocument();
    expect(
      screen.getByText('Enter your email and we will send you a reset link.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Email address')).toBeDisabled();
    expect(screen.getByText('Use the email linked to your account.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /send reset link/i })).toBeDisabled();
    expect(screen.getByRole('link', { name: /sign in/i })).toHaveAttribute('href', '/login');
  });
});
