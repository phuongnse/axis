import { LockKeyhole, LogIn, UserPlus } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { AccessPathTrace } from '@/components/shared/AccessPathTrace';
import { ActionLink } from '@/components/shared/ActionLink';
import { BrandHeader } from '@/components/shared/BrandHeader';
import { HeaderRule } from '@/components/shared/HeaderRule';
import { TopologyBackdrop } from '@/components/shared/TopologyBackdrop';
import { PreferenceControls } from '@/features/preferences';

function LandingActions() {
  const { t } = useTranslation();

  return (
    <div className="flex flex-wrap gap-3">
      <ActionLink to="/login" icon={LogIn} surface="adaptive">
        {t('common.signIn')}
      </ActionLink>
      <ActionLink to="/register" icon={UserPlus} surface="adaptive" variant="secondary">
        {t('common.createAccount')}
      </ActionLink>
    </div>
  );
}

function LandingHeroPanel() {
  const { t } = useTranslation();

  return (
    <section className="relative overflow-hidden rounded-lg border border-border/70 bg-card/95 text-foreground shadow-feature-panel backdrop-blur dark:border-inverse-border dark:bg-gradient-inverse-panel dark:text-inverse-foreground">
      <div className="relative grid min-h-[460px] gap-8 p-7 sm:p-8 lg:grid-cols-[minmax(0,1fr)_320px] lg:items-center lg:p-10">
        <div className="max-w-xl space-y-10">
          <BrandHeader label={t('landing.accessLabel')} tone="adaptive" />

          <div className="space-y-4">
            <h1 className="max-w-xl text-2xl font-semibold leading-[1.12] tracking-tight sm:text-3xl lg:text-[2.5rem] lg:leading-[1.08]">
              {t('landing.heroTitle')}
            </h1>
            <p className="max-w-md text-sm leading-6 text-muted-foreground dark:text-inverse-muted">
              {t('landing.heroBody')}
            </p>
          </div>

          <LandingActions />
        </div>

        <div className="border-t border-border/70 pt-7 dark:border-inverse-border lg:border-l lg:border-t-0 lg:pl-8 lg:pt-0">
          <GatewayPreview />
        </div>
      </div>
    </section>
  );
}

function GatewayPreview() {
  const { t } = useTranslation();

  return (
    <div className="relative w-full">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground dark:text-inverse-muted">
          {t('landing.accessPath')}
        </p>
        <LockKeyhole className="size-4 text-primary" aria-hidden />
      </div>
      <HeaderRule tone="adaptive" className="mt-6" />

      <AccessPathTrace surface="adaptive" size="md" className="mt-9" />
    </div>
  );
}

export function LandingPage() {
  return (
    <main className="relative flex min-h-screen items-center justify-center overflow-hidden bg-background p-4 sm:p-6">
      <TopologyBackdrop className="opacity-90 dark:opacity-70" />
      <PreferenceControls className="absolute right-4 top-4 z-20 sm:right-6 sm:top-6" />
      <div className="relative z-10 w-full max-w-5xl">
        <LandingHeroPanel />
      </div>
    </main>
  );
}
