import { Link } from '@tanstack/react-router';
import { Mail } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { loadRegistrationContext } from '@/features/auth/registration-context';

export function EmailConfirmationPage() {
  const { t } = useTranslation();
  const context = loadRegistrationContext();
  const { resend, state, reset } = useResendVerification();

  async function handleResend() {
    if (!context?.email || state === 'sending' || state === 'rate_limited') return;
    reset();
    await resend(context.email).catch(() => undefined);
  }

  return (
    <AuthCard
      title={t('auth.confirm.title')}
      footer={
        <>
          {t('auth.confirm.useAnother')}{' '}
          <Link to="/register" className="font-medium text-primary hover:underline">
            {t('auth.backToRegistration')}
          </Link>
        </>
      }
    >
      <div className="space-y-4">
        <Alert>
          <Mail aria-hidden />
          <AlertDescription>
            <div className="space-y-2">
              <p>{t('auth.confirm.body1')}</p>
              <p>{t('auth.confirm.body2')}</p>
              {context?.email ? (
                <p className="text-xs">{t('auth.confirm.sentTo', { email: context.email })}</p>
              ) : null}
            </div>
          </AlertDescription>
        </Alert>

        {state === 'sending' ? (
          <StatusNotice title={t('notice.sendingEmailTitle')}>
            {t('notice.sendingEmail')}
          </StatusNotice>
        ) : null}

        {state === 'success' ? (
          <StatusNotice tone="success" title={t('notice.resendSentTitle')}>
            {t('notice.resendSent')}
          </StatusNotice>
        ) : null}

        {state === 'rate_limited' ? (
          <StatusNotice tone="warning" title={t('notice.resendLimitedTitle')}>
            {t('notice.resendLimited')}
          </StatusNotice>
        ) : null}

        {state === 'error' ? (
          <StatusNotice tone="destructive" title={t('notice.resendErrorTitle')}>
            {t('notice.resendError')}
          </StatusNotice>
        ) : null}

        <div className="flex flex-wrap items-center gap-x-1.5 gap-y-1 text-sm">
          <span className="text-muted-foreground">{t('auth.confirm.didNotReceive')}</span>
          {context?.email ? (
            <Button
              type="button"
              variant="link"
              disabled={state === 'sending' || state === 'rate_limited'}
              onClick={() => void handleResend()}
            >
              <Mail className="size-3.5" aria-hidden />
              {t('auth.resendEmail')}
            </Button>
          ) : (
            <Link to="/register" className="font-medium text-primary hover:underline">
              {t('auth.backToRegistration')}
            </Link>
          )}
        </div>
      </div>
    </AuthCard>
  );
}
