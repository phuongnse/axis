import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const rulesNavigationContributions: readonly ModuleNavigationContribution[] = [
  {
    id: 'rules.fieldDefinitions',
    labelKey: 'rules.nav.definitions',
    icon: 'rules',
    to: '/rules',
    group: {
      id: 'workspace',
      labelKey: 'nav.group.workspace',
      order: 100,
    },
    order: 110,
  },
];
