import { createFileRoute } from '@tanstack/react-router';
import {
  solutionInstallationsQueryOptions,
  solutionVersionsQueryOptions,
} from '@/features/solutions';
import type { MyRouterContext } from '../__root';

export type SolutionsDialog = 'publish' | 'release' | 'installation';

export interface SolutionsRouteSearch {
  query?: string;
  dialog?: SolutionsDialog;
  versionId?: string;
  installationId?: string;
}

export const Route = createFileRoute('/_authenticated/solutions')({
  validateSearch: validateSolutionsSearch,
  loader: ({ context }) => loadSolutionsRoute(context),
});

export function loadSolutionsRoute({ queryClient }: MyRouterContext) {
  return Promise.all([
    queryClient.ensureQueryData(solutionVersionsQueryOptions()),
    queryClient.ensureQueryData(solutionInstallationsQueryOptions()),
  ]);
}

function validateSolutionsSearch(search: Record<string, unknown>): SolutionsRouteSearch {
  const query = typeof search.query === 'string' && search.query.trim() ? search.query : undefined;
  const dialog =
    search.dialog === 'publish' || search.dialog === 'release' || search.dialog === 'installation'
      ? search.dialog
      : undefined;
  const versionId =
    typeof search.versionId === 'string' && search.versionId ? search.versionId : undefined;
  const installationId =
    typeof search.installationId === 'string' && search.installationId
      ? search.installationId
      : undefined;

  return {
    ...(query ? { query } : {}),
    ...(dialog ? { dialog } : {}),
    ...(versionId ? { versionId } : {}),
    ...(installationId ? { installationId } : {}),
  };
}
