import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useCallback, useEffect, useMemo } from 'react';
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
import { StatusNotice } from '@/components/shared/StatusNotice';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { ApiError } from '@/lib/api';
import {
  type BusinessObjectDefinitionListItem,
  businessObjectDefinitionCollectionActionsQueryOptions,
  businessObjectDefinitionDetailQueryOptions,
  businessObjectDefinitionsDefaultPageSize,
  businessObjectDefinitionsListQueryOptions,
} from '../api';
import {
  businessObjectCreateWindowDescriptor,
  businessObjectDefinitionWindowDescriptor,
} from '../managed-windows';

const route = getRouteApi('/_authenticated/business-objects');

export function BusinessObjectsPage() {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const { openWindow } = useManagedWindowActions();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const debouncedSearch = useDebouncedValue(search.query ?? '');
  const definitionsQuery = useQuery(
    businessObjectDefinitionsListQueryOptions(
      search.page,
      businessObjectDefinitionsDefaultPageSize,
      debouncedSearch,
      i18n.language,
      search.sortBy,
      search.sortDirection,
    ),
  );
  const collectionActionsQuery = useQuery(businessObjectDefinitionCollectionActionsQueryOptions());
  const canStartCreate = collectionActionsQuery.data?.canStartCreate === true;
  const actionsUnavailable =
    collectionActionsQuery.error instanceof ApiError && collectionActionsQuery.error.status === 503;
  const definitions = definitionsQuery.data?.items ?? [];
  const tableQuery = useMemo<DataTableQueryState>(
    () => ({
      globalFilter: search.query ?? '',
      filterExpression: createEmptyFilterExpression(),
      sorting:
        search.sortBy && search.sortDirection
          ? [
              {
                id: businessObjectColumnId(search.sortBy),
                desc: search.sortDirection === 'Descending',
              },
            ]
          : [],
      grouping: [],
    }),
    [search.query, search.sortBy, search.sortDirection],
  );
  const launchDefinitionQuery = useQuery({
    ...businessObjectDefinitionDetailQueryOptions(search.recordId ?? ''),
    enabled: (search.dialog === 'view' || search.dialog === 'edit') && Boolean(search.recordId),
  });
  const openDefinition = useCallback(
    (definition: BusinessObjectDefinitionListItem) => {
      if (!definition.id) return;
      openWindow(
        businessObjectDefinitionWindowDescriptor({
          recordId: definition.id,
          mode: definition.status === 'Published' ? 'view' : 'edit',
          title: definition.name ?? t('businessObjects.definitionTitle'),
        }),
      );
    },
    [openWindow, t],
  );

  useEffect(() => {
    if (!search.dialog) return;
    if (search.dialog === 'create') {
      if (collectionActionsQuery.isPending || collectionActionsQuery.isError) return;
      if (canStartCreate) {
        openWindow(businessObjectCreateWindowDescriptor(t('businessObjects.defineTitle')));
      }
    } else if (search.recordId) {
      if (launchDefinitionQuery.isPending) return;
      const definition = definitions.find((candidate) => candidate.id === search.recordId);
      openWindow(
        businessObjectDefinitionWindowDescriptor({
          recordId: search.recordId,
          mode: search.dialog,
          title:
            launchDefinitionQuery.data?.name ??
            definition?.name ??
            search.recordId ??
            t('businessObjects.definitionTitle'),
        }),
      );
    } else {
      return;
    }
    void navigate({
      replace: true,
      search: (current) => ({ ...current, dialog: undefined, recordId: undefined }),
    });
  }, [
    canStartCreate,
    collectionActionsQuery.isError,
    collectionActionsQuery.isPending,
    definitions,
    launchDefinitionQuery.data?.name,
    launchDefinitionQuery.isPending,
    navigate,
    openWindow,
    search.dialog,
    search.recordId,
    t,
  ]);

  const prefetchDefinition = useCallback(
    (id: string | undefined) => {
      if (!id) return;
      void queryClient.prefetchQuery(businessObjectDefinitionDetailQueryOptions(id));
    },
    [queryClient],
  );

  const tableDefinition = useMemo<DataTableDefinition<BusinessObjectDefinitionListItem>>(() => {
    const columns: DataTableColumnDef<BusinessObjectDefinitionListItem>[] = [
      {
        id: 'name',
        accessorKey: 'name',
        size: 280,
        minSize: 220,
        meta: { label: t('businessObjects.name'), cell: { kind: 'action' } },
        cell: ({ row }) => (
          <DataTableRecordAction
            onFocus={() => prefetchDefinition(row.original.id)}
            onMouseEnter={() => prefetchDefinition(row.original.id)}
            onClick={() => openDefinition(row.original)}
          >
            {row.original.name}
          </DataTableRecordAction>
        ),
      },
      {
        id: 'key',
        accessorKey: 'objectKey',
        size: 200,
        minSize: 180,
        meta: { label: t('businessObjects.objectKey'), cell: { kind: 'identifier' } },
      },
      {
        id: 'status',
        accessorKey: 'status',
        size: 150,
        minSize: 140,
        meta: {
          label: t('businessObjects.status'),
          cell: { kind: 'status' },
          searchable: false,
        },
        cell: ({ row }) => <DefinitionStatusBadge status={row.original.status} />,
      },
      {
        id: 'version',
        accessorKey: 'latestPublishedVersionNumber',
        size: 130,
        minSize: 120,
        meta: {
          label: t('businessObjects.version'),
          cell: { kind: 'version' },
          searchable: false,
        },
      },
      ...createResourceMetadataColumns<BusinessObjectDefinitionListItem>({
        revision: t('metadata.revision'),
        createdBy: t('metadata.createdBy'),
        createdAt: t('metadata.createdAt'),
        modifiedBy: t('metadata.modifiedBy'),
        modifiedAt: t('metadata.modifiedAt'),
      }),
    ];

    return {
      ariaLabel: t('businessObjects.listTitle'),
      locale: i18n.language,
      source: {
        mode: 'page',
        data: definitions,
        pagination: {
          pageIndex: search.page - 1,
          pageSize: definitionsQuery.data?.pageSize ?? businessObjectDefinitionsDefaultPageSize,
        },
        rowCount: definitionsQuery.data?.totalCount ?? 0,
        onPaginationChange: (pagination) => {
          void navigate({
            search: (current) => ({ ...current, page: pagination.pageIndex + 1 }),
          });
        },
      },
      columns,
      messages: createDataTableMessages(t, {
        searchLabel: t('businessObjects.searchLabel'),
        searchPlaceholder: t('businessObjects.searchPlaceholder'),
        emptyTitle: t('businessObjects.emptyTitle'),
        emptyDescription: t('businessObjects.emptyDescription'),
        errorTitle: t('businessObjects.loadError'),
        errorDescription: t('businessObjects.loadErrorDescription'),
      }),
      getRowId: (definition) =>
        definition.id ?? definition.objectKey ?? definition.name ?? 'definition',
      queryState: tableQuery,
      onQueryStateChange: (next) => {
        const sort = next.sorting[0];
        const sortBy = sort ? businessObjectSortField(sort.id) : undefined;
        void navigate({
          search: (current) => ({
            ...current,
            page: 1,
            query: next.globalFilter.trim() || undefined,
            sortBy,
            sortDirection: sortBy ? (sort?.desc ? 'Descending' : 'Ascending') : undefined,
          }),
        });
      },
      globalSearch: true,
      grouping: false,
      columnControls: true,
      enableColumnResizing: true,
      renderToolbarActions: canStartCreate
        ? () => (
            <PageAction
              type="button"
              size="sm"
              onClick={() => {
                openWindow(businessObjectCreateWindowDescriptor(t('businessObjects.defineTitle')));
              }}
            >
              <Plus aria-hidden />
              {t('businessObjects.new')}
            </PageAction>
          )
        : undefined,
      loading: definitionsQuery.isPending,
      error: definitionsQuery.isError,
      onRetry: () => void definitionsQuery.refetch(),
    };
  }, [
    canStartCreate,
    definitions,
    definitionsQuery.data?.pageSize,
    definitionsQuery.data?.totalCount,
    definitionsQuery.isError,
    definitionsQuery.isPending,
    definitionsQuery.refetch,
    i18n.language,
    navigate,
    openDefinition,
    openWindow,
    prefetchDefinition,
    search.page,
    tableQuery,
    t,
  ]);

  return (
    <ResourceWorkspace
      surfaceId="business-object-definitions"
      title={t('businessObjects.title')}
      description={t('businessObjects.pageDescription')}
      status={
        actionsUnavailable ? (
          <StatusNotice tone="warning" title={t('businessObjects.actionsUnavailableTitle')}>
            <span>{t('businessObjects.actionsUnavailableDescription')}</span>{' '}
            <PageAction
              type="button"
              variant="link"
              disabled={collectionActionsQuery.isFetching}
              onClick={() => void collectionActionsQuery.refetch()}
            >
              {t('app.retry')}
            </PageAction>
          </StatusNotice>
        ) : undefined
      }
    >
      <DataTable definition={tableDefinition} />
    </ResourceWorkspace>
  );
}

function DefinitionStatusBadge({ status }: { status?: 'Unpublished' | 'Published' }) {
  const { t } = useTranslation();
  return status === 'Published' ? (
    <StatusBadge state="positive">{t('businessObjects.published')}</StatusBadge>
  ) : (
    <StatusBadge state="neutral">{t('businessObjects.unpublished')}</StatusBadge>
  );
}

function businessObjectColumnId(
  sortBy:
    | 'Name'
    | 'Key'
    | 'Status'
    | 'Version'
    | 'Revision'
    | 'CreatedBy'
    | 'CreatedAt'
    | 'ModifiedBy'
    | 'ModifiedAt',
) {
  return `${sortBy[0].toLowerCase()}${sortBy.slice(1)}`;
}

function businessObjectSortField(
  columnId: string,
):
  | 'Name'
  | 'Key'
  | 'Status'
  | 'Version'
  | 'Revision'
  | 'CreatedBy'
  | 'CreatedAt'
  | 'ModifiedBy'
  | 'ModifiedAt'
  | undefined {
  if (columnId === 'name') return 'Name';
  if (columnId === 'key') return 'Key';
  if (columnId === 'status') return 'Status';
  if (columnId === 'version') return 'Version';
  if (columnId === 'revision') return 'Revision';
  if (columnId === 'createdBy') return 'CreatedBy';
  if (columnId === 'createdAt') return 'CreatedAt';
  if (columnId === 'modifiedBy') return 'ModifiedBy';
  if (columnId === 'modifiedAt') return 'ModifiedAt';
  return undefined;
}
