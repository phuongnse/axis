import type { ReactNode } from 'react';

import { AppHeader } from '@/components/layout/AppHeader';
import { AppSidebar } from '@/components/layout/AppSidebar';
import { signOut } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const clearSession = useAuthStore((s) => s.clearSession);

  async function handleSignOut() {
    try {
      await signOut();
    } finally {
      clearSession();
      window.location.href = '/login';
    }
  }

  return (
    <div className="flex min-h-screen bg-muted/30">
      <AppSidebar />
      <div className="flex flex-1 flex-col min-w-0">
        <AppHeader onSignOut={handleSignOut} />
        <main className="flex-1 p-6 md:p-8 overflow-auto">{children}</main>
      </div>
    </div>
  );
}
