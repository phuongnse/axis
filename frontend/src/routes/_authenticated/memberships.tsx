import { createFileRoute } from '@tanstack/react-router';
import {
  workspaceInvitationsQueryOptions,
  workspaceProductBuildersQueryOptions,
} from '@/features/memberships';
import type { CollectionSortDirection, WorkspaceInvitationSortField } from '@/lib/api-generated';
import type { MyRouterContext } from '../__root';

export const Route = createFileRoute('/_authenticated/memberships')({
  validateSearch: validateMembershipsSearch,
  loaderDeps: ({ search }) => search,
  loader: ({ context, deps }) => loadMembershipsRoute(context, deps),
});

type InvitationTableSortField = Exclude<WorkspaceInvitationSortField, 'Created' | 'CreatedBy'>;

export interface MembershipsRouteSearch {
  page: number;
  pageSize: number;
  sortBy?: InvitationTableSortField;
  sortDirection?: CollectionSortDirection;
}

export function loadMembershipsRoute(
  { queryClient }: MyRouterContext,
  search: MembershipsRouteSearch = { page: 1, pageSize: 20 },
) {
  return Promise.all([
    queryClient.ensureQueryData(workspaceProductBuildersQueryOptions()),
    queryClient.ensureQueryData(
      workspaceInvitationsQueryOptions(
        search.page,
        search.pageSize,
        search.sortBy,
        search.sortDirection,
      ),
    ),
  ]);
}

function validateMembershipsSearch(search: Record<string, unknown>): MembershipsRouteSearch {
  const requestedPage = Number(search.page);
  const page = Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const requestedPageSize = Number(search.pageSize);
  const pageSize = [20, 50, 100].includes(requestedPageSize) ? requestedPageSize : 20;
  const sortBy = isWorkspaceInvitationSortField(search.sortBy) ? search.sortBy : undefined;
  const sortDirection = isCollectionSortDirection(search.sortDirection)
    ? search.sortDirection
    : undefined;

  return {
    page,
    pageSize,
    ...(sortBy && sortDirection ? { sortBy, sortDirection } : {}),
  };
}

function isWorkspaceInvitationSortField(value: unknown): value is InvitationTableSortField {
  return (
    value === 'Email' ||
    value === 'Status' ||
    value === 'Role' ||
    value === 'Expires' ||
    value === 'Delivery' ||
    value === 'Revision' ||
    value === 'ModifiedBy' ||
    value === 'ModifiedAt'
  );
}

function isCollectionSortDirection(value: unknown): value is CollectionSortDirection {
  return value === 'Ascending' || value === 'Descending';
}
