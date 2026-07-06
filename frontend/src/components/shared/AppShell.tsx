import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useRef, useState } from 'react';

import { AppFooter } from '@/components/shared/AppFooter';
import { AppHeader } from '@/components/shared/AppHeader';
import { signOutUser } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { clearPkceSession } from '@/features/auth/pkce';
import { PreferencesProfileSync } from '@/features/preferences';

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const clearSession = useAuthStore((s) => s.clearSession);
  const signOutPendingRef = useRef(false);
  const [signingOut, setSigningOut] = useState(false);
  const [signOutError, setSignOutError] = useState(false);

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
    <div className="flex min-h-screen flex-col bg-background text-foreground">
      <PreferencesProfileSync />
      <AppHeader onSignOut={handleSignOut} signOutError={signOutError} signingOut={signingOut} />
      <main className="axis-grid w-full min-w-0 flex-1 px-4 py-6 sm:px-6 lg:px-8">{children}</main>
      <AppFooter />
    </div>
  );
}
