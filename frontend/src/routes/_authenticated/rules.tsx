import { createFileRoute } from '@tanstack/react-router';
import { ruleDefinitionsListQueryOptions } from '@/features/rules';
import type { MyRouterContext } from '../__root';

export const Route = createFileRoute('/_authenticated/rules')({
  validateSearch: validateRulesSearch,
  loader: ({ context }) => loadRulesRoute(context),
});

export interface RulesRouteSearch {
  dialog?: 'create' | 'edit';
  definitionKey?: string;
}

export function loadRulesRoute({ queryClient }: MyRouterContext) {
  return queryClient.ensureQueryData(ruleDefinitionsListQueryOptions());
}

function validateRulesSearch(search: Record<string, unknown>): RulesRouteSearch {
  const dialog = search.dialog === 'create' || search.dialog === 'edit' ? search.dialog : undefined;
  const definitionKey =
    typeof search.definitionKey === 'string' && search.definitionKey
      ? search.definitionKey
      : undefined;
  return {
    ...(dialog ? { dialog } : {}),
    ...(definitionKey ? { definitionKey } : {}),
  };
}
