import { Link } from '@tanstack/react-router';
import { LogIn, Send } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { useSignIn } from '@/features/auth/hooks/useSignIn';

function SignInFooter() {
  return (
    <span>
      New here?{' '}
      <Link to="/register" className="font-medium text-primary hover:underline">
        Create an account
      </Link>
    </span>
  );
}

export function SignInPage() {
  const { form, loading, submit, verificationEmail, rateLimited } = useSignIn();
  const { resend, state: resendState, rateLimitMessage } = useResendVerification();
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = form;
  const submitError = errors.root?.message;
  const showVerificationRequired = Boolean(verificationEmail);
  const resendDisabled =
    !verificationEmail || resendState === 'sending' || resendState === 'rate_limited';

  return (
    <AuthCard title="Sign in" footer={<SignInFooter />}>
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
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
          <FieldDescription id="email-help">Use the email for your account.</FieldDescription>
          {errors.email ? <FieldError id="email-error">{errors.email.message}</FieldError> : null}
        </Field>

        <Field data-invalid={errors.password ? true : undefined}>
          <FieldLabel htmlFor="password">Password</FieldLabel>
          <Input
            id="password"
            type="password"
            autoComplete="current-password"
            aria-describedby={errors.password ? 'password-help password-error' : 'password-help'}
            aria-invalid={errors.password ? true : undefined}
            {...register('password')}
          />
          <FieldDescription id="password-help">
            Enter the password exactly as created.
          </FieldDescription>
          {errors.password ? (
            <FieldError id="password-error">{errors.password.message}</FieldError>
          ) : null}
        </Field>

        {submitError ? (
          <AuthNotice
            variant={showVerificationRequired ? 'default' : 'destructive'}
            title={showVerificationRequired ? 'Verify your email' : undefined}
          >
            {submitError}
          </AuthNotice>
        ) : null}

        {showVerificationRequired ? (
          <div className="space-y-3">
            <Button
              type="button"
              variant="outline"
              className="h-9 w-full"
              disabled={resendDisabled}
              onClick={() => {
                if (verificationEmail) void resend(verificationEmail);
              }}
            >
              <Send className="size-4" aria-hidden />
              {resendState === 'sending' ? 'Sending...' : 'Resend verification email'}
            </Button>
            {resendState === 'success' ? <AuthNotice>Verification email sent.</AuthNotice> : null}
            {resendState === 'error' ? (
              <AuthNotice variant="destructive">Something went wrong, please try again</AuthNotice>
            ) : null}
            {resendState === 'rate_limited' ? (
              <AuthNotice variant="destructive">
                {rateLimitMessage ?? 'Please wait before requesting another verification email.'}
              </AuthNotice>
            ) : null}
          </div>
        ) : null}

        <Button type="submit" className="h-9 w-full" disabled={loading || rateLimited}>
          <LogIn className="size-4" aria-hidden />
          {loading ? 'Signing in...' : 'Sign in'}
        </Button>
      </form>
    </AuthCard>
  );
}
