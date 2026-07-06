import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/features/auth/auth-store';
import { getCurrentUserProfile } from '@/features/dashboard/api';
import { AppShell } from '../src/components/shared/AppShell';

const routerState = { location: { pathname: '/dashboard' } };

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    to,
    children,
    ...props
  }: AnchorHTMLAttributes<HTMLAnchorElement> & { to: string; children: ReactNode }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
  useRouterState: ({ select }: { select?: (state: typeof routerState) => unknown } = {}) =>
    select ? select(routerState) : routerState,
}));

vi.mock('@/features/preferences', async (importActual) => {
  const actual = await importActual<typeof import('@/features/preferences')>();

  return {
    ...actual,
    LanguageControl: () => <div>Language control</div>,
    ThemeControl: () => <div>Theme control</div>,
    PreferencesProfileSync: () => null,
  };
});

vi.mock('@/features/dashboard/api', () => ({
  dashboardQueryKeys: {
    all: ['dashboard'] as const,
    currentUser: () => ['dashboard', 'current-user'] as const,
  },
  getCurrentUserProfile: vi.fn(),
}));

describe('AppShell', () => {
  beforeEach(() => {
    vi.mocked(getCurrentUserProfile).mockResolvedValue({
      id: '11111111-1111-4111-8111-111111111111',
      email: 'ada@example.com',
      fullName: 'Ada Lovelace',
      isActive: true,
      language: 'en',
      theme: 'light',
      workspaceId: '22222222-2222-4222-8222-222222222222',
      workspaces: [],
    });
    useAuthStore.setState({
      accessToken: 'token',
      userLabel: 'User',
      userInitials: '?',
    });
  });

  it('renders the authenticated app frame around page content', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
        },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell>
          <section aria-label="Work area">Frame content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    expect(screen.getByRole('banner')).toHaveTextContent('Dashboard');
    expect(await screen.findByText('Ada Lovelace')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    expect(screen.queryByText('Profile')).not.toBeInTheDocument();
    expect(screen.getAllByText('AL')).toHaveLength(1);
    expect(screen.getAllByText('Ada Lovelace')).toHaveLength(1);
    expect(screen.getByText('Preferences')).toBeInTheDocument();
    expect(screen.getByText('Language control')).toBeInTheDocument();
    expect(screen.getByText('Theme control')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();

    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();

    expect(screen.getByRole('main')).toHaveTextContent('Frame content');
    expect(screen.getByRole('contentinfo')).toHaveTextContent('Version 0.1.0');
    expect(screen.getByRole('contentinfo')).toHaveTextContent('Axis Platform');
    expect(screen.getByRole('contentinfo')).toHaveTextContent('2026');
  });
});
