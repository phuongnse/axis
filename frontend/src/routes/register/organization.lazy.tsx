import { createLazyFileRoute } from '@tanstack/react-router';

import { RegisterOrganizationPage } from '@/features/auth/components/RegisterOrganizationPage';

export const Route = createLazyFileRoute('/register/organization')({
  component: RegisterOrganizationPage,
});
