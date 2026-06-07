import type { ReactNode } from 'react';
import axisLogo from '@/assets/axis-logo.svg';
import { HeaderRule } from '@/components/visual/HeaderRule';
import { TopologyBackdrop } from '@/components/visual/TopologyBackdrop';
import { AuthSignalPanel } from '@/features/auth/components/AuthSignalPanel';
import { PreferenceControls } from '@/features/preferences';
import { cn } from '@/lib/utils';

interface AuthCardProps {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  banner?: ReactNode;
}

function AuthHeader() {
  return (
    <header className="px-6 pt-5">
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <img src={axisLogo} alt="Axis" className="size-9 shrink-0" width={36} height={36} />
          <span aria-hidden className="text-lg font-semibold tracking-tight text-foreground">
            Axis
          </span>
        </div>
        <PreferenceControls />
      </div>
      <HeaderRule className="mt-5" />
    </header>
  );
}

export function AuthCard({ title, children, footer, banner }: AuthCardProps) {
  return (
    <div className="relative flex min-h-screen items-center justify-center overflow-hidden bg-background p-4 sm:p-6">
      <TopologyBackdrop className="opacity-85 dark:opacity-65" />
      <div
        className={cn(
          'relative z-10 grid w-full max-w-4xl gap-3 lg:grid-cols-[360px_minmax(0,420px)]',
          'items-stretch justify-center',
        )}
      >
        <AuthSignalPanel />

        <div className="flex w-full flex-col overflow-hidden rounded-lg border border-border/70 bg-card/95 shadow-[0_18px_55px_hsl(198_24%_5%/0.16)] backdrop-blur">
          <AuthHeader />
          <div className="flex-1 space-y-5 px-6 py-6">
            <h1 className="text-[17px] font-semibold text-foreground">{title}</h1>
            {banner}
            {children}
          </div>
          {footer ? (
            <div className="border-t border-border bg-muted/30 px-6 py-4 text-center text-xs text-primary">
              {footer}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
