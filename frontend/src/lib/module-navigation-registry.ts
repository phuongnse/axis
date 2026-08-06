import { businessObjectsNavigationContributions } from '@/features/business-objects';
import { membershipsNavigationContributions } from '@/features/memberships';
import { rulesNavigationContributions } from '@/features/rules';
import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const moduleNavigationContributions: readonly ModuleNavigationContribution[] = [
  ...membershipsNavigationContributions,
  ...businessObjectsNavigationContributions,
  ...rulesNavigationContributions,
];
