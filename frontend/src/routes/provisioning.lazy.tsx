import { createLazyFileRoute } from '@tanstack/react-router';

import { WorkspaceProvisioningPage } from '@/features/auth/components/WorkspaceProvisioningPage';

export const Route = createLazyFileRoute('/provisioning')({
  component: WorkspaceProvisioningPage,
});
