import { createFileRoute } from '@tanstack/react-router';
import { fieldRuleDefinitionsListQueryOptions } from '@/features/rules';
import type { MyRouterContext } from '../__root';

export const Route = createFileRoute('/_authenticated/rules')({
  loader: ({ context }) => loadRulesRoute(context),
});

export function loadRulesRoute({ queryClient }: MyRouterContext) {
  return queryClient.ensureQueryData(fieldRuleDefinitionsListQueryOptions());
}
