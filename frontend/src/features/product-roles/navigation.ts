import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const productRolesNavigationContributions: readonly ModuleNavigationContribution[] = [
  {
    id: 'authorization.product-roles',
    labelKey: 'productRoles.nav',
    icon: 'productRoles',
    to: '/product-role-assignments',
    group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 100 },
    order: 97,
    requiresServerAvailability: true,
  },
];
