import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import { Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createDataTableMessages,
  DataTable,
  type DataTableColumnDef,
  type DataTableDefinition,
} from '@/components/shared/data-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { ApiError } from '@/lib/api';
import {
  createRuleDefinition,
  type RuleDefinitionDetail,
  type RuleDefinitionSummary,
  type RuleScope,
  ruleContextSchemasQueryOptions,
  ruleDefinitionQueryKeys,
  ruleDefinitionsListQueryOptions,
} from '../api';
import {
  compareFieldTypes,
  compareRuleDefinitions,
  fieldTypeTranslationKey,
  ruleDescriptionTranslationKey,
  ruleNameTranslationKey,
  ruleSetupTranslationKey,
} from '../metadata';
import { RuleEditorDialog } from './RuleEditorDialog';

const scopes: RuleScope[] = ['Field', 'Object', 'Record', 'Lifecycle'];
const route = getRouteApi('/_authenticated/rules');

export function RulesPage() {
  const { t } = useTranslation();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const definitionsQuery = useQuery(ruleDefinitionsListQueryOptions());
  const definitions = definitionsQuery.data?.items ?? [];
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

    const columns: DataTableColumnDef<RuleDefinitionSummary>[] = [
      {
        id: 'rule',
        accessorFn: (definition) => localizedRuleName(definition, t),
        sortingFn: (left, right) => compareRuleDefinitions(left.original, right.original),
        size: 360,
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
              row.original.origin === 'Workspace' && row.original.definitionKey
                ? () => {
                    void navigate({
                      search: { dialog: 'edit', definitionKey: row.original.definitionKey },
                    });
                  }
                : undefined
            }
          />
        ),
      },
      {
        id: 'appliesTo',
        accessorFn: ruleTargets,
        size: 240,
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
        accessorFn: (definition) => definition.scope ?? 'Object',
        size: 180,
        minSize: 150,
        enableGrouping: true,
        meta: {
          label: t('rules.scope'),
          searchable: true,
          searchValue: (definition) => t(`rules.scope${definition.scope ?? 'Object'}`),
          filter: {
            kind: 'singleChoice',
            options: scopes.map((scope) => ({ value: scope, label: t(`rules.scope${scope}`) })),
          },
        },
        cell: ({ row }) => <RuleScopeCell definition={row.original} />,
      },
      {
        id: 'status',
        accessorFn: ruleStatusValues,
        size: 130,
        minSize: 120,
        enableGrouping: true,
        meta: {
          label: t('rules.status'),
          searchable: true,
          searchValue: (definition) =>
            ruleStatusValues(definition).map((value) =>
              value === 'System'
                ? t('rules.builtIn')
                : value === 'Workspace'
                  ? t('rules.originWorkspace')
                  : t(`rules.status${value}`),
            ),
          filter: {
            kind: 'multiChoice',
            options: [
              { value: 'System', label: t('rules.builtIn') },
              { value: 'Workspace', label: t('rules.originWorkspace') },
              { value: 'Published', label: t('rules.statusPublished') },
              { value: 'Draft', label: t('rules.statusDraft') },
              { value: 'Archived', label: t('rules.statusArchived') },
            ],
            getValue: ruleStatusValues,
          },
        },
        cell: ({ row }) => <OriginStatusBadge definition={row.original} />,
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
          onClick={() => void navigate({ search: { dialog: 'create' } })}
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
    navigate,
    t,
  ]);

  return (
    <div className="flex h-full min-h-0 w-full min-w-0 flex-col gap-4 overflow-hidden p-4 sm:p-6 lg:p-8">
      <header className="min-w-0 shrink-0">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold text-foreground">{t('rules.title')}</h1>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            {t('rules.pageDescription')}
          </p>
        </div>
      </header>

      <div className="min-h-0 flex-1">
        <DataTable definition={tableDefinition} />
      </div>

      <CreateRuleDialog
        open={search.dialog === 'create'}
        onOpenChange={(open) => {
          if (!open) void navigate({ search: {} });
        }}
        onCreated={(definition) => {
          void navigate({
            replace: true,
            search: definition.definitionKey
              ? { dialog: 'edit', definitionKey: definition.definitionKey }
              : {},
          });
        }}
      />
      <RuleEditorDialog
        definitionKey={search.definitionKey ?? null}
        open={search.dialog === 'edit' && Boolean(search.definitionKey)}
        onOpenChange={(open) => {
          if (!open) void navigate({ search: {} });
        }}
      />
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
        <Button type="button" variant="link" size="inline" onClick={onOpen}>
          {name}
        </Button>
      ) : (
        <p className="font-semibold text-foreground">{name}</p>
      )}
      <p className="mt-1 line-clamp-2 text-sm leading-5 text-muted-foreground">
        {localizedRuleDescription(definition, t)}
      </p>
    </div>
  );
}

function RuleTargetsCell({ definition }: { definition: RuleDefinitionSummary }) {
  const { t } = useTranslation();
  const targetTypes = [...(definition.applicability?.targetTypeKeys ?? [])].sort(compareFieldTypes);
  return targetTypes.length > 0 ? (
    <div className="flex flex-wrap gap-1.5 whitespace-normal">
      {targetTypes.map((fieldType) => (
        <Badge key={fieldType} variant="primaryOutline">
          {t(fieldTypeTranslationKey(fieldType))}
        </Badge>
      ))}
    </div>
  ) : (
    <span className="whitespace-normal text-sm text-foreground">
      {humanizeContext(definition.contextKey, t('rules.contextUnavailable'))}
    </span>
  );
}

function RuleScopeCell({ definition }: { definition: RuleDefinitionSummary }) {
  const { t } = useTranslation();
  const setupKey = ruleSetupTranslationKey(definition.definitionKey);
  return (
    <div className="whitespace-normal">
      <div className="flex flex-wrap gap-1.5">
        <ScopeBadge scope={definition.scope} />
        {definition.outcomeKind === 'Decision' ? (
          <Badge variant="muted">{t('rules.outcomeDecision')}</Badge>
        ) : null}
      </div>
      <p className="mt-1.5 text-xs text-muted-foreground">
        {setupKey ? t(setupKey) : t('rules.setup.configured')}
      </p>
    </div>
  );
}

function OriginStatusBadge({ definition }: { definition: RuleDefinitionSummary }) {
  const { t } = useTranslation();
  if (definition.origin === 'System') {
    return <Badge variant="primaryOutline">{t('rules.builtIn')}</Badge>;
  }
  if (definition.status === 'Published') {
    return <Badge variant="successOutline">{t('rules.statusPublished')}</Badge>;
  }
  if (definition.status === 'Archived') {
    return <Badge variant="muted">{t('rules.statusArchived')}</Badge>;
  }
  return <Badge variant="warningOutline">{t('rules.statusDraft')}</Badge>;
}

function ScopeBadge({ scope }: { scope: RuleScope | undefined }) {
  const { t } = useTranslation();
  const variant =
    scope === 'Record'
      ? 'successOutline'
      : scope === 'Lifecycle'
        ? 'warningOutline'
        : scope === 'Field'
          ? 'primaryOutline'
          : 'secondary';
  return <Badge variant={variant}>{t(`rules.scope${scope ?? 'Object'}`)}</Badge>;
}

function CreateRuleDialog({
  open,
  onOpenChange,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCreated: (definition: RuleDefinitionDetail) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const schemasQuery = useQuery({ ...ruleContextSchemasQueryOptions(), enabled: open });
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [scope, setScope] = useState<RuleScope>('Field');
  const [contextKey, setContextKey] = useState('');
  const [outcomeKind, setOutcomeKind] = useState<'Validation' | 'Decision'>('Validation');
  const [error, setError] = useState('');
  const schemas = Array.isArray(schemasQuery.data) ? schemasQuery.data : [];
  const availableSchemas = schemas.filter((schema) => schema.scope === scope);
  const selectedSchema = availableSchemas.find((schema) => schema.contextKey === contextKey);

  const createMutation = useMutation({
    mutationFn: createRuleDefinition,
    onSuccess: async (definition) => {
      await queryClient.invalidateQueries({ queryKey: ruleDefinitionQueryKeys.all });
      reset();
      onCreated(definition);
    },
    onError: (mutationError) => setError(readApiError(mutationError, t('rules.createError'))),
  });

  function reset() {
    setName('');
    setDescription('');
    setScope('Field');
    setContextKey('');
    setOutcomeKind('Validation');
    setError('');
  }

  function changeScope(nextScope: RuleScope) {
    setScope(nextScope);
    const firstSchema = schemas.find((schema) => schema.scope === nextScope);
    setContextKey(firstSchema?.contextKey ?? '');
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen) reset();
        onOpenChange(nextOpen);
      }}
    >
      <DialogContent size="form" closeLabel={t('rules.close')}>
        <DialogHeader>
          <DialogTitle>{t('rules.createTitle')}</DialogTitle>
          <DialogDescription>{t('rules.createDescription')}</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field className="sm:col-span-2">
            <FieldLabel htmlFor="rule-name" required>
              {t('rules.name')}
            </FieldLabel>
            <Input
              id="rule-name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              maxLength={200}
            />
            <FieldDescription>
              {t('rules.derivedKey', { key: deriveRuleKey(name) })}
            </FieldDescription>
          </Field>
          <Field className="sm:col-span-2">
            <FieldLabel htmlFor="rule-description" required>
              {t('rules.description')}
            </FieldLabel>
            <Textarea
              id="rule-description"
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              maxLength={1000}
            />
          </Field>
          <Field>
            <FieldLabel htmlFor="rule-scope" required>
              {t('rules.scope')}
            </FieldLabel>
            <Select
              value={scope}
              onValueChange={(value) => value && changeScope(value as RuleScope)}
            >
              <SelectTrigger id="rule-scope">
                <SelectValue>{(value) => t(`rules.scope${value}`)}</SelectValue>
              </SelectTrigger>
              <SelectContent>
                {scopes.map((value) => (
                  <SelectItem key={value} value={value}>
                    {t(`rules.scope${value}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel htmlFor="rule-outcome-kind" required>
              {t('rules.outcome')}
            </FieldLabel>
            <Select
              value={outcomeKind}
              onValueChange={(value) => value && setOutcomeKind(value as 'Validation' | 'Decision')}
            >
              <SelectTrigger id="rule-outcome-kind">
                <SelectValue>
                  {(value) =>
                    value === 'Decision' ? t('rules.outcomeDecision') : t('rules.outcomeValidation')
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Validation">{t('rules.outcomeValidation')}</SelectItem>
                <SelectItem value="Decision">{t('rules.outcomeDecision')}</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field className="sm:col-span-2">
            <FieldLabel htmlFor="rule-context" required>
              {t('rules.context')}
            </FieldLabel>
            <Select
              value={contextKey || null}
              onValueChange={(value) => setContextKey(value ?? '')}
              disabled={schemasQuery.isLoading || availableSchemas.length === 0}
            >
              <SelectTrigger id="rule-context">
                <SelectValue>
                  {(value) =>
                    availableSchemas.find((schema) => schema.contextKey === value)?.displayName ??
                    t('rules.selectContext')
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {availableSchemas.map((schema) => (
                  <SelectItem
                    key={`${schema.contextKey}:${schema.version}`}
                    value={schema.contextKey}
                  >
                    {schema.displayName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {availableSchemas.length === 0 && !schemasQuery.isLoading ? (
              <FieldDescription>{t('rules.noContextForScope')}</FieldDescription>
            ) : null}
          </Field>
          <FieldError className="sm:col-span-2">{error}</FieldError>
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            {t('app.cancel')}
          </Button>
          <Button
            type="button"
            disabled={
              createMutation.isPending ||
              !name.trim() ||
              !description.trim() ||
              !selectedSchema?.contextKey
            }
            onClick={() => {
              if (!selectedSchema?.contextKey || selectedSchema.version === undefined) return;
              setError('');
              createMutation.mutate({
                name: name.trim(),
                description: description.trim(),
                scope,
                contextKey: selectedSchema.contextKey,
                contextSchemaVersion: selectedSchema.version,
                outcomeKind,
              });
            }}
          >
            {t('rules.createAction')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
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

function ruleStatusValues(definition: RuleDefinitionSummary): string[] {
  return definition.origin === 'System' ? ['System'] : ['Workspace', definition.status ?? 'Draft'];
}

function deriveRuleKey(name: string): string {
  const normalized = name
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .toLocaleLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
  const key = /^[a-z]/.test(normalized) ? normalized : normalized ? `rule_${normalized}` : 'rule';
  return key.slice(0, 63).replace(/_+$/g, '') || 'rule';
}

function readApiError(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError) || typeof error.data !== 'object' || error.data === null) {
    return fallback;
  }
  const detail = (error.data as { detail?: unknown }).detail;
  return typeof detail === 'string' && detail ? detail : fallback;
}
