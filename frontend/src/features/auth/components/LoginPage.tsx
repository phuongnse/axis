import { Link } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useLogin } from '@/features/auth/hooks/useLogin';

export function LoginPage() {
  const { form, loginError, loading, submit } = useLogin();
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = form;

  return (
    <AuthCard
      title="Sign in to Axis"
      footer={
        <>
          Don&apos;t have an account?{' '}
          <Link to="/register" className="font-medium hover:underline">
            Sign up
          </Link>
        </>
      }
      banner={
        loginError ? (
          <div
            className="rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive"
            role="alert"
          >
            {loginError.message}
            {loginError.kind === 'unverified' ? (
              <p className="mt-2 text-center text-xs text-muted-foreground">
                Check your inbox for the verification link.
              </p>
            ) : null}
          </div>
        ) : null
      }
    >
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        <div className="space-y-1.5">
          <Label htmlFor="email">Email address</Label>
          <Input
            id="email"
            type="email"
            autoComplete="username"
            placeholder="you@company.com"
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
            autoComplete="current-password"
            placeholder="••••••••"
            aria-invalid={errors.password ? true : undefined}
            {...register('password')}
          />
          {errors.password ? (
            <p className="text-xs text-destructive">{errors.password.message}</p>
          ) : null}
        </div>

        <div className="flex justify-end">
          <Link to="/forgot-password" className="text-xs text-primary hover:underline">
            Forgot password?
          </Link>
        </div>

        <Button type="submit" variant="cta" className="w-full h-9" disabled={loading}>
          {loading ? 'Signing in…' : 'Sign in'}
        </Button>
      </form>
    </AuthCard>
  );
}
