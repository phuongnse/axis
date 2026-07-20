import { type ReactNode, useId } from 'react';
import { useTranslation } from 'react-i18next';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { RuleDefinitionSummary } from '../api';
import {
  fieldTypeTranslationKey,
  ruleDescriptionTranslationKey,
  ruleNameTranslationKey,
  ruleSetupTranslationKey,
} from '../metadata';

export function SystemRuleDetailsDialog({
  definition,
  fallbackTitle,
  loading = false,
  unavailable = false,
  open,
  onOpenChange,
}: {
  definition: RuleDefinitionSummary | null;
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
        <span className="flex items-center gap-1">
          <StatusBadge tone="neutral">{t('rules.builtIn')}</StatusBadge>
          <StatusBadge tone="muted">{t('rules.readOnly')}</StatusBadge>
        </span>
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
              <dl className="grid gap-x-4 gap-y-2 sm:grid-cols-2 xl:grid-cols-3">
                <DetailItem
                  className="sm:col-span-2"
                  label={t('rules.description')}
                  value={description}
                />
                <DetailItem
                  label={t('rules.scope')}
                  value={definition.scope ? t(`rules.scope${definition.scope}`) : '—'}
                />
                <DetailItem
                  label={t('rules.status')}
                  value={
                    definition.status ? t(`rules.status${definition.status}`) : t('rules.notSet')
                  }
                />
                <DetailItem
                  label={t('rules.outcome')}
                  value={t(
                    definition.outcomeKind === 'Decision'
                      ? 'rules.outcomeDecision'
                      : 'rules.outcomeValidation',
                  )}
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
              <dl className="grid gap-2">
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
              headingId={`${detailsId}-parameters`}
              title={t('rules.parameters')}
              description={t('rules.parametersHelp')}
            >
              {parameters.length > 0 ? (
                <dl className="grid gap-x-4 gap-y-2 sm:grid-cols-2">
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
                <p className="text-sm text-muted-foreground">{t('rules.noParameters')}</p>
              )}
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
        'grid gap-2 py-2 first:pt-0 last:pb-0 sm:grid-cols-3 sm:gap-4',
        divided && 'border-t',
      )}
    >
      <div>
        <h3 id={headingId} className="text-sm font-semibold text-foreground">
          {title}
        </h3>
        <p className="mt-1 text-xs text-muted-foreground">{description}</p>
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
    <div className={cn('space-y-1', className)}>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="text-sm font-medium text-foreground">{value}</dd>
    </div>
  );
}
