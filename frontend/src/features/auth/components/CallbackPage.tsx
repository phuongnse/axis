import { Link, useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { exchangeAuthorizationCode } from '@/features/auth/api';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { clearPkceSession, loadPkceSession } from '@/features/auth/pkce';

type CallbackErrorKind = 'invalid' | 'tokenFailed';
type Translate = ReturnType<typeof useTranslation>['t'];

function SignInAgainFooter({ t }: { t: Translate }) {
  return (
    <>
      {t('auth.needStartOver')}{' '}
      <Link to="/sign-in" className="font-medium text-primary hover:underline">
        {t('auth.signIn')}
      </Link>
    </>
  );
}

export function CallbackPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [error, setError] = useState<CallbackErrorKind | null>(null);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const code = params.get('code');
    const state = params.get('state');
    const pkce = loadPkceSession();

    if (!code || !pkce || state !== pkce.state) {
      clearPkceSession();
      setError('invalid');
      return;
    }

    exchangeAuthorizationCode(code)
      .then(() => {
        void navigate({ to: '/dashboard', replace: true });
      })
      .catch(() => {
        clearPkceSession();
        setError('tokenFailed');
      });
  }, [navigate]);

  if (error) {
    const message =
      error === 'invalid' ? t('auth.callback.invalid') : t('auth.callback.tokenFailed');
    return (
      <AuthCard title={t('auth.callback.retryTitle')} footer={<SignInAgainFooter t={t} />}>
        <AuthNotice variant="destructive">{message}</AuthNotice>
      </AuthCard>
    );
  }

  return (
    <AuthCard title={t('auth.callback.title')} footer={<SignInAgainFooter t={t} />}>
      <p className="text-sm text-muted-foreground">{t('auth.callback.completing')}</p>
    </AuthCard>
  );
}
