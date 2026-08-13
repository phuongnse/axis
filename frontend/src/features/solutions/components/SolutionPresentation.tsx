import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import { StatusBadge, type StatusBadgeState } from '@/components/shared/StatusBadge';
import type {
  SolutionComponentPlanDto,
  SolutionComponentStatusDto,
  SolutionOperationStatus,
  SolutionProvisioningStatus,
  SolutionStepStatus,
  SolutionTrustStatus,
  SolutionVersionSummaryDto,
} from '@/lib/api-generated';

export function SolutionTrustBadge({ status }: { status?: SolutionTrustStatus }) {
  const { t } = useTranslation();
  return <StatusBadge state={trustState(status)}>{solutionTrustLabel(status, t)}</StatusBadge>;
}

export function OperationStatusBadge({ status }: { status?: SolutionOperationStatus }) {
  const { t } = useTranslation();
  return <StatusBadge state={operationState(status)}>{operationLabel(status, t)}</StatusBadge>;
}

export function ProvisioningStatusBadge({ status }: { status?: SolutionProvisioningStatus }) {
  const { t } = useTranslation();
  return (
    <StatusBadge state={provisioningState(status)}>{provisioningLabel(status, t)}</StatusBadge>
  );
}

export function ComplianceStatusBadge({ status }: { status?: 'Compliant' | 'Noncompliant' }) {
  const { t } = useTranslation();
  if (!status) return <StatusBadge state="inactive">{t('solutions.notAvailable')}</StatusBadge>;
  return (
    <StatusBadge state={status === 'Compliant' ? 'positive' : 'critical'}>
      {status === 'Compliant' ? t('solutions.compliant') : t('solutions.noncompliant')}
    </StatusBadge>
  );
}

export function VersionFacts({ version }: { version: SolutionVersionSummaryDto }) {
  const { t } = useTranslation();
  const facts = [
    [t('solutions.solutionKey'), version.solutionKey],
    [t('solutions.version'), version.solutionVersion],
    [t('solutions.packageHash'), version.packageSha256],
    [t('solutions.publisher'), version.publisherId],
    [t('solutions.publisherKey'), version.publisherKeyId],
    [t('solutions.openApiHash'), version.axisOpenApiSha256],
    [t('solutions.sourceRevision'), version.sourceRevision],
    [t('solutions.buildId'), version.buildId],
    [t('solutions.sourceUri'), version.sourceUri],
  ];
  return (
    <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
      {facts.map(([label, value]) => (
        <div key={label} className="min-w-0">
          <dt className="font-medium text-muted-foreground">{label}</dt>
          <dd className="break-all">{value || t('solutions.notAvailable')}</dd>
        </div>
      ))}
    </dl>
  );
}

export function ComponentPlan({ components }: { components: SolutionComponentPlanDto[] }) {
  const { t } = useTranslation();
  const titleId = useId();
  if (components.length === 0) {
    return <p className="text-sm text-muted-foreground">{t('solutions.componentPlanEmpty')}</p>;
  }
  return (
    <section className="grid gap-2" aria-labelledby={titleId}>
      <h3 id={titleId} className="font-medium">
        {t('solutions.componentPlanLabel')}
      </h3>
      <ol className="grid gap-2">
        {components.map((component, index) => (
          <li
            key={`${component.type ?? 'component'}-${component.key ?? index}`}
            className="flex min-w-0 items-start gap-3 text-sm"
          >
            <span className="text-muted-foreground">{index + 1}.</span>
            <span className="min-w-0 flex-1">
              <span className="block break-all font-medium">
                {component.key ?? t('solutions.notAvailable')}
              </span>
              <span className="block break-all text-xs text-muted-foreground">
                {component.type ?? t('solutions.notAvailable')} ·{' '}
                {component.sha256 ?? t('solutions.notAvailable')}
              </span>
            </span>
          </li>
        ))}
      </ol>
    </section>
  );
}

export function ComponentSequence({ components }: { components: SolutionComponentStatusDto[] }) {
  const { t } = useTranslation();
  if (components.length === 0) {
    return <p className="text-sm text-muted-foreground">{t('solutions.componentsEmpty')}</p>;
  }
  return (
    <ol className="grid gap-2" aria-label={t('solutions.componentPlanLabel')}>
      {components.map((component, index) => (
        <li
          key={`${component.type ?? 'component'}-${component.key ?? index}`}
          className="flex min-w-0 items-start gap-3 text-sm"
        >
          <span className="text-muted-foreground">{index + 1}.</span>
          <span className="min-w-0 flex-1">
            <span className="block break-all font-medium">
              {component.key ?? t('solutions.notAvailable')}
            </span>
            <span className="block break-all text-xs text-muted-foreground">
              {component.type ?? t('solutions.notAvailable')} ·{' '}
              {component.sha256 ?? t('solutions.notAvailable')}
            </span>
            {component.problemCode ? (
              <span className="block break-all text-xs text-muted-foreground">
                {t('solutions.safeProblemCode', { code: component.problemCode })}
              </span>
            ) : null}
          </span>
          <StatusBadge state={stepState(component.status)}>
            {stepLabel(component.status, t)}
          </StatusBadge>
        </li>
      ))}
    </ol>
  );
}

function trustState(status: SolutionTrustStatus | undefined): StatusBadgeState {
  if (status === 'Trusted') return 'positive';
  if (status === 'Revoked') return 'critical';
  return 'neutral';
}

function operationState(status: SolutionOperationStatus | undefined): StatusBadgeState {
  if (status === 'Succeeded') return 'positive';
  if (status === 'Pending' || status === 'Running') return 'informative';
  if (status === 'Blocked') return 'caution';
  if (status === 'Failed') return 'critical';
  return 'inactive';
}

function provisioningState(status: SolutionProvisioningStatus | undefined): StatusBadgeState {
  if (status === 'Installed') return 'positive';
  if (status === 'Installing') return 'informative';
  if (status === 'Failed') return 'critical';
  return 'inactive';
}

function stepState(status: SolutionStepStatus | undefined): StatusBadgeState {
  if (status === 'Confirmed') return 'positive';
  if (status === 'Applying') return 'informative';
  if (status === 'Pending') return 'neutral';
  if (status === 'Failed') return 'critical';
  return 'inactive';
}

function solutionTrustLabel(
  status: SolutionTrustStatus | undefined,
  t: (key: string) => string,
): string {
  if (status === 'Trusted') return t('solutions.trusted');
  if (status === 'Revoked') return t('solutions.revoked');
  return t('solutions.trustUnknown');
}

function operationLabel(
  status: SolutionOperationStatus | undefined,
  t: (key: string) => string,
): string {
  if (!status) return t('solutions.notAvailable');
  if (status === 'Pending') return t('solutions.operationPending');
  if (status === 'Running') return t('solutions.operationRunning');
  if (status === 'Succeeded') return t('solutions.operationSucceeded');
  if (status === 'Blocked') return t('solutions.operationBlocked');
  return t('solutions.operationFailed');
}

function provisioningLabel(
  status: SolutionProvisioningStatus | undefined,
  t: (key: string) => string,
): string {
  if (!status) return t('solutions.notAvailable');
  if (status === 'Installing') return t('solutions.installingStatus');
  if (status === 'Installed') return t('solutions.installedStatus');
  return t('solutions.failedStatus');
}

function stepLabel(status: SolutionStepStatus | undefined, t: (key: string) => string): string {
  if (!status) return t('solutions.notAvailable');
  if (status === 'Pending') return t('solutions.stepPending');
  if (status === 'Applying') return t('solutions.stepApplying');
  if (status === 'Confirmed') return t('solutions.stepConfirmed');
  return t('solutions.stepFailed');
}
