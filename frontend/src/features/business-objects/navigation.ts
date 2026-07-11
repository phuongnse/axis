import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const businessObjectsNavigationContributions: readonly ModuleNavigationContribution[] = [
  {
    id: 'businessObjects.definitions',
    labelKey: 'businessObjects.nav.definitions',
    icon: 'businessObjects',
    to: '/business-objects',
    group: {
      id: 'workspace',
      labelKey: 'nav.group.workspace',
      order: 100,
    },
    order: 100,
  },
];
