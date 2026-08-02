import { createFileRoute } from '@tanstack/react-router';
import { applicationRecordsQueryOptions } from '@/features/applications';
import type { MyRouterContext } from '../__root';

export interface ApplicationsRouteSearch {
  recordId?: string;
}

export const Route = createFileRoute('/_authenticated/applications')({
  validateSearch: validateApplicationsSearch,
  loader: ({ context }) => loadApplicationsRoute(context),
});

export function loadApplicationsRoute({ queryClient }: MyRouterContext) {
  return queryClient.ensureQueryData(applicationRecordsQueryOptions());
}

function validateApplicationsSearch(search: Record<string, unknown>): ApplicationsRouteSearch {
  const recordId =
    typeof search.recordId === 'string' && search.recordId ? search.recordId : undefined;
  return recordId ? { recordId } : {};
}
