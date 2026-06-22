import type { ReactNode } from 'react';
import { BrandHeader } from '@/components/visual/BrandHeader';
import { TopologyBackdrop } from '@/components/visual/TopologyBackdrop';
import { PreferenceControls } from '@/features/preferences';

interface AuthCardProps {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  banner?: ReactNode;
}

export function AuthCard({ title, children, footer, banner }: AuthCardProps) {
  return (
    <div className="relative flex min-h-screen items-center justify-center overflow-hidden bg-background p-4 py-8 sm:p-6">
      <TopologyBackdrop className="opacity-85 dark:opacity-65" />
      <PreferenceControls className="absolute right-4 top-4 z-20 sm:right-6 sm:top-6" />
      <div className="relative z-10 flex w-full max-w-[440px] justify-center pt-12 sm:pt-10 lg:pt-0">
        <div className="flex w-full flex-col overflow-hidden rounded-lg border border-border/70 bg-card/95 shadow-panel backdrop-blur">
          <div className="space-y-6 px-6 py-6">
            <BrandHeader label={title} labelElement="h1" />
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
