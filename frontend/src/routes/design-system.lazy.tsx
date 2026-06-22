import { createLazyFileRoute } from '@tanstack/react-router';

import { DesignSystemCatalog } from '@/features/design-system';

export const Route = createLazyFileRoute('/design-system')({
  component: DesignSystemCatalog,
});
