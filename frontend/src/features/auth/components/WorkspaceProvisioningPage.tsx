import { Link } from '@tanstack/react-router';
import { AlertCircle, CheckCircle2, Circle, Loader2 } from 'lucide-react';

import { AuthCard } from '@/features/auth/components/AuthCard';
import { useProvisioningStatus } from '@/features/auth/hooks/useProvisioningStatus';
import type { ProvisioningStepVisual } from '@/features/auth/provisioning-steps';
import { loadRegistrationContext } from '@/features/auth/registration-context';
import { useQueryParam } from '@/features/auth/use-query-param';
import { cn } from '@/lib/utils';

const STEP_LABELS = [
  { label: 'Creating your workspace', sub: "Preparing your organization's data" },
  { label: 'Assigning admin role', sub: null },
  { label: 'Opening workspace', sub: null },
] as const;

function StepIcon({ state }: { state: ProvisioningStepVisual }) {
  if (state === 'complete') {
    return <CheckCircle2 className="h-4 w-4 text-emerald-600" aria-hidden />;
  }
  if (state === 'active') {
    return <Loader2 className="h-4 w-4 animate-spin text-primary" aria-hidden />;
  }
  if (state === 'failed') {
    return <AlertCircle className="h-4 w-4 text-destructive" aria-hidden />;
  }
  return <Circle className="h-4 w-4 text-muted-foreground/40" aria-hidden />;
}

export function WorkspaceProvisioningPage() {
  const token = useQueryParam('token');
  const context = loadRegistrationContext();
  const organizationName = context?.organizationName ?? 'your organization';
  const { status, uiState, loading, error } = useProvisioningStatus(token);

  if (!token) {
    return (
      <AuthCard title="Setup unavailable">
        <p className="text-sm text-muted-foreground">
          Missing provisioning token. Open the verification link from your email or{' '}
          <Link to="/login" className="font-medium text-primary hover:underline">
            sign in
          </Link>
          .
        </p>
      </AuthCard>
    );
  }

  if (loading && !status) {
    return (
      <AuthCard title={`Setting up "${organizationName}" workspace…`}>
        <div className="flex items-center gap-3 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
          <span>Loading provisioning status…</span>
        </div>
      </AuthCard>
    );
  }

  if (error && !status) {
    return (
      <AuthCard title="Setup unavailable">
        <p className="text-sm text-muted-foreground">
          Unable to load provisioning status.{' '}
          <Link to="/login" className="font-medium text-primary hover:underline">
            Sign in
          </Link>{' '}
          to continue.
        </p>
      </AuthCard>
    );
  }

  const failed = uiState?.failed ?? false;

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-6">
      <div className="w-full max-w-lg space-y-6">
        <div className="text-center space-y-3">
          <div
            className={cn(
              'mx-auto flex h-14 w-14 items-center justify-center rounded-full',
              failed ? 'bg-destructive/10 text-destructive' : 'bg-primary/10 text-primary',
            )}
          >
            {failed ? (
              <AlertCircle className="h-7 w-7" aria-hidden />
            ) : (
              <Loader2 className="h-7 w-7 animate-spin" aria-hidden />
            )}
          </div>
          <h1 className="text-xl font-semibold text-foreground">
            {failed ? 'Setup failed' : `Setting up "${organizationName}" workspace…`}
          </h1>
          {failed ? (
            <p className="text-sm text-muted-foreground">Provisioning failed after 3 attempts.</p>
          ) : null}
        </div>

        <div className="rounded-xl border border-border bg-card p-6 shadow-sm space-y-4">
          {STEP_LABELS.map((step, index) => {
            const stepState = uiState?.steps[index] ?? 'pending';
            return (
              <div key={step.label} className="flex items-start gap-3">
                <div className="mt-0.5">
                  <StepIcon state={stepState} />
                </div>
                <div>
                  <p
                    className={cn(
                      'text-sm font-medium',
                      stepState === 'pending' ? 'text-muted-foreground/60' : 'text-foreground',
                    )}
                  >
                    {step.label}
                  </p>
                  {step.sub ? (
                    <p className="text-xs text-muted-foreground mt-0.5">{step.sub}</p>
                  ) : null}
                </div>
              </div>
            );
          })}

          {uiState?.showAttemptLine ? (
            <p className="text-xs text-muted-foreground pt-2">
              Processing attempt {uiState.attemptCount} of 3
            </p>
          ) : null}

          {failed ? (
            <div className="pt-2 space-y-2 text-sm">
              <p>
                <span className="text-muted-foreground">Automatic retries were exhausted. </span>
                <span className="font-medium text-primary">Try again</span>
                <span className="text-muted-foreground"> (coming soon)</span>
              </p>
              <p>
                <span className="text-muted-foreground">If the issue persists, </span>
                <a
                  href="mailto:support@axis.app"
                  className="font-medium text-primary hover:underline"
                >
                  contact support →
                </a>
              </p>
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
