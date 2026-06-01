import { createLazyFileRoute, useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { exchangeAuthorizationCode } from '@/features/auth/api';
import { clearPkceSession, loadPkceSession } from '@/features/auth/pkce';
import { consumePostVerifyProvisioningToken } from '@/features/auth/post-verify-session';

export const Route = createLazyFileRoute('/callback')({
  component: CallbackPage,
});

function CallbackPage() {
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
        // Consume the post-verify token only once the exchange has succeeded, so a
        // failed/invalid callback does not discard a still-valid provisioning token.
        const provisioningToken = consumePostVerifyProvisioningToken();
        if (provisioningToken) {
          void navigate({
            to: '/provisioning',
            search: { token: provisioningToken },
            replace: true,
          });
          return;
        }
        void navigate({ to: '/dashboard', replace: true });
      })
      .catch(() => {
        clearPkceSession();
        setError('Could not complete sign-in. Please try again.');
      });
  }, [navigate]);

  if (error) {
    return (
      <div className="min-h-screen flex items-center justify-center p-4">
        <p className="text-destructive">{error}</p>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center p-4">
      <p className="text-muted-foreground">Completing sign-in…</p>
    </div>
  );
}
