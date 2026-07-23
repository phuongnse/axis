import { useQuery } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import { Plus } from 'lucide-react';
import { useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createDataTableMessages,
  DataTable,
  type DataTableColumnDef,
  type DataTableDefinition,
} from '@/components/shared/data-table';
import { useManagedWindowActions } from '@/components/shared/ManagedWindowManager';
import { StatusBadge, type StatusBadgeTone } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import { type RuleDefinitionSummary, ruleDefinitionsListQueryOptions } from '../api';
import { ruleCreateWindowDescriptor, ruleDefinitionWindowDescriptor } from '../managed-windows';
import {
  compareFieldTypes,
  compareRuleDefinitions,
  fieldTypeTranslationKey,
  ruleDescriptionTranslationKey,
  ruleNameTranslationKey,
  ruleSetupTranslationKey,
} from '../metadata';
import { RuleOriginBadge } from './RuleOriginBadge';

const route = getRouteApi('/_authenticated/rules');

export function RulesPage() {
  const { t } = useTranslation();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const { openWindow } = useManagedWindowActions();
  const definitionsQuery = useQuery(ruleDefinitionsListQueryOptions());
  const definitions = definitionsQuery.data?.items ?? [];
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
      selectedDefinition ? localizedRuleName(selectedDefinition, t) : search.definitionKey,
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
    t,
  ]);
  const tableDefinition = useMemo<DataTableDefinition<RuleDefinitionSummary>>(() => {
    const appliesTo = new Map<string, string>();
    for (const definition of definitions) {
      const targetTypes = definition.applicability?.targetTypeKeys ?? [];
      for (const fieldType of targetTypes) {
        appliesTo.set(fieldType, t(fieldTypeTranslationKey(fieldType)));
      }
      if (targetTypes.length === 0 && definition.contextKey) {
        appliesTo.set(
          definition.contextKey,
          humanizeContext(definition.contextKey, t('rules.contextUnavailable')),
        );
      }
    }
    const catalogScopes = distinctDefined(definitions.map((definition) => definition.scope));
    const origins = distinctDefined(definitions.map((definition) => definition.origin));
    const statuses = distinctDefined(definitions.map((definition) => definition.status));

    const columns: DataTableColumnDef<RuleDefinitionSummary>[] = [
      {
        id: 'rule',
        accessorFn: (definition) => localizedRuleName(definition, t),
        sortingFn: (left, right) => compareRuleDefinitions(left.original, right.original),
        size: 330,
        minSize: 280,
        enableGrouping: false,
        meta: {
          label: t('rules.ruleColumn'),
          searchable: true,
          searchValue: (definition) => [
            localizedRuleName(definition, t),
            localizedRuleDescription(definition, t),
          ],
          filter: {
            kind: 'text',
            getValue: (definition) => [
              localizedRuleName(definition, t),
              localizedRuleDescription(definition, t),
            ],
          },
        },
        cell: ({ row }) => (
          <RuleIdentityCell
            definition={row.original}
            onOpen={
              row.original.definitionKey
                ? () => {
                    const descriptor = ruleDefinitionWindowDescriptor(
                      row.original,
                      localizedRuleName(row.original, t),
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
        enableGrouping: false,
        meta: {
          label: t('rules.appliesToColumn'),
          searchable: true,
          searchValue: (definition) =>
            ruleTargets(definition).map((value) => appliesTo.get(value) ?? value),
          filter: {
            kind: 'multiChoice',
            options: [...appliesTo].map(([value, label]) => ({ value, label })),
            getValue: ruleTargets,
          },
        },
        cell: ({ row }) => <RuleTargetsCell definition={row.original} />,
      },
      {
        id: 'scope',
        accessorFn: (definition) => definition.scope,
        size: 160,
        minSize: 150,
        enableGrouping: true,
        meta: {
          label: t('rules.scope'),
          searchable: true,
          searchValue: (definition) =>
            definition.scope ? t(`rules.scope${definition.scope}`) : [],
          filter: {
            kind: 'singleChoice',
            options: catalogScopes.map((scope) => ({
              value: scope,
              label: t(`rules.scope${scope}`),
            })),
          },
        },
        cell: ({ row }) => <RuleScopeCell definition={row.original} />,
      },
      {
        id: 'origin',
        accessorFn: (definition) => definition.origin,
        size: 130,
        minSize: 120,
        enableGrouping: true,
        meta: {
          label: t('rules.origin'),
          searchable: true,
          searchValue: (definition) =>
            definition.origin
              ? definition.origin === 'System'
                ? t('rules.builtIn')
                : t('rules.originWorkspace')
              : [],
          filter: {
            kind: 'singleChoice',
            options: origins.map((origin) => ({
              value: origin,
              label: origin === 'System' ? t('rules.builtIn') : t('rules.originWorkspace'),
            })),
          },
        },
        cell: ({ row }) => <RuleOriginCell definition={row.original} />,
      },
      {
        id: 'status',
        accessorFn: (definition) => definition.status,
        size: 130,
        minSize: 120,
        enableGrouping: true,
        meta: {
          label: t('rules.status'),
          searchable: true,
          searchValue: (definition) =>
            definition.status ? t(`rules.status${definition.status}`) : [],
          filter: {
            kind: 'singleChoice',
            options: statuses.map((status) => ({
              value: status,
              label: t(`rules.status${status}`),
            })),
          },
        },
        cell: ({ row }) => <RuleStatusCell definition={row.original} />,
      },
    ];

    return {
      ariaLabel: t('rules.catalogTitle'),
      source: { mode: 'client', data: definitions, pagination: { pageSize: 20 } },
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
      initialState: {
        sorting: [{ id: 'rule', desc: false }],
      },
      enableColumnResizing: true,
      globalSearch: true,
      columnControls: true,
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
      loading: definitionsQuery.isLoading,
      error: definitionsQuery.isError,
      onRetry: () => void definitionsQuery.refetch(),
    };
  }, [
    definitions,
    definitionsQuery.isError,
    definitionsQuery.isLoading,
    definitionsQuery.refetch,
    openWindow,
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
  const { t } = useTranslation();
  const name = localizedRuleName(definition, t);
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
        {localizedRuleDescription(definition, t)}
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
  const { t } = useTranslation();
  const setupKey = ruleSetupTranslationKey(definition.definitionKey);
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
      <p className="mt-1.5 text-xs text-muted-foreground">
        {setupKey ? t(setupKey) : t('rules.setup.configured')}
      </p>
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

function localizedRuleName(definition: RuleDefinitionSummary, t: TFunction): string {
  const nameKey = ruleNameTranslationKey(definition.definitionKey);
  return nameKey ? t(nameKey) : (definition.name ?? t('rules.unknownRule'));
}

function localizedRuleDescription(definition: RuleDefinitionSummary, t: TFunction): string {
  const descriptionKey = ruleDescriptionTranslationKey(definition.definitionKey);
  return descriptionKey
    ? t(descriptionKey)
    : (definition.description ?? t('rules.unknownRuleDescription'));
}

function ruleTargets(definition: RuleDefinitionSummary): string[] {
  const targetTypes = definition.applicability?.targetTypeKeys ?? [];
  return targetTypes.length > 0
    ? [...targetTypes]
    : definition.contextKey
      ? [definition.contextKey]
      : [];
}

function distinctDefined<T>(values: (T | null | undefined)[]): T[] {
  return [...new Set(values.filter((value): value is T => value != null))];
}
