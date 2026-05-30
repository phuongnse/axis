import { Link } from '@tanstack/react-router';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useRegister } from '@/features/auth/hooks/useRegister';
import { useSlugPreview } from '@/features/auth/hooks/useSlugPreview';

export function RegisterPage() {
  const { form, loading, successMessage, submit, resetFlow } = useRegister();
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = form;
  const organizationName = watch('organizationName');
  const { slug } = useSlugPreview(organizationName);
  const submitError = errors.root?.message;

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
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        <div className="space-y-1.5">
          <Label htmlFor="organizationName">Organization name</Label>
          <Input
            id="organizationName"
            autoComplete="organization"
            aria-invalid={errors.organizationName ? true : undefined}
            {...register('organizationName')}
          />
          {slug ? (
            <p className="text-xs text-muted-foreground">
              Workspace URL: <span className="font-medium text-foreground">{slug}</span>
            </p>
          ) : null}
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
          {errors.email ? <p className="text-xs text-destructive">{errors.email.message}</p> : null}
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
              I agree to the{' '}
              <a href="/legal/terms" className="font-medium text-primary hover:underline">
                Terms of Service
              </a>{' '}
              and{' '}
              <a href="/legal/privacy" className="font-medium text-primary hover:underline">
                Privacy Policy
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
          {loading ? 'Creating account…' : 'Create account'}
        </Button>
      </form>
    </AuthCard>
  );
}
