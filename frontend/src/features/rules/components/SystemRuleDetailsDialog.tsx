import { type ReactNode, useId } from 'react';
import { useTranslation } from 'react-i18next';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import { MetadataTag } from '@/components/shared/MetadataTag';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import { referenceContent } from '@/lib/reference-metadata';
import { cn } from '@/lib/utils';
import type { RuleDefinitionDetail } from '../api';
import { compareFieldTypes, fieldTypeTranslationKey } from '../metadata';
import { RuleBehaviorSummary } from './RuleBehaviorSummary';
import { RuleOriginBadge } from './RuleOriginBadge';

export function SystemRuleDetailsDialog({
  definition,
  fallbackTitle,
  loading = false,
  unavailable = false,
  open,
  onOpenChange,
}: {
  definition: RuleDefinitionDetail | null;
  fallbackTitle?: string;
  loading?: boolean;
  unavailable?: boolean;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { t, i18n } = useTranslation();
  const detailsId = useId();

  const documentation = referenceContent(definition?.documentation, i18n.language);
  const name =
    documentation?.displayName ?? definition?.name ?? fallbackTitle ?? t('rules.unknownRule');
  const description =
    documentation?.summary ?? definition?.description ?? t('rules.unknownRuleDescription');
  const targetTypes = [...(definition?.applicability?.targetTypeKeys ?? [])].sort(
    compareFieldTypes,
  );
  const parameters = definition?.parameters ?? [];
  const setup =
    documentation?.usage ??
    t(parameters.length > 0 ? 'rules.setup.configured' : 'rules.setup.none');

  return (
    <ManagedDialog
      open={open}
      onOpenChange={onOpenChange}
      title={name}
      titleAccessory={
        <>
          <RuleOriginBadge origin="System" />
          <StatusBadge tone="success">{t('rules.statusPublished')}</StatusBadge>
        </>
      }
      footer={
        <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
          {t('app.close')}
        </Button>
      }
    >
      <ManagedDialogBody>
        {loading ? <p role="status">{t('rules.loadingRule')}</p> : null}
        {unavailable ? (
          <p role="alert" className="text-sm text-muted-foreground">
            {t('dialog.unavailable')}
          </p>
        ) : null}
        {definition ? (
          <div data-slot="system-rule-details" className="@container/system-rule-details space-y-6">
            <p
              data-slot="system-rule-summary"
              className="text-sm leading-relaxed text-muted-foreground"
            >
              {description}
            </p>

            <section
              aria-labelledby={`${detailsId}-behavior`}
              data-slot="system-rule-behavior"
              className="space-y-4"
            >
              <SectionHeading
                headingId={`${detailsId}-behavior`}
                title={t('rules.whatThisRuleDoes')}
                description={t('rules.ruleBehaviorDescription')}
              />
              <RuleBehaviorSummary
                definitionKey={definition.definitionKey}
                condition={definition.condition}
                outcome={definition.outcome}
                expressionLanguageVersion={definition.expressionLanguageVersion}
                flowSlot="system-rule-behavior-flow"
                outcomeSlot="system-rule-outcome"
                effectSlot="system-rule-effect"
                parameters={definition.parameters}
                references
              />
            </section>

            <section
              aria-labelledby={`${detailsId}-applicability`}
              data-slot="system-rule-applicability"
              className="space-y-4 border-t pt-6"
            >
              <SectionHeading
                headingId={`${detailsId}-applicability`}
                title={t('rules.whereThisRuleApplies')}
                description={t('rules.applicabilityDescription')}
              />
              <dl
                data-slot="system-rule-applicability-grid"
                className="grid gap-5 @md/system-rule-details:grid-cols-2"
              >
                <DetailItem
                  label={t('rules.scope')}
                  value={
                    <>
                      <span>{definition.scope ? t(`rules.scope${definition.scope}`) : '—'}</span>
                      {definition.scope ? (
                        <span className="mt-1 block text-xs leading-relaxed font-normal text-muted-foreground">
                          {t(`rules.scope${definition.scope}Description`)}
                        </span>
                      ) : null}
                    </>
                  }
                />
                <DetailItem
                  label={t('rules.supportedFieldTypes')}
                  value={
                    targetTypes.length > 0 ? (
                      <span className="flex flex-wrap gap-2">
                        {targetTypes.map((fieldType) => (
                          <MetadataTag key={fieldType}>
                            {t(fieldTypeTranslationKey(fieldType))}
                          </MetadataTag>
                        ))}
                      </span>
                    ) : (
                      t('rules.fieldTypesUnavailable')
                    )
                  }
                />
                <DetailItem label={t('rules.setupColumn')} value={setup} />
              </dl>
            </section>

            {parameters.length > 0 ? (
              <section
                aria-labelledby={`${detailsId}-parameters`}
                data-slot="system-rule-parameters"
                className="space-y-4 border-t pt-6"
              >
                <SectionHeading
                  headingId={`${detailsId}-parameters`}
                  title={t('rules.parameters')}
                  description={t('rules.parametersHelp')}
                />
                <dl className="grid gap-x-6 gap-y-5 @md/system-rule-details:grid-cols-2">
                  {parameters.map((parameter) => (
                    <DetailItem
                      key={parameter.key}
                      label={parameter.key || t('rules.unnamedParameter')}
                      value={
                        <span className="flex flex-wrap gap-2">
                          <MetadataTag>
                            {parameter.type
                              ? t(`rules.parameterType${parameter.type}`)
                              : t('rules.unknownParameterType')}
                          </MetadataTag>
                          <MetadataTag>
                            {t(
                              parameter.isRequired
                                ? 'rules.parameterRequired'
                                : 'rules.parameterOptional',
                            )}
                          </MetadataTag>
                        </span>
                      }
                    />
                  ))}
                </dl>
              </section>
            ) : null}

            <section
              aria-labelledby={`${detailsId}-version-references`}
              className="space-y-4 border-t pt-6"
            >
              <SectionHeading
                headingId={`${detailsId}-version-references`}
                title={t('rules.versionAndReferences')}
                description={t('rules.versionAndReferencesDescription')}
              />
              <dl className="grid gap-5 @md/system-rule-details:grid-cols-2">
                <DetailItem
                  label={t('rules.publishedVersion')}
                  value={t('rules.version', {
                    version: definition.latestPublishedVersion ?? 1,
                  })}
                />
                <DetailItem
                  label={t('rules.expressionLanguage')}
                  value={t('rules.expressionLanguageVersion', {
                    version: definition.expressionLanguageVersion ?? 1,
                  })}
                />
                {definition.outcome?.violationCode ? (
                  <DetailItem
                    label={t('rules.violationCode')}
                    value={definition.outcome.violationCode}
                  />
                ) : null}
              </dl>
            </section>
          </div>
        ) : null}
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function SectionHeading({
  headingId,
  title,
  description,
}: {
  headingId: string;
  title: string;
  description: string;
}) {
  return (
    <div>
      <h3 id={headingId} className="text-sm font-semibold text-foreground">
        {title}
      </h3>
      <p className="mt-1 text-xs/relaxed text-muted-foreground">{description}</p>
    </div>
  );
}

function DetailItem({
  label,
  value,
  className,
}: {
  label: string;
  value: ReactNode;
  className?: string;
}) {
  return (
    <div className={cn('space-y-2', className)}>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="text-sm leading-relaxed font-medium text-foreground">{value}</dd>
    </div>
  );
}
