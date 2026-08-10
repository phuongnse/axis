import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const serviceIdentitiesNavigationContributions: readonly ModuleNavigationContribution[] = [
  {
    id: 'identity.service-identities',
    labelKey: 'serviceIdentities.nav',
    icon: 'serviceIdentities',
    to: '/service-identities',
    group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 100 },
    order: 95,
    requiresServerAvailability: true,
  },
];
