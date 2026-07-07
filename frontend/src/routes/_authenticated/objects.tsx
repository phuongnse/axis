import { createFileRoute } from '@tanstack/react-router';
import { objectDefinitionsListQueryOptions } from '@/features/objects/api';
import type { MyRouterContext } from '../__root';

export const Route = createFileRoute('/_authenticated/objects')({
  loader: ({ context }) => loadObjectDefinitionsRoute(context),
});

export function loadObjectDefinitionsRoute({ queryClient }: MyRouterContext) {
  return queryClient.ensureQueryData(objectDefinitionsListQueryOptions());
}
