import type { ReactNode } from 'react';
import { BrandHeader } from '@/components/shared/BrandHeader';
import { TopologyBackdrop } from '@/components/shared/TopologyBackdrop';

interface AuthCardProps {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  banner?: ReactNode;
}

export function AuthCard({ title, children, footer, banner }: AuthCardProps) {
  return (
    <div className="relative flex min-h-screen items-center justify-center overflow-hidden bg-background p-4 py-8 sm:p-6">
      <TopologyBackdrop className="opacity-90" />
      <div className="relative z-10 flex w-full max-w-[440px] justify-center">
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
