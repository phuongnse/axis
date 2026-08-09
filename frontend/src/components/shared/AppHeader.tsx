import { Link, useRouterState } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { AppActionsMenu } from '@/components/shared/AppActionsMenu';
import type { CreatedOrganizationWorkspace, EligibleWorkspace } from '@/features/workspaces/api';
import type {
  WorkspaceChangeResult,
  WorkspaceContextState,
} from '@/features/workspaces/WorkspaceControl';

interface AppHeaderProps {
  onSignOut: () => void;
  onRetryWorkspaceContext: () => Promise<void>;
  onWorkspaceChange: (
    target: EligibleWorkspace | CreatedOrganizationWorkspace,
  ) => Promise<WorkspaceChangeResult>;
  signOutError?: boolean;
  signingOut?: boolean;
  workspaceContext: WorkspaceContextState;
}

function pageTitleKeyForPath(pathname: string) {
  if (pathname.startsWith('/business-objects')) return 'app.businessObjects';
  if (pathname.startsWith('/rules')) return 'app.rules';
  return pathname.startsWith('/dashboard') ? 'app.dashboard' : 'app.account';
}

export function AppHeader({
  onSignOut,
  onRetryWorkspaceContext,
  onWorkspaceChange,
  signOutError = false,
  signingOut = false,
  workspaceContext,
}: AppHeaderProps) {
  const { t } = useTranslation();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const pageTitle = t(pageTitleKeyForPath(pathname));

  return (
    <header className="shrink-0 border-b border-border bg-card">
      <div className="flex min-h-16 w-full min-w-0 flex-wrap items-center gap-3 px-4 py-3 sm:px-6 lg:px-8">
        <Link to="/dashboard" className="flex min-w-0 items-center gap-3">
          <img src="/axis-logo.svg" alt="" className="size-11 shrink-0" width={44} height={44} />
          <span className="block min-w-0 truncate text-xs uppercase tracking-widest text-muted-foreground">
            {pageTitle}
          </span>
        </Link>

        <div className="ml-auto flex min-w-0 shrink items-center gap-2">
          <AppActionsMenu
            onSignOut={onSignOut}
            onRetryWorkspaceContext={onRetryWorkspaceContext}
            onWorkspaceChange={onWorkspaceChange}
            signOutError={signOutError}
            signingOut={signingOut}
            workspaceContext={workspaceContext}
          />
        </div>
      </div>
    </header>
  );
}
