import { objectsNavigationContributions } from '@/features/objects/navigation';
import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const moduleNavigationContributions: readonly ModuleNavigationContribution[] = [
  ...objectsNavigationContributions,
];
