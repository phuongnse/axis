import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const objectsNavigationContributions: readonly ModuleNavigationContribution[] = [
  {
    id: 'objects.definitions',
    labelKey: 'objects.nav.definitions',
    icon: 'objects',
    to: '/objects',
    group: {
      id: 'workspace',
      labelKey: 'nav.group.workspace',
      order: 100,
    },
    order: 100,
  },
];
