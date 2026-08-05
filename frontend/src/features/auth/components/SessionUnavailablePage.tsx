import { useTranslation } from 'react-i18next';

import { StatusNotice } from '@/components/shared/StatusNotice';
import { Button } from '@/components/ui/button';
import { AuthCard } from '@/features/auth/components/AuthCard';

interface SessionUnavailablePageProps {
  onRetry: () => void;
}

export function SessionUnavailablePage({ onRetry }: SessionUnavailablePageProps) {
  const { t } = useTranslation();

  return (
    <AuthCard title={t('auth.sessionUnavailableTitle')}>
      <div className="space-y-4">
        <StatusNotice tone="warning" title={t('auth.sessionUnavailableTitle')}>
          {t('auth.sessionUnavailableBody')}
        </StatusNotice>
        <Button type="button" className="w-full" onClick={onRetry}>
          {t('auth.sessionUnavailableRetry')}
        </Button>
      </div>
    </AuthCard>
  );
}
