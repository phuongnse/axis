import { UserPlus } from 'lucide-react';
import { Controller } from 'react-hook-form';

import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { PasswordCriteria } from '@/features/auth/components/PasswordCriteria';
import { useRegister } from '@/features/auth/hooks/useRegister';

export function RegisterPage() {
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
    <AuthCard title="Create account">
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        <Field data-invalid={errors.fullName ? true : undefined}>
          <FieldLabel htmlFor="fullName">Full name</FieldLabel>
          <Input
            id="fullName"
            autoComplete="name"
            aria-describedby={errors.fullName ? 'fullName-help fullName-error' : 'fullName-help'}
            aria-invalid={errors.fullName ? true : undefined}
            {...register('fullName')}
          />
          <FieldDescription id="fullName-help">
            This name will appear on your account.
          </FieldDescription>
          {errors.fullName ? (
            <FieldError id="fullName-error">{errors.fullName.message}</FieldError>
          ) : null}
        </Field>

        <Field data-invalid={errors.email ? true : undefined}>
          <FieldLabel htmlFor="email">Email address</FieldLabel>
          <Input
            id="email"
            type="email"
            autoComplete="email"
            aria-describedby={errors.email ? 'email-help email-error' : 'email-help'}
            aria-invalid={errors.email ? true : undefined}
            {...register('email')}
          />
          <FieldDescription id="email-help">
            We will send a verification link to this address.
          </FieldDescription>
          {errors.email ? <FieldError id="email-error">{errors.email.message}</FieldError> : null}
        </Field>

        <Field data-invalid={errors.password ? true : undefined}>
          <FieldLabel htmlFor="password">Password</FieldLabel>
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
          <FieldDescription id="password-help">
            Use a memorable passphrase that is hard to guess.
          </FieldDescription>
          <PasswordCriteria id="password-criteria" password={password} />
          {errors.password ? (
            <FieldError id="password-error">{errors.password.message}</FieldError>
          ) : null}
        </Field>

        <Field data-invalid={errors.passwordConfirmation ? true : undefined}>
          <FieldLabel htmlFor="passwordConfirmation">Confirm password</FieldLabel>
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
            Re-enter the password exactly as typed.
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
                  I agree to the{' '}
                  <a
                    href="/legal/terms"
                    target="_blank"
                    rel="noopener noreferrer"
                    className="font-medium text-primary hover:underline"
                  >
                    Terms of Service
                  </a>{' '}
                  and{' '}
                  <a
                    href="/legal/privacy"
                    target="_blank"
                    rel="noopener noreferrer"
                    className="font-medium text-primary hover:underline"
                  >
                    Privacy Policy
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
          {loading ? 'Creating account...' : 'Create account'}
        </Button>
      </form>
    </AuthCard>
  );
}
