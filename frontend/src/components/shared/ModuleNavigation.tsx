import { Link } from '@tanstack/react-router';
import { Blocks, ListChecks } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { buttonVariants } from '@/components/ui/button';
import type {
  ModuleNavigationContext,
  ModuleNavigationIcon,
  VisibleModuleNavigationContribution,
} from '@/lib/module-navigation';
import { cn } from '@/lib/utils';
import { persistentItemHighlight, transientItemHighlight } from './interactionStates';

interface ModuleNavigationProps {
  context: ModuleNavigationContext;
  items: readonly VisibleModuleNavigationContribution[];
}

const iconByToken: Record<ModuleNavigationIcon, typeof Blocks> = {
  businessObjects: Blocks,
  rules: ListChecks,
};

export function ModuleNavigation({ context, items }: ModuleNavigationProps) {
  const { t } = useTranslation();
  const groups = groupItems(items);

  if (items.length === 0) {
    return null;
  }

  return (
    <nav
      aria-label={t('nav.modules')}
      className="min-h-0 shrink-0 border-b border-border bg-card/80 md:w-60 md:border-r md:border-b-0"
    >
      <div className="flex min-w-0 gap-2 overflow-x-auto px-4 py-2 md:h-full md:min-h-0 md:flex-col md:gap-4 md:overflow-x-hidden md:overflow-y-auto md:px-3 md:py-4">
        {groups.map((group) => (
          <div key={group.id} className="flex min-w-max gap-2 md:min-w-0 md:flex-col">
            <p className="hidden px-2 text-xs font-medium text-muted-foreground md:block">
              {t(group.labelKey)}
            </p>
            <div className="flex gap-2 md:flex-col">
              {group.items.map((item) => {
                const Icon = iconByToken[item.icon];
                const active = item.isActive(context);

                return (
                  <Link
                    key={item.id}
                    to={item.to}
                    aria-current={active ? 'page' : undefined}
                    className={cn(
                      buttonVariants({ variant: 'ghost' }),
                      'md:w-full md:justify-start',
                      transientItemHighlight,
                      active && persistentItemHighlight,
                    )}
                  >
                    <Icon className="size-4 shrink-0" aria-hidden />
                    <span className="truncate">{t(item.labelKey)}</span>
                  </Link>
                );
              })}
            </div>
          </div>
        ))}
      </div>
    </nav>
  );
}

function groupItems(items: readonly VisibleModuleNavigationContribution[]) {
  const groups = new Map<
    string,
    {
      id: string;
      labelKey: string;
      order: number;
      items: VisibleModuleNavigationContribution[];
    }
  >();

  for (const item of items) {
    const group = groups.get(item.group.id) ?? {
      id: item.group.id,
      labelKey: item.group.labelKey,
      order: item.group.order,
      items: [],
    };
    group.items.push(item);
    groups.set(group.id, group);
  }

  return [...groups.values()].sort((left, right) => left.order - right.order);
}
