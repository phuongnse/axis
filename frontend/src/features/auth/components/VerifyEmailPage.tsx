import { Link } from '@tanstack/react-router';
import { AlertCircle, CheckCircle2, Clock, Loader2 } from 'lucide-react';
import { useEffect, useState } from 'react';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { useVerifyEmail } from '@/features/auth/hooks/useVerifyEmail';
import { loadRegistrationContext } from '@/features/auth/registration-context';
import type { VerifyEmailErrorKind } from '@/features/auth/types';
import { useQueryParam } from '@/features/auth/use-query-param';

function VerifyEmailOutcome({
  kind,
  email,
  onResend,
  resendLoading,
}: {
  kind: VerifyEmailErrorKind;
  email: string;
  onResend: (email: string) => Promise<void>;
  resendLoading: boolean;
}) {
  const [resendEmail, setResendEmail] = useState(email);
  const showResend = kind === 'expired' || kind === 'rate_limited';

  const config = {
    expired: {
      icon: Clock,
      title: 'Verification link expired',
      body: 'This link was valid for 24 hours. Request a new verification email.',
      iconClass: 'text-amber-600 bg-amber-500/10',
    },
    already_used: {
      icon: CheckCircle2,
      title: 'Already verified',
      body: 'This link has already been used.',
      iconClass: 'text-muted-foreground bg-muted',
    },
    invalid: {
      icon: AlertCircle,
      title: 'Invalid verification link',
      body: 'This link is invalid or has been tampered with.',
      iconClass: 'text-destructive bg-destructive/10',
    },
    rate_limited: {
      icon: Clock,
      title: 'Please wait',
      body: 'Too many resend requests for this email.',
      iconClass: 'text-amber-600 bg-amber-500/10',
    },
  }[kind];

  const Icon = config.icon;

  return (
    <AuthCard
      title={config.title}
      footer={
        kind === 'already_used' ? (
          <>
            Ready to continue?{' '}
            <Link to="/login" className="font-medium hover:underline">
              Go to sign in →
            </Link>
          </>
        ) : kind === 'invalid' ? (
          <Link to="/register" className="font-medium hover:underline">
            Back to registration
          </Link>
        ) : undefined
      }
    >
      <div className="space-y-4">
        <div className="flex items-start gap-3">
          <div
            className={`mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full ${config.iconClass}`}
          >
            <Icon className="h-4 w-4" aria-hidden />
          </div>
          <p className="text-sm text-muted-foreground">{config.body}</p>
        </div>

        {showResend ? (
          <form
            className="space-y-3"
            onSubmit={(event) => {
              event.preventDefault();
              void onResend(resendEmail.trim());
            }}
          >
            <div className="space-y-1.5">
              <Label htmlFor="resend-email">Email address</Label>
              <Input
                id="resend-email"
                type="email"
                autoComplete="email"
                value={resendEmail}
                onChange={(event) => setResendEmail(event.target.value)}
                disabled={kind === 'rate_limited' || resendLoading}
              />
            </div>
            <Button
              type="submit"
              variant="cta"
              className="w-full h-9"
              disabled={!resendEmail.trim() || kind === 'rate_limited' || resendLoading}
            >
              {resendLoading ? 'Sending…' : 'Resend verification email'}
            </Button>
          </form>
        ) : null}
      </div>
    </AuthCard>
  );
}

export function VerifyEmailPage() {
  const token = useQueryParam('token');
  const context = loadRegistrationContext();
  const { submit, loading, errorKind } = useVerifyEmail();
  const { resend, state: resendState } = useResendVerification();
  const [started, setStarted] = useState(false);

  useEffect(() => {
    if (!token || started) return;
    setStarted(true);
    void submit(token).catch(() => {
      // Error state is shown via errorKind.
    });
  }, [token, started, submit]);

  if (!token) {
    return (
      <VerifyEmailOutcome
        kind="invalid"
        email={context?.email ?? ''}
        onResend={async () => {}}
        resendLoading={false}
      />
    );
  }

  if (loading) {
    return (
      <AuthCard title="Verifying your email">
        <div className="flex items-center gap-3 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
          <span>Confirming your verification link…</span>
        </div>
      </AuthCard>
    );
  }

  if (errorKind) {
    return (
      <VerifyEmailOutcome
        kind={errorKind}
        email={context?.email ?? ''}
        onResend={resend}
        resendLoading={resendState === 'sending'}
      />
    );
  }

  return (
    <AuthCard title="Verifying your email">
      <div className="flex items-center gap-3 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
        <span>Redirecting to workspace setup…</span>
      </div>
    </AuthCard>
  );
}
