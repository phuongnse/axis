import { type ReactNode, useId } from 'react';
import { useTranslation } from 'react-i18next';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { RuleDefinitionDetail } from '../api';
import {
  fieldTypeTranslationKey,
  ruleDescriptionTranslationKey,
  ruleNameTranslationKey,
  ruleSetupTranslationKey,
} from '../metadata';
import { RuleExpressionView } from './RuleExpressionView';
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
  const { t } = useTranslation();
  const detailsId = useId();

  const nameKey = ruleNameTranslationKey(definition?.definitionKey);
  const descriptionKey = ruleDescriptionTranslationKey(definition?.definitionKey);
  const setupKey = ruleSetupTranslationKey(definition?.definitionKey);
  const name = nameKey ? t(nameKey) : (definition?.name ?? fallbackTitle ?? t('rules.unknownRule'));
  const description = descriptionKey
    ? t(descriptionKey)
    : (definition?.description ?? t('rules.unknownRuleDescription'));
  const targetTypes = definition?.applicability?.targetTypeKeys ?? [];
  const parameters = definition?.parameters ?? [];

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
      autoSizeKey={`system:${definition?.definitionKey ?? fallbackTitle ?? 'unknown'}`}
      autoSizeReady={!loading}
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
          <div>
            <DetailsSection
              headingId={`${detailsId}-definition`}
              title={t('rules.definitionSection')}
              description={t('rules.definitionDetailsDescription')}
            >
              <dl className="grid gap-x-6 gap-y-5 sm:grid-cols-2 xl:grid-cols-3">
                <DetailItem
                  className="sm:col-span-2"
                  label={t('rules.description')}
                  value={description}
                />
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
                  label={t('rules.versionHistory')}
                  value={t('rules.version', {
                    version: definition.latestPublishedVersion ?? 1,
                  })}
                />
              </dl>
            </DetailsSection>

            <DetailsSection
              divided
              headingId={`${detailsId}-field-types`}
              title={t('rules.supportedFieldTypes')}
              description={t('rules.supportedFieldTypesDescription')}
            >
              <dl className="grid gap-y-5">
                <DetailItem
                  label={t('rules.fieldTypes')}
                  value={
                    targetTypes.length > 0
                      ? targetTypes
                          .map((fieldType) => t(fieldTypeTranslationKey(fieldType)))
                          .join(', ')
                      : t('rules.contextUnavailable')
                  }
                />
                <DetailItem
                  label={t('rules.setupColumn')}
                  value={setupKey ? t(setupKey) : t('rules.setup.configured')}
                />
              </dl>
            </DetailsSection>

            <DetailsSection
              divided
              headingId={`${detailsId}-logic`}
              title={t('rules.ruleLogic')}
              description={t('rules.ruleLogicDescription')}
            >
              <dl className="grid gap-y-5">
                <DetailItem
                  label={t('rules.expressionLanguage')}
                  value={t('rules.expressionLanguageVersion', {
                    version: definition.expressionLanguageVersion ?? 1,
                  })}
                />
                <DetailItem
                  label={t('rules.expression')}
                  value={
                    definition.condition ? (
                      <RuleExpressionView condition={definition.condition} />
                    ) : (
                      t('rules.notSet')
                    )
                  }
                />
              </dl>
            </DetailsSection>

            <DetailsSection
              divided
              headingId={`${detailsId}-parameters`}
              title={t('rules.parameters')}
              description={t('rules.parametersHelp')}
            >
              {parameters.length > 0 ? (
                <dl className="grid gap-x-6 gap-y-5 sm:grid-cols-2">
                  {parameters.map((parameter) => (
                    <DetailItem
                      key={parameter.key}
                      label={parameter.key || t('rules.unnamedParameter')}
                      value={`${
                        parameter.type
                          ? t(`rules.parameterType${parameter.type}`)
                          : t('rules.unknownParameterType')
                      } · ${t(
                        parameter.isRequired
                          ? 'rules.parameterRequired'
                          : 'rules.parameterOptional',
                      )}`}
                    />
                  ))}
                </dl>
              ) : (
                <p className="text-sm leading-relaxed text-muted-foreground">
                  {t('rules.noParameters')}
                </p>
              )}
            </DetailsSection>

            <DetailsSection
              divided
              headingId={`${detailsId}-outcome`}
              title={t('rules.outcome')}
              description={t('rules.outcomeDetailsDescription')}
            >
              <dl className="grid gap-x-6 gap-y-5 sm:grid-cols-2">
                <DetailItem
                  label={t('rules.type')}
                  value={t(
                    definition.outcomeKind === 'Decision'
                      ? 'rules.outcomeDecision'
                      : 'rules.outcomeValidation',
                  )}
                />
                {definition.outcome?.severity ? (
                  <DetailItem
                    label={t('rules.severity')}
                    value={t(`rules.severity${definition.outcome.severity}`)}
                  />
                ) : null}
                {definition.outcome?.violationCode ? (
                  <DetailItem
                    label={t('rules.violationCode')}
                    value={definition.outcome.violationCode}
                  />
                ) : null}
                <DetailItem
                  className="sm:col-span-2"
                  label={t('rules.message')}
                  value={
                    definition.outcome?.message ?? definition.outcome?.decision ?? t('rules.notSet')
                  }
                />
              </dl>
            </DetailsSection>
          </div>
        ) : null}
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function DetailsSection({
  headingId,
  title,
  description,
  divided = false,
  children,
}: {
  headingId: string;
  title: string;
  description: string;
  divided?: boolean;
  children: ReactNode;
}) {
  return (
    <section
      aria-labelledby={headingId}
      data-slot="system-rule-details-section"
      className={cn(
        'grid gap-4 py-6 first:pt-0 last:pb-0 sm:grid-cols-3 sm:gap-8',
        divided && 'border-t',
      )}
    >
      <div>
        <h3 id={headingId} className="text-sm font-semibold text-foreground">
          {title}
        </h3>
        <p className="mt-2 text-xs/relaxed text-muted-foreground">{description}</p>
      </div>
      <div data-slot="system-rule-details-section-content" className="sm:col-span-2">
        {children}
      </div>
    </section>
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
