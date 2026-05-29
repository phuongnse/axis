import type { ReactNode } from 'react';

import axisLogo from '@/assets/axis-logo.svg';
import { cn } from '@/lib/utils';

interface AuthCardProps {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  banner?: ReactNode;
}

export function AuthCard({ title, children, footer, banner }: AuthCardProps) {
  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-6">
      <div
        className={cn(
          'w-full max-w-[440px] rounded-xl border border-border bg-card shadow-sm',
          'flex flex-col overflow-hidden',
        )}
      >
        <div className="px-6 pt-5 pb-4 border-b border-border">
          <div className="flex items-center justify-center gap-1.5" aria-label="Axis">
            <img src={axisLogo} alt="" className="h-9 w-9 shrink-0" width={36} height={36} />
            <span className="text-lg font-semibold tracking-tight text-foreground">Axis</span>
          </div>
        </div>
        <div className="px-6 py-6 space-y-5">
          <h1 className="text-[17px] font-semibold text-foreground">{title}</h1>
          {banner}
          {children}
        </div>
        {footer ? (
          <div className="px-6 py-4 border-t border-border text-center text-xs text-primary">
            {footer}
          </div>
        ) : null}
      </div>
    </div>
  );
}
