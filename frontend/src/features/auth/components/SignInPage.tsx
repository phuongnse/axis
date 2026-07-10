import { Link } from '@tanstack/react-router';
import { LogIn, Send } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { useSignIn } from '@/features/auth/hooks/useSignIn';

function SignInFooter({ t }: { t: ReturnType<typeof useTranslation>['t'] }) {
  return (
    <span>
      {t('auth.newHere')}{' '}
      <Link to="/register" className="font-medium text-primary hover:underline">
        {t('auth.createAccount')}
      </Link>
    </span>
  );
}

export function SignInPage() {
  const { t } = useTranslation();
  const { form, loading, submit, submitError, verificationEmail, rateLimited } = useSignIn();
  const { resend, state: resendState } = useResendVerification();
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = form;
  const showVerificationRequired = Boolean(verificationEmail);
  const submitErrorTitle = showVerificationRequired
    ? t('auth.verifyEmail')
    : t('auth.signInErrorTitle');
  const resendDisabled =
    !verificationEmail || resendState === 'sending' || resendState === 'rate_limited';

  return (
    <AuthCard title={t('auth.signIn')} footer={<SignInFooter t={t} />}>
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        {submitError ? (
          <AuthNotice
            variant={showVerificationRequired ? 'warning' : 'destructive'}
            title={submitErrorTitle}
          >
            {submitError}
          </AuthNotice>
        ) : null}

        <Field data-invalid={errors.email ? true : undefined}>
          <FieldLabel htmlFor="email" required>
            {t('auth.email')}
          </FieldLabel>
          <Input
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
          <FieldLabel htmlFor="password" required>
            {t('auth.password')}
          </FieldLabel>
          <Input
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

        {showVerificationRequired ? (
          <div className="space-y-3">
            <Button
              type="button"
              variant="outline"
              size="lg"
              className="w-full"
              disabled={resendDisabled}
              onClick={() => {
                if (verificationEmail) void resend(verificationEmail);
              }}
            >
              <Send className="size-4" aria-hidden />
              {resendState === 'sending' ? t('auth.sending') : t('auth.resendVerification')}
            </Button>
            {resendState === 'success' ? (
              <AuthNotice variant="success">{t('notice.verificationEmailSent')}</AuthNotice>
            ) : null}
            {resendState === 'error' ? (
              <AuthNotice variant="destructive">{t('auth.genericError')}</AuthNotice>
            ) : null}
            {resendState === 'rate_limited' ? (
              <AuthNotice variant="warning">{t('notice.resendLimited')}</AuthNotice>
            ) : null}
          </div>
        ) : null}

        <Button type="submit" size="lg" className="w-full" disabled={loading || rateLimited}>
          <LogIn className="size-4" aria-hidden />
          {loading ? t('auth.signingIn') : t('auth.signIn')}
        </Button>
      </form>
    </AuthCard>
  );
}
