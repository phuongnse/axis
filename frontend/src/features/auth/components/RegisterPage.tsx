import { Link } from '@tanstack/react-router';
import { UserPlus } from 'lucide-react';
import type { ReactNode } from 'react';
import { Controller } from 'react-hook-form';
import { Trans, useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { PasswordCriteria } from '@/features/auth/components/PasswordCriteria';
import { useRegister } from '@/features/auth/hooks/useRegister';

interface LegalLinkProps {
  children?: ReactNode;
  href: string;
}

function LegalLink({ children, href }: LegalLinkProps) {
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className="whitespace-nowrap font-medium text-primary hover:underline"
    >
      {children}
    </a>
  );
}

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
      title={t('auth.register.title')}
      footer={
        <>
          {t('auth.alreadyHaveAccount')}{' '}
          <Link to="/sign-in" className="font-medium text-primary hover:underline">
            {t('auth.signIn')}
          </Link>
        </>
      }
    >
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        <Field data-invalid={errors.fullName ? true : undefined}>
          <FieldLabel htmlFor="fullName">{t('auth.fullName')}</FieldLabel>
          <Input
            id="fullName"
            autoComplete="name"
            aria-describedby={errors.fullName ? 'fullName-help fullName-error' : 'fullName-help'}
            aria-invalid={errors.fullName ? true : undefined}
            {...register('fullName')}
          />
          <FieldDescription id="fullName-help">{t('auth.fullNameHelp')}</FieldDescription>
          {errors.fullName ? (
            <FieldError id="fullName-error">{errors.fullName.message}</FieldError>
          ) : null}
        </Field>

        <Field data-invalid={errors.email ? true : undefined}>
          <FieldLabel htmlFor="email">{t('auth.email')}</FieldLabel>
          <Input
            id="email"
            type="email"
            autoComplete="email"
            aria-describedby={errors.email ? 'email-help email-error' : 'email-help'}
            aria-invalid={errors.email ? true : undefined}
            {...register('email')}
          />
          <FieldDescription id="email-help">{t('auth.emailHelp')}</FieldDescription>
          {errors.email ? <FieldError id="email-error">{errors.email.message}</FieldError> : null}
        </Field>

        <Field data-invalid={errors.password ? true : undefined}>
          <FieldLabel htmlFor="password">{t('auth.password')}</FieldLabel>
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
          <FieldDescription id="password-help">{t('auth.passwordHelp')}</FieldDescription>
          <PasswordCriteria id="password-criteria" password={password} />
          {errors.password ? (
            <FieldError id="password-error">{errors.password.message}</FieldError>
          ) : null}
        </Field>

        <Field data-invalid={errors.passwordConfirmation ? true : undefined}>
          <FieldLabel htmlFor="passwordConfirmation">{t('auth.passwordConfirmation')}</FieldLabel>
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
            {t('auth.passwordConfirmationHelp')}
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
              <div className="flex items-start gap-2">
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
                <FieldLabel
                  htmlFor="acceptedTerms"
                  className="block min-w-0 flex-1 w-full font-normal leading-5"
                >
                  <Trans
                    i18nKey="auth.termsAgreement"
                    components={{
                      terms: <LegalLink href="/legal/terms" />,
                      privacy: <LegalLink href="/legal/privacy" />,
                    }}
                  />
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
          {loading ? t('auth.creatingAccount') : t('auth.createAccount')}
        </Button>
      </form>
    </AuthCard>
  );
}
