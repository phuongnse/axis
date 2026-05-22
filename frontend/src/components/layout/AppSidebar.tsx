import { Link, useRouterState } from '@tanstack/react-router';
import { ChevronDown } from 'lucide-react';

import { shellNavItems } from '@/components/layout/shell-nav';
import { cn } from '@/lib/utils';

export function AppSidebar() {
  const pathname = useRouterState({ select: (s) => s.location.pathname });

  return (
    <aside className="flex w-60 shrink-0 flex-col border-r border-border bg-card min-h-screen">
      <div className="flex items-center gap-2 px-5 py-5 border-b border-border">
        <span className="text-xl text-primary" aria-hidden>
          ⬡
        </span>
        <button
          type="button"
          className="flex flex-1 items-center gap-1 text-sm font-medium text-foreground hover:text-primary"
          disabled
          aria-label="Organization (coming soon)"
        >
          <span className="truncate">Acme Corp</span>
          <ChevronDown className="size-4 text-muted-foreground shrink-0" />
        </button>
      </div>

      <nav className="flex-1 px-3 py-4 space-y-1" aria-label="Main navigation">
        {shellNavItems.map((item) => {
          const Icon = item.icon;
          const active = item.to !== undefined && pathname.startsWith(item.to);
          const baseClass = cn(
            'flex items-center gap-3 rounded-lg px-3 py-2 text-[13px] transition-colors',
            active && 'bg-secondary text-primary font-medium',
            !active && !item.disabled && 'text-foreground/80 hover:bg-muted',
            item.disabled && 'text-muted-foreground cursor-not-allowed opacity-70',
          );

          if (item.disabled || !item.to) {
            return (
              <span key={item.label} className={baseClass} aria-disabled>
                <Icon className="size-4 shrink-0" aria-hidden />
                {item.label}
              </span>
            );
          }

          return (
            <Link key={item.label} to={item.to} className={baseClass}>
              <Icon className="size-4 shrink-0" aria-hidden />
              {item.label}
            </Link>
          );
        })}
      </nav>

      <div className="mt-auto border-t border-border px-4 py-4">
        <div className="flex items-center gap-3">
          <div
            className="flex size-8 shrink-0 items-center justify-center rounded-full border border-primary/30 bg-secondary text-xs font-medium text-primary"
            aria-hidden
          >
            AB
          </div>
          <span className="text-xs font-medium text-foreground truncate">Alex Brown</span>
        </div>
      </div>
    </aside>
  );
}
