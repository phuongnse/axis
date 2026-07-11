import { createFileRoute } from '@tanstack/react-router';
import { businessObjectDefinitionsListQueryOptions } from '@/features/business-objects';
import { ruleDefinitionsListQueryOptions } from '@/features/rules';
import type { MyRouterContext } from '../__root';

export type BusinessObjectsDialogMode = 'create' | 'edit' | 'view';

export interface BusinessObjectsRouteSearch {
  page: number;
  dialog?: BusinessObjectsDialogMode;
  recordId?: string;
}

export const Route = createFileRoute('/_authenticated/business-objects')({
  validateSearch: validateBusinessObjectsSearch,
  loaderDeps: ({ search }) => ({ page: search.page }),
  loader: ({ context, deps }) => loadBusinessObjectDefinitionsRoute(context, deps.page),
});

export function loadBusinessObjectDefinitionsRoute({ queryClient }: MyRouterContext, page = 1) {
  return Promise.all([
    queryClient.ensureQueryData(businessObjectDefinitionsListQueryOptions(page)),
    queryClient.ensureQueryData(
      ruleDefinitionsListQueryOptions({ page: 1, pageSize: 100, scope: 'Field' }),
    ),
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

  return {
    page,
    ...(dialog ? { dialog } : {}),
    ...(recordId ? { recordId } : {}),
  };
}
