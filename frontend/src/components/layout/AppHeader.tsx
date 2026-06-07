import { Link, useRouterState } from '@tanstack/react-router';
import { Bell, Search } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { pageTitleKeyForPath, shellNavItems } from '@/components/layout/shell-nav';
import { PreferenceControls } from '@/features/preferences';
import { cn } from '@/lib/utils';

interface AppHeaderProps {
  onSignOut: () => void;
}

export function AppHeader({ onSignOut }: AppHeaderProps) {
  const { t } = useTranslation();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const pageTitle = t(pageTitleKeyForPath(pathname));

  return (
    <header className="shrink-0 border-b border-border bg-background/90 backdrop-blur">
      <div className="flex min-h-14 items-center gap-3 px-4 py-3 sm:px-6">
        <p className="min-w-0 shrink text-[13px] text-muted-foreground sm:min-w-[160px]">
          <span className="font-medium text-primary">Axis</span>
          <span className="mx-2 text-border">/</span>
          <span className="inline-block max-w-[9rem] truncate align-bottom sm:max-w-none">
            {pageTitle}
          </span>
        </p>

        <div className="mx-auto hidden max-w-lg flex-1 justify-center md:flex">
          <search
            aria-label={t('shell.searchLabel')}
            className="flex h-8 w-full items-center gap-2 rounded-md border border-input bg-card px-3 text-muted-foreground shadow-sm"
          >
            <Search className="size-4 shrink-0" aria-hidden />
            <span className="flex-1 text-left text-xs">{t('shell.searchPlaceholder')}</span>
            <kbd className="hidden rounded-sm border border-border bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground sm:inline">
              {t('common.ctrlK')}
            </kbd>
          </search>
        </div>

        <div className="ml-auto flex shrink-0 items-center gap-2">
          <PreferenceControls />
          <button
            type="button"
            className="hidden size-9 items-center justify-center rounded-md text-muted-foreground hover:bg-muted hover:text-foreground sm:inline-flex"
            aria-label={t('shell.notificationsLabel')}
            disabled
          >
            <Bell className="size-4" />
          </button>
          <button
            type="button"
            onClick={onSignOut}
            className="rounded-md border border-border bg-card px-3 py-1.5 text-xs font-medium text-primary hover:bg-muted"
          >
            {t('common.signOut')}
          </button>
        </div>
      </div>

      <nav
        className="flex gap-1 overflow-x-auto border-t border-border px-3 py-2 lg:hidden"
        aria-label={t('shell.mobileNavigation')}
      >
        {shellNavItems.map((item) => {
          const Icon = item.icon;
          const active = item.to !== undefined && pathname.startsWith(item.to);
          const className = cn(
            'inline-flex shrink-0 items-center gap-2 rounded-md border px-3 py-2 text-xs',
            active && 'border-primary/20 bg-primary/10 font-medium text-primary',
            !active && !item.disabled && 'border-transparent text-muted-foreground',
            item.disabled && 'border-transparent text-muted-foreground/45',
          );

          if (item.disabled || !item.to) {
            return (
              <span key={item.labelKey} className={className} aria-disabled>
                <Icon className="size-3.5" aria-hidden />
                {t(item.labelKey)}
              </span>
            );
          }

          return (
            <Link key={item.labelKey} to={item.to} className={className}>
              <Icon className="size-3.5" aria-hidden />
              {t(item.labelKey)}
            </Link>
          );
        })}
      </nav>
    </header>
  );
}
