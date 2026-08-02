import type { ManagedWindowRendererRegistry } from '@/components/shared/ManagedWindowManager';
import { applicationsManagedWindowRenderers } from '@/features/applications';
import { businessObjectsManagedWindowRenderers } from '@/features/business-objects';
import { rulesManagedWindowRenderers } from '@/features/rules';

export const managedWindowRenderers: ManagedWindowRendererRegistry = {
  ...applicationsManagedWindowRenderers,
  ...businessObjectsManagedWindowRenderers,
  ...rulesManagedWindowRenderers,
};
