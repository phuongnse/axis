import { useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Card, CardContent } from '@/components/ui/card';
import {
  assistRuleExpression,
  type RuleContextSchema,
  type RuleDefinitionDetail,
  type RuleExpressionAuthoring,
  type RuleParameterDefinition,
  ruleDefinitionQueryKeys,
} from '../api';
import { RuleExpressionView } from './RuleExpressionView';

export function RuleBehaviorSummary({
  condition,
  outcome,
  expressionLanguageVersion,
  definitionKey,
  flowSlot = 'rule-behavior-flow',
  outcomeSlot = 'rule-outcome',
  effectSlot = 'rule-effect',
  contextSchema,
  parameters,
  references = false,
  authoring,
}: {
  condition: RuleDefinitionDetail['condition'];
  outcome: RuleDefinitionDetail['outcome'];
  expressionLanguageVersion?: number;
  definitionKey?: string;
  flowSlot?: string;
  outcomeSlot?: string;
  effectSlot?: string;
  contextSchema?: RuleContextSchema;
  parameters?: RuleParameterDefinition[];
  references?: boolean;
  authoring?: RuleExpressionAuthoring;
}) {
  const { t, i18n } = useTranslation();
  const projectionRequest = useMemo(
    () => ({
      expressionLanguageVersion,
      contextKey: contextSchema?.contextKey ?? null,
      contextSchemaVersion: contextSchema?.version ?? null,
      parameters: parameters ?? [],
      syntax: null,
      condition: condition ?? undefined,
      cursorOffset: 0,
      language: i18n.language,
    }),
    [
      condition,
      contextSchema?.contextKey,
      contextSchema?.version,
      expressionLanguageVersion,
      i18n.language,
      parameters,
    ],
  );
  const projectionQuery = useQuery({
    queryKey: ruleDefinitionQueryKeys.expressionAssist(projectionRequest),
    queryFn: () => assistRuleExpression(projectionRequest),
    enabled: Boolean(condition) && !authoring,
    staleTime: Number.POSITIVE_INFINITY,
  });
  const resolved = authoring ?? projectionQuery.data;

  return (
    <Card size="sm">
      <CardContent>
        <ol data-slot={flowSlot} className="space-y-0">
          <li data-slot="rule-timeline-item" className="flex gap-3">
            <div aria-hidden="true" className="flex w-5 shrink-0 flex-col items-center">
              <span
                data-slot="rule-timeline-marker"
                className="mt-1 size-2.5 shrink-0 rounded-full bg-foreground"
              />
              <span data-slot="rule-timeline-line" className="w-px flex-1 bg-border" />
            </div>
            <div className="min-w-0 flex-1 pb-5">
              <p className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                {t('rules.when')}
              </p>
              <div className="mt-2">
                {condition ? (
                  projectionQuery.isLoading && !authoring ? (
                    <p role="status" className="text-sm text-muted-foreground">
                      {t('rules.referenceLoading')}
                    </p>
                  ) : resolved?.display ? (
                    <RuleExpressionView
                      display={resolved.display}
                      definitionKey={definitionKey}
                      expressionLanguageVersion={expressionLanguageVersion}
                      contextSchema={contextSchema}
                      parameters={parameters}
                      references={references}
                    />
                  ) : (
                    <p role="alert" className="text-sm text-destructive">
                      {t('rules.loadErrorTitle')}
                    </p>
                  )
                ) : (
                  <p className="text-sm text-muted-foreground">{t('rules.notSet')}</p>
                )}
              </div>
            </div>
          </li>
          <li data-slot="rule-timeline-item" className="flex gap-3">
            <div aria-hidden="true" className="flex w-5 shrink-0 flex-col items-center">
              <span data-slot="rule-timeline-tail" className="h-1 w-px shrink-0 bg-border" />
              <span
                data-slot="rule-timeline-marker"
                className="size-2.5 shrink-0 rounded-full bg-foreground"
              />
            </div>
            <div className="min-w-0 flex-1">
              <p className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                {t('rules.then')}
              </p>
              <div className="mt-2 space-y-2">
                <p
                  data-slot={outcomeSlot}
                  className="text-sm leading-relaxed font-medium text-foreground"
                >
                  {outcome?.message ?? outcome?.decision ?? t('rules.notSet')}
                </p>
                {outcome?.severity ? (
                  <p
                    data-slot={effectSlot}
                    className="text-xs leading-relaxed text-muted-foreground"
                  >
                    <span className="font-medium text-foreground">{t('rules.effect')}:</span>{' '}
                    {t(`rules.effect${outcome.severity}`)}
                  </p>
                ) : null}
              </div>
            </div>
          </li>
        </ol>
      </CardContent>
    </Card>
  );
}
