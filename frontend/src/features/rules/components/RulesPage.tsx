import { useQuery } from '@tanstack/react-query';
import { AlertCircle, DatabaseZap, ListChecks, RefreshCw, ShieldCheck } from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  type FieldRuleDefinition,
  type FieldRuleParameterDefinition,
  fieldRuleDefinitionsListQueryOptions,
} from '../api';
import {
  compareFieldRuleDefinitions,
  compareFieldTypes,
  fieldRuleCategoryTranslationKey,
  fieldRuleDescriptionTranslationKey,
  fieldRuleNameTranslationKey,
  fieldTypeTranslationKey,
} from '../metadata';

export function RulesPage() {
  const { t } = useTranslation();
  const definitionsQuery = useQuery(fieldRuleDefinitionsListQueryOptions());

  if (definitionsQuery.isLoading) {
    return <RulesLoadingPage />;
  }

  if (definitionsQuery.isError) {
    return <RulesErrorPage onRetry={() => void definitionsQuery.refetch()} />;
  }

  const definitions = [...(definitionsQuery.data ?? [])].sort(compareFieldRuleDefinitions);
  const supportedFieldTypes = [
    ...new Set(definitions.flatMap((definition) => definition.supportedFieldTypes ?? [])),
  ].sort(compareFieldTypes);
  const parameterCount = definitions.reduce(
    (count, definition) => count + (definition.parameters?.length ?? 0),
    0,
  );

  return (
    <div className="flex h-full min-h-0 w-full min-w-0 flex-col gap-4 overflow-y-auto overflow-x-hidden px-4 pb-8 pt-4 sm:px-6 sm:pb-10 sm:pt-6 lg:px-8 lg:pb-12 lg:pt-8">
      <header className="flex min-w-0 shrink-0 flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold text-foreground">{t('rules.title')}</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            {t('rules.pageDescription')}
          </p>
        </div>
        <Badge variant="outline" className="h-7 rounded-md px-2.5">
          <ShieldCheck className="size-3.5" aria-hidden />
          {t('rules.systemManaged')}
        </Badge>
      </header>

      <div className="grid shrink-0 gap-3 sm:grid-cols-3">
        <Metric label={t('rules.definitionCount', { count: definitions.length })} value="01" />
        <Metric label={t('rules.parameterCount', { count: parameterCount })} value="02" />
        <Metric
          label={t('rules.supportedTypeCount', { count: supportedFieldTypes.length })}
          value="03"
        />
      </div>

      <div className="grid min-h-0 min-w-0 gap-4 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <section aria-labelledby="rules-catalog-title" className="min-w-0">
          <Card size="sm" className="min-w-0 gap-0 py-0">
            <div className="flex min-w-0 items-start justify-between gap-3 border-b border-border px-4 py-4">
              <div className="min-w-0">
                <h2 id="rules-catalog-title" className="text-sm font-semibold">
                  {t('rules.catalogTitle')}
                </h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t('rules.catalogDescription')}
                </p>
              </div>
              <Badge variant="secondary" className="rounded-md">
                {t('rules.readOnly')}
              </Badge>
            </div>

            <CardContent className="px-0">
              {definitions.length === 0 ? (
                <div className="px-4 py-5">
                  <p className="text-sm font-medium text-foreground">{t('rules.emptyTitle')}</p>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t('rules.emptyDescription')}
                  </p>
                </div>
              ) : (
                <div className="divide-y divide-border">
                  {definitions.map((definition) => (
                    <RuleDefinitionRow key={definition.definitionKey} definition={definition} />
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </section>

        <aside className="grid content-start gap-4">
          <Card size="sm">
            <CardHeader>
              <CardTitle>{t('rules.contractTitle')}</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm leading-6 text-muted-foreground">
                {t('rules.contractDescription')}
              </p>
              <div className="mt-4 grid gap-2 text-sm">
                <ContractFact icon={<ListChecks className="size-4" aria-hidden />}>
                  {t('rules.contractStableKeys')}
                </ContractFact>
                <ContractFact icon={<DatabaseZap className="size-4" aria-hidden />}>
                  {t('rules.contractParameters')}
                </ContractFact>
                <ContractFact icon={<ShieldCheck className="size-4" aria-hidden />}>
                  {t('rules.contractNoCustom')}
                </ContractFact>
              </div>
            </CardContent>
          </Card>

          <Card size="sm">
            <CardHeader>
              <CardTitle>{t('rules.fieldTypesTitle')}</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm leading-6 text-muted-foreground">
                {t('rules.fieldTypesDescription')}
              </p>
              <div className="mt-4 flex flex-wrap gap-2">
                {supportedFieldTypes.map((fieldType) => (
                  <Badge key={fieldType} variant="outline" className="rounded-md">
                    {t(fieldTypeTranslationKey(fieldType))}
                  </Badge>
                ))}
              </div>
            </CardContent>
          </Card>
        </aside>
      </div>
    </div>
  );
}

function RulesLoadingPage() {
  const { t } = useTranslation();

  return (
    <div className="flex h-full min-h-0 w-full flex-col gap-4 overflow-y-auto overflow-x-hidden px-4 pb-8 pt-4 sm:px-6 sm:pb-10 sm:pt-6 lg:px-8 lg:pb-12 lg:pt-8">
      <div>
        <h1 className="text-2xl font-semibold text-foreground">{t('rules.title')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t('rules.pageDescription')}</p>
      </div>
      <Card size="sm" className="max-w-5xl p-4">
        <Skeleton className="h-4 w-40" />
        <Skeleton className="mt-6 h-16 w-full" />
        <Skeleton className="mt-3 h-16 w-full" />
        <Skeleton className="mt-3 h-16 w-full" />
      </Card>
    </div>
  );
}

function RulesErrorPage({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation();

  return (
    <div className="h-full w-full overflow-y-auto overflow-x-hidden px-4 pb-8 pt-4 sm:px-6 sm:pb-10 sm:pt-6 lg:px-8 lg:pb-12 lg:pt-8">
      <div className="max-w-3xl">
        <Alert variant="destructive">
          <AlertCircle className="size-4" aria-hidden />
          <AlertTitle>{t('rules.loadErrorTitle')}</AlertTitle>
          <AlertDescription>{t('rules.loadErrorBody')}</AlertDescription>
        </Alert>
        <Button type="button" variant="outline" className="mt-4" onClick={onRetry}>
          <RefreshCw className="size-4" aria-hidden />
          {t('app.retry')}
        </Button>
      </div>
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <Card size="sm" className="gap-1 p-4">
      <p className="text-xs font-medium text-muted-foreground">{label}</p>
      <p className="text-xl font-semibold text-foreground">{value}</p>
    </Card>
  );
}

function RuleDefinitionRow({ definition }: { definition: FieldRuleDefinition }) {
  const { t } = useTranslation();
  const nameKey = fieldRuleNameTranslationKey(definition.definitionKey);
  const descriptionKey = fieldRuleDescriptionTranslationKey(definition.definitionKey);
  const categoryKey = fieldRuleCategoryTranslationKey(definition.definitionKey);
  const displayName = nameKey
    ? t(nameKey)
    : (definition.displayName ?? definition.definitionKey ?? t('rules.unknownRule'));
  const description = descriptionKey
    ? t(descriptionKey)
    : (definition.description ?? t('rules.unknownRuleDescription'));
  const supportedFieldTypes = [...(definition.supportedFieldTypes ?? [])].sort(compareFieldTypes);

  return (
    <article className="grid min-w-0 gap-4 px-4 py-4 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,24rem)]">
      <div className="min-w-0">
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <h3 className="text-sm font-semibold text-foreground">{displayName}</h3>
          {categoryKey ? (
            <Badge variant="outline" className="rounded-md">
              {t(categoryKey)}
            </Badge>
          ) : null}
        </div>
        <p className="mt-1 text-sm leading-6 text-muted-foreground">{description}</p>
        <dl className="mt-3 grid gap-1 text-xs">
          <div className="flex min-w-0 flex-wrap gap-x-2 gap-y-1">
            <dt className="font-medium text-muted-foreground">{t('rules.definitionKey')}</dt>
            <dd className="font-mono text-foreground">{definition.definitionKey}</dd>
          </div>
        </dl>
        <div className="mt-3 flex flex-wrap gap-2">
          {supportedFieldTypes.map((fieldType) => (
            <Badge key={fieldType} variant="secondary" className="rounded-md">
              {t(fieldTypeTranslationKey(fieldType))}
            </Badge>
          ))}
        </div>
      </div>

      <div className="min-w-0 rounded-md border border-border bg-muted/25 p-3">
        <h4 className="text-xs font-semibold text-muted-foreground">{t('rules.parameters')}</h4>
        {definition.parameters?.length ? (
          <div className="mt-3 grid gap-2">
            {[...definition.parameters].sort(compareParameters).map((parameter) => (
              <ParameterRow key={parameter.key} parameter={parameter} />
            ))}
          </div>
        ) : (
          <p className="mt-2 text-sm text-muted-foreground">{t('rules.noParameters')}</p>
        )}
      </div>
    </article>
  );
}

function ParameterRow({ parameter }: { parameter: FieldRuleParameterDefinition }) {
  const { t } = useTranslation();

  return (
    <div className="flex min-w-0 flex-wrap items-center gap-2 text-sm">
      <span className="font-mono text-foreground">{parameter.key}</span>
      <Badge variant="outline" className="rounded-md">
        {parameter.type
          ? t(`rules.parameterType${parameter.type}`)
          : t('rules.unknownParameterType')}
      </Badge>
      <Badge variant={parameter.isRequired ? 'default' : 'secondary'} className="rounded-md">
        {parameter.isRequired ? t('rules.parameterRequired') : t('rules.parameterOptional')}
      </Badge>
      <span className="text-xs text-muted-foreground">
        {parameter.allowMultiple ? t('rules.parameterMultiple') : t('rules.parameterSingle')}
      </span>
    </div>
  );
}

function ContractFact({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <div className="flex items-center gap-2 text-foreground">
      <span className="inline-flex size-7 shrink-0 items-center justify-center rounded-md border border-border bg-background text-muted-foreground">
        {icon}
      </span>
      <span>{children}</span>
    </div>
  );
}

function compareParameters(
  left: FieldRuleParameterDefinition,
  right: FieldRuleParameterDefinition,
): number {
  return (left.key ?? '').localeCompare(right.key ?? '');
}
