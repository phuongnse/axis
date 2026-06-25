import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { AppShell } from '../src/components/shared/AppShell';
import { renderWithRouter } from './render-with-router';

describe('AppShell', () => {
  it('renders the dashboard navigation shell', async () => {
    await renderWithRouter(
      <AppShell>
        <p>Child content</p>
      </AppShell>,
      { path: '/dashboard' },
    );

    const sidebarNav = screen.getByRole('navigation', { name: /sidebar navigation/i });
    expect(sidebarNav).toBeInTheDocument();
    expect(within(sidebarNav).getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /sign out/i })).toBeInTheDocument();
    expect(screen.getByText('Child content')).toBeInTheDocument();
  });
});
