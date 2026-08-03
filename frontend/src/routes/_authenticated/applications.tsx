import { createFileRoute } from '@tanstack/react-router';
import { applicationRecordsQueryOptions } from '@/features/applications';
import type { MyRouterContext } from '../__root';

export interface ApplicationsRouteSearch {
  page: number;
  pageSize: number;
  recordId?: string;
}

export const Route = createFileRoute('/_authenticated/applications')({
  validateSearch: validateApplicationsSearch,
  loaderDeps: ({ search }) => ({ page: search.page, pageSize: search.pageSize }),
  loader: ({ context, deps }) => loadApplicationsRoute(context, deps.page, deps.pageSize),
});

export function loadApplicationsRoute({ queryClient }: MyRouterContext, page = 1, pageSize = 20) {
  return queryClient.ensureQueryData(applicationRecordsQueryOptions(page, pageSize));
}

function validateApplicationsSearch(search: Record<string, unknown>): ApplicationsRouteSearch {
  const requestedPage = Number(search.page);
  const page = Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const requestedPageSize = Number(search.pageSize);
  const pageSize = [10, 20, 50, 100].includes(requestedPageSize) ? requestedPageSize : 20;
  const recordId =
    typeof search.recordId === 'string' && search.recordId ? search.recordId : undefined;
  return {
    page,
    pageSize,
    ...(recordId ? { recordId } : {}),
  };
}
