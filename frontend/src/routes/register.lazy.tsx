import { createLazyFileRoute, Link } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { AuthCard } from '@/features/auth/components/AuthCard';

export const Route = createLazyFileRoute('/register')({
  component: RegisterPage,
});

function RegisterPage() {
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
      <p className="text-sm text-muted-foreground">
        Organization registration is coming soon. Use the sign-in page if you already have an
        account.
      </p>
      <div className="space-y-4 opacity-60 pointer-events-none" aria-hidden>
        <div className="space-y-1.5">
          <Label htmlFor="reg-name">Full name</Label>
          <Input id="reg-name" placeholder="Alex Brown" disabled />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="reg-email">Email address</Label>
          <Input id="reg-email" type="email" placeholder="you@company.com" disabled />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="reg-password">Password</Label>
          <Input id="reg-password" type="password" placeholder="••••••••" disabled />
        </div>
        <Button variant="cta" className="w-full h-9" disabled>
          Create account
        </Button>
      </div>
    </AuthCard>
  );
}
