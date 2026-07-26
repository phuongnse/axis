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
import { StatusBadge, type StatusBadgeTone } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { referenceContent } from '@/lib/reference-metadata';
import { type RuleDefinitionSummary, ruleDefinitionsListQueryOptions } from '../api';
import { ruleCreateWindowDescriptor, ruleDefinitionWindowDescriptor } from '../managed-windows';
import { compareFieldTypes, fieldTypeTranslationKey } from '../metadata';
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
      openWindow(ruleCreateWindowDescriptor(t('rules.createTitle')));
      void navigate({ replace: true, search: {} });
      return;
    }
    if (!search.definitionKey || definitionsQuery.isLoading) return;
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
    definitionsQuery.isLoading,
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
        id: 'appliesTo',
        accessorFn: ruleTargets,
        size: 220,
        minSize: 200,
        enableSorting: false,
        enableGrouping: false,
        meta: { label: t('rules.appliesToColumn') },
        cell: ({ row }) => <RuleTargetsCell definition={row.original} />,
      },
      {
        id: 'scope',
        accessorFn: (definition) => definition.scope,
        size: 160,
        minSize: 150,
        enableSorting: false,
        enableGrouping: false,
        meta: { label: t('rules.scope') },
        cell: ({ row }) => <RuleScopeCell definition={row.original} />,
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
        `${definition.origin ?? 'Unknown'}:${definition.name ?? definition.contextKey ?? 'rule'}`,
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
      renderToolbarActions: () => (
        <Button
          type="button"
          size="sm"
          onClick={() => openWindow(ruleCreateWindowDescriptor(t('rules.createTitle')))}
        >
          <Plus aria-hidden />
          {t('rules.newRule')}
        </Button>
      ),
      loading: definitionsQuery.isFetching,
      error: definitionsQuery.isError,
      onRetry: () => void definitionsQuery.refetch(),
    };
  }, [
    definitions,
    definitionsQuery.isError,
    definitionsQuery.isFetching,
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
    <div className="flex h-full min-h-0 w-full min-w-0 flex-col gap-4 overflow-hidden p-4 sm:p-6 lg:p-8">
      <header className="min-w-0 shrink-0">
        <div className="min-w-0">
          <h1 className="font-heading text-2xl font-semibold text-foreground">
            {t('rules.title')}
          </h1>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            {t('rules.pageDescription')}
          </p>
        </div>
      </header>

      <div className="min-h-0 flex-1">
        <DataTable definition={tableDefinition} />
      </div>
    </div>
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
        <Button
          data-slot="rule-table-value"
          type="button"
          variant="link"
          className="h-auto p-0"
          onClick={onOpen}
        >
          {name}
        </Button>
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

function RuleTargetsCell({ definition }: { definition: RuleDefinitionSummary }) {
  const { t } = useTranslation();
  const targetTypes = [...(definition.applicability?.targetTypeKeys ?? [])].sort(compareFieldTypes);
  return targetTypes.length > 0 ? (
    <span data-slot="rule-table-value" className="whitespace-normal text-sm text-foreground">
      {targetTypes.map((fieldType) => t(fieldTypeTranslationKey(fieldType))).join(', ')}
    </span>
  ) : (
    <span data-slot="rule-table-value" className="whitespace-normal text-sm text-foreground">
      {humanizeContext(definition.contextKey, t('rules.contextUnavailable'))}
    </span>
  );
}

function RuleScopeCell({ definition }: { definition: RuleDefinitionSummary }) {
  const { t, i18n } = useTranslation();
  const setup = referenceContent(definition.documentation, i18n.language)?.usage;
  return (
    <div className="whitespace-normal">
      <div className="flex flex-wrap gap-x-1.5 text-sm font-medium text-foreground">
        <span data-slot="rule-table-value">
          {definition.scope ? t(`rules.scope${definition.scope}`) : '—'}
        </span>
        {definition.outcomeKind === 'Decision' ? (
          <>
            <span aria-hidden>·</span>
            <span>{t('rules.outcomeDecision')}</span>
          </>
        ) : null}
      </div>
      <p className="mt-1.5 text-xs text-muted-foreground">{setup ?? t('rules.setup.configured')}</p>
    </div>
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
    definition.status === 'Published'
      ? 'success'
      : definition.status === 'Draft'
        ? 'neutral'
        : 'muted';
  return (
    <StatusBadge data-slot="rule-table-value" tone={tone}>
      {label}
    </StatusBadge>
  );
}

function humanizeContext(contextKey: string | null | undefined, fallback: string): string {
  if (!contextKey) return fallback;
  const label = contextKey.split('.').slice(1).join(' ');
  return label.charAt(0).toUpperCase() + label.slice(1);
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

function ruleTargets(definition: RuleDefinitionSummary): string[] {
  const targetTypes = definition.applicability?.targetTypeKeys ?? [];
  return targetTypes.length > 0
    ? [...targetTypes]
    : definition.contextKey
      ? [definition.contextKey]
      : [];
}
