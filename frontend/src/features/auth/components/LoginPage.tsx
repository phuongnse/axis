import { Link } from '@tanstack/react-router';
import { LogIn } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { FormField } from '@/components/ui/form-field';
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
          <AuthNotice variant="error">
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
        <FormField
          id="email"
          label={t('common.emailAddress')}
          helpText={t('login.emailHelp')}
          error={errors.email?.message}
        >
          {({ describedBy }) => (
            <Input
              id="email"
              type="email"
              autoComplete="username"
              aria-describedby={describedBy}
              aria-invalid={errors.email ? true : undefined}
              {...register('email')}
            />
          )}
        </FormField>

        <FormField
          id="password"
          label={t('common.password')}
          helpText={t('login.passwordHelp')}
          error={errors.password?.message}
        >
          {({ describedBy }) => (
            <Input
              id="password"
              type="password"
              autoComplete="current-password"
              aria-describedby={describedBy}
              aria-invalid={errors.password ? true : undefined}
              {...register('password')}
            />
          )}
        </FormField>

        <div className="flex justify-end">
          <Link to="/forgot-password" className="text-xs text-primary hover:underline">
            {t('login.forgotPassword')}
          </Link>
        </div>

        <Button type="submit" variant="cta" className="w-full h-9" disabled={loading}>
          <LogIn className="size-4" aria-hidden />
          {loading ? t('login.signingIn') : t('common.signIn')}
        </Button>
      </form>
    </AuthCard>
  );
}
