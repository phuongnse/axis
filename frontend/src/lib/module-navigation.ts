export type ModuleNavigationIcon = 'businessObjects' | 'rules';
export type ModuleNavigationRouteTarget = '/business-objects' | '/rules';

export interface ModuleNavigationGroup {
  id: string;
  labelKey: string;
  order: number;
}

export interface ModuleNavigationContext {
  pathname: string;
}

export interface ModuleNavigationContribution {
  id: string;
  labelKey: string;
  icon: ModuleNavigationIcon;
  to: ModuleNavigationRouteTarget;
  group: ModuleNavigationGroup;
  order: number;
  isVisible?: (context: ModuleNavigationContext) => boolean;
  isActive?: (context: ModuleNavigationContext) => boolean;
}

export interface VisibleModuleNavigationContribution extends ModuleNavigationContribution {
  isActive: (context: ModuleNavigationContext) => boolean;
}

function isValidContribution(contribution: ModuleNavigationContribution): boolean {
  return Boolean(
    contribution.id.trim() &&
      contribution.labelKey.trim() &&
      contribution.to.trim() &&
      contribution.group.id.trim() &&
      contribution.group.labelKey.trim() &&
      contribution.icon,
  );
}

function defaultActiveMatch(contribution: ModuleNavigationContribution, pathname: string): boolean {
  return pathname === contribution.to || pathname.startsWith(`${contribution.to}/`);
}

export function visibleModuleNavigationContributions(
  contributions: readonly ModuleNavigationContribution[],
  context: ModuleNavigationContext,
): VisibleModuleNavigationContribution[] {
  return contributions
    .filter(isValidContribution)
    .filter((contribution) => contribution.isVisible?.(context) ?? true)
    .map((contribution) => ({
      ...contribution,
      isActive: (activeContext: ModuleNavigationContext) =>
        contribution.isActive?.(activeContext) ??
        defaultActiveMatch(contribution, activeContext.pathname),
    }))
    .sort((left, right) => {
      const groupOrder = left.group.order - right.group.order;
      if (groupOrder !== 0) return groupOrder;

      const itemOrder = left.order - right.order;
      if (itemOrder !== 0) return itemOrder;

      return left.id.localeCompare(right.id);
    });
}
