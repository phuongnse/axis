import type { ReactNode } from 'react';

import { AppFooter } from '@/components/shared/AppFooter';
import { AppHeader } from '@/components/shared/AppHeader';
import { useAuthStore } from '@/features/auth/auth-store';
import { PreferencesProfileSync } from '@/features/preferences';

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const clearSession = useAuthStore((s) => s.clearSession);

  function handleSignOut() {
    clearSession();
    window.location.href = '/sign-in';
  }

  return (
    <div className="flex min-h-screen flex-col bg-background text-foreground">
      <PreferencesProfileSync />
      <AppHeader onSignOut={handleSignOut} />
      <main className="axis-grid w-full min-w-0 flex-1 px-4 py-6 sm:px-6 lg:px-8">{children}</main>
      <AppFooter />
    </div>
  );
}
