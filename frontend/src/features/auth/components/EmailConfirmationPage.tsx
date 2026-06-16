import { Link } from '@tanstack/react-router';
import { Mail, UserPlus } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { ActionLink } from '@/components/ui/action-link';
import { Button } from '@/components/ui/button';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { loadRegistrationContext } from '@/features/auth/registration-context';

export function EmailConfirmationPage() {
  const { t } = useTranslation();
  const context = loadRegistrationContext();
  const { resend, state, rateLimitMessage, reset } = useResendVerification();

  async function handleResend() {
    if (!context?.email || state === 'sending' || state === 'rate_limited') return;
    reset();
    try {
      await resend(context.email);
    } catch {
      // Resend state is derived from mutation error.
    }
  }

  return (
    <AuthCard
      title={t('emailConfirmation.title')}
      footer={
        <>
          {t('emailConfirmation.footerPrompt')}{' '}
          <Link to="/login" className="font-medium hover:underline">
            {t('emailConfirmation.goToSignIn')}
          </Link>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex items-start gap-3">
          <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-sky-500/10 text-sky-600">
            <Mail className="h-4 w-4" aria-hidden />
          </div>
          <div className="space-y-2 text-sm text-muted-foreground">
            <p>{t('emailConfirmation.intro')}</p>
            <p>{t('emailConfirmation.checkInbox')}</p>
            {context?.email ? (
              <p className="text-xs text-muted-foreground/80">
                {t('emailConfirmation.sentTo', { email: context.email })}
              </p>
            ) : null}
          </div>
        </div>

        {state === 'sending' ? (
          <AuthNotice variant="info" title={t('emailConfirmation.sendingTitle')}>
            {t('emailConfirmation.sendingBody')}
          </AuthNotice>
        ) : null}

        {state === 'success' ? (
          <AuthNotice variant="success" title={t('emailConfirmation.successTitle')}>
            {t('emailConfirmation.successBody')}
          </AuthNotice>
        ) : null}

        {state === 'rate_limited' ? (
          <AuthNotice variant="warning" title={t('emailConfirmation.waitTitle')}>
            {rateLimitMessage ?? t('emailConfirmation.waitBodyDefault')}
          </AuthNotice>
        ) : null}

        {state === 'error' ? (
          <AuthNotice variant="error" title={t('emailConfirmation.errorTitle')}>
            {t('emailConfirmation.errorBody')}
          </AuthNotice>
        ) : null}

        <div className="text-sm">
          <span className="text-muted-foreground">{t('emailConfirmation.didntReceive')} </span>
          {context?.email ? (
            <Button
              type="button"
              variant="link"
              className="h-auto p-0 text-sm font-medium disabled:text-muted-foreground disabled:no-underline"
              disabled={state === 'sending' || state === 'rate_limited'}
              onClick={() => void handleResend()}
            >
              <Mail className="size-3.5" aria-hidden />
              {t('emailConfirmation.resendEmail')}
            </Button>
          ) : (
            <Link to="/register" className="font-medium text-primary hover:underline">
              {t('emailConfirmation.backToRegistration')}
            </Link>
          )}
        </div>

        <ActionLink to="/register" icon={UserPlus} variant="secondary" className="w-full">
          {t('emailConfirmation.registerAnotherAccount')}
        </ActionLink>
      </div>
    </AuthCard>
  );
}
