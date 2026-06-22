import { useTranslation } from 'react-i18next';

import { AccessPathTrace } from '@/components/visual/AccessPathTrace';
import { BrandHeader } from '@/components/visual/BrandHeader';

export function AuthSignalPanel() {
  const { t } = useTranslation();

  return (
    <section className="relative hidden min-h-[420px] overflow-hidden rounded-lg border border-inverse-border bg-gradient-inverse-panel p-7 text-inverse-foreground shadow-panel lg:flex lg:flex-col lg:justify-start">
      <div className="relative flex flex-col gap-8">
        <BrandHeader label={t('common.controlPlane')} tone="inverted" />

        <div>
          <p className="text-xs uppercase tracking-[0.18em] text-inverse-muted">
            {t('auth.secureWorkspaceAccess')}
          </p>
          <h2 className="mt-4 max-w-xs text-2xl font-semibold leading-tight tracking-tight">
            {t('auth.sideTitle')}
          </h2>
          <p className="mt-3 max-w-xs text-sm leading-6 text-inverse-muted">{t('auth.sideBody')}</p>
        </div>

        <AccessPathTrace surface="dark" size="md" />
      </div>
    </section>
  );
}
