import { Link } from '@tanstack/react-router';
import { Blocks, Bot, KeyRound, ListChecks, PackageOpen, Users } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { buttonVariants } from '@/components/ui/button';
import type {
  ModuleNavigationContext,
  ModuleNavigationIcon,
  VisibleModuleNavigationContribution,
} from '@/lib/module-navigation';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';
import { persistentItemHighlight, transientItemHighlight } from './interactionStates';

interface ModuleNavigationProps {
  context: ModuleNavigationContext;
  items: readonly VisibleModuleNavigationContribution[];
}

const iconByToken: Record<ModuleNavigationIcon, typeof Blocks> = {
  businessObjects: Blocks,
  memberships: Users,
  productRoles: KeyRound,
  rules: ListChecks,
  serviceIdentities: Bot,
  solutions: PackageOpen,
};

export function ModuleNavigation({ context, items }: ModuleNavigationProps) {
  const { t } = useTranslation();
  const groups = groupItems(items);
  const activeItemId = items.find((item) => item.isActive(context))?.id;
  const navigationItemsRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const navigationItems = navigationItemsRef.current;
    if (!navigationItems || !activeItemId) return;

    const revealActiveItem = () => {
      const activeItem = navigationItems.querySelector<HTMLElement>('[aria-current="page"]');
      if (typeof activeItem?.scrollIntoView === 'function') {
        activeItem.scrollIntoView({ block: 'nearest', inline: 'center' });
      }
    };

    revealActiveItem();
    if (typeof ResizeObserver === 'undefined') return;

    const observer = new ResizeObserver(revealActiveItem);
    observer.observe(navigationItems);
    return () => observer.disconnect();
  }, [activeItemId]);

  if (items.length === 0) {
    return null;
  }

  return (
    <nav
      aria-label={t('nav.modules')}
      className="min-h-0 shrink-0 border-b border-border bg-card md:w-60 md:border-r md:border-b-0"
    >
      <div
        ref={navigationItemsRef}
        data-slot="module-navigation-items"
        className={cn(
          'flex min-w-0 overflow-x-auto md:h-full md:min-h-0 md:flex-col md:overflow-x-hidden md:overflow-y-auto md:px-3',
          axisStyles.spacing.gap.inline,
          axisStyles.spacing.padding.inline.pageCompact,
          axisStyles.spacing.padding.block.inline,
          axisStyles.spacing.gap.regionAtMedium,
          axisStyles.spacing.padding.block.regionAtMedium,
        )}
      >
        {groups.map((group) => (
          <div
            key={group.id}
            className={cn('flex min-w-max md:min-w-0 md:flex-col', axisStyles.spacing.gap.inline)}
          >
            <p
              className={cn(
                'hidden text-muted-foreground md:block',
                axisStyles.spacing.padding.inline.inline,
                axisStyles.typography.scale.metadata,
                axisStyles.typography.weight.label,
              )}
            >
              {t(group.labelKey)}
            </p>
            <div className={cn('flex md:flex-col', axisStyles.spacing.gap.inline)}>
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
                      axisStyles.density.minHeight.touchTarget,
                      axisStyles.density.minWidth.touchTarget,
                      'md:min-h-0 md:min-w-0 md:w-full md:justify-start',
                      transientItemHighlight,
                      active && persistentItemHighlight,
                    )}
                  >
                    <Icon className={cn('shrink-0', axisStyles.icon.size.navigation)} aria-hidden />
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
