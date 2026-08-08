import { createFileRoute } from '@tanstack/react-router';
import {
  solutionInstallationsQueryOptions,
  solutionVersionsQueryOptions,
} from '@/features/solutions';
import type { MyRouterContext } from '../__root';

export const Route = createFileRoute('/_authenticated/solutions')({
  loader: ({ context }) => loadSolutionsRoute(context),
});

export function loadSolutionsRoute({ queryClient }: MyRouterContext) {
  return Promise.all([
    queryClient.ensureQueryData(solutionVersionsQueryOptions()),
    queryClient.ensureQueryData(solutionInstallationsQueryOptions()),
  ]);
}
