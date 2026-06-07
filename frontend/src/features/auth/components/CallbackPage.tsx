import { useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { exchangeAuthorizationCode } from '@/features/auth/api';
import { clearPkceSession, loadPkceSession } from '@/features/auth/pkce';
import { consumePostVerifyProvisioningToken } from '@/features/auth/post-verify-session';

export function CallbackPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const code = params.get('code');
    const state = params.get('state');
    const pkce = loadPkceSession();

    if (!code || !pkce || state !== pkce.state) {
      clearPkceSession();
      setError(t('callback.invalid'));
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
        setError(t('callback.failed'));
      });
  }, [navigate, t]);

  if (error) {
    return (
      <div className="flex min-h-screen items-center justify-center p-4">
        <p className="text-destructive">{error}</p>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-4">
      <p className="text-muted-foreground">{t('callback.completing')}</p>
    </div>
  );
}
