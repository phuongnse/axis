import type { ManagedWindowRendererRegistry } from '@/components/shared/ManagedWindowManager';
import { businessObjectsManagedWindowRenderers } from '@/features/business-objects';
import { rulesManagedWindowRenderers } from '@/features/rules';

export const managedWindowRenderers: ManagedWindowRendererRegistry = {
  ...businessObjectsManagedWindowRenderers,
  ...rulesManagedWindowRenderers,
};
