import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useRef, useState } from 'react';

import { AppFooter } from '@/components/shared/AppFooter';
import { AppHeader } from '@/components/shared/AppHeader';
import { ModuleNavigation } from '@/components/shared/ModuleNavigation';
import { signOutUser } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { clearPkceSession } from '@/features/auth/pkce';
import { PreferencesProfileSync } from '@/features/preferences';
import type { ModuleNavigationContribution } from '@/lib/module-navigation';
import { visibleModuleNavigationContributions } from '@/lib/module-navigation';
import { moduleNavigationContributions } from '@/lib/module-navigation-registry';

interface AppShellProps {
  children: ReactNode;
  navigationContributions?: readonly ModuleNavigationContribution[];
}

export function AppShell({
  children,
  navigationContributions = moduleNavigationContributions,
}: AppShellProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const clearSession = useAuthStore((s) => s.clearSession);
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
    clearSession();
    queryClient.clear();
    void navigate({ to: '/sign-in', replace: true });
  }

  return (
    <div className="flex h-dvh min-h-0 flex-col overflow-hidden bg-background text-foreground">
      <PreferencesProfileSync />
      <AppHeader onSignOut={handleSignOut} signOutError={signOutError} signingOut={signingOut} />
      <div className="flex min-h-0 min-w-0 flex-1 flex-col md:flex-row">
        <ModuleNavigation context={navigationContext} items={visibleNavigationItems} />
        <main className="axis-grid flex min-h-0 w-full min-w-0 flex-1 overflow-hidden px-4 py-6 sm:px-6 lg:px-8">
          {children}
        </main>
      </div>
      <AppFooter />
    </div>
  );
}
