import { useQuery } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import { Plus } from 'lucide-react';
import { useEffect, useMemo } from 'react';
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
import { StatusNotice } from '@/components/shared/StatusNotice';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { ApiError } from '@/lib/api';
import { referenceContent } from '@/lib/reference-metadata';
import {
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
      sorting: [],
      grouping: [],
    }),
    [search.query],
  );
  const selectedDefinition = definitions.find(
    (definition) => definition.definitionKey === search.definitionKey,
  );

  useEffect(() => {
    if (!search.dialog) return;
    if (search.dialog === 'create') {
      if (collectionActionsQuery.isPending || collectionActionsQuery.isError) return;
      if (canStartCreate) openWindow(ruleCreateWindowDescriptor(t('rules.createTitle')));
      void navigate({ replace: true, search: {} });
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
    void navigate({ replace: true, search: {} });
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
        enableSorting: false,
        enableGrouping: false,
        meta: {
          label: t('rules.ruleColumn'),
        },
        cell: ({ row }) => (
          <RuleIdentityCell
            definition={row.original}
            onOpen={
              row.original.definitionKey
                ? () => {
                    const descriptor = ruleDefinitionWindowDescriptor(
                      row.original,
                      localizedRuleName(row.original, i18n.language, t),
                    );
                    if (descriptor) openWindow(descriptor);
                  }
                : undefined
            }
          />
        ),
      },
      {
        id: 'inputs',
        accessorFn: (definition) => (definition.inputs ?? []).map((input) => input.label ?? ''),
        size: 220,
        minSize: 200,
        enableSorting: false,
        enableGrouping: false,
        meta: { label: t('rules.inputs') },
        cell: ({ row }) => <RuleInputsCell definition={row.original} />,
      },
      {
        id: 'origin',
        accessorFn: (definition) => definition.origin,
        size: 130,
        minSize: 120,
        enableSorting: false,
        enableGrouping: false,
        meta: { label: t('rules.origin') },
        cell: ({ row }) => <RuleOriginCell definition={row.original} />,
      },
      {
        id: 'status',
        accessorFn: (definition) => definition.status,
        size: 130,
        minSize: 120,
        enableSorting: false,
        enableGrouping: false,
        meta: { label: t('rules.status') },
        cell: ({ row }) => <RuleStatusCell definition={row.original} />,
      },
    ];

    return {
      ariaLabel: t('rules.catalogTitle'),
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
        void navigate({
          replace: true,
          search: (current) => ({
            ...current,
            page: 1,
            query: next.globalFilter.trim() || undefined,
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
    <PageLayout scrollMode="contained">
      <PageHeader title={t('rules.title')} description={t('rules.pageDescription')} />

      {actionsUnavailable ? (
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
      ) : null}

      <div className="min-h-0 flex-1">
        <DataTable definition={tableDefinition} />
      </div>
    </PageLayout>
  );
}

function RuleIdentityCell({
  definition,
  onOpen,
}: {
  definition: RuleDefinitionSummary;
  onOpen?: () => void;
}) {
  const { t, i18n } = useTranslation();
  const name = localizedRuleName(definition, i18n.language, t);
  return (
    <div className="min-w-0 whitespace-normal">
      {onOpen ? (
        <PageAction data-slot="rule-table-value" type="button" variant="link" onClick={onOpen}>
          {name}
        </PageAction>
      ) : (
        <p data-slot="rule-table-value" className="font-semibold text-foreground">
          {name}
        </p>
      )}
      <p className="mt-1 line-clamp-2 text-xs leading-5 text-muted-foreground">
        {localizedRuleDescription(definition, i18n.language, t)}
      </p>
    </div>
  );
}

function RuleInputsCell({ definition }: { definition: RuleDefinitionSummary }) {
  const inputs = definition.inputs ?? [];
  return inputs.length > 0 ? (
    <span data-slot="rule-table-value" className="whitespace-normal text-sm text-foreground">
      {inputs
        .map((input) => input.label)
        .filter(Boolean)
        .join(', ')}
    </span>
  ) : (
    <span data-slot="rule-table-value" className="whitespace-normal text-sm text-foreground">
      —
    </span>
  );
}

function RuleOriginCell({ definition }: { definition: RuleDefinitionSummary }) {
  return definition.origin ? (
    <RuleOriginBadge data-slot="rule-table-value" origin={definition.origin} />
  ) : (
    <span data-slot="rule-table-value">—</span>
  );
}

function RuleStatusCell({ definition }: { definition: RuleDefinitionSummary }) {
  const { t } = useTranslation();
  const label = definition.status ? t(`rules.status${definition.status}`) : '—';
  const tone: StatusBadgeTone =
    definition.status === 'Active'
      ? 'success'
      : definition.status === 'Draft' || definition.status === 'Inactive'
        ? 'neutral'
        : 'muted';
  return (
    <StatusBadge data-slot="rule-table-value" tone={tone}>
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

function localizedRuleDescription(
  definition: RuleDefinitionSummary,
  locale: string,
  t: TFunction,
): string {
  return (
    referenceContent(definition.documentation, locale)?.summary ??
    definition.description ??
    t('rules.unknownRuleDescription')
  );
}
