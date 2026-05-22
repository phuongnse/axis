import { useRouterState } from '@tanstack/react-router';
import { Bell, Search } from 'lucide-react';
import { pageTitleForPath } from '@/components/layout/shell-nav';

interface AppHeaderProps {
  onSignOut: () => void;
}

export function AppHeader({ onSignOut }: AppHeaderProps) {
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const pageTitle = pageTitleForPath(pathname);

  return (
    <header className="flex h-14 shrink-0 items-center gap-4 border-b border-border bg-card px-6">
      <p className="text-[13px] text-muted-foreground shrink-0 min-w-[140px]">
        <span className="text-primary font-medium">Axis</span>
        <span className="mx-2 text-border">/</span>
        {pageTitle}
      </p>

      <div className="flex flex-1 justify-center max-w-md mx-auto">
        <search
          aria-label="Search (coming soon)"
          className="flex w-full items-center gap-2 rounded-lg border border-input bg-muted/50 px-3 h-8 text-muted-foreground"
        >
          <Search className="size-4 shrink-0" aria-hidden />
          <span className="text-xs flex-1 text-left">Search anything…</span>
          <kbd className="hidden sm:inline rounded border border-border bg-card px-1.5 py-0.5 text-[10px] font-medium">
            ⌘K
          </kbd>
        </search>
      </div>

      <div className="flex items-center gap-2 shrink-0">
        <button
          type="button"
          className="inline-flex size-9 items-center justify-center rounded-lg text-muted-foreground hover:bg-muted hover:text-foreground"
          aria-label="Notifications (coming soon)"
          disabled
        >
          <Bell className="size-4" />
        </button>
        <button
          type="button"
          onClick={onSignOut}
          className="text-xs font-medium text-primary hover:underline px-2"
        >
          Sign out
        </button>
      </div>
    </header>
  );
}
