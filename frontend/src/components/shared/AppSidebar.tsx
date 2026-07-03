import { Link, useRouterState } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import axisLogo from '@/assets/axis-logo.svg';
import { shellNavItems } from '@/components/shared/shellNav';
import { useAuthStore } from '@/features/auth/auth-store';
import { cn } from '@/lib/utils';

export function AppSidebar() {
  const { t } = useTranslation();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const userLabel = useAuthStore((state) => state.userLabel);
  const userInitials = useAuthStore((state) => state.userInitials);

  return (
    <aside className="hidden min-h-screen w-64 shrink-0 flex-col border-r border-border bg-card text-foreground lg:flex">
      <div className="space-y-4 border-b border-border px-5 py-5">
        <div className="flex items-center gap-2">
          <img src={axisLogo} alt="" className="size-8 shrink-0" width={32} height={32} />
          <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
            {t('nav.controlPlane')}
          </p>
        </div>
      </div>

      <nav className="flex-1 space-y-1 px-3 py-4" aria-label={t('nav.sidebar')}>
        {shellNavItems.map((item) => {
          const Icon = item.icon;
          const label = t(item.labelKey);
          const active = item.to !== undefined && pathname.startsWith(item.to);
          const baseClass = cn(
            'flex items-center gap-3 rounded-lg border border-transparent px-3 py-2 text-[13px] transition-colors',
            active && 'border-primary/20 bg-primary font-medium text-primary-foreground',
            !active && !item.disabled && 'text-foreground hover:border-border hover:bg-accent',
            item.disabled && 'cursor-not-allowed text-muted-foreground/45',
          );

          if (item.disabled || !item.to) {
            return (
              <span key={item.labelKey} className={baseClass} aria-disabled>
                <Icon className="size-4 shrink-0" aria-hidden />
                {label}
              </span>
            );
          }

          return (
            <Link key={item.labelKey} to={item.to} className={baseClass}>
              <Icon className="size-4 shrink-0" aria-hidden />
              {label}
            </Link>
          );
        })}
      </nav>

      <div className="mt-auto border-t border-border px-4 py-4">
        <div className="flex items-center gap-3">
          <div
            className="flex size-8 shrink-0 items-center justify-center rounded-lg border border-border bg-accent text-xs font-medium text-accent-foreground"
            aria-hidden
          >
            {userInitials ?? '?'}
          </div>
          <div className="min-w-0">
            <span className="block truncate text-xs font-medium text-foreground">
              {userLabel ?? t('nav.user')}
            </span>
            <span className="block text-[11px] text-muted-foreground">
              {t('nav.verifiedAccount')}
            </span>
          </div>
        </div>
      </div>
    </aside>
  );
}
