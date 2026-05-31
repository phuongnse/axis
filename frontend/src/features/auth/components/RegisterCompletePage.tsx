import { Link } from '@tanstack/react-router';
import { useEffect } from 'react';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useRegisterComplete } from '@/features/auth/hooks/useRegisterComplete';

interface RegisterCompletePageProps {
  sessionId: string;
  invalidSession?: boolean;
}

export function RegisterCompletePage({
  sessionId,
  invalidSession = false,
}: RegisterCompletePageProps) {
  const shouldLoadSession = !invalidSession && sessionId.length > 0;
  const { form, sessionQuery, slugPreview, loading, successMessage, submit, resetFlow } =
    useRegisterComplete(sessionId, shouldLoadSession);
  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors },
  } = form;
  const acceptedTerms = watch('acceptedTerms');
  const submitError = errors.root?.message;

  useEffect(() => {
    if (sessionQuery.data?.display_name) {
      setValue('fullName', sessionQuery.data.display_name);
    }
  }, [sessionQuery.data?.display_name, setValue]);

  if (shouldLoadSession && sessionQuery.isLoading) {
    return (
      <AuthCard title="Complete registration">
        <p className="text-sm text-muted-foreground">Loading your sign-in details…</p>
      </AuthCard>
    );
  }

  if (
    invalidSession ||
    !shouldLoadSession ||
    sessionQuery.isError ||
    (!sessionQuery.isLoading && !sessionQuery.data)
  ) {
    return (
      <AuthCard
        title="Registration session expired"
        footer={
          <>
            Need a new link?{' '}
            <Link to="/register" className="font-medium hover:underline">
              Back to registration
            </Link>
          </>
        }
      >
        <p className="text-sm text-muted-foreground">
          This sign-in session is invalid or has expired. Start registration again to continue with
          your external provider.
        </p>
      </AuthCard>
    );
  }

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
          <Button type="button" variant="outline" className="w-full h-9" onClick={resetFlow}>
            Register another organization
          </Button>
        </div>
      </AuthCard>
    );
  }

  const session = sessionQuery.data;
  if (!session) {
    return null;
  }

  return (
    <AuthCard
      title="Complete registration"
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
          {errors.organizationName ? (
            <p className="text-xs text-destructive">{errors.organizationName.message}</p>
          ) : null}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="organizationSlug">Organization slug</Label>
          <Input id="organizationSlug" value={slugPreview || 'your-org'} readOnly disabled />
          <p className="text-xs text-muted-foreground">
            Auto-generated from your organization name.
          </p>
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
          <Input id="email" type="email" value={session.email} readOnly disabled />
        </div>

        <div className="space-y-2">
          <div className="flex items-start gap-2">
            <input
              id="acceptedTerms"
              type="checkbox"
              className="mt-1 h-4 w-4 rounded border border-input"
              checked={acceptedTerms === true}
              onChange={(event) =>
                setValue('acceptedTerms', event.target.checked, { shouldValidate: true })
              }
              aria-invalid={errors.acceptedTerms ? true : undefined}
            />
            <Label htmlFor="acceptedTerms" className="text-sm font-normal leading-snug">
              I agree to the{' '}
              <a href="/legal/terms" target="_blank" rel="noreferrer" className="underline">
                Terms of Service
              </a>{' '}
              and{' '}
              <a href="/legal/privacy" target="_blank" rel="noreferrer" className="underline">
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
