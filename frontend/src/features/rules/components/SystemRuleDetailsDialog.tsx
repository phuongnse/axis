import { type ReactNode, useId } from 'react';
import { useTranslation } from 'react-i18next';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import { StatusBadge } from '@/components/shared/StatusBadge';
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from '@/components/ui/accordion';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
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
  const setup =
    setupKey === 'rules.setup.required'
      ? t('rules.readyToUse')
      : setupKey
        ? t(setupKey)
        : t('rules.setup.configured');

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
              <Card size="sm">
                <CardContent
                  data-slot="system-rule-behavior-grid"
                  className="grid gap-5 @md/system-rule-details:grid-cols-2"
                >
                  <div className="space-y-2">
                    <p className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                      {t('rules.when')}
                    </p>
                    {definition.condition ? (
                      <RuleExpressionView condition={definition.condition} />
                    ) : (
                      <p className="text-sm text-muted-foreground">{t('rules.notSet')}</p>
                    )}
                  </div>
                  <div className="space-y-3">
                    <p className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">
                      {t('rules.then')}
                    </p>
                    <div className="flex flex-wrap gap-2">
                      <Badge variant="outline">
                        {t(
                          definition.outcomeKind === 'Decision'
                            ? 'rules.outcomeDecision'
                            : 'rules.outcomeValidation',
                        )}
                      </Badge>
                      {definition.outcome?.severity ? (
                        <Badge variant="destructive">
                          {t(`rules.severity${definition.outcome.severity}`)}
                        </Badge>
                      ) : null}
                    </div>
                    <p className="text-sm leading-relaxed font-medium text-foreground">
                      {definition.outcome?.message ??
                        definition.outcome?.decision ??
                        t('rules.notSet')}
                    </p>
                  </div>
                </CardContent>
              </Card>
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
                  label={t('rules.fieldTypes')}
                  value={
                    targetTypes.length > 0 ? (
                      <span className="flex flex-wrap gap-2">
                        {targetTypes.map((fieldType) => (
                          <Badge key={fieldType} variant="secondary">
                            {t(fieldTypeTranslationKey(fieldType))}
                          </Badge>
                        ))}
                      </span>
                    ) : (
                      t('rules.contextUnavailable')
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
              </section>
            ) : null}

            <div className="border-t pt-3">
              <Accordion>
                <AccordionItem value="technical-details">
                  <AccordionTrigger>
                    <span>
                      <span className="block">{t('rules.technicalDetails')}</span>
                      <span className="mt-1 block text-xs font-normal text-muted-foreground">
                        {t('rules.technicalDetailsDescription')}
                      </span>
                    </span>
                  </AccordionTrigger>
                  <AccordionContent>
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
                  </AccordionContent>
                </AccordionItem>
              </Accordion>
            </div>
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
