import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useCallback, useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createDataTableMessages,
  DataTable,
  type DataTableColumnDef,
  type DataTableDefinition,
} from '@/components/shared/data-table';
import { useManagedWindowActions } from '@/components/shared/ManagedWindowManager';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import {
  type BusinessObjectDefinitionListItem,
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
  const definitionsQuery = useQuery(businessObjectDefinitionsListQueryOptions(search.page));
  const definitions = definitionsQuery.data?.items ?? [];
  const launchDefinitionQuery = useQuery({
    ...businessObjectDefinitionDetailQueryOptions(search.recordId ?? ''),
    enabled: (search.dialog === 'view' || search.dialog === 'edit') && Boolean(search.recordId),
  });
  const dateFormatter = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }),
    [i18n.language],
  );

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
      openWindow(businessObjectCreateWindowDescriptor(t('businessObjects.defineTitle')));
    } else if (search.recordId) {
      if (launchDefinitionQuery.isLoading) return;
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
    definitions,
    launchDefinitionQuery.data?.name,
    launchDefinitionQuery.isLoading,
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
        size: 320,
        minSize: 220,
        enableSorting: false,
        meta: { label: t('businessObjects.name'), searchable: false },
        cell: ({ row }) => (
          <Button
            type="button"
            variant="link"
            onFocus={() => prefetchDefinition(row.original.id)}
            onMouseEnter={() => prefetchDefinition(row.original.id)}
            onClick={() => openDefinition(row.original)}
          >
            {row.original.name}
          </Button>
        ),
      },
      {
        id: 'key',
        accessorKey: 'objectKey',
        size: 240,
        minSize: 180,
        enableSorting: false,
        meta: { label: t('businessObjects.objectKey'), searchable: false },
      },
      {
        id: 'status',
        accessorKey: 'status',
        size: 160,
        minSize: 140,
        enableSorting: false,
        meta: { label: t('businessObjects.status'), searchable: false },
        cell: ({ row }) => <DefinitionStatusBadge status={row.original.status} />,
      },
      {
        id: 'version',
        accessorKey: 'latestPublishedVersionNumber',
        size: 140,
        minSize: 120,
        enableSorting: false,
        meta: { label: t('businessObjects.version'), searchable: false },
        cell: ({ row }) =>
          row.original.latestPublishedVersionNumber
            ? t('businessObjects.latestVersion', {
                version: row.original.latestPublishedVersionNumber,
              })
            : t('businessObjects.notAvailable'),
      },
      {
        id: 'updated',
        accessorKey: 'updatedAt',
        size: 220,
        minSize: 180,
        enableSorting: false,
        meta: { label: t('businessObjects.updated'), searchable: false },
        cell: ({ row }) =>
          row.original.updatedAt
            ? dateFormatter.format(new Date(row.original.updatedAt))
            : t('businessObjects.notAvailable'),
      },
    ];

    return {
      ariaLabel: t('businessObjects.listTitle'),
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
      globalSearch: false,
      grouping: false,
      columnControls: true,
      enableColumnResizing: true,
      renderToolbarActions: () => (
        <Button
          type="button"
          size="sm"
          onClick={() => {
            openWindow(businessObjectCreateWindowDescriptor(t('businessObjects.defineTitle')));
          }}
        >
          <Plus aria-hidden />
          {t('businessObjects.new')}
        </Button>
      ),
      loading: definitionsQuery.isLoading,
      error: definitionsQuery.isError,
      onRetry: () => void definitionsQuery.refetch(),
    };
  }, [
    dateFormatter,
    definitions,
    definitionsQuery.data?.pageSize,
    definitionsQuery.data?.totalCount,
    definitionsQuery.isError,
    definitionsQuery.isLoading,
    definitionsQuery.refetch,
    navigate,
    openDefinition,
    openWindow,
    prefetchDefinition,
    search.page,
    t,
  ]);

  return (
    <div className="flex h-full min-h-0 w-full min-w-0 flex-col gap-4 overflow-hidden p-4 sm:p-6 lg:p-8">
      <header className="min-w-0 shrink-0">
        <h1 className="font-heading text-2xl font-semibold text-foreground">
          {t('businessObjects.title')}
        </h1>
        <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
          {t('businessObjects.pageDescription')}
        </p>
      </header>

      <div className="min-h-0 flex-1">
        <DataTable definition={tableDefinition} />
      </div>
    </div>
  );
}

function DefinitionStatusBadge({ status }: { status?: 'Unpublished' | 'Published' }) {
  const { t } = useTranslation();
  return status === 'Published' ? (
    <StatusBadge tone="success">{t('businessObjects.published')}</StatusBadge>
  ) : (
    <StatusBadge tone="neutral">{t('businessObjects.unpublished')}</StatusBadge>
  );
}
