import type { ReactNode } from 'react';
import { BrandHeader } from '@/components/shared/BrandHeader';
import { TopologyBackdrop } from '@/components/shared/TopologyBackdrop';
import { PreferencesMenu } from '@/features/preferences';

interface AuthCardProps {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  banner?: ReactNode;
}

export function AuthCard({ title, children, footer, banner }: AuthCardProps) {
  return (
    <div className="relative min-h-screen overflow-hidden bg-background p-4 pb-8 pt-40 sm:p-6">
      <TopologyBackdrop className="opacity-90" />
      <div className="absolute right-4 top-4 z-20 sm:right-6 sm:top-6">
        <PreferencesMenu />
      </div>
      <div className="relative z-10 mx-auto flex min-h-[calc(100vh-12rem)] w-full max-w-[520px] items-center justify-center sm:min-h-[calc(100vh-3rem)]">
        <div className="flex w-full flex-col overflow-hidden rounded-xl border border-border/80 bg-card/95 backdrop-blur">
          <div className="space-y-6 px-6 py-6">
            <BrandHeader label={title} labelElement="h1" />
            {banner}
            {children}
          </div>
          {footer ? (
            <div className="border-t border-border bg-muted/35 px-6 py-4 text-center text-xs text-muted-foreground">
              {footer}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
