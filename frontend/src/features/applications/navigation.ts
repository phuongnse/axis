import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const applicationsNavigationContributions: readonly ModuleNavigationContribution[] = [
  {
    id: 'applications.records',
    labelKey: 'applications.nav.records',
    icon: 'applications',
    to: '/applications',
    group: {
      id: 'workspace',
      labelKey: 'nav.group.workspace',
      order: 100,
    },
    order: 90,
  },
];
