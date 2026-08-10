import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const membershipsNavigationContributions: readonly ModuleNavigationContribution[] = [
  {
    id: 'identity.memberships',
    labelKey: 'memberships.nav',
    icon: 'memberships',
    to: '/memberships',
    group: {
      id: 'workspace',
      labelKey: 'nav.group.workspace',
      order: 100,
    },
    order: 90,
    requiresServerAvailability: true,
  },
];
