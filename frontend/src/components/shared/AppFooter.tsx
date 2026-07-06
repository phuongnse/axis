import { Copyright } from 'lucide-react';
import { useTranslation } from 'react-i18next';

const APP_VERSION = '0.1.0';
const COPYRIGHT_YEAR = '2026';

export function AppFooter() {
  const { t } = useTranslation();

  return (
    <footer className="shrink-0 border-t border-border bg-card">
      <div className="flex w-full min-w-0 flex-col gap-2 px-4 py-4 text-xs text-muted-foreground sm:flex-row sm:items-center sm:justify-between sm:px-6 lg:px-8">
        <p className="font-medium">{t('nav.version', { version: APP_VERSION })}</p>

        <p className="inline-flex items-center gap-1.5 font-medium">
          <span>{t('app.productName')}</span>
          <Copyright className="size-3.5" aria-hidden />
          <span className="sr-only">{t('nav.copyright')}</span>
          <span>{COPYRIGHT_YEAR}</span>
        </p>
      </div>
    </footer>
  );
}
