import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { AppShell } from '../src/components/layout/AppShell';
import { renderWithRouter } from './render-with-router';

vi.mock('@/features/auth/api', () => ({
  signOut: vi.fn().mockResolvedValue(undefined),
}));

describe('AppShell', () => {
  it('renders sidebar nav and search bar from wireframe', async () => {
    await renderWithRouter(
      <AppShell>
        <p>Child content</p>
      </AppShell>,
      { path: '/dashboard' },
    );

    expect(screen.getByRole('navigation', { name: /main navigation/i })).toBeInTheDocument();
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('Data Models')).toBeInTheDocument();
    expect(screen.getByLabelText(/search/i)).toBeInTheDocument();
    expect(screen.getByText('Child content')).toBeInTheDocument();
  });
});
