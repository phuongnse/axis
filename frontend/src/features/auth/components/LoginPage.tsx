import { Link } from '@tanstack/react-router';
import { LogIn } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { useLogin } from '@/features/auth/hooks/useLogin';

export function LoginPage() {
  const { t } = useTranslation();
  const { form, loginError, loading, submit } = useLogin();
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = form;

  return (
    <AuthCard
      title={t('login.title')}
      footer={
        <>
          {t('login.footerPrompt')}{' '}
          <Link to="/register" className="font-medium hover:underline">
            {t('common.signUp')}
          </Link>
        </>
      }
      banner={
        loginError ? (
          <AuthNotice variant="destructive">
            {loginError.message}
            {loginError.kind === 'unverified' ? (
              <p className="mt-2 text-center text-xs text-muted-foreground">
                {t('auth.unverifiedHint')}
              </p>
            ) : null}
          </AuthNotice>
        ) : null
      }
    >
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        <Field data-invalid={errors.email ? true : undefined}>
          <FieldLabel htmlFor="email">{t('common.emailAddress')}</FieldLabel>
          <Input
            id="email"
            type="email"
            autoComplete="username"
            aria-describedby={errors.email ? 'email-help email-error' : 'email-help'}
            aria-invalid={errors.email ? true : undefined}
            {...register('email')}
          />
          <FieldDescription id="email-help">{t('login.emailHelp')}</FieldDescription>
          {errors.email ? <FieldError id="email-error">{errors.email.message}</FieldError> : null}
        </Field>

        <Field data-invalid={errors.password ? true : undefined}>
          <FieldLabel htmlFor="password">{t('common.password')}</FieldLabel>
          <Input
            id="password"
            type="password"
            autoComplete="current-password"
            aria-describedby={errors.password ? 'password-help password-error' : 'password-help'}
            aria-invalid={errors.password ? true : undefined}
            {...register('password')}
          />
          <FieldDescription id="password-help">{t('login.passwordHelp')}</FieldDescription>
          {errors.password ? (
            <FieldError id="password-error">{errors.password.message}</FieldError>
          ) : null}
        </Field>

        <div className="flex justify-end">
          <Link to="/forgot-password" className="text-xs text-primary hover:underline">
            {t('login.forgotPassword')}
          </Link>
        </div>

        <Button type="submit" className="h-9 w-full" disabled={loading}>
          <LogIn className="size-4" aria-hidden />
          {loading ? t('login.signingIn') : t('common.signIn')}
        </Button>
      </form>
    </AuthCard>
  );
}
