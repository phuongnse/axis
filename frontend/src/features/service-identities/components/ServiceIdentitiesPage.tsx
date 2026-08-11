import { useQuery } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useMemo } from 'react';
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
import { PageAction } from '@/components/shared/PageLayout';
import { ResourceWorkspace } from '@/components/shared/ResourceWorkspace';
import { StatusBadge } from '@/components/shared/StatusBadge';
import type { ServiceIdentityDto } from '@/lib/api-generated';
import { serviceIdentitiesQueryOptions } from '../api';
import {
  serviceIdentityCreateWindowDescriptor,
  serviceIdentityWindowDescriptor,
} from '../managed-windows';

export function ServiceIdentitiesPage() {
  const { t } = useTranslation();
  const { openWindow } = useManagedWindowActions();
  const identitiesQuery = useQuery(serviceIdentitiesQueryOptions());
  const identities = identitiesQuery.data ?? [];
  const tableQuery = useMemo<DataTableQueryState>(
    () => ({
      globalFilter: '',
      filterExpression: createEmptyFilterExpression(),
      sorting: [],
      grouping: [],
    }),
    [],
  );
  const tableDefinition = useMemo<DataTableDefinition<ServiceIdentityDto>>(() => {
    const columns: DataTableColumnDef<ServiceIdentityDto>[] = [
      {
        id: 'clientId',
        accessorKey: 'clientId',
        size: 260,
        minSize: 220,
        enableSorting: false,
        meta: { label: t('serviceIdentities.clientId') },
        cell: ({ row }) => {
          const title = row.original.clientId ?? t('serviceIdentities.notAvailable');
          return (
            <PageAction
              type="button"
              variant="link"
              onClick={() => openWindow(serviceIdentityWindowDescriptor(row.original, title))}
            >
              {title}
            </PageAction>
          );
        },
      },
      {
        id: 'subject',
        accessorFn: (identity) => identity.subject?.subjectId,
        size: 280,
        minSize: 240,
        enableSorting: false,
        meta: { label: t('serviceIdentities.subject') },
        cell: ({ row }) => row.original.subject?.subjectId ?? t('serviceIdentities.notAvailable'),
      },
      {
        id: 'status',
        accessorKey: 'status',
        size: 140,
        minSize: 120,
        enableSorting: false,
        meta: { label: t('serviceIdentities.status') },
        cell: ({ row }) => (
          <StatusBadge tone={row.original.status === 'Active' ? 'success' : 'muted'}>
            {row.original.status === 'Active'
              ? t('serviceIdentities.active')
              : t('serviceIdentities.inactive')}
          </StatusBadge>
        ),
      },
      {
        id: 'grant',
        accessorKey: 'workspaceGrantStatus',
        size: 170,
        minSize: 150,
        enableSorting: false,
        meta: { label: t('serviceIdentities.grantStatus') },
      },
      {
        id: 'keys',
        size: 130,
        minSize: 110,
        enableSorting: false,
        meta: { label: t('serviceIdentities.keysTitle') },
        cell: ({ row }) => String(row.original.keys?.length ?? 0),
      },
      {
        id: 'revision',
        accessorKey: 'revision',
        size: 120,
        minSize: 100,
        enableSorting: false,
        meta: { label: t('serviceIdentities.revision') },
      },
    ];

    return {
      ariaLabel: t('serviceIdentities.listTitle'),
      source: {
        mode: 'client',
        data: identities,
        pagination: { pageSize: 20, pageSizeOptions: [20, 50, 100] },
      },
      columns,
      messages: createDataTableMessages(t, {
        searchLabel: t('serviceIdentities.listTitle'),
        searchPlaceholder: t('serviceIdentities.listTitle'),
        emptyTitle: t('serviceIdentities.empty'),
        emptyDescription: t('serviceIdentities.description'),
        errorTitle: t('serviceIdentities.loadFailed'),
        errorDescription: t('serviceIdentities.actionFailedDescription'),
      }),
      getRowId: (identity) => identity.id ?? identity.clientId ?? 'service-identity',
      queryState: tableQuery,
      onQueryStateChange: () => undefined,
      columnControls: true,
      grouping: false,
      renderToolbarActions: identitiesQuery.isSuccess
        ? () => (
            <PageAction
              type="button"
              size="sm"
              onClick={() =>
                openWindow(serviceIdentityCreateWindowDescriptor(t('serviceIdentities.create')))
              }
            >
              <Plus aria-hidden />
              {t('serviceIdentities.create')}
            </PageAction>
          )
        : undefined,
      loading: identitiesQuery.isPending,
      error: identitiesQuery.isError,
      onRetry: () => void identitiesQuery.refetch(),
    };
  }, [
    identities,
    identitiesQuery.isError,
    identitiesQuery.isPending,
    identitiesQuery.isSuccess,
    identitiesQuery.refetch,
    openWindow,
    t,
    tableQuery,
  ]);

  return (
    <ResourceWorkspace
      surfaceId="service-identities"
      title={t('serviceIdentities.title')}
      description={t('serviceIdentities.description')}
    >
      <DataTable definition={tableDefinition} />
    </ResourceWorkspace>
  );
}
