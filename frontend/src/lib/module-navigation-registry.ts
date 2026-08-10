import { businessObjectsNavigationContributions } from '@/features/business-objects';
import { membershipsNavigationContributions } from '@/features/memberships';
import { productRolesNavigationContributions } from '@/features/product-roles';
import { rulesNavigationContributions } from '@/features/rules';
import { serviceIdentitiesNavigationContributions } from '@/features/service-identities';
import { solutionsNavigationContributions } from '@/features/solutions';
import type { ModuleNavigationContribution } from '@/lib/module-navigation';

export const moduleNavigationContributions: readonly ModuleNavigationContribution[] = [
  ...membershipsNavigationContributions,
  ...serviceIdentitiesNavigationContributions,
  ...productRolesNavigationContributions,
  ...businessObjectsNavigationContributions,
  ...rulesNavigationContributions,
  ...solutionsNavigationContributions,
];
