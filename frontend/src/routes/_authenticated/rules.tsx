import { createFileRoute } from '@tanstack/react-router';
import { ruleDefinitionsListQueryOptions } from '@/features/rules';
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
  return {
    page,
    ...(query ? { query } : {}),
    ...(dialog ? { dialog } : {}),
    ...(definitionKey ? { definitionKey } : {}),
  };
}
