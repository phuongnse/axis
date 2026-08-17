import { useQuery } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import { Plus } from 'lucide-react';
import { useEffect, useMemo } from 'react';
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
import { StatusBadge, type StatusBadgeState } from '@/components/shared/StatusBadge';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { ApiError } from '@/lib/api';
import { referenceContent } from '@/lib/reference-metadata';
import {
  type RuleDefinitionSortField,
  type RuleDefinitionSummary,
  ruleDefinitionCollectionActionsQueryOptions,
  ruleDefinitionsListQueryOptions,
} from '../api';
import { ruleCreateWindowDescriptor, ruleDefinitionWindowDescriptor } from '../managed-windows';
import { RuleOriginBadge } from './RuleOriginBadge';

const route = getRouteApi('/_authenticated/rules');

export function RulesPage() {
  const { t, i18n } = useTranslation();
  const search = route.useSearch();
  const page = search.page ?? 1;
  const navigate = route.useNavigate();
  const { openWindow } = useManagedWindowActions();
  const debouncedSearch = useDebouncedValue(search.query ?? '');
  const definitionsQuery = useQuery(
    ruleDefinitionsListQueryOptions({
      page,
      pageSize: 20,
      query: debouncedSearch,
      language: i18n.language,
      sortBy: search.sortBy,
      sortDirection: search.sortDirection,
    }),
  );
  const collectionActionsQuery = useQuery(ruleDefinitionCollectionActionsQueryOptions());
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
          ? [{ id: ruleColumnId(search.sortBy), desc: search.sortDirection === 'Descending' }]
          : [],
      grouping: [],
    }),
    [search.query, search.sortBy, search.sortDirection],
  );
  const selectedDefinition = definitions.find(
    (definition) => definition.definitionKey === search.definitionKey,
  );

  useEffect(() => {
    if (!search.dialog) return;
    if (search.dialog === 'create') {
      if (collectionActionsQuery.isPending || collectionActionsQuery.isError) return;
      if (canStartCreate) openWindow(ruleCreateWindowDescriptor(t('rules.createTitle')));
      void navigate({
        replace: true,
        search: (current) => ({ ...current, dialog: undefined, definitionKey: undefined }),
      });
      return;
    }
    if (!search.definitionKey || definitionsQuery.isPending) return;
    const definition =
      selectedDefinition ??
      ({ definitionKey: search.definitionKey, origin: 'Workspace' } as RuleDefinitionSummary);
    const descriptor = ruleDefinitionWindowDescriptor(
      definition,
      selectedDefinition
        ? localizedRuleName(selectedDefinition, i18n.language, t)
        : search.definitionKey,
    );
    if (descriptor) openWindow(descriptor);
    void navigate({
      replace: true,
      search: (current) => ({ ...current, dialog: undefined, definitionKey: undefined }),
    });
  }, [
    canStartCreate,
    collectionActionsQuery.isError,
    collectionActionsQuery.isPending,
    definitionsQuery.isPending,
    navigate,
    openWindow,
    search.definitionKey,
    search.dialog,
    selectedDefinition,
    i18n.language,
    t,
  ]);
  const tableDefinition = useMemo<DataTableDefinition<RuleDefinitionSummary>>(() => {
    const columns: DataTableColumnDef<RuleDefinitionSummary>[] = [
      {
        id: 'rule',
        accessorFn: (definition) => localizedRuleName(definition, i18n.language, t),
        size: 330,
        minSize: 280,
        enableGrouping: false,
        meta: {
          label: t('rules.ruleColumn'),
          cell: { kind: 'action' },
        },
        cell: ({ row }) => {
          const name = localizedRuleName(row.original, i18n.language, t);
          if (!row.original.definitionKey) {
            return <span className="truncate font-medium text-foreground">{name}</span>;
          }
          return (
            <DataTableRecordAction
              onClick={() => {
                const descriptor = ruleDefinitionWindowDescriptor(row.original, name);
                if (descriptor) openWindow(descriptor);
              }}
            >
              {name}
            </DataTableRecordAction>
          );
        },
      },
      {
        id: 'inputs',
        accessorFn: (definition) => (definition.inputs ?? []).map((input) => input.label ?? ''),
        size: 220,
        minSize: 200,
        enableSorting: false,
        enableGrouping: false,
        meta: { label: t('rules.inputs'), cell: { kind: 'list' } },
      },
      {
        id: 'origin',
        accessorFn: (definition) => definition.origin,
        size: 130,
        minSize: 120,
        enableGrouping: false,
        meta: { label: t('rules.origin'), cell: { kind: 'status' } },
        cell: ({ row }) => <RuleOriginCell definition={row.original} />,
      },
      {
        id: 'status',
        accessorFn: (definition) => definition.status,
        size: 130,
        minSize: 120,
        enableGrouping: false,
        meta: { label: t('rules.status'), cell: { kind: 'status' } },
        cell: ({ row }) => <RuleStatusCell definition={row.original} />,
      },
      {
        id: 'activeVersion',
        accessorKey: 'activeVersion',
        size: 150,
        minSize: 140,
        enableGrouping: false,
        meta: {
          label: t('rules.activeVersion'),
          cell: { kind: 'version' },
          searchable: false,
        },
      },
      {
        id: 'latestVersion',
        accessorKey: 'latestVersion',
        size: 150,
        minSize: 140,
        enableGrouping: false,
        meta: {
          label: t('rules.latestVersion'),
          cell: { kind: 'version' },
          searchable: false,
        },
      },
      ...createResourceMetadataColumns<RuleDefinitionSummary>({
        revision: t('metadata.revision'),
        createdBy: t('metadata.createdBy'),
        createdAt: t('metadata.createdAt'),
        modifiedBy: t('metadata.modifiedBy'),
        modifiedAt: t('metadata.modifiedAt'),
      }),
    ];

    return {
      ariaLabel: t('rules.catalogTitle'),
      locale: i18n.language,
      source: {
        mode: 'page',
        data: definitions,
        pagination: {
          pageIndex: page - 1,
          pageSize: definitionsQuery.data?.pageSize ?? 20,
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
        searchLabel: t('rules.searchLabel'),
        searchPlaceholder: t('rules.searchPlaceholder'),
        emptyTitle: t('rules.emptyTitle'),
        emptyDescription: t('rules.emptyDescription'),
        errorTitle: t('rules.loadErrorTitle'),
        errorDescription: t('rules.loadErrorBody'),
      }),
      getRowId: (definition) =>
        definition.definitionKey ??
        `${definition.origin ?? 'Unknown'}:${definition.name ?? definition.definitionKey ?? 'rule'}`,
      queryState: tableQuery,
      onQueryStateChange: (next) => {
        const sort = next.sorting[0];
        const sortBy = ruleSortField(sort?.id);
        void navigate({
          replace: true,
          search: (current) => ({
            ...current,
            page: 1,
            query: next.globalFilter.trim() || undefined,
            sortBy,
            sortDirection: sortBy ? (sort?.desc ? 'Descending' : 'Ascending') : undefined,
          }),
        });
      },
      enableColumnResizing: true,
      globalSearch: true,
      columnControls: true,
      grouping: false,
      renderToolbarActions: canStartCreate
        ? () => (
            <PageAction
              type="button"
              size="sm"
              onClick={() => openWindow(ruleCreateWindowDescriptor(t('rules.createTitle')))}
            >
              <Plus aria-hidden />
              {t('rules.newRule')}
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
    definitionsQuery.isError,
    definitionsQuery.isPending,
    definitionsQuery.refetch,
    definitionsQuery.data?.pageSize,
    definitionsQuery.data?.totalCount,
    i18n.language,
    navigate,
    openWindow,
    page,
    tableQuery,
    t,
  ]);

  return (
    <ResourceWorkspace
      surfaceId="rule-definitions"
      title={t('rules.title')}
      description={t('rules.pageDescription')}
      status={
        actionsUnavailable ? (
          <StatusNotice tone="warning" title={t('rules.actionsUnavailableTitle')}>
            <span>{t('rules.actionsUnavailableDescription')}</span>{' '}
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

function ruleSortField(columnId: string | undefined): RuleDefinitionSortField | undefined {
  if (columnId === 'rule') return 'Name';
  if (columnId === 'origin') return 'Origin';
  if (columnId === 'status') return 'Status';
  if (columnId === 'activeVersion') return 'ActiveVersion';
  if (columnId === 'latestVersion') return 'LatestVersion';
  if (columnId === 'revision') return 'Revision';
  if (columnId === 'createdBy') return 'CreatedBy';
  if (columnId === 'createdAt') return 'CreatedAt';
  if (columnId === 'modifiedBy') return 'ModifiedBy';
  if (columnId === 'modifiedAt') return 'ModifiedAt';
  return undefined;
}

function ruleColumnId(sortBy: RuleDefinitionSortField): string {
  if (sortBy === 'Name') return 'rule';
  return `${sortBy[0].toLowerCase()}${sortBy.slice(1)}`;
}

function RuleOriginCell({ definition }: { definition: RuleDefinitionSummary }) {
  const { t } = useTranslation();
  return definition.origin ? (
    <RuleOriginBadge data-slot="rule-table-value" origin={definition.origin} />
  ) : (
    <span data-slot="rule-table-value">{t('table.emptyValue')}</span>
  );
}

function RuleStatusCell({ definition }: { definition: RuleDefinitionSummary }) {
  const { t } = useTranslation();
  if (!definition.status) {
    return <span data-slot="rule-table-value">{t('table.emptyValue')}</span>;
  }
  const label = t(`rules.status${definition.status}`);
  const state: StatusBadgeState =
    definition.status === 'Active'
      ? 'positive'
      : definition.status === 'Draft'
        ? 'neutral'
        : 'inactive';
  return (
    <StatusBadge data-slot="rule-table-value" state={state}>
      {label}
    </StatusBadge>
  );
}

function localizedRuleName(
  definition: RuleDefinitionSummary,
  locale: string,
  t: TFunction,
): string {
  return (
    referenceContent(definition.documentation, locale)?.displayName ??
    definition.name ??
    t('rules.unknownRule')
  );
}
