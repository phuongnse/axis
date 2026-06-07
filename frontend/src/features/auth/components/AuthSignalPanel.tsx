import { useTranslation } from 'react-i18next';

import axisLogo from '@/assets/axis-logo.svg';
import { AccessPathTrace } from '@/components/visual/AccessPathTrace';
import { HeaderRule } from '@/components/visual/HeaderRule';

export function AuthSignalPanel() {
  const { t } = useTranslation();

  return (
    <section className="relative hidden min-h-[480px] overflow-hidden rounded-lg border border-white/10 bg-[linear-gradient(145deg,hsl(174_25%_13%),hsl(174_23%_10%))] p-8 text-white shadow-[0_18px_55px_hsl(198_35%_4%/0.22)] lg:flex lg:flex-col lg:justify-start">
      <div
        className="absolute -left-24 top-12 size-56 rounded-full bg-primary/10 blur-3xl"
        aria-hidden
      />
      <div
        className="absolute -bottom-24 right-4 size-52 rounded-full bg-accent/10 blur-3xl"
        aria-hidden
      />

      <div className="relative flex flex-col gap-10">
        <div className="space-y-7">
          <div className="flex items-center gap-3">
            <img src={axisLogo} alt="" className="size-10 shrink-0" width={40} height={40} />
            <div>
              <p className="text-base font-semibold">Axis</p>
              <p className="text-xs uppercase tracking-[0.18em] text-white/45">
                {t('common.controlPlane')}
              </p>
            </div>
          </div>
          <HeaderRule tone="inverted" />
        </div>

        <div>
          <p className="text-xs uppercase tracking-[0.18em] text-white/40">
            {t('auth.secureWorkspaceAccess')}
          </p>
          <h2 className="mt-4 max-w-xs text-3xl font-semibold leading-tight tracking-tight">
            {t('auth.sideTitle')}
          </h2>
          <p className="mt-4 max-w-xs text-sm leading-6 text-white/60">{t('auth.sideBody')}</p>
        </div>

        <AccessPathTrace surface="dark" size="md" />
      </div>
    </section>
  );
}
