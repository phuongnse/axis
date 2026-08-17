import { createFileRoute } from '@tanstack/react-router';
import { ruleDefinitionsListQueryOptions } from '@/features/rules';
import type { CollectionSortDirection, RuleDefinitionSortField } from '@/lib/api-generated';
import type { MyRouterContext } from '../__root';

export const Route = createFileRoute('/_authenticated/rules')({
  validateSearch: validateRulesSearch,
  loader: ({ context }) => loadRulesRoute(context),
});

export interface RulesRouteSearch {
  page?: number;
  query?: string;
  dialog?: 'create' | 'edit';
  definitionKey?: string;
  sortBy?: RuleDefinitionSortField;
  sortDirection?: CollectionSortDirection;
}

export function loadRulesRoute({ queryClient }: MyRouterContext) {
  return queryClient.ensureQueryData(ruleDefinitionsListQueryOptions());
}

function validateRulesSearch(search: Record<string, unknown>): RulesRouteSearch {
  const requestedPage = Number(search.page);
  const page = Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const query = typeof search.query === 'string' && search.query.trim() ? search.query : undefined;
  const dialog = search.dialog === 'create' || search.dialog === 'edit' ? search.dialog : undefined;
  const definitionKey =
    typeof search.definitionKey === 'string' && search.definitionKey
      ? search.definitionKey
      : undefined;
  const sortBy =
    search.sortBy === 'Name' || search.sortBy === 'Origin' || search.sortBy === 'Status'
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
    ...(definitionKey ? { definitionKey } : {}),
    ...(sortBy && sortDirection ? { sortBy, sortDirection } : {}),
  };
}
