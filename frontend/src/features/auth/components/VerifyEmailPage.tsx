import { zodResolver } from '@hookform/resolvers/zod';
import { Link } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import { AlertCircle, CheckCircle2, Clock, Loader2, Mail } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';

import { Button } from '@/components/ui/button';
import { FormField } from '@/components/ui/form-field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { useVerifyEmail } from '@/features/auth/hooks/useVerifyEmail';
import { loadRegistrationContext } from '@/features/auth/registration-context';
import type { VerifyEmailErrorKind } from '@/features/auth/types';
import { useQueryParam } from '@/features/auth/use-query-param';

function createResendSchema(t: TFunction) {
  return z.object({
    email: z.string().min(1, t('validation.emailRequired')).email(t('validation.emailInvalid')),
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
  const { t } = useTranslation();
  const resendSchema = useMemo(() => createResendSchema(t), [t]);
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
      title: t('verifyEmail.expiredTitle'),
      body: t('verifyEmail.expiredBody'),
      iconClass: 'text-amber-600 bg-amber-500/10',
    },
    already_used: {
      icon: CheckCircle2,
      title: t('verifyEmail.alreadyUsedTitle'),
      body: t('verifyEmail.alreadyUsedBody'),
      iconClass: 'text-muted-foreground bg-muted',
    },
    invalid: {
      icon: AlertCircle,
      title: t('verifyEmail.invalidTitle'),
      body: t('verifyEmail.invalidBody'),
      iconClass: 'text-destructive bg-destructive/10',
    },
    rate_limited: {
      icon: Clock,
      title: t('verifyEmail.rateLimitedTitle'),
      body: t('verifyEmail.rateLimitedBody'),
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
            {t('verifyEmail.readyToContinue')}{' '}
            <Link to="/login" className="font-medium hover:underline">
              {t('emailConfirmation.goToSignIn')}
            </Link>
          </>
        ) : kind === 'invalid' ? (
          <Link to="/register" className="font-medium hover:underline">
            {t('emailConfirmation.backToRegistration')}
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
            onSubmit={handleSubmit((values) => void onResend(values.email.trim()))}
            noValidate
          >
            <FormField
              id="resend-email"
              label={t('common.emailAddress')}
              helpText={t('verifyEmail.resendEmailHelp')}
              error={errors.email?.message}
            >
              {({ describedBy }) => (
                <Input
                  id="resend-email"
                  type="email"
                  autoComplete="email"
                  aria-describedby={describedBy}
                  aria-invalid={errors.email ? true : undefined}
                  disabled={kind === 'rate_limited' || resendLoading}
                  {...registerResend('email')}
                />
              )}
            </FormField>
            <Button
              type="submit"
              variant="cta"
              className="w-full h-9"
              disabled={kind === 'rate_limited' || resendLoading}
            >
              {resendLoading ? (
                <Loader2 className="size-4 animate-spin" aria-hidden />
              ) : (
                <Mail className="size-4" aria-hidden />
              )}
              {resendLoading ? t('verifyEmail.sending') : t('verifyEmail.resendVerificationEmail')}
            </Button>
          </form>
        ) : null}
      </div>
    </AuthCard>
  );
}

export function VerifyEmailPage() {
  const { t } = useTranslation();
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
      <AuthCard title={t('verifyEmail.verifyingTitle')}>
        <div className="flex items-center gap-3 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
          <span>{t('verifyEmail.confirmingLink')}</span>
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
    <AuthCard title={t('verifyEmail.verifyingTitle')}>
      <div className="flex items-center gap-3 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
        <span>{t('verifyEmail.completingSignIn')}</span>
      </div>
    </AuthCard>
  );
}
