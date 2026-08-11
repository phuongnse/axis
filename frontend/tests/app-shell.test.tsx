import path from 'node:path';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { toast } from 'sonner';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { restoreBrowserSession, signOutUser } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { dashboardQueryKeys, getCurrentUserProfile } from '@/features/dashboard/api';
import {
  beginWorkspaceTransition,
  confirmWorkspaceTransition,
  recoverWorkspaceTransition,
  workspaceKeys,
} from '@/features/workspaces/api';
import { invalidateClientRequestSession } from '@/lib/api';
import type { ModuleNavigationContribution } from '@/lib/module-navigation';
import { axisStyles } from '@/theme.generated';
import { AppShell } from '../src/components/shared/AppShell';
import { ManagedDialog, ManagedDialogBody } from '../src/components/shared/ManagedDialog';
import {
  type ManagedWindowRendererProps,
  type ManagedWindowRendererRegistry,
  useCurrentManagedWindow,
  useManagedWindowActions,
} from '../src/components/shared/ManagedWindowManager';

const routerState = { location: { pathname: '/dashboard' } };
const navigateMock = vi.fn();
const routerInvalidateMock = vi.fn(() => Promise.resolve());
const moduleNavigationAvailabilityMock = vi.hoisted(() => vi.fn());
const testWindowRenderers: ManagedWindowRendererRegistry = {
  test: TestWindowRenderer,
  'sizing-test': SizingTestWindowRenderer,
};

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
  useRouter: () => ({ invalidate: routerInvalidateMock }),
  getRouteApi: () => ({
    useSearch: () => ({}),
    useNavigate: () => navigateMock,
  }),
}));

vi.mock('@/features/auth/api', () => ({
  signOutUser: vi.fn(() => Promise.resolve()),
  restoreBrowserSession: vi.fn(() => Promise.resolve(true)),
}));

vi.mock('@/lib/api', async (importActual) => {
  const actual = await importActual<typeof import('@/lib/api')>();
  return { ...actual, invalidateClientRequestSession: vi.fn() };
});

vi.mock('@/features/workspaces/WorkspaceControl', () => ({
  useWorkspaceControl: ({
    contextState,
    onRetryContext,
    onWorkspaceChange,
  }: {
    contextState: { failure: string | null; phase: string };
    onRetryContext: () => Promise<void>;
    onWorkspaceChange: (target: {
      workspaceId: string;
      name: string;
      slug: string;
      type: 'Personal';
      organizationId: null;
      isCurrent: false;
    }) => Promise<unknown>;
  }) => ({
    workspace: {
      busy: contextState.phase === 'switching' || contextState.phase === 'refreshing',
      feedback: contextState.failure,
      loadState: 'ready',
      onCreate: vi.fn(),
      onRetryContext,
      onRetryLoad: vi.fn(),
      onSelect: () =>
        onWorkspaceChange({
          workspaceId: '33333333-3333-4333-8333-333333333333',
          name: 'Personal workspace',
          slug: 'personal-workspace',
          type: 'Personal',
          organizationId: null,
          isCurrent: false,
        }),
      options: [
        {
          current: false,
          id: '33333333-3333-4333-8333-333333333333',
          kind: 'person',
          label: 'Simulate Workspace change',
        },
      ],
    },
    overlay: (
      <output data-testid="workspace-context-state">
        {contextState.phase}:{contextState.failure ?? 'none'}
      </output>
    ),
  }),
}));

vi.mock('@/features/workspaces/api', async (importActual) => {
  const actual = await importActual<typeof import('@/features/workspaces/api')>();
  return {
    ...actual,
    beginWorkspaceTransition: vi.fn(),
    confirmWorkspaceTransition: vi.fn(),
    recoverWorkspaceTransition: vi.fn(),
  };
});

vi.mock('@/features/preferences', async (importActual) => {
  const actual = await importActual<typeof import('@/features/preferences')>();

  return {
    ...actual,
    useAccountLanguagePreferenceModel: () => ({
      feedback: null,
      label: 'Language',
      onRetry: vi.fn(),
      onSelect: vi.fn(),
      options: [
        { icon: 'EN', label: 'English', value: 'en' },
        { icon: 'VI', label: 'Vietnamese', value: 'vi' },
      ],
      pendingLabel: 'Saving...',
      value: 'en',
    }),
    useAccountThemePreferenceModel: () => ({
      feedback: null,
      label: 'Theme',
      onRetry: vi.fn(),
      onSelect: vi.fn(),
      options: [
        { icon: 'S', label: 'System', value: 'system' },
        { icon: 'L', label: 'Light', value: 'light' },
        { icon: 'D', label: 'Dark', value: 'dark' },
      ],
      pendingLabel: 'Saving...',
      value: 'system',
    }),
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

vi.mock('@/lib/module-navigation-api', () => ({
  moduleNavigationAvailabilityKeys: {
    all: ['module-navigation-availability'] as const,
  },
  moduleNavigationAvailabilityQueryOptions: () => ({
    queryKey: ['module-navigation-availability'],
    queryFn: moduleNavigationAvailabilityMock,
  }),
}));

describe('AppShell', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'matchMedia',
      vi.fn().mockReturnValue({
        matches: false,
        media: '',
        onchange: null,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
      }),
    );
    routerState.location.pathname = '/dashboard';
    navigateMock.mockClear();
    routerInvalidateMock.mockClear();
    vi.mocked(signOutUser).mockReset();
    vi.mocked(signOutUser).mockResolvedValue();
    vi.mocked(restoreBrowserSession).mockReset();
    vi.mocked(restoreBrowserSession).mockResolvedValue(true);
    vi.mocked(invalidateClientRequestSession).mockReset();
    vi.mocked(beginWorkspaceTransition).mockReset();
    vi.mocked(beginWorkspaceTransition).mockResolvedValue({
      transitionId: '44444444-4444-4444-8444-444444444444',
      status: 'Pending',
      expiresAt: '2026-08-06T12:00:00Z',
      authoritativeWorkspaceId: null,
    });
    vi.mocked(confirmWorkspaceTransition).mockReset();
    vi.mocked(confirmWorkspaceTransition).mockResolvedValue({
      transitionId: '44444444-4444-4444-8444-444444444444',
      status: 'Completed',
      expiresAt: '2026-08-06T12:00:00Z',
      authoritativeWorkspaceId: '33333333-3333-4333-8333-333333333333',
    });
    vi.mocked(recoverWorkspaceTransition).mockReset();
    vi.mocked(recoverWorkspaceTransition).mockResolvedValue({
      transitionId: '44444444-4444-4444-8444-444444444444',
      status: 'Completed',
      expiresAt: '2026-08-06T12:00:00Z',
      authoritativeWorkspaceId: '33333333-3333-4333-8333-333333333333',
    });
    vi.mocked(getCurrentUserProfile).mockResolvedValue({
      id: '11111111-1111-4111-8111-111111111111',
      email: 'ada@example.com',
      fullName: 'Ada Lovelace',
      isActive: true,
      language: 'en',
      theme: 'light',
      workspaceId: '22222222-2222-4222-8222-222222222222',
      workspaces: [
        {
          id: '22222222-2222-4222-8222-222222222222',
          name: 'Axis Reference Product',
          slug: 'axis-reference-product',
          type: 'Organization',
          isCurrent: true,
        },
      ],
    });
    moduleNavigationAvailabilityMock.mockReset();
    moduleNavigationAvailabilityMock.mockResolvedValue({ availableContributionIds: [] });
    useAuthStore.getState().setBrowserSession({
      authenticated: true,
      csrfToken: 'csrf-token',
      user: {
        userId: '11111111-1111-4111-8111-111111111111',
        workspaceId: '22222222-2222-4222-8222-222222222222',
        email: 'ada@example.com',
        name: 'Ada Lovelace',
      },
    });
  });

  afterEach(() => {
    toast.dismiss();
    vi.unstubAllGlobals();
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
        <AppShell surfaceId="authenticated-frame" navigationContributions={[]}>
          <section aria-label="Work area">Frame content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    const frame = document.querySelector('[data-axis-surface-id="authenticated-frame"]');
    expect(frame).toHaveAttribute('data-axis-surface-contract', 'authenticated-frame');
    const appHeader = screen.getByRole('banner');
    expect(appHeader).toHaveTextContent('Dashboard');
    expect(appHeader).toHaveClass('bg-card');
    expect(appHeader).not.toHaveClass('bg-card/95', 'backdrop-blur');
    expect(await screen.findByText('Ada Lovelace')).toBeInTheDocument();
    const accountMenu = screen.getByRole('button', { name: 'Account menu' });
    await waitFor(() => expect(accountMenu).toHaveTextContent('Axis Reference Product'));
    await user.click(accountMenu);
    expect(accountMenu).toHaveAttribute('aria-expanded', 'true');
    const accountSurface = document.querySelector('[data-slot="account-surface"]');
    expect(accountSurface).toHaveAttribute('aria-label', 'Account menu');
    expect(accountSurface).toHaveAttribute('data-axis-surface-contract', 'account-surface');
    expect(accountSurface).toHaveAttribute('data-axis-surface-id', 'account-actions');
    expect(screen.queryByText('Profile')).not.toBeInTheDocument();
    const accountIdentity = screen.getByRole('region', { name: 'Account' });
    expect(accountIdentity).toHaveTextContent('AL');
    expect(accountIdentity).toHaveTextContent('Ada Lovelace');
    expect(accountIdentity).toHaveTextContent('ada@example.com');
    expect(screen.getByText('Preferences')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Simulate Workspace change' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Language' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Theme' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign out' })).toHaveAttribute(
      'data-axis-account-role',
      'section-action',
    );

    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();

    expect(screen.getByRole('main')).toHaveTextContent('Frame content');
    const footer = screen.getByRole('contentinfo');
    expect(footer).toHaveTextContent('Version 0.1.0');
    expect(footer).toHaveTextContent('Axis Platform');
    expect(footer).toHaveTextContent('2026');

    const windowHost = document.querySelector('[data-slot="managed-window-host"]');
    expect(windowHost).not.toBeNull();
    expect(windowHost?.parentElement).toContainElement(screen.getByRole('main'));
    expect(windowHost?.parentElement?.nextElementSibling).toBe(footer);
  });

  it('statically guards the app-wide reduced-motion base rule', async () => {
    const fs = await import('node:fs');
    const indexStyles = fs.readFileSync(path.resolve(__dirname, '../src/index.css'), 'utf-8');

    expect(indexStyles).toContain('@media (prefers-reduced-motion: reduce)');
    expect(indexStyles).toContain('animation-duration: 0.01ms');
    expect(indexStyles).toContain('animation-delay: 0ms');
    expect(indexStyles).toContain('animation-iteration-count: 1');
    expect(indexStyles).toContain('transition-duration: 0.01ms');
    expect(indexStyles).toContain('transition-delay: 0ms');
    expect(indexStyles).toContain('scroll-behavior: auto');
  });

  it('renders the Rules route title in the authenticated app frame', () => {
    routerState.location.pathname = '/rules';
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
        },
      },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell surfaceId="authenticated-frame" navigationContributions={[]}>
          <section aria-label="Work area">Rules content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    expect(screen.getByRole('banner')).toHaveTextContent('Rules');
  });

  it('shows the user name instead of the Workspace name for a Personal context', async () => {
    vi.mocked(getCurrentUserProfile).mockResolvedValue({
      id: '11111111-1111-4111-8111-111111111111',
      email: 'ada@example.com',
      fullName: 'Ada Lovelace',
      isActive: true,
      language: 'en',
      theme: 'light',
      workspaceId: '22222222-2222-4222-8222-222222222222',
      workspaces: [
        {
          id: '22222222-2222-4222-8222-222222222222',
          name: 'Personal workspace',
          slug: 'personal-workspace',
          type: 'Personal',
          isCurrent: true,
        },
      ],
    });
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell surfaceId="authenticated-frame" navigationContributions={[]}>
          <section>Frame content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    const accountMenu = screen.getByRole('button', { name: 'Account menu' });
    await waitFor(() => expect(accountMenu).toHaveTextContent('Ada Lovelace'));
    expect(accountMenu).toHaveTextContent('AL');
    expect(accountMenu).not.toHaveTextContent('Personal workspace');
  });

  it('renders only navigation contributions reported available by the server', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const contributions: ModuleNavigationContribution[] = [
      {
        id: 'businessObjects.definitions',
        labelKey: 'businessObjects.nav.definitions',
        icon: 'businessObjects',
        to: '/business-objects',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 100 },
        order: 100,
        requiresServerAvailability: true,
      },
      {
        id: 'rules.fieldDefinitions',
        labelKey: 'rules.nav.definitions',
        icon: 'rules',
        to: '/rules',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 100 },
        order: 110,
        requiresServerAvailability: true,
      },
    ];
    moduleNavigationAvailabilityMock.mockResolvedValue({
      availableContributionIds: ['businessObjects.definitions'],
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell surfaceId="authenticated-frame" navigationContributions={contributions}>
          <section aria-label="Work area">Frame content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    expect(await screen.findByRole('link', { name: 'Business objects' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Rules' })).not.toBeInTheDocument();
  });

  it('keeps managed windows mounted across route content changes and clears them on sign-out', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const shell = (content: ReactNode) => (
      <QueryClientProvider client={queryClient}>
        <AppShell
          surfaceId="authenticated-frame"
          navigationContributions={[]}
          windowRenderers={testWindowRenderers}
        >
          {content}
        </AppShell>
      </QueryClientProvider>
    );
    const view = render(shell(<TestWindowLauncher />));

    await user.click(screen.getByRole('button', { name: 'Open test window' }));
    expect(await screen.findByRole('dialog', { name: 'Persistent test window' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Windows (1)' })).toBeVisible();

    view.rerender(shell(<section>Another authenticated route</section>));
    expect(screen.getByRole('dialog', { name: 'Persistent test window' })).toBeVisible();
    toast.success('Persistent window saved');
    expect(await screen.findByText('Persistent window saved')).toBeVisible();

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() =>
      expect(
        screen.queryByRole('dialog', { name: 'Persistent test window' }),
      ).not.toBeInTheDocument(),
    );
    expect(screen.queryByRole('button', { name: 'Windows (1)' })).not.toBeInTheDocument();
  });

  it('keeps the account surface and shell geometry stable through the authoritative context cutover', async () => {
    const user = userEvent.setup();
    let resolveSessionRestore!: (authenticated: boolean) => void;
    vi.mocked(restoreBrowserSession).mockReturnValue(
      new Promise<boolean>((resolve) => {
        resolveSessionRestore = (authenticated) => {
          if (authenticated) {
            vi.mocked(getCurrentUserProfile).mockResolvedValue({
              id: '11111111-1111-4111-8111-111111111111',
              email: 'ada@example.com',
              fullName: 'Ada Lovelace',
              isActive: true,
              language: 'en',
              theme: 'light',
              workspaceId: '33333333-3333-4333-8333-333333333333',
              workspaces: [
                {
                  id: '33333333-3333-4333-8333-333333333333',
                  name: 'Personal workspace',
                  slug: 'personal-workspace',
                  type: 'Personal',
                  isCurrent: true,
                },
              ],
            });
            useAuthStore.getState().setBrowserSession({
              authenticated: true,
              csrfToken: 'target-csrf-token',
              user: {
                userId: '11111111-1111-4111-8111-111111111111',
                workspaceId: '33333333-3333-4333-8333-333333333333',
                email: 'ada@example.com',
                name: 'Ada Lovelace',
              },
            });
          }
          resolve(authenticated);
        };
      }),
    );
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(['business-objects', 'workspace-source'], {
      name: 'Source definition',
    });
    queryClient.setQueryData(workspaceKeys.eligible, [
      {
        workspaceId: '22222222-2222-4222-8222-222222222222',
        name: 'Axis Reference Product',
        slug: 'axis-reference-product',
        type: 'Organization',
        organizationId: '44444444-4444-4444-8444-444444444444',
        isCurrent: true,
      },
      {
        workspaceId: '33333333-3333-4333-8333-333333333333',
        name: 'Personal workspace',
        slug: 'personal-workspace',
        type: 'Personal',
        organizationId: null,
        isCurrent: false,
      },
    ]);

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell
          surfaceId="authenticated-frame"
          navigationContributions={[]}
          windowRenderers={testWindowRenderers}
        >
          <TestWindowLauncher />
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Open test window' }));
    expect(await screen.findByRole('dialog', { name: 'Persistent test window' })).toBeVisible();

    const accountMenu = screen.getByRole('button', { name: 'Account menu' });
    await user.click(accountMenu);
    await user.click(screen.getByRole('button', { name: 'Simulate Workspace change' }));

    expect(accountMenu).toHaveAttribute('aria-expanded', 'true');
    await waitFor(() => expect(restoreBrowserSession).toHaveBeenCalledWith({ force: true }));
    const routeContent = document.querySelector('[data-slot="authenticated-route-content"]');
    expect(routeContent).not.toHaveClass('invisible');
    expect(routeContent).toHaveAttribute('inert');
    expect(routeContent).toHaveTextContent('Open test window');
    const refreshStatus = await screen.findByText('Refreshing Workspace');
    const contextSurface = document.querySelector('[data-slot="workspace-context-surface"]');
    expect(routeContent).toHaveClass('invisible');
    expect(contextSurface).toHaveClass('absolute', 'inset-0', 'overflow-hidden');
    expect(contextSurface).not.toHaveClass('overflow-y-auto');
    expect(refreshStatus).toBeVisible();
    expect(refreshStatus.closest('[data-slot="alert"]')).toBeNull();
    expect(invalidateClientRequestSession).toHaveBeenCalledTimes(2);
    expect(vi.mocked(invalidateClientRequestSession).mock.invocationCallOrder[0]).toBeLessThan(
      vi.mocked(beginWorkspaceTransition).mock.invocationCallOrder[0] ?? Number.MAX_SAFE_INTEGER,
    );
    expect(
      screen.queryByRole('dialog', { name: 'Persistent test window' }),
    ).not.toBeInTheDocument();
    expect(queryClient.getQueryData(['business-objects', 'workspace-source'])).toEqual({
      name: 'Source definition',
    });
    expect(queryClient.getQueryData(workspaceKeys.eligible)).toEqual([
      expect.objectContaining({
        workspaceId: '22222222-2222-4222-8222-222222222222',
        isCurrent: false,
      }),
      expect.objectContaining({
        workspaceId: '33333333-3333-4333-8333-333333333333',
        isCurrent: true,
      }),
    ]);

    await act(async () => resolveSessionRestore(true));
    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith({ to: '/dashboard', replace: true }),
    );
    await waitFor(() =>
      expect(queryClient.getQueryData(['business-objects', 'workspace-source'])).toBeUndefined(),
    );
    expect(queryClient.getQueryData(workspaceKeys.eligible)).toEqual([
      expect.objectContaining({
        workspaceId: '22222222-2222-4222-8222-222222222222',
        isCurrent: false,
      }),
      expect.objectContaining({
        workspaceId: '33333333-3333-4333-8333-333333333333',
        isCurrent: true,
      }),
    ]);
    expect(queryClient.getQueryData(dashboardQueryKeys.currentUser())).toEqual(
      expect.objectContaining({
        workspaceId: '33333333-3333-4333-8333-333333333333',
      }),
    );
    await waitFor(() => expect(routerInvalidateMock).toHaveBeenCalledOnce());
    await new Promise((resolve) => window.setTimeout(resolve, 0));
    expect(refreshStatus).toBeVisible();
    await waitFor(() => expect(refreshStatus).not.toBeInTheDocument(), { timeout: 1_000 });
  });

  it('finishes a fast context refresh without flashing a transition surface', async () => {
    const user = userEvent.setup();
    vi.mocked(restoreBrowserSession).mockImplementation(async () => {
      useAuthStore.getState().setBrowserSession({
        authenticated: true,
        csrfToken: 'target-csrf-token',
        user: {
          userId: '11111111-1111-4111-8111-111111111111',
          workspaceId: '33333333-3333-4333-8333-333333333333',
          email: 'ada@example.com',
          name: 'Ada Lovelace',
        },
      });
      return true;
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell surfaceId="authenticated-frame" navigationContributions={[]}>
          <section>Stable route content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Simulate Workspace change' }));

    await waitFor(() => expect(routerInvalidateMock).toHaveBeenCalledOnce());
    expect(screen.getByTestId('workspace-context-state')).toHaveTextContent('idle:none');
    expect(screen.queryByText('Refreshing Workspace')).not.toBeInTheDocument();
    expect(document.querySelector('[data-slot="workspace-context-surface"]')).toBeNull();
    expect(document.querySelector('[data-slot="authenticated-route-content"]')).not.toHaveClass(
      'invisible',
    );
  });

  it('keeps a known cutover failed refresh recoverable without replaying the transition', async () => {
    const user = userEvent.setup();
    vi.mocked(restoreBrowserSession).mockResolvedValue(false);
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell surfaceId="authenticated-frame" navigationContributions={[]}>
          <section>Source route content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Simulate Workspace change' }));

    await waitFor(() =>
      expect(screen.getByTestId('workspace-context-state')).toHaveTextContent(
        'failed:refresh-failed',
      ),
    );
    expect(restoreBrowserSession).toHaveBeenCalledOnce();
    expect(confirmWorkspaceTransition).toHaveBeenCalledOnce();
    expect(recoverWorkspaceTransition).not.toHaveBeenCalled();
    expect(navigateMock).not.toHaveBeenCalled();
    expect(document.querySelector('[data-slot="authenticated-route-content"]')).toHaveTextContent(
      'Source route content',
    );
    expect(document.querySelector('[data-slot="authenticated-route-content"]')).toHaveClass(
      'invisible',
    );
    expect(screen.getByRole('button', { name: 'Account menu' })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
    expect(screen.getByRole('button', { name: 'Retry refresh' })).toBeEnabled();
  });

  it('renders managed windows with restrained elevation', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell
          surfaceId="authenticated-frame"
          navigationContributions={[]}
          windowRenderers={testWindowRenderers}
        >
          <TestWindowLauncher />
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Open test window' }));
    const dialog = await screen.findByRole('dialog', { name: 'Persistent test window' });
    expect(dialog.querySelector('[data-slot="managed-dialog-window"]')).toHaveClass(
      axisStyles.elevation.managed,
    );
    expect(dialog.querySelector('[data-slot="managed-dialog-header"]')).toHaveClass('items-center');
  });

  it('keeps the Windows trigger fully opaque in dark mode', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell
          surfaceId="authenticated-frame"
          navigationContributions={[]}
          windowRenderers={testWindowRenderers}
        >
          <TestWindowLauncher />
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Open test window' }));
    const windowsTrigger = await screen.findByRole('button', { name: 'Windows (1)' });

    expect(windowsTrigger).toHaveClass('bg-popover', 'dark:bg-popover', 'dark:hover:bg-muted');
    expect(windowsTrigger).not.toHaveClass('dark:bg-input/30', 'dark:hover:bg-input/50');
  });

  it('offers an explicit footer Close action when a renderer is unavailable', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell surfaceId="authenticated-frame" navigationContributions={[]} windowRenderers={{}}>
          <TestWindowLauncher />
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Open test window' }));
    const dialog = await screen.findByRole('dialog', { name: 'Persistent test window' });
    const footer = dialog.querySelector('[data-slot="managed-dialog-footer"]');
    expect(footer).not.toBeNull();
    expect(within(footer as HTMLElement).getByRole('button', { name: 'Close' })).toBeEnabled();

    await user.click(within(footer as HTMLElement).getByRole('button', { name: 'Close' }));
    await waitFor(() =>
      expect(
        screen.queryByRole('dialog', { name: 'Persistent test window' }),
      ).not.toBeInTheDocument(),
    );
  });

  it('AT-001 keeps runtime overflow windowed and uses fullscreen only for an explicit workflow policy', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const clientHeight = vi
      .spyOn(HTMLElement.prototype, 'clientHeight', 'get')
      .mockImplementation(function () {
        return this.getAttribute('data-slot') === 'dialog-body' ? 300 : 0;
      });
    const scrollHeight = vi
      .spyOn(HTMLElement.prototype, 'scrollHeight', 'get')
      .mockImplementation(function () {
        return this.getAttribute('data-content-overflow') === 'true' ? 480 : 240;
      });
    const clientWidth = vi
      .spyOn(HTMLElement.prototype, 'clientWidth', 'get')
      .mockImplementation(function () {
        return this.getAttribute('data-slot') === 'dialog-body' ? 600 : 0;
      });
    const scrollWidth = vi
      .spyOn(HTMLElement.prototype, 'scrollWidth', 'get')
      .mockImplementation(() => 560);

    try {
      render(
        <QueryClientProvider client={queryClient}>
          <AppShell
            surfaceId="authenticated-frame"
            navigationContributions={[]}
            windowRenderers={testWindowRenderers}
          >
            <SizingTestWindowLauncher />
          </AppShell>
        </QueryClientProvider>,
      );

      await user.click(screen.getByRole('button', { name: 'Open overflowing window' }));
      const overflowingDialog = await screen.findByRole('dialog', { name: 'Overflowing window' });
      expect(overflowingDialog.querySelector('[data-slot="managed-dialog-header"]')).toHaveClass(
        'items-center',
      );
      const overflowingWindow = overflowingDialog.querySelector(
        '[data-slot="managed-dialog-window"]',
      );
      await waitFor(() =>
        expect(overflowingWindow).toHaveAttribute('data-dialog-preset', 'windowed'),
      );
      const workAreaWidth = window.visualViewport?.width ?? window.innerWidth;
      const workAreaHeight = window.visualViewport?.height ?? window.innerHeight;
      expect(overflowingWindow).toHaveStyle({
        width: `${workAreaWidth * 0.5}px`,
        height: `${workAreaHeight * 0.75}px`,
      });
      await user.click(screen.getByRole('button', { name: 'Reset dialog' }));
      expect(overflowingWindow).toHaveAttribute('data-dialog-preset', 'windowed');
      await user.click(screen.getByRole('button', { name: 'Close dialog' }));

      await user.click(screen.getByRole('button', { name: 'Open fullscreen workflow' }));
      const fullscreenDialog = await screen.findByRole('dialog', { name: 'Fullscreen workflow' });
      const fullscreenWindow = fullscreenDialog.querySelector(
        '[data-slot="managed-dialog-window"]',
      );
      await waitFor(() =>
        expect(fullscreenWindow).toHaveAttribute('data-dialog-preset', 'fullscreen'),
      );
      await user.click(screen.getByRole('button', { name: 'Restore dialog size' }));
      expect(fullscreenWindow).toHaveAttribute('data-dialog-preset', 'windowed');
      await user.click(screen.getByRole('button', { name: 'Reset dialog' }));
      expect(fullscreenWindow).toHaveAttribute('data-dialog-preset', 'fullscreen');
    } finally {
      clientHeight.mockRestore();
      scrollHeight.mockRestore();
      clientWidth.mockRestore();
      scrollWidth.mockRestore();
    }
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

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell surfaceId="authenticated-frame" navigationContributions={[]}>
          <section aria-label="Work area">Frame content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => expect(signOutUser).toHaveBeenCalledTimes(1));
    expect(invalidateClientRequestSession).toHaveBeenCalledOnce();
    expect(vi.mocked(signOutUser).mock.invocationCallOrder[0]).toBeLessThan(
      vi.mocked(invalidateClientRequestSession).mock.invocationCallOrder[0] ?? 0,
    );
    expect(useAuthStore.getState().browserSessionStatus).toBe('guest');
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
        <AppShell surfaceId="authenticated-frame" navigationContributions={[]}>
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
        <AppShell surfaceId="authenticated-frame" navigationContributions={[]}>
          <section aria-label="Work area">Frame content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Sign out did not complete. Try again.',
    );
    expect(useAuthStore.getState().browserSessionStatus).toBe('authenticated');
    expect(navigateMock).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeEnabled();
  });
});

function TestWindowLauncher() {
  const { openWindow } = useManagedWindowActions();
  return (
    <button
      type="button"
      onClick={() =>
        openWindow({
          id: 'test:persistent',
          kind: 'test',
          resourceKey: 'persistent',
          title: 'Persistent test window',
        })
      }
    >
      Open test window
    </button>
  );
}

function TestWindowRenderer() {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  return (
    <ManagedDialog
      surfaceId="managed-window-host"
      open
      title="Persistent test window"
      onOpenChange={(open) => {
        if (!open) closeWindow(windowId);
      }}
      footer={
        <button type="button" onClick={() => closeWindow(windowId)}>
          Close
        </button>
      }
    >
      <ManagedDialogBody>Persistent state</ManagedDialogBody>
    </ManagedDialog>
  );
}

type SizingTestPayload = {
  overflow: boolean;
  initialSize?: 'fullscreen';
};

function SizingTestWindowLauncher() {
  const { openWindow } = useManagedWindowActions();
  const openSizingTestWindow = (id: string, title: string, payload: SizingTestPayload) =>
    openWindow({
      id,
      kind: 'sizing-test',
      resourceKey: id,
      title,
      payload,
      initialSize: payload.initialSize,
    });

  return (
    <>
      <button
        type="button"
        onClick={() =>
          openSizingTestWindow('test:overflowing', 'Overflowing window', { overflow: true })
        }
      >
        Open overflowing window
      </button>
      <button
        type="button"
        onClick={() =>
          openSizingTestWindow('test:fullscreen', 'Fullscreen workflow', {
            overflow: false,
            initialSize: 'fullscreen',
          })
        }
      >
        Open fullscreen workflow
      </button>
    </>
  );
}

function SizingTestWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  const payload = descriptor.payload as SizingTestPayload;
  return (
    <ManagedDialog
      surfaceId="managed-window-host"
      open
      title={descriptor.title}
      description="Sizing test window description"
      onOpenChange={(open) => {
        if (!open) closeWindow(windowId);
      }}
      footer={
        <button type="button" onClick={() => closeWindow(windowId)}>
          Close
        </button>
      }
    >
      <ManagedDialogBody data-content-overflow={payload.overflow}>
        Sizing test content
      </ManagedDialogBody>
    </ManagedDialog>
  );
}
