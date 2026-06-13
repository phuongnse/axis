import { useTranslation } from 'react-i18next';

import { AccessPathTrace } from '@/components/visual/AccessPathTrace';
import { BrandHeader } from '@/components/visual/BrandHeader';

export function AuthSignalPanel() {
  const { t } = useTranslation();

  return (
    <section className="relative hidden min-h-[420px] overflow-hidden rounded-lg border border-white/10 bg-[linear-gradient(145deg,hsl(174_25%_13%),hsl(174_23%_10%))] p-7 text-white shadow-[0_18px_55px_hsl(198_35%_4%/0.18)] lg:flex lg:flex-col lg:justify-start">
      <div
        className="absolute -left-24 top-12 size-56 rounded-full bg-primary/10 blur-3xl"
        aria-hidden
      />
      <div
        className="absolute -bottom-24 right-4 size-52 rounded-full bg-accent/10 blur-3xl"
        aria-hidden
      />

      <div className="relative flex flex-col gap-8">
        <BrandHeader label={t('common.controlPlane')} tone="inverted" />

        <div>
          <p className="text-xs uppercase tracking-[0.18em] text-white/40">
            {t('auth.secureWorkspaceAccess')}
          </p>
          <h2 className="mt-4 max-w-xs text-2xl font-semibold leading-tight tracking-tight">
            {t('auth.sideTitle')}
          </h2>
          <p className="mt-3 max-w-xs text-sm leading-6 text-white/60">{t('auth.sideBody')}</p>
        </div>

        <AccessPathTrace surface="dark" size="md" />
      </div>
    </section>
  );
}
