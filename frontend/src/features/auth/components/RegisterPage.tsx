import { Link } from '@tanstack/react-router';
import { UserPlus } from 'lucide-react';
import { Controller } from 'react-hook-form';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
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
    control,
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
        <Field data-invalid={errors.fullName ? true : undefined}>
          <FieldLabel htmlFor="fullName">{t('register.fullName')}</FieldLabel>
          <Input
            id="fullName"
            autoComplete="name"
            aria-describedby={errors.fullName ? 'fullName-help fullName-error' : 'fullName-help'}
            aria-invalid={errors.fullName ? true : undefined}
            {...register('fullName')}
          />
          <FieldDescription id="fullName-help">{t('register.fullNameHelp')}</FieldDescription>
          {errors.fullName ? (
            <FieldError id="fullName-error">{errors.fullName.message}</FieldError>
          ) : null}
        </Field>

        <Field data-invalid={errors.email ? true : undefined}>
          <FieldLabel htmlFor="email">{t('common.emailAddress')}</FieldLabel>
          <Input
            id="email"
            type="email"
            autoComplete="email"
            aria-describedby={errors.email ? 'email-help email-error' : 'email-help'}
            aria-invalid={errors.email ? true : undefined}
            {...register('email')}
          />
          <FieldDescription id="email-help">{t('register.emailHelp')}</FieldDescription>
          {errors.email ? <FieldError id="email-error">{errors.email.message}</FieldError> : null}
        </Field>

        <Field data-invalid={errors.password ? true : undefined}>
          <FieldLabel htmlFor="password">{t('common.password')}</FieldLabel>
          <Input
            id="password"
            type="password"
            autoComplete="new-password"
            aria-describedby={
              errors.password
                ? 'password-help password-criteria password-error'
                : 'password-help password-criteria'
            }
            aria-invalid={errors.password ? true : undefined}
            {...register('password')}
          />
          <FieldDescription id="password-help">{t('register.passwordHelp')}</FieldDescription>
          <PasswordCriteria id="password-criteria" password={password} />
          {errors.password ? (
            <FieldError id="password-error">{errors.password.message}</FieldError>
          ) : null}
        </Field>

        <Field data-invalid={errors.passwordConfirmation ? true : undefined}>
          <FieldLabel htmlFor="passwordConfirmation">{t('register.confirmPassword')}</FieldLabel>
          <Input
            id="passwordConfirmation"
            type="password"
            autoComplete="new-password"
            aria-describedby={
              errors.passwordConfirmation
                ? 'passwordConfirmation-help passwordConfirmation-error'
                : 'passwordConfirmation-help'
            }
            aria-invalid={errors.passwordConfirmation ? true : undefined}
            {...register('passwordConfirmation')}
          />
          <FieldDescription id="passwordConfirmation-help">
            {t('register.confirmPasswordHelp')}
          </FieldDescription>
          {errors.passwordConfirmation ? (
            <FieldError id="passwordConfirmation-error">
              {errors.passwordConfirmation.message}
            </FieldError>
          ) : null}
        </Field>

        <Controller
          control={control}
          name="acceptedTerms"
          render={({ field }) => (
            <Field data-invalid={errors.acceptedTerms ? true : undefined}>
              <div className="flex items-center gap-2">
                <Checkbox
                  id="acceptedTerms"
                  name={field.name}
                  checked={field.value}
                  inputRef={field.ref}
                  onBlur={field.onBlur}
                  onCheckedChange={(checked) => field.onChange(checked)}
                  aria-describedby={errors.acceptedTerms ? 'acceptedTerms-error' : undefined}
                  aria-invalid={errors.acceptedTerms ? true : undefined}
                />
                <FieldLabel htmlFor="acceptedTerms" className="font-normal leading-4">
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
                </FieldLabel>
              </div>
              {errors.acceptedTerms ? (
                <FieldError id="acceptedTerms-error">{errors.acceptedTerms.message}</FieldError>
              ) : null}
            </Field>
          )}
        />

        {submitError ? <AuthNotice variant="destructive">{submitError}</AuthNotice> : null}

        <Button type="submit" className="h-9 w-full" disabled={loading}>
          <UserPlus className="size-4" aria-hidden />
          {loading ? t('register.creatingAccount') : t('common.createAccount')}
        </Button>
      </form>
    </AuthCard>
  );
}
