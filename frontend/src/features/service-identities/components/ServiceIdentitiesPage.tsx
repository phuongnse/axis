import { useQuery } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createDataTableMessages,
  createEmptyFilterExpression,
  createResourceMetadataColumns,
  DataTable,
  type DataTableColumnDef,
  type DataTableDefinition,
  type DataTableQueryState,
  DataTableRecordAction,
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
  const { t, i18n } = useTranslation();
  const { openWindow } = useManagedWindowActions();
  const identitiesQuery = useQuery(serviceIdentitiesQueryOptions());
  const identities = identitiesQuery.data ?? [];
  const [tableQuery, setTableQuery] = useState<DataTableQueryState>(() => ({
    globalFilter: '',
    filterExpression: createEmptyFilterExpression(),
    sorting: [],
    grouping: [],
  }));
  const tableDefinition = useMemo<DataTableDefinition<ServiceIdentityDto>>(() => {
    const columns: DataTableColumnDef<ServiceIdentityDto>[] = [
      {
        id: 'clientId',
        accessorKey: 'clientId',
        size: 260,
        minSize: 220,
        meta: { label: t('serviceIdentities.clientId'), cell: { kind: 'action' } },
        cell: ({ row }) => {
          const title = row.original.clientId ?? t('serviceIdentities.notAvailable');
          return (
            <DataTableRecordAction
              onClick={() => openWindow(serviceIdentityWindowDescriptor(row.original, title))}
            >
              {title}
            </DataTableRecordAction>
          );
        },
      },
      {
        id: 'subject',
        accessorFn: (identity) => identity.subject?.subjectId,
        size: 280,
        minSize: 240,
        meta: { label: t('serviceIdentities.subject'), cell: { kind: 'identifier' } },
      },
      {
        id: 'status',
        accessorKey: 'status',
        size: 140,
        minSize: 120,
        meta: { label: t('serviceIdentities.status'), cell: { kind: 'status' } },
        cell: ({ row }) => (
          <StatusBadge state={row.original.status === 'Active' ? 'positive' : 'inactive'}>
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
        meta: { label: t('serviceIdentities.grantStatus'), cell: { kind: 'status' } },
      },
      {
        id: 'keys',
        accessorFn: (identity) => identity.keys?.length ?? 0,
        size: 130,
        minSize: 110,
        meta: { label: t('serviceIdentities.keysTitle'), cell: { kind: 'number' } },
      },
      ...createResourceMetadataColumns<ServiceIdentityDto>(
        {
          revision: t('metadata.revision'),
          createdBy: t('metadata.createdBy'),
          createdAt: t('metadata.createdAt'),
          modifiedBy: t('metadata.modifiedBy'),
          modifiedAt: t('metadata.modifiedAt'),
        },
        { locale: i18n.language },
      ),
    ];

    return {
      ariaLabel: t('serviceIdentities.listTitle'),
      locale: i18n.language,
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
      onQueryStateChange: setTableQuery,
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
    i18n.language,
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
