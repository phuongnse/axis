import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { exchangeAuthorizationCode } from '@/features/auth/api';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { clearPkceSession, loadPkceSession } from '@/features/auth/pkce';

function SignInAgainFooter() {
  return (
    <>
      Need to start over?{' '}
      <Link to="/sign-in" className="font-medium text-primary hover:underline">
        Sign in
      </Link>
    </>
  );
}

export function CallbackPage() {
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const code = params.get('code');
    const state = params.get('state');
    const pkce = loadPkceSession();

    if (!code || !pkce || state !== pkce.state) {
      clearPkceSession();
      setError('Invalid authorization response. Please try signing in again.');
      return;
    }

    exchangeAuthorizationCode(code)
      .then(() => {
        void navigate({ to: '/dashboard', replace: true });
      })
      .catch(() => {
        clearPkceSession();
        setError('Token exchange failed. Please try signing in again.');
      });
  }, [navigate]);

  if (error) {
    return (
      <AuthCard title="Sign-in interrupted" footer={<SignInAgainFooter />}>
        <AuthNotice variant="destructive">{error}</AuthNotice>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="Completing sign-in" footer={<SignInAgainFooter />}>
      <p className="text-sm text-muted-foreground">Completing sign-in...</p>
    </AuthCard>
  );
}
