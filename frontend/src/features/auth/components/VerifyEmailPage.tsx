import { zodResolver } from '@hookform/resolvers/zod';
import { Link } from '@tanstack/react-router';
import { AlertCircle, ArrowRight, CheckCircle2, Clock, Loader2, Mail } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { type FieldPath, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useRefreshClientValidationErrors } from '@/features/auth/hooks/useRefreshClientValidationErrors';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { useVerifyEmail } from '@/features/auth/hooks/useVerifyEmail';
import { loadRegistrationContext } from '@/features/auth/registration-context';
import type { VerifyEmailErrorKind } from '@/features/auth/types';
import { useQueryParam } from '@/features/auth/use-query-param';
import { currentSiteLanguage } from '@/features/preferences';
import { cn } from '@/lib/utils';

type Translate = ReturnType<typeof useTranslation>['t'];

function createResendSchema(t: Translate) {
  return z.object({
    email: z.string().min(1, t('validation.emailRequired')).email(t('validation.emailInvalid')),
  });
}

type ResendFormValues = z.infer<ReturnType<typeof createResendSchema>>;

const resendClientValidationFields: FieldPath<ResendFormValues>[] = ['email'];
export const verifySuccessHoldMs = 5_000;

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
  const language = currentSiteLanguage();
  const resendSchema = useMemo(() => createResendSchema(t), [t]);
  const form = useForm<ResendFormValues>({
    resolver: zodResolver(resendSchema),
    mode: 'onSubmit',
    defaultValues: { email },
  });
  useRefreshClientValidationErrors(form, resendClientValidationFields, language);
  const {
    register: registerResend,
    handleSubmit,
    formState: { errors },
  } = form;
  const showResend = kind === 'expired' || kind === 'rate_limited';

  const config = {
    expired: {
      icon: Clock,
      title: t('verify.expired.title'),
      body: t('verify.expired.body'),
      iconClass: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-500/15 dark:text-yellow-300',
    },
    already_used: {
      icon: CheckCircle2,
      title: t('verify.alreadyUsed.title'),
      body: t('verify.alreadyUsed.body'),
      iconClass: 'text-muted-foreground bg-muted',
    },
    invalid: {
      icon: AlertCircle,
      title: t('verify.invalid.title'),
      body: t('verify.invalid.body'),
      iconClass: 'text-destructive bg-destructive/10',
    },
    rate_limited: {
      icon: Clock,
      title: t('verify.rateLimited.title'),
      body: t('verify.rateLimited.body'),
      iconClass: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-500/15 dark:text-yellow-300',
    },
  }[kind];

  const Icon = config.icon;
  const footer =
    kind === 'already_used' ? (
      <Link to="/sign-in" className="font-medium text-primary hover:underline">
        {t('auth.signIn')}
      </Link>
    ) : (
      <>
        {t('auth.needFreshStart')}{' '}
        <Link to="/register" className="font-medium text-primary hover:underline">
          {t('auth.backToRegistration')}
        </Link>
      </>
    );

  return (
    <AuthCard title={config.title} footer={footer}>
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
              <FieldLabel htmlFor="resend-email" required>
                {t('auth.email')}
              </FieldLabel>
              <Input
                id="resend-email"
                type="email"
                autoComplete="email"
                required
                aria-describedby={
                  errors.email ? 'resend-email-help resend-email-error' : 'resend-email-help'
                }
                aria-invalid={errors.email ? true : undefined}
                disabled={kind === 'rate_limited' || resendLoading}
                {...registerResend('email')}
              />
              <FieldDescription id="resend-email-help">
                {t('verify.resendEmailHelp')}
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
              {resendLoading ? t('auth.sending') : t('auth.resendVerification')}
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
  const { submit, completeSignIn, loading, errorKind, sessionEstablished } = useVerifyEmail();
  const { resend, state: resendState } = useResendVerification();
  const [started, setStarted] = useState(false);
  const [handoffStarted, setHandoffStarted] = useState(false);
  const completionTimerRef = useRef<number | null>(null);
  const completionTriggeredRef = useRef(false);

  const continueToDashboard = useCallback(() => {
    if (completionTriggeredRef.current) return;
    completionTriggeredRef.current = true;
    setHandoffStarted(true);
    if (completionTimerRef.current !== null) {
      window.clearTimeout(completionTimerRef.current);
      completionTimerRef.current = null;
    }
    void completeSignIn();
  }, [completeSignIn]);

  useEffect(() => {
    if (!token || started) return;
    setStarted(true);
    void submit(token).catch(() => undefined);
  }, [token, started, submit]);

  useEffect(() => {
    if (!sessionEstablished || completionTriggeredRef.current) return;

    completionTimerRef.current = window.setTimeout(() => {
      continueToDashboard();
    }, verifySuccessHoldMs);

    return () => {
      if (completionTimerRef.current === null) return;
      window.clearTimeout(completionTimerRef.current);
      completionTimerRef.current = null;
    };
  }, [continueToDashboard, sessionEstablished]);

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
      <AuthCard
        title={t('verify.title')}
        footer={
          <>
            {t('auth.needStartOver')}{' '}
            <Link to="/sign-in" className="font-medium text-primary hover:underline">
              {t('auth.signIn')}
            </Link>
          </>
        }
      >
        <div className="flex items-center gap-3 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
          <span>{t('verify.confirming')}</span>
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

  if (sessionEstablished) {
    return (
      <AuthCard
        title={t('verify.success.title')}
        footer={
          <>
            {t('auth.needStartOver')}{' '}
            <Link to="/sign-in" className="font-medium text-primary hover:underline">
              {t('auth.signIn')}
            </Link>
          </>
        }
      >
        <div className="space-y-4" aria-live="polite">
          <div className="flex items-start gap-3 text-sm text-muted-foreground">
            <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-emerald-500/10 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">
              <CheckCircle2 className="h-4 w-4" aria-hidden />
            </div>
            <p>{t('verify.success.body')}</p>
          </div>
          <Button
            type="button"
            className="h-9 w-full"
            onClick={continueToDashboard}
            disabled={handoffStarted}
          >
            {handoffStarted ? (
              <Loader2 className="size-4 animate-spin" aria-hidden />
            ) : (
              <ArrowRight className="size-4" aria-hidden />
            )}
            {handoffStarted ? t('verify.success.continuing') : t('verify.success.action')}
          </Button>
        </div>
      </AuthCard>
    );
  }

  return (
    <AuthCard
      title={t('verify.title')}
      footer={
        <>
          {t('auth.needStartOver')}{' '}
          <Link to="/sign-in" className="font-medium text-primary hover:underline">
            {t('auth.signIn')}
          </Link>
        </>
      }
    >
      <div className="flex items-center gap-3 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
        <span>{t('verify.completing')}</span>
      </div>
    </AuthCard>
  );
}
