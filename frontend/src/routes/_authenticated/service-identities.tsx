import { createFileRoute } from '@tanstack/react-router';
import { serviceIdentitiesQueryOptions } from '@/features/service-identities';
export const Route = createFileRoute('/_authenticated/service-identities')({
  loader: ({ context }) => context.queryClient.ensureQueryData(serviceIdentitiesQueryOptions()),
});
