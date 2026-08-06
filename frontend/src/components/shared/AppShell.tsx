import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useRouter, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { AppFooter } from '@/components/shared/AppFooter';
import { AppHeader } from '@/components/shared/AppHeader';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import {
  ManagedWindowProvider,
  type ManagedWindowRendererRegistry,
  useManagedWindowActions,
} from '@/components/shared/ManagedWindowManager';
import { ModuleNavigation } from '@/components/shared/ModuleNavigation';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Button } from '@/components/ui/button';
import { Toaster } from '@/components/ui/sonner';
import { restoreBrowserSession, signOutUser } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { PreferencesProfileSync } from '@/features/preferences';
import { invalidateClientRequestSession } from '@/lib/api';
import { managedWindowRenderers } from '@/lib/managed-window-registry';
import type { ModuleNavigationContribution } from '@/lib/module-navigation';
import { visibleModuleNavigationContributions } from '@/lib/module-navigation';
import { moduleNavigationContributions } from '@/lib/module-navigation-registry';

interface AppShellProps {
  children: ReactNode;
  navigationContributions?: readonly ModuleNavigationContribution[];
  windowRenderers?: ManagedWindowRendererRegistry;
}

export function AppShell({
  children,
  navigationContributions = moduleNavigationContributions,
  windowRenderers = managedWindowRenderers,
}: AppShellProps) {
  return (
    <ManagedWindowProvider renderers={windowRenderers}>
      <AppShellContent navigationContributions={navigationContributions}>
        {children}
      </AppShellContent>
    </ManagedWindowProvider>
  );
}

function AppShellContent({
  children,
  navigationContributions,
}: {
  children: ReactNode;
  navigationContributions: readonly ModuleNavigationContribution[];
}) {
  const navigate = useNavigate();
  const router = useRouter();
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { clearWindows } = useManagedWindowActions();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const markBrowserSessionGuest = useAuthStore((s) => s.markBrowserSessionGuest);
  const signOutPendingRef = useRef(false);
  const [signingOut, setSigningOut] = useState(false);
  const [signOutError, setSignOutError] = useState(false);
  const [workspaceRefresh, setWorkspaceRefresh] = useState<'idle' | 'pending' | 'failed'>('idle');
  const navigationContext = { pathname };
  const visibleNavigationItems = visibleModuleNavigationContributions(
    navigationContributions,
    navigationContext,
  );

  async function handleSignOut() {
    if (signOutPendingRef.current) return;

    signOutPendingRef.current = true;
    setSigningOut(true);
    setSignOutError(false);

    try {
      await signOutUser();
    } catch {
      signOutPendingRef.current = false;
      setSigningOut(false);
      setSignOutError(true);
      return;
    }

    invalidateClientRequestSession();
    clearWindows();
    markBrowserSessionGuest();
    queryClient.clear();
    void navigate({ to: '/sign-in', replace: true });
  }

  async function refreshWorkspaceSession() {
    setWorkspaceRefresh('pending');
    try {
      if (!(await restoreBrowserSession({ force: true }))) {
        throw new Error('The target browser session is unavailable.');
      }
      await navigate({ to: '/dashboard', replace: true });
      await router.invalidate();
      setWorkspaceRefresh('idle');
    } catch {
      setWorkspaceRefresh('failed');
    }
  }

  async function handleWorkspaceChanged() {
    invalidateClientRequestSession();
    setWorkspaceRefresh('pending');
    clearWindows();
    queryClient.clear();
    await refreshWorkspaceSession();
  }

  return (
    <div className="flex h-dvh min-h-0 flex-col overflow-hidden bg-background text-foreground">
      <PreferencesProfileSync />
      <AppHeader
        onSignOut={handleSignOut}
        onWorkspaceChanged={handleWorkspaceChanged}
        signOutError={signOutError}
        signingOut={signingOut}
      />
      <div data-slot="authenticated-work-area" className="relative min-h-0 min-w-0 flex-1">
        {workspaceRefresh === 'idle' ? (
          <>
            <div className="flex h-full min-h-0 min-w-0 flex-col md:flex-row">
              <ModuleNavigation context={navigationContext} items={visibleNavigationItems} />
              <main className="flex min-h-0 w-full min-w-0 flex-1 overflow-hidden bg-background">
                {children}
              </main>
            </div>
            <ManagedWindowHost />
          </>
        ) : (
          <main className="flex h-full min-h-0 items-center justify-center overflow-auto p-6">
            <section className="grid w-full max-w-lg gap-4" aria-live="polite">
              {workspaceRefresh === 'pending' ? (
                <StatusNotice tone="info" title={t('workspace.refreshingTitle')}>
                  {t('workspace.refreshingDescription')}
                </StatusNotice>
              ) : (
                <>
                  <StatusNotice tone="destructive" title={t('workspace.refreshFailedTitle')}>
                    {t('workspace.refreshFailedDescription')}
                  </StatusNotice>
                  <div className="flex flex-wrap gap-2">
                    <Button type="button" onClick={() => void refreshWorkspaceSession()}>
                      {t('workspace.retryRefresh')}
                    </Button>
                    <Button type="button" variant="outline" onClick={handleSignOut}>
                      {t('nav.signOut')}
                    </Button>
                  </div>
                </>
              )}
            </section>
          </main>
        )}
        <Toaster />
      </div>
      <AppFooter />
    </div>
  );
}
