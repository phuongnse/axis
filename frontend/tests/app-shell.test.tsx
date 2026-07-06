import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { signOutUser } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { getCurrentUserProfile } from '@/features/dashboard/api';
import { AppShell } from '../src/components/shared/AppShell';

const routerState = { location: { pathname: '/dashboard' } };
const navigateMock = vi.fn();

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
  useNavigate: () => navigateMock,
}));

vi.mock('@/features/auth/api', () => ({
  signOutUser: vi.fn(() => Promise.resolve()),
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
    navigateMock.mockClear();
    vi.mocked(signOutUser).mockReset();
    vi.mocked(signOutUser).mockResolvedValue();
    sessionStorage.clear();
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

  it('AT-003 signs out after the browser session is ended and clears local session state', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
        },
      },
    });
    queryClient.setQueryData(['dashboard', 'current-user'], { fullName: 'Ada Lovelace' });
    sessionStorage.setItem('pkce_verifier', 'verifier');
    sessionStorage.setItem('pkce_state', 'state');

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell>
          <section aria-label="Work area">Frame content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => expect(signOutUser).toHaveBeenCalledTimes(1));
    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
    expect(queryClient.getQueryData(['dashboard', 'current-user'])).toBeUndefined();
    expect(navigateMock).toHaveBeenCalledWith({ to: '/sign-in', replace: true });
  });

  it('AT-004 disables sign-out while the request is pending', async () => {
    const user = userEvent.setup();
    let resolveSignOut!: () => void;
    vi.mocked(signOutUser).mockReturnValue(
      new Promise<void>((resolve) => {
        resolveSignOut = resolve;
      }),
    );
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

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    const signOutButton = screen.getByRole('button', { name: 'Sign out' });
    await user.click(signOutButton);

    expect(signOutButton).toBeDisabled();
    expect(signOutButton).toHaveTextContent('Signing out');
    await user.click(signOutButton);
    expect(signOutUser).toHaveBeenCalledTimes(1);

    resolveSignOut();
    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith({ to: '/sign-in', replace: true }),
    );
  });

  it('AT-005 keeps the authenticated session active when sign-out fails', async () => {
    const user = userEvent.setup();
    vi.mocked(signOutUser).mockRejectedValue(new Error('network failed'));
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

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Sign out did not complete. Try again.',
    );
    expect(useAuthStore.getState().accessToken).toBe('token');
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeEnabled();
  });
});
