import { zodResolver } from '@hookform/resolvers/zod';
import { Link } from '@tanstack/react-router';
import { ArrowRight, Mail } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { type FieldPath, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { AsyncContent } from '@/components/shared/AsyncContent';
import {
  EntryAsyncAction,
  EntryInput,
  type EntryPreferencesModel,
  EntrySurface,
} from '@/components/shared/EntrySurface';
import { StatusNotice, type StatusNoticeTone } from '@/components/shared/StatusNotice';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { useRefreshClientValidationErrors } from '@/features/auth/hooks/useRefreshClientValidationErrors';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { useVerifyEmail } from '@/features/auth/hooks/useVerifyEmail';
import { loadRegistrationContext } from '@/features/auth/registration-context';
import type { VerifyEmailErrorKind } from '@/features/auth/types';
import { useQueryParam } from '@/features/auth/use-query-param';
import { currentSiteLanguage, useEntryPreferencesModel } from '@/features/preferences';

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
  preferences,
  resendLoading,
}: {
  kind: VerifyEmailErrorKind;
  email: string;
  onResend: (email: string) => Promise<void>;
  preferences: EntryPreferencesModel;
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

  const config = (
    {
      expired: {
        title: t('verify.expired.title'),
        body: t('verify.expired.body'),
        tone: 'warning',
      },
      already_used: {
        title: t('verify.alreadyUsed.title'),
        body: t('verify.alreadyUsed.body'),
        tone: 'success',
      },
      invalid: {
        title: t('verify.invalid.title'),
        body: t('verify.invalid.body'),
        tone: 'destructive',
      },
      rate_limited: {
        title: t('verify.rateLimited.title'),
        body: t('verify.rateLimited.body'),
        tone: 'warning',
      },
    } satisfies Record<
      VerifyEmailErrorKind,
      { title: string; body: string; tone: StatusNoticeTone }
    >
  )[kind];

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
    <EntrySurface
      surfaceId="verify-email"
      preferences={preferences}
      title={config.title}
      footer={footer}
    >
      <div className="space-y-4">
        <StatusNotice tone={config.tone}>{config.body}</StatusNotice>

        {showResend ? (
          <form
            className="space-y-3"
            onSubmit={handleSubmit((values) => void onResend(values.email.trim()))}
            noValidate
          >
            <Field data-invalid={errors.email ? true : undefined}>
              <FieldLabel htmlFor="resend-email">{t('auth.email')}</FieldLabel>
              <EntryInput
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
            <EntryAsyncAction
              type="submit"
              size="lg"
              disabled={kind === 'rate_limited' || resendLoading}
              icon={<Mail aria-hidden />}
              pending={resendLoading}
              pendingLabel={t('auth.sending')}
            >
              {t('auth.resendVerification')}
            </EntryAsyncAction>
          </form>
        ) : null}
      </div>
    </EntrySurface>
  );
}

export function VerifyEmailPage() {
  const { t } = useTranslation();
  const preferences = useEntryPreferencesModel();
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

  if (!token && !started) {
    return (
      <VerifyEmailOutcome
        kind="invalid"
        email={context?.email ?? ''}
        onResend={async () => {}}
        preferences={preferences}
        resendLoading={false}
      />
    );
  }

  if (loading) {
    return (
      <EntrySurface
        surfaceId="verify-email"
        preferences={preferences}
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
        <AsyncContent pending pendingLabel={t('verify.confirming')}>
          <span />
        </AsyncContent>
      </EntrySurface>
    );
  }

  if (errorKind) {
    return (
      <VerifyEmailOutcome
        kind={errorKind}
        email={context?.email ?? ''}
        onResend={resend}
        preferences={preferences}
        resendLoading={resendState === 'sending'}
      />
    );
  }

  if (sessionEstablished) {
    return (
      <EntrySurface
        surfaceId="verify-email"
        preferences={preferences}
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
          <StatusNotice tone="success">{t('verify.success.body')}</StatusNotice>
          <EntryAsyncAction
            type="button"
            size="lg"
            onClick={continueToDashboard}
            disabled={handoffStarted}
            icon={<ArrowRight aria-hidden />}
            pending={handoffStarted}
            pendingLabel={t('verify.success.continuing')}
          >
            {t('verify.success.action')}
          </EntryAsyncAction>
        </div>
      </EntrySurface>
    );
  }

  return (
    <EntrySurface
      surfaceId="verify-email"
      preferences={preferences}
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
      <AsyncContent pending pendingLabel={t('verify.completing')}>
        <span />
      </AsyncContent>
    </EntrySurface>
  );
}
