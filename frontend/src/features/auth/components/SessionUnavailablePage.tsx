import { useTranslation } from 'react-i18next';

import { EntryAction, EntrySurface } from '@/components/shared/EntrySurface';
import { StatusNotice } from '@/components/shared/StatusNotice';
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
        <EntryAction type="button" onClick={onRetry}>
          {t('auth.sessionUnavailableRetry')}
        </EntryAction>
      </div>
    </EntrySurface>
  );
}
