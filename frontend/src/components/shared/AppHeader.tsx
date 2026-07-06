import { Link, useRouterState } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { AppActionsMenu } from '@/components/shared/AppActionsMenu';

interface AppHeaderProps {
  onSignOut: () => void;
}

function pageTitleKeyForPath(pathname: string) {
  return pathname.startsWith('/dashboard') ? 'app.dashboard' : 'app.account';
}

export function AppHeader({ onSignOut }: AppHeaderProps) {
  const { t } = useTranslation();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const pageTitle = t(pageTitleKeyForPath(pathname));

  return (
    <header className="shrink-0 border-b border-border bg-card/95 backdrop-blur">
      <div className="flex min-h-16 w-full min-w-0 flex-wrap items-center gap-3 px-4 py-3 sm:px-6 lg:px-8">
        <Link to="/dashboard" className="flex min-w-0 items-center gap-3">
          <img src="/axis-logo.svg" alt="" className="size-11 shrink-0" width={44} height={44} />
          <span className="block min-w-0 truncate text-xs uppercase tracking-[0.18em] text-muted-foreground">
            {pageTitle}
          </span>
        </Link>

        <div className="ml-auto flex shrink-0 items-center">
          <AppActionsMenu onSignOut={onSignOut} />
        </div>
      </div>
    </header>
  );
}
