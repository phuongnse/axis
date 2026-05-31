import { Link } from '@tanstack/react-router';

import { Button, buttonVariants } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { buildExternalRegistrationUrl } from '@/features/auth/api';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useExternalProviders } from '@/features/auth/hooks/useExternalProviders';
import { useRegister } from '@/features/auth/hooks/useRegister';
import { EXTERNAL_PROVIDER_LABELS, type ExternalProviderId } from '@/features/auth/types';
import { cn } from '@/lib/utils';

const PROVIDER_ERROR_MESSAGES: Record<string, string> = {
  account_exists: 'An account with this email already exists. Sign in instead.',
  no_verified_email:
    'Your provider did not return a verified email address. Use email/password registration instead.',
  provider_failed: 'Sign-in with that provider failed. Please try again or use email/password.',
};

function isExternalProviderId(provider: string): provider is ExternalProviderId {
  return provider in EXTERNAL_PROVIDER_LABELS;
}

export function RegisterPage() {
  const { form, loading, successMessage, submit, resetFlow } = useRegister();
  const { data: providers = [], isLoading: providersLoading } = useExternalProviders();
  const providerErrorCode =
    typeof window !== 'undefined'
      ? (new URLSearchParams(window.location.search).get('error') ?? undefined)
      : undefined;
  const providerError = providerErrorCode ? PROVIDER_ERROR_MESSAGES[providerErrorCode] : undefined;
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = form;
  const submitError = errors.root?.message;
  const enabledProviders = providers.filter(isExternalProviderId);

  if (successMessage) {
    return (
      <AuthCard
        title="Check your email"
        footer={
          <>
            Already verified?{' '}
            <Link to="/login" className="font-medium hover:underline">
              Sign in
            </Link>
          </>
        }
      >
        <div className="space-y-4">
          <p className="text-sm text-muted-foreground">{successMessage}</p>
          <p className="text-sm text-muted-foreground">
            If an account exists for this email, you will receive a verification link shortly.
          </p>
          <Button type="button" variant="outline" className="w-full h-9" onClick={resetFlow}>
            Register another organization
          </Button>
        </div>
      </AuthCard>
    );
  }

  return (
    <AuthCard
      title="Create your account"
      footer={
        <>
          Already have an account?{' '}
          <Link to="/login" className="font-medium hover:underline">
            Sign in
          </Link>
        </>
      }
    >
      <div className="space-y-4">
        {providerError ? (
          <div
            className="rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive"
            role="alert"
          >
            {providerError}
          </div>
        ) : null}

        {!providersLoading && enabledProviders.length > 0 ? (
          <div className="space-y-2">
            {enabledProviders.map((provider) => (
              <a
                key={provider}
                href={buildExternalRegistrationUrl(provider)}
                className={cn(buttonVariants({ variant: 'outline' }), 'w-full h-9')}
              >
                Continue with {EXTERNAL_PROVIDER_LABELS[provider]}
              </a>
            ))}
            <div className="relative py-2">
              <div className="absolute inset-0 flex items-center">
                <span className="w-full border-t border-border" />
              </div>
              <div className="relative flex justify-center text-xs uppercase">
                <span className="bg-card px-2 text-muted-foreground">or</span>
              </div>
            </div>
          </div>
        ) : null}

        <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
          <div className="space-y-1.5">
            <Label htmlFor="organizationName">Organization name</Label>
            <Input
              id="organizationName"
              autoComplete="organization"
              aria-invalid={errors.organizationName ? true : undefined}
              {...register('organizationName')}
            />
            {errors.organizationName ? (
              <p className="text-xs text-destructive">{errors.organizationName.message}</p>
            ) : null}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="fullName">Full name</Label>
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
            <Label htmlFor="email">Email address</Label>
            <Input
              id="email"
              type="email"
              autoComplete="email"
              aria-invalid={errors.email ? true : undefined}
              {...register('email')}
            />
            {errors.email ? (
              <p className="text-xs text-destructive">{errors.email.message}</p>
            ) : null}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="password">Password</Label>
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
            <Label htmlFor="passwordConfirmation">Confirm password</Label>
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

          {submitError ? (
            <div
              className="rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive"
              role="alert"
            >
              {submitError}
            </div>
          ) : null}

          <Button type="submit" variant="cta" className="w-full h-9" disabled={loading}>
            {loading ? 'Creating account…' : 'Create account'}
          </Button>
        </form>
      </div>
    </AuthCard>
  );
}
