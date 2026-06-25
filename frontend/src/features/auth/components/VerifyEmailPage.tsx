import { zodResolver } from '@hookform/resolvers/zod';
import { Link } from '@tanstack/react-router';
import { AlertCircle, CheckCircle2, Clock, Loader2, Mail } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { useVerifyEmail } from '@/features/auth/hooks/useVerifyEmail';
import { loadRegistrationContext } from '@/features/auth/registration-context';
import type { VerifyEmailErrorKind } from '@/features/auth/types';
import { useQueryParam } from '@/features/auth/use-query-param';
import { cn } from '@/lib/utils';

function createResendSchema() {
  return z.object({
    email: z.string().min(1, 'Email address is required').email('Enter a valid email address'),
  });
}

type ResendFormValues = z.infer<ReturnType<typeof createResendSchema>>;

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
  const resendSchema = useMemo(() => createResendSchema(), []);
  const {
    register: registerResend,
    handleSubmit,
    formState: { errors },
  } = useForm<ResendFormValues>({
    resolver: zodResolver(resendSchema),
    mode: 'onSubmit',
    defaultValues: { email },
  });
  const showResend = kind === 'expired' || kind === 'rate_limited';

  const config = {
    expired: {
      icon: Clock,
      title: 'Verification link expired',
      body: 'Request a new verification email, then open the latest link.',
      iconClass: 'bg-state-warning-background text-state-warning-foreground',
    },
    already_used: {
      icon: CheckCircle2,
      title: 'Already verified',
      body: 'This email link has already been used.',
      iconClass: 'text-muted-foreground bg-muted',
    },
    invalid: {
      icon: AlertCircle,
      title: 'Invalid verification link',
      body: 'This link is missing or cannot be verified.',
      iconClass: 'text-destructive bg-destructive/10',
    },
    rate_limited: {
      icon: Clock,
      title: 'Please wait',
      body: 'Too many attempts. Try again shortly.',
      iconClass: 'bg-state-warning-background text-state-warning-foreground',
    },
  }[kind];

  const Icon = config.icon;

  return (
    <AuthCard
      title={config.title}
      footer={
        kind === 'already_used' ? (
          <Link to="/register" className="font-medium hover:underline">
            Register another account
          </Link>
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
            className={cn(
              'mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full',
              config.iconClass,
            )}
          >
            <Icon className="h-4 w-4" aria-hidden />
          </div>
          <p className="text-sm text-muted-foreground">{config.body}</p>
        </div>

        {showResend ? (
          <form
            className="space-y-3"
            onSubmit={handleSubmit((values) => void onResend(values.email.trim()))}
            noValidate
          >
            <Field data-invalid={errors.email ? true : undefined}>
              <FieldLabel htmlFor="resend-email">Email address</FieldLabel>
              <Input
                id="resend-email"
                type="email"
                autoComplete="email"
                aria-describedby={
                  errors.email ? 'resend-email-help resend-email-error' : 'resend-email-help'
                }
                aria-invalid={errors.email ? true : undefined}
                disabled={kind === 'rate_limited' || resendLoading}
                {...registerResend('email')}
              />
              <FieldDescription id="resend-email-help">
                Use the email that received the verification link.
              </FieldDescription>
              {errors.email ? (
                <FieldError id="resend-email-error">{errors.email.message}</FieldError>
              ) : null}
            </Field>
            <Button
              type="submit"
              className="w-full h-9"
              disabled={kind === 'rate_limited' || resendLoading}
            >
              {resendLoading ? (
                <Loader2 className="size-4 animate-spin" aria-hidden />
              ) : (
                <Mail className="size-4" aria-hidden />
              )}
              {resendLoading ? 'Sending...' : 'Resend verification email'}
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
      <AuthCard title="Verifying email">
        <div className="flex items-center gap-3 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
          <span>Confirming your verification link...</span>
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
    <AuthCard title="Verifying email">
      <div className="flex items-center gap-3 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
        <span>Completing sign-in...</span>
      </div>
    </AuthCard>
  );
}
