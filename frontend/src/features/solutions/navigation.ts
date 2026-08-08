import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const solutionsNavigationContributions: readonly ModuleNavigationContribution[] = [
  {
    id: 'solutions.management',
    labelKey: 'solutions.nav',
    icon: 'solutions',
    to: '/solutions',
    group: {
      id: 'workspace',
      labelKey: 'nav.group.workspace',
      order: 100,
    },
    order: 110,
  },
];
