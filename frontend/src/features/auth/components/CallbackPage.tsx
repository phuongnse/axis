import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';

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
  const params = new URLSearchParams(window.location.search);
  const error: CallbackErrorKind =
    params.get('error') === 'tokenFailed' ? 'tokenFailed' : 'invalid';
  const message = error === 'invalid' ? t('auth.callback.invalid') : t('auth.callback.tokenFailed');

  return (
    <AuthCard title={t('auth.callback.retryTitle')} footer={<SignInAgainFooter t={t} />}>
      <AuthNotice variant="destructive">{message}</AuthNotice>
    </AuthCard>
  );
}
