import { businessObjectsNavigationContributions } from '@/features/business-objects';
import { rulesNavigationContributions } from '@/features/rules';
import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const moduleNavigationContributions: readonly ModuleNavigationContribution[] = [
  ...businessObjectsNavigationContributions,
  ...rulesNavigationContributions,
];
