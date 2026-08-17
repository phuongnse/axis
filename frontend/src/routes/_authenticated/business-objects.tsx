import { createFileRoute } from '@tanstack/react-router';
import { businessObjectDefinitionsListQueryOptions } from '@/features/business-objects';
import { ruleDefinitionsListQueryOptions } from '@/features/rules';
import type { BusinessObjectDefinitionSortField } from '@/lib/api-generated';
import type { MyRouterContext } from '../__root';

export type BusinessObjectsDialogMode = 'create' | 'edit' | 'view';
type BusinessObjectsTableSortField = Exclude<
  BusinessObjectDefinitionSortField,
  'CreatedBy' | 'CreatedAt'
>;

export interface BusinessObjectsRouteSearch {
  page: number;
  query?: string;
  dialog?: BusinessObjectsDialogMode;
  recordId?: string;
  sortBy?: BusinessObjectsTableSortField;
  sortDirection?: 'Ascending' | 'Descending';
}

export const Route = createFileRoute('/_authenticated/business-objects')({
  validateSearch: validateBusinessObjectsSearch,
  loaderDeps: ({ search }) => ({ page: search.page }),
  loader: ({ context, deps }) => loadBusinessObjectDefinitionsRoute(context, deps.page),
});

export function loadBusinessObjectDefinitionsRoute({ queryClient }: MyRouterContext, page = 1) {
  return Promise.all([
    queryClient.ensureQueryData(businessObjectDefinitionsListQueryOptions(page)),
    queryClient.ensureQueryData(ruleDefinitionsListQueryOptions({ page: 1, pageSize: 100 })),
  ]);
}

function validateBusinessObjectsSearch(
  search: Record<string, unknown>,
): BusinessObjectsRouteSearch {
  const requestedPage = Number(search.page);
  const page = Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const dialog =
    search.dialog === 'create' || search.dialog === 'edit' || search.dialog === 'view'
      ? search.dialog
      : undefined;
  const recordId =
    typeof search.recordId === 'string' && search.recordId ? search.recordId : undefined;

  const query = typeof search.query === 'string' && search.query.trim() ? search.query : undefined;
  const sortBy =
    search.sortBy === 'Name' ||
    search.sortBy === 'Key' ||
    search.sortBy === 'Status' ||
    search.sortBy === 'Version' ||
    search.sortBy === 'Revision' ||
    search.sortBy === 'ModifiedBy' ||
    search.sortBy === 'ModifiedAt'
      ? search.sortBy
      : undefined;
  const sortDirection =
    search.sortDirection === 'Ascending' || search.sortDirection === 'Descending'
      ? search.sortDirection
      : undefined;

  return {
    page,
    ...(query ? { query } : {}),
    ...(dialog ? { dialog } : {}),
    ...(recordId ? { recordId } : {}),
    ...(sortBy && sortDirection ? { sortBy, sortDirection } : {}),
  };
}
