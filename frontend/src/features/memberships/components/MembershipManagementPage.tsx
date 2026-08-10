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
} from '@/components/shared/data-table';
import { useManagedWindowActions } from '@/components/shared/ManagedWindowManager';
import { PageAction, PageHeader, PageLayout } from '@/components/shared/PageLayout';
import { StatusBadge, type StatusBadgeTone } from '@/components/shared/StatusBadge';
import type { WorkspaceInvitationLifecycleDto } from '@/lib/api-generated';
import { workspaceInvitationsQueryOptions } from '../api';
import {
  membershipInvitationWindowDescriptor,
  membershipInviteWindowDescriptor,
} from '../managed-windows';

export function MembershipManagementPage() {
  const { t, i18n } = useTranslation();
  const { openWindow } = useManagedWindowActions();
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
  const tableQuery = useMemo<DataTableQueryState>(
    () => ({
      globalFilter: '',
      filterExpression: createEmptyFilterExpression(),
      sorting: [],
      grouping: [],
    }),
    [],
  );
  const tableDefinition = useMemo<DataTableDefinition<WorkspaceInvitationLifecycleDto>>(() => {
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
            <PageAction
              type="button"
              variant="link"
              onClick={() => openWindow(membershipInvitationWindowDescriptor(row.original, email))}
            >
              {email}
            </PageAction>
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
          <StatusBadge tone={statusTone(row.original.status)}>
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
          <StatusBadge tone={deliveryTone(row.original.deliveryStatus)}>
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
      queryState: tableQuery,
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
    tableQuery,
  ]);

  return (
    <PageLayout scrollMode="contained">
      <PageHeader title={t('memberships.title')} description={t('memberships.description')} />
      <div className="min-h-0 flex-1">
        <DataTable definition={tableDefinition} />
      </div>
    </PageLayout>
  );
}

function statusTone(status: string | undefined): StatusBadgeTone {
  if (status === 'Accepted') return 'success';
  if (status === 'Pending') return 'info';
  return 'muted';
}

function deliveryTone(status: string | undefined): StatusBadgeTone {
  if (status === 'Delivered') return 'success';
  if (status === 'Pending') return 'info';
  return 'muted';
}
