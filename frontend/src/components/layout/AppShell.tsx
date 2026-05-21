import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { signOut } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';

interface AppShellProps {
  children: ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const clearSession = useAuthStore((s) => s.clearSession);

  async function handleSignOut() {
    await signOut();
    clearSession();
    window.location.href = '/login';
  }

  return (
    <div className="min-h-screen flex flex-col">
      <header className="border-b bg-card px-6 py-3 flex items-center justify-between">
        <Link to="/dashboard" className="font-semibold text-lg">
          Axis
        </Link>
        <Button type="button" variant="outline" size="sm" onClick={handleSignOut}>
          Sign out
        </Button>
      </header>
      <div className="flex flex-1">
        <aside className="w-56 border-r bg-muted/30 p-4 space-y-1 text-sm">
          <p className="px-2 py-1 text-xs font-medium text-muted-foreground uppercase tracking-wide">
            Workspace
          </p>
          <Link
            to="/dashboard"
            className="block rounded px-2 py-1 hover:bg-muted font-medium"
            activeProps={{ className: 'bg-muted' }}
          >
            Dashboard
          </Link>
          <span className="block rounded px-2 py-1 text-muted-foreground cursor-not-allowed">
            Data Models
          </span>
          <span className="block rounded px-2 py-1 text-muted-foreground cursor-not-allowed">
            Workflows
          </span>
          <span className="block rounded px-2 py-1 text-muted-foreground cursor-not-allowed">
            Forms
          </span>
          <span className="block rounded px-2 py-1 text-muted-foreground cursor-not-allowed">
            Executions
          </span>
          <span className="block rounded px-2 py-1 text-muted-foreground cursor-not-allowed">
            Settings
          </span>
        </aside>
        <main className="flex-1 p-6">{children}</main>
      </div>
    </div>
  );
}
