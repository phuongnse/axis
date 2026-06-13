import { Link } from '@tanstack/react-router';
import { UserPlus } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { CheckboxField } from '@/components/ui/checkbox-field';
import { FormField } from '@/components/ui/form-field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { PasswordCriteria } from '@/features/auth/components/PasswordCriteria';
import { useRegister } from '@/features/auth/hooks/useRegister';

export function RegisterPage() {
  const { t } = useTranslation();
  const { form, loading, submit } = useRegister();
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = form;
  const submitError = errors.root?.message;
  const password = watch('password');

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
        <FormField
          id="fullName"
          label={t('register.fullName')}
          helpText={t('register.fullNameHelp')}
          error={errors.fullName?.message}
        >
          {({ describedBy }) => (
            <Input
              id="fullName"
              autoComplete="name"
              aria-describedby={describedBy}
              aria-invalid={errors.fullName ? true : undefined}
              {...register('fullName')}
            />
          )}
        </FormField>

        <FormField
          id="email"
          label={t('common.emailAddress')}
          helpText={t('register.emailHelp')}
          error={errors.email?.message}
        >
          {({ describedBy }) => (
            <Input
              id="email"
              type="email"
              autoComplete="email"
              aria-describedby={describedBy}
              aria-invalid={errors.email ? true : undefined}
              {...register('email')}
            />
          )}
        </FormField>

        <FormField
          id="password"
          label={t('common.password')}
          helpText={t('register.passwordHelp')}
          descriptionIds={['password-criteria']}
          error={errors.password?.message}
        >
          {({ describedBy }) => (
            <>
              <Input
                id="password"
                type="password"
                autoComplete="new-password"
                aria-describedby={describedBy}
                aria-invalid={errors.password ? true : undefined}
                {...register('password')}
              />
              <PasswordCriteria id="password-criteria" password={password} />
            </>
          )}
        </FormField>

        <FormField
          id="passwordConfirmation"
          label={t('register.confirmPassword')}
          helpText={t('register.confirmPasswordHelp')}
          error={errors.passwordConfirmation?.message}
        >
          {({ describedBy }) => (
            <Input
              id="passwordConfirmation"
              type="password"
              autoComplete="new-password"
              aria-describedby={describedBy}
              aria-invalid={errors.passwordConfirmation ? true : undefined}
              {...register('passwordConfirmation')}
            />
          )}
        </FormField>

        <CheckboxField
          id="acceptedTerms"
          error={errors.acceptedTerms?.message}
          {...register('acceptedTerms')}
        >
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
        </CheckboxField>

        {submitError ? <AuthNotice variant="error">{submitError}</AuthNotice> : null}

        <Button type="submit" variant="cta" className="w-full h-9" disabled={loading}>
          <UserPlus className="size-4" aria-hidden />
          {loading ? t('register.creatingAccount') : t('common.createAccount')}
        </Button>
      </form>
    </AuthCard>
  );
}
