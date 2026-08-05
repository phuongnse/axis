import { useQuery } from '@tanstack/react-query';
import { GitBranch, LogIn, LogOut, type LucideIcon } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { MetadataTag } from '@/components/shared/MetadataTag';
import {
  projectRuleCondition,
  type RuleDefinitionDetail,
  type RuleDraftInputDefinition,
  type RuleInputDefinition,
  ruleDefinitionQueryKeys,
  ruleExpressionLanguageQueryOptions,
} from '../api';
import { toDraftInputs } from '../condition-references';
import { valueTypeLabel } from '../reference';
import { RuleExpressionView } from './RuleExpressionView';

export function RuleBehaviorSummary({
  condition,
  output,
  expressionLanguageVersion,
  flowSlot = 'rule-behavior-flow',
  inputs,
}: {
  condition: RuleDefinitionDetail['condition'];
  output?: RuleDefinitionDetail['output'];
  expressionLanguageVersion?: number;
  flowSlot?: string;
  inputs?: (RuleInputDefinition | RuleDraftInputDefinition)[];
}) {
  const { t, i18n } = useTranslation();
  const languageQuery = useQuery(ruleExpressionLanguageQueryOptions());

  return (
    <div data-slot="rule-behavior-summary">
      <ol data-slot="rule-behavior-steps" className="relative">
        <li data-slot="rule-input-contract" className="flex min-w-0 gap-3">
          <RuleBehaviorMarker icon={LogIn} position="start" />
          <div className="min-w-0 flex-1 pb-4">
            <h3 className="flex h-6 items-center text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              {t('rules.inputs')}
            </h3>
            <div className="mt-1.5 min-w-0">
              {inputs?.length ? (
                <dl className="space-y-2">
                  {inputs.map((input) => {
                    const inputLabel = input.label ?? t('rules.unknownInput');
                    const acceptedTypes = (input.types ?? []).map((type) => ({
                      type,
                      label: valueTypeLabel(languageQuery.data, type, i18n.language),
                    }));
                    const requirement = input.isRequired
                      ? t('rules.inputRequirementRequired')
                      : t('rules.inputRequirementOptional');
                    return (
                      <div key={'key' in input ? (input.key ?? inputLabel) : inputLabel}>
                        <dt className="flex flex-wrap items-baseline gap-x-1.5 gap-y-0.5">
                          <span className="text-sm font-medium text-foreground">{inputLabel}</span>
                          <span className="text-xs text-muted-foreground" aria-hidden="true">
                            ·
                          </span>
                          <span className="text-xs text-muted-foreground">{requirement}</span>
                        </dt>
                        <dd className="mt-1 space-y-1">
                          <div className="flex flex-wrap items-center gap-1.5">
                            <span className="text-xs text-muted-foreground">
                              {t('rules.acceptedTypes')}
                            </span>
                            {acceptedTypes.length ? (
                              acceptedTypes.map(({ type, label }) => (
                                <MetadataTag key={type}>{label}</MetadataTag>
                              ))
                            ) : (
                              <span className="text-xs text-muted-foreground">
                                {t('rules.unknownValueType')}
                              </span>
                            )}
                          </div>
                          {input.allowedValues?.length ? (
                            <p className="text-xs text-muted-foreground">
                              {t('rules.allowedValues')}: {input.allowedValues.join(', ')}
                            </p>
                          ) : null}
                        </dd>
                      </div>
                    );
                  })}
                </dl>
              ) : (
                <p className="text-sm text-muted-foreground">{t('rules.notSet')}</p>
              )}
            </div>
          </div>
        </li>

        <li data-slot={flowSlot} className="flex min-w-0 gap-3">
          <RuleBehaviorMarker icon={GitBranch} position="middle" />
          <div className="min-w-0 flex-1 pb-4">
            <h3 className="flex h-6 items-center text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              {t('rules.logic')}
            </h3>
            <div className="mt-1.5 min-w-0">
              <RuleLogicPreview
                condition={condition}
                expressionLanguageVersion={expressionLanguageVersion}
                inputs={inputs}
              />
            </div>
          </div>
        </li>

        <li data-slot="rule-output-contract" className="flex min-w-0 gap-3">
          <RuleBehaviorMarker icon={LogOut} position="end" />
          <div className="min-w-0 flex-1">
            <h3 className="flex h-6 items-center text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              {t('rules.outputs')}
            </h3>
            <div className="mt-1.5 min-w-0">
              <RuleOutputSummary output={output} />
            </div>
          </div>
        </li>
      </ol>
    </div>
  );
}

function RuleBehaviorMarker({
  icon: Icon,
  position,
}: {
  icon: LucideIcon;
  position: 'start' | 'middle' | 'end';
}) {
  return (
    <div aria-hidden="true" className="relative flex w-6 shrink-0 self-stretch justify-center">
      <span
        data-slot="rule-behavior-rail-connector"
        className={
          position === 'start'
            ? 'absolute top-3 bottom-0 left-3 w-px bg-border'
            : position === 'end'
              ? 'absolute top-0 left-3 h-3 w-px bg-border'
              : 'absolute inset-y-0 left-3 w-px bg-border'
        }
      />
      <span
        data-slot="rule-behavior-rail-node"
        className="relative z-10 flex size-6 items-center justify-center rounded-full border bg-background text-muted-foreground"
      >
        <Icon className="size-3.5" strokeWidth={1.75} />
      </span>
    </div>
  );
}

export function RuleLogicPreview({
  condition,
  expressionLanguageVersion,
  inputs,
}: {
  condition: RuleDefinitionDetail['condition'];
  expressionLanguageVersion?: number;
  inputs?: (RuleInputDefinition | RuleDraftInputDefinition)[];
}) {
  const { t, i18n } = useTranslation();
  const projectionRequest = useMemo(
    () => ({
      expressionLanguageVersion,
      inputs: toDraftInputs(inputs ?? []),
      condition: condition ?? undefined,
      language: i18n.language,
    }),
    [condition, expressionLanguageVersion, i18n.language, inputs],
  );
  const projectionQuery = useQuery({
    queryKey: ruleDefinitionQueryKeys.conditionProjection(projectionRequest),
    queryFn: () => projectRuleCondition(projectionRequest),
    enabled: Boolean(condition),
    staleTime: Number.POSITIVE_INFINITY,
  });
  if (!condition) return <p className="text-sm text-muted-foreground">{t('rules.notSet')}</p>;
  if (projectionQuery.isLoading)
    return (
      <p role="status" className="text-sm text-muted-foreground">
        {t('rules.referenceLoading')}
      </p>
    );
  if (!projectionQuery.data?.display)
    return (
      <p role="alert" className="text-sm text-destructive">
        {t('rules.loadErrorTitle')}
      </p>
    );

  return <RuleExpressionView display={projectionQuery.data.display} />;
}

export function RuleOutputSummary({ output }: { output?: RuleDefinitionDetail['output'] }) {
  const { t, i18n } = useTranslation();
  const languageQuery = useQuery(ruleExpressionLanguageQueryOptions());
  if (!output) return <p className="text-sm text-muted-foreground">{t('rules.notSet')}</p>;

  const outputType = valueTypeLabel(languageQuery.data, output.type, i18n.language);
  return (
    <p data-slot="rule-output-summary" className="text-sm font-medium text-foreground">
      {outputType || t('rules.unknownValueType')}
    </p>
  );
}
