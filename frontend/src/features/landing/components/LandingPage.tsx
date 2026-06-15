import { Building2, LockKeyhole, LogIn, UserPlus } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { ActionLink } from '@/components/ui/action-link';
import { AccessPathTrace } from '@/components/visual/AccessPathTrace';
import { BrandHeader } from '@/components/visual/BrandHeader';
import { HeaderRule } from '@/components/visual/HeaderRule';
import { TopologyBackdrop } from '@/components/visual/TopologyBackdrop';
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
      <ActionLink
        to="/register/organization"
        icon={Building2}
        surface="adaptive"
        variant="secondary"
      >
        {t('organizationRegistration.createWorkspace')}
      </ActionLink>
    </div>
  );
}

function LandingHeroPanel() {
  const { t } = useTranslation();

  return (
    <section className="relative overflow-hidden rounded-lg border border-border/70 bg-card/95 text-foreground shadow-[0_22px_70px_hsl(198_24%_5%/0.14)] backdrop-blur dark:border-[hsl(174_18%_18%)] dark:bg-[linear-gradient(145deg,hsl(174_25%_12%),hsl(174_23%_9%))] dark:text-white dark:shadow-[0_22px_70px_hsl(198_35%_5%/0.24)]">
      <div
        className="absolute -left-20 top-10 size-64 rounded-full bg-primary/15 blur-3xl dark:bg-primary/10"
        aria-hidden
      />
      <div
        className="absolute -bottom-24 right-8 size-72 rounded-full bg-accent/10 blur-3xl"
        aria-hidden
      />

      <div className="relative grid min-h-[460px] gap-8 p-7 sm:p-8 lg:grid-cols-[minmax(0,1fr)_320px] lg:items-center lg:p-10">
        <div className="max-w-xl space-y-10">
          <BrandHeader label={t('landing.accessLabel')} tone="adaptive" />

          <div className="space-y-4">
            <h1 className="max-w-xl text-2xl font-semibold leading-[1.12] tracking-tight sm:text-3xl lg:text-[2.5rem] lg:leading-[1.08]">
              {t('landing.heroTitle')}
            </h1>
            <p className="max-w-md text-sm leading-6 text-muted-foreground dark:text-white/60">
              {t('landing.heroBody')}
            </p>
          </div>

          <LandingActions />
        </div>

        <div className="border-t border-border/70 pt-7 dark:border-white/10 lg:border-l lg:border-t-0 lg:pl-8 lg:pt-0">
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
        <p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground dark:text-white/45">
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
