import { createLazyFileRoute, Link } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { AuthCard } from '@/features/auth/components/AuthCard';

export const Route = createLazyFileRoute('/forgot-password')({
  component: ForgotPasswordPage,
});

function ForgotPasswordPage() {
  return (
    <AuthCard
      title="Reset your password"
      footer={
        <>
          Remember your password?{' '}
          <Link to="/login" className="font-medium hover:underline">
            Sign in
          </Link>
        </>
      }
    >
      <p className="text-sm text-muted-foreground">
        Enter your email and we will send you a reset link.
      </p>
      <form className="space-y-4" onSubmit={(e) => e.preventDefault()}>
        <div className="space-y-1.5">
          <Label htmlFor="fp-email">Email address</Label>
          <Input
            id="fp-email"
            type="email"
            autoComplete="username"
            placeholder="you@company.com"
            disabled
          />
        </div>
        <Button variant="cta" className="w-full h-9" disabled>
          Send reset link
        </Button>
      </form>
    </AuthCard>
  );
}
