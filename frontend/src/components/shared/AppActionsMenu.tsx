import { useQuery } from '@tanstack/react-query';
import { Building2, ChevronDown, LogOut, Settings2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AccountAvatar } from '@/components/shared/AccountAvatar';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { transientItemHighlight } from '@/components/shared/interactionStates';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Separator } from '@/components/ui/separator';
import { useAuthStore } from '@/features/auth/auth-store';
import { sessionDisplayFromLabel } from '@/features/auth/session-display';
import { dashboardQueryKeys, getCurrentUserProfile } from '@/features/dashboard/api';
import { LanguageControl, ThemeControl } from '@/features/preferences';
import type { CreatedOrganizationWorkspace, EligibleWorkspace } from '@/features/workspaces/api';
import {
  type WorkspaceChangeResult,
  type WorkspaceContextState,
  WorkspaceControl,
} from '@/features/workspaces/WorkspaceControl';

interface AppActionsMenuProps {
  onSignOut: () => void;
  onRetryWorkspaceContext: () => Promise<void>;
  onWorkspaceChange: (
    target: EligibleWorkspace | CreatedOrganizationWorkspace,
  ) => Promise<WorkspaceChangeResult>;
  signOutError?: boolean;
  signingOut?: boolean;
  workspaceContext: WorkspaceContextState;
}

function firstNonEmpty(...values: Array<string | null | undefined>): string | null {
  for (const value of values) {
    const trimmed = value?.trim();
    if (trimmed) return trimmed;
  }
  return null;
}

export function AppActionsMenu({
  onSignOut,
  onRetryWorkspaceContext,
  onWorkspaceChange,
  signOutError = false,
  signingOut = false,
  workspaceContext,
}: AppActionsMenuProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const authenticated = useAuthStore((state) => state.browserSessionStatus === 'authenticated');
  const userLabel = useAuthStore((state) => state.userLabel);
  const userInitials = useAuthStore((state) => state.userInitials);
  const profileQuery = useQuery({
    queryKey: dashboardQueryKeys.currentUser(),
    queryFn: getCurrentUserProfile,
    enabled: authenticated,
  });
  const profileLabel = firstNonEmpty(profileQuery.data?.fullName, profileQuery.data?.email);
  const profileDisplay = profileLabel ? sessionDisplayFromLabel(profileLabel) : null;
  const displayName = profileDisplay?.userLabel ?? userLabel ?? t('nav.user');
  const displayInitials = profileDisplay?.userInitials ?? userInitials ?? '?';
  const currentWorkspace = profileQuery.data?.workspaces?.find((workspace) => workspace.isCurrent);
  const currentWorkspaceName = firstNonEmpty(currentWorkspace?.name);
  const topBarLabel =
    currentWorkspace?.type === 'Organization' && currentWorkspaceName
      ? currentWorkspaceName
      : displayName;
  const contextTransitionActive =
    workspaceContext.phase === 'switching' || workspaceContext.phase === 'refreshing';

  return (
    <Popover
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen && contextTransitionActive) return;
        setOpen(nextOpen);
      }}
    >
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="ghost"
            size="lg"
            className={`min-h-11 max-w-64 gap-2 px-2 text-foreground ${transientItemHighlight}`}
            aria-label={t('nav.accountMenu')}
            title={t('nav.accountMenu')}
          >
            {currentWorkspace?.type === 'Organization' ? (
              <Avatar aria-hidden>
                <AvatarFallback>
                  <Building2 className="size-4" />
                </AvatarFallback>
              </Avatar>
            ) : (
              <AccountAvatar initials={displayInitials} size="md" />
            )}
            <span className="hidden min-w-0 truncate sm:inline">{topBarLabel}</span>
            <ChevronDown className="size-3.5 text-muted-foreground" aria-hidden />
          </Button>
        }
      />
      <PopoverContent
        align="end"
        className="max-h-(--available-height) w-80 max-w-full overflow-y-auto"
        aria-label={t('nav.accountMenu')}
      >
        <WorkspaceControl
          contextState={workspaceContext}
          onRetryContext={onRetryWorkspaceContext}
          onWorkspaceChange={onWorkspaceChange}
        />

        <Separator />

        <section aria-label={t('app.preferences')} className="grid gap-3">
          <div className="flex items-center gap-2 px-1 text-xs font-medium text-muted-foreground">
            <Settings2 className="size-3.5" aria-hidden />
            {t('app.preferences')}
          </div>
          <LanguageControl authenticated variant="menu" />
          <ThemeControl authenticated variant="menu" />
        </section>

        <Separator />

        <section aria-label={t('app.account')} className="grid gap-2">
          <AsyncButton
            type="button"
            variant="destructive"
            size="sm"
            className="w-full"
            icon={<LogOut />}
            pending={signingOut}
            pendingLabel={t('nav.signingOut')}
            onClick={onSignOut}
          >
            {t('nav.signOut')}
          </AsyncButton>
          {signOutError ? (
            <StatusNotice tone="destructive">{t('nav.signOutFailed')}</StatusNotice>
          ) : null}
        </section>
      </PopoverContent>
    </Popover>
  );
}
