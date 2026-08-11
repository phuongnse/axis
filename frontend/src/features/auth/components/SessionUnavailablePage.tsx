import { useTranslation } from 'react-i18next';

import { EntrySurface } from '@/components/shared/EntrySurface';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Button } from '@/components/ui/button';
import { PreferencesMenu } from '@/features/preferences';

interface SessionUnavailablePageProps {
  onRetry: () => void;
}

export function SessionUnavailablePage({ onRetry }: SessionUnavailablePageProps) {
  const { t } = useTranslation();

  return (
    <EntrySurface
      surfaceId="session-unavailable"
      utilities={<PreferencesMenu />}
      title={t('auth.sessionUnavailableTitle')}
    >
      <div className="space-y-4">
        <StatusNotice tone="warning" title={t('auth.sessionUnavailableTitle')}>
          {t('auth.sessionUnavailableBody')}
        </StatusNotice>
        <Button type="button" className="w-full" onClick={onRetry}>
          {t('auth.sessionUnavailableRetry')}
        </Button>
      </div>
    </EntrySurface>
  );
}
