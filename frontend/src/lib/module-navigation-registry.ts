import { objectsNavigationContributions } from '@/features/objects/navigation';
import { rulesNavigationContributions } from '@/features/rules';
import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const moduleNavigationContributions: readonly ModuleNavigationContribution[] = [
  ...objectsNavigationContributions,
  ...rulesNavigationContributions,
];
