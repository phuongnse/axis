import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import { ManagedDialogTabs } from '@/components/shared/ManagedDialogTabs';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import { referenceContent } from '@/lib/reference-metadata';
import type { RuleDefinitionDetail } from '../api';
import { RuleBehaviorSummary } from './RuleBehaviorSummary';
import { RuleBindingUsagePanel } from './RuleBindingUsagePanel';
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
  const [activeSection, setActiveSection] = useState('general');
  const documentation = referenceContent(definition?.documentation, i18n.language);
  const name =
    documentation?.displayName ?? definition?.name ?? fallbackTitle ?? t('rules.unknownRule');
  const description =
    documentation?.summary ?? definition?.description ?? t('rules.unknownRuleDescription');

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
          <div data-slot="system-rule-details">
            <ManagedDialogTabs
              label={t('rules.definitionSections')}
              generalLabel={t('dialog.general')}
              activeSection={activeSection}
              onActiveSectionChange={setActiveSection}
              general={
                <dl className="grid gap-5 sm:grid-cols-2">
                  <Detail label={t('rules.name')} value={name} />
                  <Detail
                    label={t('rules.publishedVersion')}
                    value={String(definition.latestPublishedVersion ?? 1)}
                  />
                  <div className="sm:col-span-2">
                    <Detail label={t('rules.description')} value={description} />
                  </div>
                </dl>
              }
              sections={[
                {
                  id: 'behavior',
                  label: t('rules.ruleBehavior'),
                  content: (
                    <RuleBehaviorSummary
                      condition={definition.condition}
                      output={definition.output}
                      expressionLanguageVersion={definition.expressionLanguageVersion}
                      inputs={definition.inputs}
                    />
                  ),
                },
                {
                  id: 'usage',
                  label: t('rules.usage'),
                  content: (
                    <RuleBindingUsagePanel
                      definitionKey={definition.definitionKey ?? ''}
                      version={definition.latestPublishedVersion ?? 1}
                      active={activeSection === 'usage'}
                    />
                  ),
                },
              ]}
              systemInfo={{
                label: t('dialog.systemInfo'),
                content: (
                  <dl className="grid gap-5 sm:grid-cols-2">
                    <Detail
                      label={t('rules.definitionKey')}
                      value={definition.definitionKey ?? '—'}
                    />
                    <Detail
                      label={t('rules.expressionLanguage')}
                      value={String(definition.expressionLanguageVersion ?? 1)}
                    />
                  </dl>
                ),
              }}
            />
          </div>
        ) : null}
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="text-sm font-medium">{value}</dd>
    </div>
  );
}
