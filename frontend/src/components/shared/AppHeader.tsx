import { useQuery } from '@tanstack/react-query';
import { Link, useRouterState } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { AccountSurface } from '@/components/shared/AccountSurface';
import { useAuthStore } from '@/features/auth/auth-store';
import { sessionDisplayFromLabel } from '@/features/auth/session-display';
import { dashboardQueryKeys, getCurrentUserProfile } from '@/features/dashboard/api';
import {
  useAccountLanguagePreferenceModel,
  useAccountThemePreferenceModel,
} from '@/features/preferences';
import type { CreatedOrganizationWorkspace, EligibleWorkspace } from '@/features/workspaces/api';
import {
  useWorkspaceControl,
  type WorkspaceChangeResult,
  type WorkspaceContextState,
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

function firstNonEmpty(...values: Array<string | null | undefined>): string | null {
  for (const value of values) {
    const trimmed = value?.trim();
    if (trimmed) return trimmed;
  }
  return null;
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
  const authenticated = useAuthStore((state) => state.browserSessionStatus === 'authenticated');
  const userLabel = useAuthStore((state) => state.userLabel);
  const userInitials = useAuthStore((state) => state.userInitials);
  const profileQuery = useQuery({
    queryKey: dashboardQueryKeys.currentUser(),
    queryFn: getCurrentUserProfile,
    enabled: authenticated,
  });
  const profileName = firstNonEmpty(profileQuery.data?.fullName);
  const profileEmail = firstNonEmpty(profileQuery.data?.email);
  const profileLabel = firstNonEmpty(profileName, profileEmail);
  const profileDisplay = profileLabel ? sessionDisplayFromLabel(profileLabel) : null;
  const displayName = profileDisplay?.userLabel ?? userLabel ?? t('nav.user');
  const displayInitials = profileDisplay?.userInitials ?? userInitials ?? '?';
  const currentWorkspace = profileQuery.data?.workspaces?.find((workspace) => workspace.isCurrent);
  const currentWorkspaceName = firstNonEmpty(currentWorkspace?.name);
  const triggerLabel =
    currentWorkspace?.type === 'Organization' && currentWorkspaceName
      ? currentWorkspaceName
      : displayName;
  const transitionLocked =
    workspaceContext.phase === 'switching' || workspaceContext.phase === 'refreshing';
  const workspaceControl = useWorkspaceControl({
    contextState: workspaceContext,
    onRetryContext: onRetryWorkspaceContext,
    onWorkspaceChange,
  });
  const languagePreference = useAccountLanguagePreferenceModel();
  const themePreference = useAccountThemePreferenceModel();

  return (
    <>
      <header className="shrink-0 border-b border-border bg-card">
        <div className="flex min-h-16 w-full min-w-0 flex-wrap items-center gap-axis-region px-axis-page-compact py-3 sm:px-axis-page-default lg:px-axis-page-wide">
          <Link to="/dashboard" className="flex min-w-0 items-center gap-axis-region">
            <img src="/axis-logo.svg" alt="" className="size-11 shrink-0" width={44} height={44} />
            <span className="block min-w-0 truncate text-axis-metadata font-axis-metadata uppercase tracking-widest text-muted-foreground">
              {pageTitle}
            </span>
          </Link>

          <div className="ml-auto flex min-w-0 shrink items-center gap-axis-inline">
            <AccountSurface
              surfaceId="account-actions"
              identity={{
                displayName,
                initials: displayInitials,
                secondaryLabel: profileName && profileEmail ? profileEmail : undefined,
                triggerKind: currentWorkspace?.type === 'Organization' ? 'organization' : 'person',
                triggerLabel,
              }}
              onSignOut={onSignOut}
              preferences={{ language: languagePreference, theme: themePreference }}
              signOutError={signOutError}
              signingOut={signingOut}
              transitionLocked={transitionLocked}
              workspace={workspaceControl.workspace}
            />
          </div>
        </div>
      </header>
      {workspaceControl.overlay}
    </>
  );
}
