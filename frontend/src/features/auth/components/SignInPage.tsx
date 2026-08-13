import { LogIn } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { EntryAsyncAction, EntryInput, EntrySurface } from '@/components/shared/EntrySurface';
import {
  InlinePromptAction,
  InlinePromptActionButton,
  InlinePromptActionFeedback,
  InlinePromptActionLink,
} from '@/components/shared/InlinePromptAction';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { useSignIn } from '@/features/auth/hooks/useSignIn';
import { useEntryPreferencesModel } from '@/features/preferences';

function SignInFooter({ t }: { t: ReturnType<typeof useTranslation>['t'] }) {
  return (
    <InlinePromptAction prompt={t('auth.newHere')}>
      <InlinePromptActionLink to="/register">{t('auth.createAccount')}</InlinePromptActionLink>
    </InlinePromptAction>
  );
}

export function SignInPage() {
  const { t } = useTranslation();
  const preferences = useEntryPreferencesModel();
  const {
    form,
    loading,
    submit,
    submitError,
    authorizationRequestError,
    verificationEmail,
    rateLimited,
  } = useSignIn();
  const { resend, state: resendState } = useResendVerification();
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = form;
  const showVerificationRequired = Boolean(verificationEmail);
  const resendSending = resendState === 'sending';
  const submitErrorTitle = showVerificationRequired
    ? t('auth.signInEmailUnverifiedTitle')
    : t('auth.signInErrorTitle');
  const resendDisabled =
    !verificationEmail || resendState === 'sending' || resendState === 'rate_limited';
  const resendFeedback =
    resendState === 'success'
      ? { message: t('notice.verificationEmailSent'), tone: 'success' as const }
      : resendState === 'error'
        ? { message: t('auth.genericError'), tone: 'destructive' as const }
        : resendState === 'rate_limited'
          ? { message: t('notice.resendLimited'), tone: 'warning' as const }
          : null;

  return (
    <EntrySurface
      surfaceId="sign-in"
      preferences={preferences}
      title={t('auth.signIn')}
      footer={<SignInFooter t={t} />}
    >
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        {authorizationRequestError ? (
          <StatusNotice tone="destructive" title={t('auth.signInErrorTitle')}>
            {t('auth.authorizationRequestInvalid')}
          </StatusNotice>
        ) : null}

        {submitError ? (
          showVerificationRequired ? (
            <div className="space-y-2">
              <StatusNotice tone="warning" title={submitErrorTitle}>
                {submitError}
              </StatusNotice>

              <div className="space-y-1">
                <InlinePromptAction prompt={t('auth.confirm.didNotReceive')}>
                  <InlinePromptActionButton
                    type="button"
                    aria-label={t('auth.resendVerification')}
                    disabled={resendDisabled}
                    pending={resendSending}
                    pendingLabel={t('auth.sending')}
                    onClick={() => {
                      if (verificationEmail) {
                        void resend(verificationEmail).catch(() => undefined);
                      }
                    }}
                  >
                    {t('auth.resendEmail')}
                  </InlinePromptActionButton>
                </InlinePromptAction>

                {resendFeedback ? (
                  <InlinePromptActionFeedback tone={resendFeedback.tone}>
                    {resendFeedback.message}
                  </InlinePromptActionFeedback>
                ) : null}
              </div>
            </div>
          ) : (
            <StatusNotice tone="destructive" title={submitErrorTitle}>
              {submitError}
            </StatusNotice>
          )
        ) : null}

        <Field data-invalid={errors.email ? true : undefined}>
          <FieldLabel htmlFor="email">{t('auth.email')}</FieldLabel>
          <EntryInput
            id="email"
            type="email"
            autoComplete="email"
            required
            aria-describedby={errors.email ? 'email-help email-error' : 'email-help'}
            aria-invalid={errors.email ? true : undefined}
            {...register('email')}
          />
          <FieldDescription id="email-help">{t('auth.signInEmailHelp')}</FieldDescription>
          {errors.email ? <FieldError id="email-error">{errors.email.message}</FieldError> : null}
        </Field>

        <Field data-invalid={errors.password ? true : undefined}>
          <FieldLabel htmlFor="password">{t('auth.password')}</FieldLabel>
          <EntryInput
            id="password"
            type="password"
            autoComplete="current-password"
            required
            aria-describedby={errors.password ? 'password-help password-error' : 'password-help'}
            aria-invalid={errors.password ? true : undefined}
            {...register('password')}
          />
          <FieldDescription id="password-help">{t('auth.signInPasswordHelp')}</FieldDescription>
          {errors.password ? (
            <FieldError id="password-error">{errors.password.message}</FieldError>
          ) : null}
        </Field>

        <EntryAsyncAction
          type="submit"
          size="lg"
          disabled={loading || rateLimited}
          icon={<LogIn aria-hidden />}
          pending={loading}
          pendingLabel={t('auth.signingIn')}
        >
          {t('auth.signIn')}
        </EntryAsyncAction>
      </form>
    </EntrySurface>
  );
}
