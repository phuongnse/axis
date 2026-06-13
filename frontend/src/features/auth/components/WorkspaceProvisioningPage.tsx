import { Link } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import { AlertCircle, Database, Loader2, RefreshCw, ShieldCheck, Workflow } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { FlowTrace, type FlowTraceState, type FlowTraceStep } from '@/components/visual/FlowTrace';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useProvisioningStatus } from '@/features/auth/hooks/useProvisioningStatus';
import { useRetryProvisioning } from '@/features/auth/hooks/useRetryProvisioning';
import type { ProvisioningStepVisual } from '@/features/auth/provisioning-steps';
import { loadRegistrationContext } from '@/features/auth/registration-context';
import { useQueryParam } from '@/features/auth/use-query-param';
import { cn } from '@/lib/utils';

const STEP_LABELS = [
  {
    labelKey: 'provisioning.steps.workspace',
    subKey: 'provisioning.steps.workspaceMeta',
    icon: Database,
  },
  {
    labelKey: 'provisioning.steps.admin',
    subKey: 'provisioning.steps.adminMeta',
    icon: ShieldCheck,
  },
  { labelKey: 'provisioning.steps.open', subKey: 'provisioning.steps.openMeta', icon: Workflow },
] as const;

function toTraceState(state: ProvisioningStepVisual | undefined): FlowTraceState {
  if (state === 'complete' || state === 'active' || state === 'failed') return state;
  return 'pending';
}

function buildTrace(steps: ProvisioningStepVisual[] | undefined, t: TFunction): FlowTraceStep[] {
  return STEP_LABELS.map((step, index) => ({
    id: step.labelKey,
    label: t(step.labelKey),
    meta: t(step.subKey),
    icon: step.icon,
    state: toTraceState(steps?.[index]),
  }));
}

export function WorkspaceProvisioningPage() {
  const { t } = useTranslation();
  const token = useQueryParam('token');
  const context = loadRegistrationContext();
  const organizationName = context?.organizationName ?? t('provisioning.organizationFallback');
  const { status, uiState, loading, error } = useProvisioningStatus(token);
  const retry = useRetryProvisioning(token);

  if (!token) {
    return (
      <AuthCard title={t('provisioning.setupUnavailable')}>
        <p className="text-sm text-muted-foreground">
          {t('provisioning.missingToken')}{' '}
          <Link to="/login" className="font-medium text-primary hover:underline">
            {t('common.signIn')}
          </Link>{' '}
          {t('provisioning.continueWithSignIn')}
        </p>
      </AuthCard>
    );
  }

  if (loading && !status) {
    return (
      <AuthCard title={t('provisioning.loadingTitle', { organizationName })}>
        <div className="flex items-center gap-3 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
          <span>{t('provisioning.loadingStatus')}</span>
        </div>
      </AuthCard>
    );
  }

  if (error && !status) {
    return (
      <AuthCard title={t('provisioning.setupUnavailable')}>
        <p className="text-sm text-muted-foreground">
          {t('provisioning.loadError')}{' '}
          <Link to="/login" className="font-medium text-primary hover:underline">
            {t('common.signIn')}
          </Link>{' '}
          {t('provisioning.continueWithSignIn')}
        </p>
      </AuthCard>
    );
  }

  const failed = uiState?.failed ?? false;
  const trace = buildTrace(uiState?.steps, t);

  return (
    <div className="axis-grid flex min-h-screen items-center justify-center bg-background p-4 sm:p-6">
      <section className="grid w-full max-w-5xl overflow-hidden rounded-lg border border-border bg-card shadow-sm lg:grid-cols-[0.9fr_1.1fr]">
        <div className="flex flex-col justify-between gap-8 border-b border-border p-6 lg:border-b-0 lg:border-r">
          <div className="space-y-6">
            <div
              className={cn(
                'flex size-14 items-center justify-center rounded-md border',
                failed
                  ? 'border-destructive/25 bg-destructive/10 text-destructive'
                  : 'border-primary/20 bg-primary/10 text-primary',
              )}
            >
              {failed ? (
                <AlertCircle className="size-7" aria-hidden />
              ) : (
                <Loader2 className="size-7 animate-spin" aria-hidden />
              )}
            </div>
            <div className="space-y-2">
              <h1 className="text-2xl font-semibold tracking-tight text-foreground">
                {failed
                  ? t('provisioning.failedTitle')
                  : t('provisioning.loadingTitle', { organizationName })}
              </h1>
              <p className="text-sm text-muted-foreground">
                {failed ? t('provisioning.failedBody') : t('provisioning.activeBody')}
              </p>
            </div>
          </div>

          <div className="grid grid-cols-3 overflow-hidden rounded-md border border-border bg-background/80 text-xs">
            <div className="border-r border-border p-3">
              <p className="text-muted-foreground">{t('provisioning.attempt')}</p>
              <p className="mt-1 text-lg font-semibold text-foreground">
                {uiState?.attemptCount ?? 1}
              </p>
            </div>
            <div className="border-r border-border p-3">
              <p className="text-muted-foreground">{t('provisioning.limit')}</p>
              <p className="mt-1 text-lg font-semibold text-foreground">3</p>
            </div>
            <div className="p-3">
              <p className="text-muted-foreground">{t('provisioning.state')}</p>
              <p className="mt-1 text-lg font-semibold text-foreground">
                {failed ? t('provisioning.failedState') : t('provisioning.liveState')}
              </p>
            </div>
          </div>
        </div>

        <div className="axis-grid-strong space-y-5 bg-background/70 p-6">
          <div className="flex items-center justify-between gap-3">
            <div>
              <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
                {t('provisioning.trace')}
              </p>
              <p className="mt-1 text-sm font-medium text-foreground">{organizationName}</p>
            </div>
            <span
              className={cn(
                'border px-2 py-1 text-xs font-medium',
                failed
                  ? 'border-destructive/25 bg-destructive/10 text-destructive'
                  : 'border-primary/20 bg-primary/10 text-primary',
              )}
            >
              {failed ? t('provisioning.blocked') : t('provisioning.running')}
            </span>
          </div>

          <FlowTrace steps={trace} />

          {uiState?.showAttemptLine ? (
            <p className="text-xs text-muted-foreground">
              {t('provisioning.attemptLine', { attempt: uiState.attemptCount })}
            </p>
          ) : null}

          {failed ? (
            <div className="space-y-3 rounded-md border border-destructive/20 bg-destructive/5 p-4 text-sm">
              <p className="text-muted-foreground">{t('provisioning.exhausted')}</p>
              <Button
                type="button"
                variant="cta"
                className="h-9"
                disabled={retry.isPending}
                onClick={() => void retry.mutateAsync().catch(() => {})}
              >
                {retry.isPending ? (
                  <Loader2 className="size-4 animate-spin" aria-hidden />
                ) : (
                  <RefreshCw className="size-4" aria-hidden />
                )}
                {retry.isPending ? t('provisioning.retrying') : t('provisioning.tryAgain')}
              </Button>
              <p>
                <span className="text-muted-foreground">{t('provisioning.contactPrefix')} </span>
                <a
                  href="mailto:support@axis.app"
                  className="font-medium text-primary hover:underline"
                >
                  {t('provisioning.contactSupport')}
                </a>
              </p>
            </div>
          ) : null}
        </div>
      </section>
    </div>
  );
}
