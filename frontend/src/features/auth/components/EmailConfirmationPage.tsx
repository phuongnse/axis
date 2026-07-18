import { useTranslation } from 'react-i18next';
import {
  InlinePromptAction,
  InlinePromptActionButton,
  InlinePromptActionLink,
} from '@/components/shared/InlinePromptAction';
import { StatusNotice } from '@/components/shared/StatusNotice';
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
        <InlinePromptAction prompt={t('auth.confirm.useAnother')}>
          <InlinePromptActionLink to="/register">
            {t('auth.backToRegistration')}
          </InlinePromptActionLink>
        </InlinePromptAction>
      }
    >
      <div className="space-y-4">
        <StatusNotice tone="info">
          <div className="space-y-2">
            <p>{t('auth.confirm.body1')}</p>
            <p>{t('auth.confirm.body2')}</p>
            {context?.email ? (
              <p className="text-xs">{t('auth.confirm.sentTo', { email: context.email })}</p>
            ) : null}
          </div>
        </StatusNotice>

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

        <InlinePromptAction prompt={t('auth.confirm.didNotReceive')}>
          {context?.email ? (
            <InlinePromptActionButton
              type="button"
              disabled={state === 'sending' || state === 'rate_limited'}
              onClick={() => void handleResend()}
            >
              {t('auth.resendEmail')}
            </InlinePromptActionButton>
          ) : (
            <InlinePromptActionLink to="/register">
              {t('auth.backToRegistration')}
            </InlinePromptActionLink>
          )}
        </InlinePromptAction>
      </div>
    </AuthCard>
  );
}
