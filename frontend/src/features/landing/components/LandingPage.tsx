import { LockKeyhole, LogIn, UserPlus } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import axisLogo from '@/assets/axis-logo.svg';
import { ActionLink } from '@/components/ui/action-link';
import { AccessPathTrace } from '@/components/visual/AccessPathTrace';
import { HeaderRule } from '@/components/visual/HeaderRule';
import { TopologyBackdrop } from '@/components/visual/TopologyBackdrop';
import { PreferenceControls } from '@/features/preferences';

function BrandLockup() {
  const { t } = useTranslation();

  return (
    <div className="flex items-center gap-3">
      <img src={axisLogo} alt="Axis" className="size-11 shrink-0" width={44} height={44} />
      <div>
        <p className="text-lg font-semibold">Axis</p>
        <p className="text-xs uppercase tracking-[0.18em] text-white/45">
          {t('common.controlPlane')}
        </p>
      </div>
    </div>
  );
}

function LandingActions() {
  const { t } = useTranslation();

  return (
    <div className="flex flex-wrap gap-3">
      <ActionLink to="/login" icon={LogIn} surface="inverted">
        {t('common.signIn')}
      </ActionLink>
      <ActionLink to="/register" icon={UserPlus} surface="inverted" variant="secondary">
        {t('common.createAccount')}
      </ActionLink>
    </div>
  );
}

function LandingHeroPanel() {
  const { t } = useTranslation();

  return (
    <div className="flex h-full min-h-[380px] flex-col justify-center rounded-lg border border-[hsl(174_18%_18%)] bg-[hsl(174_25%_12%)] p-7 text-white shadow-sm">
      <div className="space-y-10">
        <div className="space-y-7">
          <BrandLockup />
          <HeaderRule tone="inverted" />
        </div>

        <div className="space-y-3">
          <h1 className="text-4xl font-semibold leading-tight tracking-tight">
            {t('landing.heroTitle')}
          </h1>
          <p className="max-w-sm text-sm leading-6 text-white/60">{t('landing.heroBody')}</p>
        </div>

        <LandingActions />
      </div>
    </div>
  );
}

function GatewayPreview() {
  const { t } = useTranslation();

  return (
    <div className="relative flex h-full min-h-[380px] items-center overflow-hidden rounded-lg border border-border/80 bg-card/90 p-7 shadow-sm backdrop-blur">
      <div className="relative w-full">
        <div className="flex items-center justify-between gap-3">
          <p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">
            {t('landing.accessPath')}
          </p>
          <LockKeyhole className="size-4 text-primary" aria-hidden />
        </div>
        <HeaderRule className="mt-6" />

        <AccessPathTrace className="mt-10" />
      </div>
    </div>
  );
}

export function LandingPage() {
  return (
    <main className="relative flex min-h-screen items-center justify-center overflow-hidden bg-background p-4 sm:p-6">
      <TopologyBackdrop className="opacity-90 dark:opacity-70" />
      <PreferenceControls className="absolute right-4 top-4 z-20 sm:right-6 sm:top-6" />
      <section className="relative z-10 grid w-full max-w-5xl items-stretch gap-4 lg:grid-cols-[420px_minmax(0,520px)]">
        <LandingHeroPanel />
        <GatewayPreview />
      </section>
    </main>
  );
}
