import { Link, useRouterState } from '@tanstack/react-router';
import { LogOut } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { pageTitleKeyForPath, shellNavItems } from '@/components/shared/shellNav';
import { Button } from '@/components/ui/button';
import { PreferencesMenu } from '@/features/preferences';
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
      <div className="flex min-h-14 flex-wrap items-center gap-3 px-4 py-3 sm:px-6">
        <p className="min-w-0 shrink truncate text-[13px] font-medium text-foreground sm:min-w-[160px]">
          {pageTitle}
        </p>

        <div className="ml-auto flex shrink-0 flex-wrap items-center justify-end gap-2">
          <PreferencesMenu authenticated />
          <Button
            type="button"
            onClick={onSignOut}
            variant="outline"
            size="sm"
            className="text-primary"
          >
            <LogOut className="size-3.5" aria-hidden />
            {t('nav.signOut')}
          </Button>
        </div>
      </div>

      <nav
        className="flex gap-1 overflow-x-auto border-t border-border px-3 py-2 lg:hidden"
        aria-label={t('nav.mobile')}
      >
        {shellNavItems.map((item) => {
          const Icon = item.icon;
          const label = t(item.labelKey);
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
                {label}
              </span>
            );
          }

          return (
            <Link key={item.labelKey} to={item.to} className={className}>
              <Icon className="size-3.5" aria-hidden />
              {label}
            </Link>
          );
        })}
      </nav>
    </header>
  );
}
