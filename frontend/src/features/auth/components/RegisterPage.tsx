import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useRegister } from '@/features/auth/hooks/useRegister';

export function RegisterPage() {
  const { t } = useTranslation();
  const { form, loading, submit } = useRegister();
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = form;
  const submitError = errors.root?.message;

  return (
    <AuthCard
      title={t('register.title')}
      footer={
        <>
          {t('register.footerPrompt')}{' '}
          <Link to="/login" className="font-medium hover:underline">
            {t('common.signIn')}
          </Link>
        </>
      }
    >
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        <div className="space-y-1.5">
          <Label htmlFor="fullName">{t('register.fullName')}</Label>
          <Input
            id="fullName"
            autoComplete="name"
            aria-invalid={errors.fullName ? true : undefined}
            {...register('fullName')}
          />
          {errors.fullName ? (
            <p className="text-xs text-destructive">{errors.fullName.message}</p>
          ) : null}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="email">{t('common.emailAddress')}</Label>
          <Input
            id="email"
            type="email"
            autoComplete="email"
            aria-invalid={errors.email ? true : undefined}
            {...register('email')}
          />
          {errors.email ? <p className="text-xs text-destructive">{errors.email.message}</p> : null}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="password">{t('common.password')}</Label>
          <Input
            id="password"
            type="password"
            autoComplete="new-password"
            aria-invalid={errors.password ? true : undefined}
            {...register('password')}
          />
          {errors.password ? (
            <p className="text-xs text-destructive">{errors.password.message}</p>
          ) : null}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="passwordConfirmation">{t('register.confirmPassword')}</Label>
          <Input
            id="passwordConfirmation"
            type="password"
            autoComplete="new-password"
            aria-invalid={errors.passwordConfirmation ? true : undefined}
            {...register('passwordConfirmation')}
          />
          {errors.passwordConfirmation ? (
            <p className="text-xs text-destructive">{errors.passwordConfirmation.message}</p>
          ) : null}
        </div>

        <div className="space-y-1.5">
          <div className="flex items-start gap-2">
            <input
              id="acceptedTerms"
              type="checkbox"
              className="mt-1 h-4 w-4 rounded border-border"
              aria-invalid={errors.acceptedTerms ? true : undefined}
              {...register('acceptedTerms')}
            />
            <Label htmlFor="acceptedTerms" className="font-normal leading-snug">
              {t('register.agreePrefix')}{' '}
              <a
                href="/legal/terms"
                target="_blank"
                rel="noopener noreferrer"
                className="font-medium text-primary hover:underline"
              >
                {t('register.termsOfService')}
              </a>{' '}
              {t('register.agreeMiddle')}{' '}
              <a
                href="/legal/privacy"
                target="_blank"
                rel="noopener noreferrer"
                className="font-medium text-primary hover:underline"
              >
                {t('register.privacyPolicy')}
              </a>
            </Label>
          </div>
          {errors.acceptedTerms ? (
            <p className="text-xs text-destructive">{errors.acceptedTerms.message}</p>
          ) : null}
        </div>

        {submitError ? (
          <div
            className="rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive"
            role="alert"
          >
            {submitError}
          </div>
        ) : null}

        <Button type="submit" variant="cta" className="w-full h-9" disabled={loading}>
          {loading ? t('register.creatingAccount') : t('common.createAccount')}
        </Button>
      </form>
    </AuthCard>
  );
}
