import { createFileRoute } from '@tanstack/react-router';
import { currentSiteLanguage } from '@/features/preferences';
import { productRoleManagementQueryOptions } from '@/features/product-roles';

export const Route = createFileRoute('/_authenticated/product-role-assignments')({
  loader: ({ context }) =>
    context.queryClient.ensureQueryData(productRoleManagementQueryOptions(currentSiteLanguage())),
});
