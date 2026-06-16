import { Link, useRouterState } from '@tanstack/react-router';
import { ChevronDown } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import axisLogo from '@/assets/axis-logo.svg';
import { shellNavItems } from '@/components/layout/shell-nav';
import { Button } from '@/components/ui/button';
import { useAuthStore } from '@/features/auth/auth-store';
import { cn } from '@/lib/utils';

export function AppSidebar() {
  const { t } = useTranslation();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const userLabel = useAuthStore((state) => state.userLabel);
  const userInitials = useAuthStore((state) => state.userInitials);

  return (
    <aside className="hidden min-h-screen w-64 shrink-0 flex-col border-r border-[hsl(174_18%_18%)] bg-[hsl(174_25%_12%)] text-white lg:flex">
      <div className="space-y-4 border-b border-white/10 px-5 py-5">
        <div className="flex items-center gap-2">
          <img src={axisLogo} alt="Axis" className="size-8 shrink-0" width={32} height={32} />
          <p className="text-[11px] uppercase tracking-[0.18em] text-white/45">
            {t('common.controlPlane')}
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          className="w-full justify-start border-white/10 bg-white/[0.06] px-3 text-left text-sm text-white hover:bg-white/[0.08] hover:text-white"
          disabled
          aria-label={t('shell.teamAccountLabel')}
        >
          <span className="truncate">{t('shell.teamAccountName')}</span>
          <ChevronDown className="ml-auto size-4 shrink-0 text-white/45" />
        </Button>
      </div>

      <nav className="flex-1 space-y-1 px-3 py-4" aria-label={t('shell.sidebarNavigation')}>
        {shellNavItems.map((item) => {
          const Icon = item.icon;
          const active = item.to !== undefined && pathname.startsWith(item.to);
          const baseClass = cn(
            'flex items-center gap-3 rounded-md border border-transparent px-3 py-2 text-[13px] transition-colors',
            active && 'border-white/10 bg-white/10 text-white font-medium',
            !active && !item.disabled && 'text-white/70 hover:bg-white/[0.07] hover:text-white',
            item.disabled && 'text-white/35 cursor-not-allowed',
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

      <div className="mt-auto border-t border-white/10 px-4 py-4">
        <div className="flex items-center gap-3">
          <div
            className="flex size-8 shrink-0 items-center justify-center rounded-md border border-white/15 bg-white/10 text-xs font-medium text-white"
            aria-hidden
          >
            {userInitials ?? '?'}
          </div>
          <div className="min-w-0">
            <span className="block truncate text-xs font-medium text-white">
              {userLabel ?? t('shell.userFallback')}
            </span>
            <span className="block text-[11px] text-white/45">{t('shell.workspaceAdmin')}</span>
          </div>
        </div>
      </div>
    </aside>
  );
}
