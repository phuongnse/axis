import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type AnchorHTMLAttributes, type ReactNode, useState } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { signOutUser } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { getCurrentUserProfile } from '@/features/dashboard/api';
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
const testWindowRenderers: ManagedWindowRendererRegistry = {
  test: TestWindowRenderer,
  'adaptive-test': AdaptiveTestWindowRenderer,
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
  getRouteApi: () => ({
    useSearch: () => ({}),
    useNavigate: () => navigateMock,
  }),
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
    routerState.location.pathname = '/dashboard';
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
        <AppShell navigationContributions={[]}>
          <section aria-label="Work area">Frame content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    expect(screen.getByRole('banner')).toHaveTextContent('Dashboard');
    expect(await screen.findByText('Ada Lovelace')).toBeInTheDocument();
    const accountMenu = screen.getByRole('button', { name: 'Account menu' });
    expect(accountMenu).toHaveClass('h-9', 'hover:bg-muted', 'dark:hover:bg-muted/50');
    expect(accountMenu).not.toHaveClass('border-border', 'bg-background');
    expect(accountMenu.querySelector('[data-slot="avatar"]')).toHaveAttribute(
      'data-size',
      'default',
    );
    await user.click(accountMenu);
    expect(accountMenu).toHaveAttribute('aria-expanded', 'true');
    expect(screen.queryByText('Profile')).not.toBeInTheDocument();
    expect(screen.getAllByText('AL')).toHaveLength(1);
    expect(screen.getAllByText('Ada Lovelace')).toHaveLength(1);
    expect(screen.getByText('Preferences')).toBeInTheDocument();
    expect(screen.getByText('Language control')).toBeInTheDocument();
    expect(screen.getByText('Theme control')).toBeInTheDocument();
    const signOut = screen.getByRole('button', { name: 'Sign out' });
    expect(signOut).toHaveClass('text-destructive', 'h-7');

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
        <AppShell navigationContributions={[]}>
          <section aria-label="Work area">Rules content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    expect(screen.getByRole('banner')).toHaveTextContent('Rules');
  });

  it('keeps managed windows mounted across route content changes and clears them on sign-out', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const shell = (content: ReactNode) => (
      <QueryClientProvider client={queryClient}>
        <AppShell navigationContributions={[]} windowRenderers={testWindowRenderers}>
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

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() =>
      expect(
        screen.queryByRole('dialog', { name: 'Persistent test window' }),
      ).not.toBeInTheDocument(),
    );
    expect(screen.queryByRole('button', { name: 'Windows (1)' })).not.toBeInTheDocument();
  });

  it('renders managed windows with restrained elevation', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell navigationContributions={[]} windowRenderers={testWindowRenderers}>
          <TestWindowLauncher />
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Open test window' }));
    const dialog = await screen.findByRole('dialog', { name: 'Persistent test window' });
    expect(dialog.querySelector('[data-slot="managed-dialog-window"]')).toHaveClass('shadow-lg');
  });

  it('keeps the Windows trigger fully opaque in dark mode', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell navigationContributions={[]} windowRenderers={testWindowRenderers}>
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
        <AppShell navigationContributions={[]} windowRenderers={{}}>
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

  it('AT-001 resolves adaptive initial sizing once content is ready and re-evaluates on reset', async () => {
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
          <AppShell navigationContributions={[]} windowRenderers={testWindowRenderers}>
            <AdaptiveTestWindowLauncher />
          </AppShell>
        </QueryClientProvider>,
      );

      await user.click(screen.getByRole('button', { name: 'Open fitting window' }));
      const fittingDialog = await screen.findByRole('dialog', { name: 'Fitting window' });
      const fittingWindow = fittingDialog.querySelector('[data-slot="managed-dialog-window"]');
      await waitFor(() => expect(fittingWindow).toHaveAttribute('data-dialog-preset', 'large'));
      const workAreaWidth = window.visualViewport?.width ?? window.innerWidth;
      const workAreaHeight = window.visualViewport?.height ?? window.innerHeight;
      expect(fittingWindow).toHaveStyle({
        width: `${workAreaWidth * 0.5}px`,
        height: `${workAreaHeight * 0.75}px`,
      });
      await user.click(screen.getByRole('button', { name: 'Close dialog' }));

      await user.click(screen.getByRole('button', { name: 'Open pending overflow window' }));
      const overflowDialog = await screen.findByRole('dialog', { name: 'Overflow window' });
      expect(overflowDialog.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
        'data-dialog-preset',
        'large',
      );

      await user.click(screen.getByRole('button', { name: 'Finish loading' }));
      await waitFor(() =>
        expect(overflowDialog.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
          'data-dialog-preset',
          'fullscreen',
        ),
      );

      await user.click(screen.getByRole('button', { name: 'Restore dialog size' }));
      expect(overflowDialog.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
        'data-dialog-preset',
        'large',
      );
      await waitFor(() =>
        expect(overflowDialog.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
          'data-dialog-preset',
          'large',
        ),
      );

      await user.click(screen.getByRole('button', { name: 'Reset dialog' }));
      await waitFor(() =>
        expect(overflowDialog.querySelector('[data-slot="managed-dialog-window"]')).toHaveAttribute(
          'data-dialog-preset',
          'fullscreen',
        ),
      );
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
    sessionStorage.setItem('pkce_verifier', 'verifier');
    sessionStorage.setItem('pkce_state', 'state');

    render(
      <QueryClientProvider client={queryClient}>
        <AppShell navigationContributions={[]}>
          <section aria-label="Work area">Frame content</section>
        </AppShell>
      </QueryClientProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => expect(signOutUser).toHaveBeenCalledTimes(1));
    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(useAuthStore.getState().browserSessionStatus).toBe('guest');
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
        <AppShell navigationContributions={[]}>
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
        <AppShell navigationContributions={[]}>
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

type AdaptiveTestPayload = {
  overflow: boolean;
  ready: boolean;
};

function AdaptiveTestWindowLauncher() {
  const { openWindow } = useManagedWindowActions();
  const openAdaptiveWindow = (id: string, title: string, payload: AdaptiveTestPayload) =>
    openWindow({
      id,
      kind: 'adaptive-test',
      resourceKey: id,
      title,
      payload,
      initialSize: 'auto',
    });

  return (
    <>
      <button
        type="button"
        onClick={() =>
          openAdaptiveWindow('test:fitting', 'Fitting window', { overflow: false, ready: true })
        }
      >
        Open fitting window
      </button>
      <button
        type="button"
        onClick={() =>
          openAdaptiveWindow('test:overflow', 'Overflow window', {
            overflow: true,
            ready: false,
          })
        }
      >
        Open pending overflow window
      </button>
    </>
  );
}

function AdaptiveTestWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  const payload = descriptor.payload as AdaptiveTestPayload;
  const [ready, setReady] = useState(payload.ready);
  return (
    <ManagedDialog
      open
      title={descriptor.title}
      autoSizeKey={descriptor.resourceKey}
      autoSizeReady={ready}
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
        Adaptive content
        {!ready ? (
          <button type="button" onClick={() => setReady(true)}>
            Finish loading
          </button>
        ) : null}
      </ManagedDialogBody>
    </ManagedDialog>
  );
}
