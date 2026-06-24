import { useMutation } from '@tanstack/react-query';
import { Link, useRouterState } from '@tanstack/react-router';
import { ChevronDown } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import axisLogo from '@/assets/axis-logo.svg';
import { shellNavItems } from '@/components/shared/shellNav';
import { Button } from '@/components/ui/button';
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select';
import { switchWorkspace } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { useCurrentUserProfileQuery } from '@/features/workspace/hooks/useWorkspaceStart';
import { cn } from '@/lib/utils';

export function AppSidebar() {
  const { t } = useTranslation();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const userLabel = useAuthStore((state) => state.userLabel);
  const userInitials = useAuthStore((state) => state.userInitials);
  const { data: profile } = useCurrentUserProfileQuery();
  const switchWorkspaceMutation = useMutation({ mutationFn: switchWorkspace });
  const workspaces = profile?.workspaces ?? [];
  const selectedWorkspaceId = profile?.workspaceId ?? '';

  return (
    <aside className="hidden min-h-screen w-[var(--size-sidebar)] shrink-0 flex-col border-r border-sidebar-border bg-sidebar text-sidebar-foreground lg:flex">
      <div className="space-y-4 border-b border-sidebar-border px-5 py-5">
        <div className="flex items-center gap-2">
          <img src={axisLogo} alt="Axis" className="size-8 shrink-0" width={32} height={32} />
          <p className="text-[11px] uppercase tracking-[0.18em] text-sidebar-muted">
            {t('common.controlPlane')}
          </p>
        </div>
        {workspaces.length > 0 ? (
          <div>
            <NativeSelect
              value={selectedWorkspaceId}
              disabled={switchWorkspaceMutation.isPending}
              aria-label={t('shell.workspaceLabel')}
              onChange={(event) => {
                const workspaceId = event.target.value;
                if (workspaceId && workspaceId !== selectedWorkspaceId) {
                  switchWorkspaceMutation.mutate(workspaceId);
                }
              }}
              className="w-full"
            >
              {workspaces.map((workspace) => (
                <NativeSelectOption key={workspace.id} value={workspace.id}>
                  {workspace.name} (
                  {workspace.type === 'Personal'
                    ? t('shell.personalWorkspace')
                    : t('shell.teamWorkspace')}
                  )
                </NativeSelectOption>
              ))}
            </NativeSelect>
          </div>
        ) : (
          <Button
            type="button"
            variant="outline"
            className="w-full justify-start border-sidebar-border bg-inverse-muted px-3 text-left text-sm text-sidebar-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
            disabled
            aria-label={t('shell.workspaceLabel')}
          >
            <span className="truncate">{t('shell.workspaceFallback')}</span>
            <ChevronDown className="ml-auto size-4 shrink-0 text-sidebar-muted" />
          </Button>
        )}
      </div>

      <nav className="flex-1 space-y-1 px-3 py-4" aria-label={t('shell.sidebarNavigation')}>
        {shellNavItems.map((item) => {
          const Icon = item.icon;
          const active = item.to !== undefined && pathname.startsWith(item.to);
          const baseClass = cn(
            'flex items-center gap-3 rounded-md border border-transparent px-3 py-2 text-[13px] transition-colors',
            active &&
              'border-sidebar-border bg-sidebar-accent font-medium text-sidebar-accent-foreground',
            !active &&
              !item.disabled &&
              'text-sidebar-foreground/70 hover:bg-sidebar-accent hover:text-sidebar-accent-foreground',
            item.disabled && 'cursor-not-allowed text-sidebar-muted',
          );

          if (item.disabled || !item.to) {
            return (
              <span key={item.labelKey} className={baseClass} aria-disabled>
                <Icon className="size-4 shrink-0" aria-hidden />
                {t(item.labelKey)}
              </span>
            );
          }

          return (
            <Link key={item.labelKey} to={item.to} className={baseClass}>
              <Icon className="size-4 shrink-0" aria-hidden />
              {t(item.labelKey)}
            </Link>
          );
        })}
      </nav>

      <div className="mt-auto border-t border-sidebar-border px-4 py-4">
        <div className="flex items-center gap-3">
          <div
            className="flex size-8 shrink-0 items-center justify-center rounded-md border border-inverse-border bg-sidebar-accent text-xs font-medium text-sidebar-accent-foreground"
            aria-hidden
          >
            {userInitials ?? '?'}
          </div>
          <div className="min-w-0">
            <span className="block truncate text-xs font-medium text-sidebar-foreground">
              {userLabel ?? t('shell.userFallback')}
            </span>
            <span className="block text-[11px] text-sidebar-muted">
              {t('shell.workspaceAdmin')}
            </span>
          </div>
        </div>
      </div>
    </aside>
  );
}
