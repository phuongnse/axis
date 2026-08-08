import { createLazyFileRoute } from '@tanstack/react-router';
import { ServiceIdentitiesPage } from '@/features/service-identities';

export const Route = createLazyFileRoute('/_authenticated/service-identities')({
  component: ServiceIdentitiesPage,
});
