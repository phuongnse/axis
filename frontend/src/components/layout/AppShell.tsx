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
    <div className="flex min-h-screen flex-col bg-background lg:flex-row">
      <AppSidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <AppHeader onSignOut={handleSignOut} />
        <main className="axis-grid flex-1 overflow-auto p-4 sm:p-6 md:p-8">{children}</main>
      </div>
    </div>
  );
}
