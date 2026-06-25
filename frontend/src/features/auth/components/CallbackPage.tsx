import { useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import { exchangeAuthorizationCode } from '@/features/auth/api';
import { clearPkceSession, loadPkceSession } from '@/features/auth/pkce';

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
      setError('Invalid authorization response. Please try registering again.');
      return;
    }

    exchangeAuthorizationCode(code)
      .then(() => {
        void navigate({ to: '/dashboard', replace: true });
      })
      .catch(() => {
        clearPkceSession();
        setError('Token exchange failed. Please try registering again.');
      });
  }, [navigate]);

  if (error) {
    return (
      <div className="flex min-h-screen items-center justify-center p-4">
        <p className="text-destructive">{error}</p>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-4">
      <p className="text-muted-foreground">Completing sign-in...</p>
    </div>
  );
}
