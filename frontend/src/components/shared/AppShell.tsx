import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useRef, useState } from 'react';

import { AppFooter } from '@/components/shared/AppFooter';
import { AppHeader } from '@/components/shared/AppHeader';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import {
  ManagedWindowProvider,
  type ManagedWindowRendererRegistry,
  useManagedWindowActions,
} from '@/components/shared/ManagedWindowManager';
import { ModuleNavigation } from '@/components/shared/ModuleNavigation';
import { signOutUser } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { clearPkceSession } from '@/features/auth/pkce';
import { PreferencesProfileSync } from '@/features/preferences';
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
  const queryClient = useQueryClient();
  const { clearWindows } = useManagedWindowActions();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const markBrowserSessionGuest = useAuthStore((s) => s.markBrowserSessionGuest);
  const signOutPendingRef = useRef(false);
  const [signingOut, setSigningOut] = useState(false);
  const [signOutError, setSignOutError] = useState(false);
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

    clearPkceSession();
    clearWindows();
    markBrowserSessionGuest();
    queryClient.clear();
    void navigate({ to: '/sign-in', replace: true });
  }

  return (
    <div className="flex h-dvh min-h-0 flex-col overflow-hidden bg-background text-foreground">
      <PreferencesProfileSync />
      <AppHeader onSignOut={handleSignOut} signOutError={signOutError} signingOut={signingOut} />
      <div data-slot="authenticated-work-area" className="relative min-h-0 min-w-0 flex-1">
        <div className="flex h-full min-h-0 min-w-0 flex-col md:flex-row">
          <ModuleNavigation context={navigationContext} items={visibleNavigationItems} />
          <main className="flex min-h-0 w-full min-w-0 flex-1 overflow-hidden bg-background">
            {children}
          </main>
        </div>
        <ManagedWindowHost />
      </div>
      <AppFooter />
    </div>
  );
}
