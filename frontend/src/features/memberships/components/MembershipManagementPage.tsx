import { useQuery } from '@tanstack/react-query';
import { MailPlus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createDataTableMessages,
  createEmptyFilterExpression,
  DataTable,
  type DataTableColumnDef,
  type DataTableDefinition,
  type DataTableQueryState,
  DataTableRecordAction,
} from '@/components/shared/data-table';
import { useManagedWindowActions } from '@/components/shared/ManagedWindowManager';
import { PageAction } from '@/components/shared/PageLayout';
import { ResourceWorkspace } from '@/components/shared/ResourceWorkspace';
import { StatusBadge, type StatusBadgeState } from '@/components/shared/StatusBadge';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import type {
  WorkspaceInvitationLifecycleDto,
  WorkspaceProductBuilderDto,
} from '@/lib/api-generated';
import { workspaceInvitationsQueryOptions, workspaceProductBuildersQueryOptions } from '../api';
import {
  membershipInvitationWindowDescriptor,
  membershipInviteWindowDescriptor,
  membershipProductBuilderWindowDescriptor,
} from '../managed-windows';

export function MembershipManagementPage() {
  const { t, i18n } = useTranslation();
  const { openWindow } = useManagedWindowActions();
  const productBuildersQuery = useQuery(workspaceProductBuildersQueryOptions());
  const members = productBuildersQuery.data ?? [];
  const [invitationPagination, setInvitationPagination] = useState({ pageIndex: 0, pageSize: 20 });
  const invitationsQuery = useQuery(
    workspaceInvitationsQueryOptions(
      invitationPagination.pageIndex + 1,
      invitationPagination.pageSize,
    ),
  );
  const invitations = invitationsQuery.data?.items ?? [];
  const dateFormatter = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }),
    [i18n.language],
  );
  const memberTableQuery = useMemo<DataTableQueryState>(
    () => ({
      globalFilter: '',
      filterExpression: createEmptyFilterExpression(),
      sorting: [],
      grouping: [],
    }),
    [],
  );
  const invitationTableQuery = useMemo<DataTableQueryState>(
    () => ({
      globalFilter: '',
      filterExpression: createEmptyFilterExpression(),
      sorting: [],
      grouping: [],
    }),
    [],
  );
  const memberTableDefinition = useMemo<DataTableDefinition<WorkspaceProductBuilderDto>>(() => {
    const columns: DataTableColumnDef<WorkspaceProductBuilderDto>[] = [
      {
        id: 'member',
        accessorKey: 'displayName',
        size: 260,
        minSize: 220,
        enableSorting: false,
        meta: { label: t('memberships.productBuilderMember') },
        cell: ({ row }) => {
          const title = row.original.displayName ?? t('memberships.productBuilderUnknownMember');
          return (
            <DataTableRecordAction
              onClick={() =>
                openWindow(membershipProductBuilderWindowDescriptor(row.original, title))
              }
            >
              {title}
            </DataTableRecordAction>
          );
        },
      },
      {
        id: 'email',
        accessorKey: 'email',
        size: 260,
        minSize: 220,
        enableSorting: false,
        meta: { label: t('memberships.email') },
        cell: ({ row }) => row.original.email ?? t('memberships.notAvailable'),
      },
      {
        id: 'role',
        accessorKey: 'workspaceRole',
        size: 190,
        minSize: 170,
        enableSorting: false,
        meta: { label: t('memberships.role') },
        cell: ({ row }) => t(`memberships.role${row.original.workspaceRole ?? 'Member'}`),
      },
      {
        id: 'productBuilder',
        accessorKey: 'isProductBuilder',
        size: 170,
        minSize: 150,
        enableSorting: false,
        meta: { label: t('memberships.productBuilder') },
        cell: ({ row }) => (
          <StatusBadge state={row.original.isProductBuilder ? 'positive' : 'inactive'}>
            {row.original.isProductBuilder
              ? t('memberships.productBuilderActive')
              : t('memberships.productBuilderInactive')}
          </StatusBadge>
        ),
      },
    ];

    return {
      ariaLabel: t('memberships.productBuilderTableLabel'),
      source: {
        mode: 'client',
        data: members,
        pagination: { pageSize: 20, pageSizeOptions: [20, 50, 100] },
      },
      columns,
      messages: createDataTableMessages(t, {
        searchLabel: t('memberships.productBuilderTableLabel'),
        searchPlaceholder: t('memberships.productBuilderSearch'),
        emptyTitle: t('memberships.productBuilderEmpty'),
        emptyDescription: t('memberships.productBuilderEmptyDescription'),
        errorTitle: t('memberships.productBuilderLoadFailed'),
        errorDescription: t('memberships.productBuilderLoadFailedDescription'),
      }),
      getRowId: (member) => member.userId ?? member.email ?? 'workspace-member',
      queryState: memberTableQuery,
      onQueryStateChange: () => undefined,
      columnControls: true,
      grouping: false,
      loading: productBuildersQuery.isPending,
      error: productBuildersQuery.isError,
      onRetry: () => void productBuildersQuery.refetch(),
    };
  }, [
    memberTableQuery,
    members,
    openWindow,
    productBuildersQuery.isError,
    productBuildersQuery.isPending,
    productBuildersQuery.refetch,
    t,
  ]);
  const invitationTableDefinition = useMemo<
    DataTableDefinition<WorkspaceInvitationLifecycleDto>
  >(() => {
    const columns: DataTableColumnDef<WorkspaceInvitationLifecycleDto>[] = [
      {
        id: 'email',
        accessorKey: 'recipientEmail',
        size: 260,
        minSize: 220,
        enableSorting: false,
        meta: { label: t('memberships.email') },
        cell: ({ row }) => {
          const email = row.original.recipientEmail ?? t('memberships.recipientRemoved');
          return row.original.invitationId ? (
            <DataTableRecordAction
              onClick={() => openWindow(membershipInvitationWindowDescriptor(row.original, email))}
            >
              {email}
            </DataTableRecordAction>
          ) : (
            email
          );
        },
      },
      {
        id: 'role',
        accessorKey: 'requestedRole',
        size: 190,
        minSize: 170,
        enableSorting: false,
        meta: { label: t('memberships.role') },
        cell: ({ row }) => t(`memberships.role${row.original.requestedRole ?? 'Member'}`),
      },
      {
        id: 'status',
        accessorKey: 'status',
        size: 140,
        minSize: 130,
        enableSorting: false,
        meta: { label: t('memberships.status') },
        cell: ({ row }) => (
          <StatusBadge state={invitationState(row.original.status)}>
            {t(`memberships.status${row.original.status ?? 'Pending'}`)}
          </StatusBadge>
        ),
      },
      {
        id: 'delivery',
        accessorKey: 'deliveryStatus',
        size: 150,
        minSize: 140,
        enableSorting: false,
        meta: { label: t('memberships.delivery') },
        cell: ({ row }) => (
          <StatusBadge state={deliveryState(row.original.deliveryStatus)}>
            {t(`memberships.delivery${row.original.deliveryStatus ?? 'Pending'}`)}
          </StatusBadge>
        ),
      },
      {
        id: 'expires',
        accessorKey: 'expiresAt',
        size: 210,
        minSize: 190,
        enableSorting: false,
        meta: { label: t('memberships.expires') },
        cell: ({ row }) =>
          row.original.expiresAt
            ? dateFormatter.format(new Date(row.original.expiresAt))
            : t('memberships.notAvailable'),
      },
    ];

    return {
      ariaLabel: t('memberships.tableLabel'),
      source: {
        mode: 'page',
        data: invitations,
        pagination: invitationPagination,
        rowCount: invitationsQuery.data?.totalCount ?? 0,
        pageSizeOptions: [20, 50, 100],
        onPaginationChange: setInvitationPagination,
      },
      columns,
      messages: createDataTableMessages(t, {
        searchLabel: t('memberships.tableLabel'),
        searchPlaceholder: t('memberships.tableLabel'),
        emptyTitle: t('memberships.empty'),
        emptyDescription: t('memberships.description'),
        errorTitle: t('memberships.loadFailed'),
        errorDescription: t('memberships.loadFailedDescription'),
      }),
      getRowId: (invitation) =>
        invitation.invitationId ?? invitation.recipientEmail ?? 'workspace-invitation',
      queryState: invitationTableQuery,
      onQueryStateChange: () => undefined,
      columnControls: true,
      grouping: false,
      renderToolbarActions: invitationsQuery.isSuccess
        ? () => (
            <PageAction
              type="button"
              size="sm"
              onClick={() => openWindow(membershipInviteWindowDescriptor(t('memberships.invite')))}
            >
              <MailPlus aria-hidden />
              {t('memberships.invite')}
            </PageAction>
          )
        : undefined,
      loading: invitationsQuery.isPending,
      error: invitationsQuery.isError,
      onRetry: () => void invitationsQuery.refetch(),
    };
  }, [
    dateFormatter,
    invitationPagination,
    invitations,
    invitationsQuery.data?.totalCount,
    invitationsQuery.isError,
    invitationsQuery.isPending,
    invitationsQuery.isSuccess,
    invitationsQuery.refetch,
    openWindow,
    t,
    invitationTableQuery,
  ]);

  return (
    <ResourceWorkspace
      surfaceId="membership-management"
      title={t('memberships.title')}
      description={t('memberships.description')}
    >
      <Tabs defaultValue="invitations" className="h-full min-h-0">
        <TabsList variant="line" aria-label={t('memberships.sections')}>
          <TabsTrigger value="members">{t('memberships.membersTab')}</TabsTrigger>
          <TabsTrigger value="invitations">{t('memberships.invitationsTab')}</TabsTrigger>
        </TabsList>
        <TabsContent value="members" className="min-h-0">
          <DataTable definition={memberTableDefinition} />
        </TabsContent>
        <TabsContent value="invitations" className="min-h-0">
          <DataTable definition={invitationTableDefinition} />
        </TabsContent>
      </Tabs>
    </ResourceWorkspace>
  );
}

function invitationState(status: string | undefined): StatusBadgeState {
  if (status === 'Accepted') return 'positive';
  if (status === 'Pending') return 'informative';
  if (status === 'Expired') return 'caution';
  if (status === 'Revoked') return 'inactive';
  return 'neutral';
}

function deliveryState(status: string | undefined): StatusBadgeState {
  if (status === 'Delivered') return 'positive';
  if (status === 'Pending') return 'informative';
  if (status === 'Failed') return 'critical';
  return 'neutral';
}
