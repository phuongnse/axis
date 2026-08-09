import type { ManagedWindowRendererRegistry } from '@/components/shared/ManagedWindowManager';
import { businessObjectsManagedWindowRenderers } from '@/features/business-objects';
import { membershipsManagedWindowRenderers } from '@/features/memberships';
import { productRolesManagedWindowRenderers } from '@/features/product-roles';
import { rulesManagedWindowRenderers } from '@/features/rules';
import { serviceIdentitiesManagedWindowRenderers } from '@/features/service-identities';

export const managedWindowRenderers: ManagedWindowRendererRegistry = {
  ...businessObjectsManagedWindowRenderers,
  ...membershipsManagedWindowRenderers,
  ...productRolesManagedWindowRenderers,
  ...rulesManagedWindowRenderers,
  ...serviceIdentitiesManagedWindowRenderers,
};
