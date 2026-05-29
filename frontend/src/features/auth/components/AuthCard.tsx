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
        <div className="px-6 pt-5 pb-4 text-center border-b border-border">
          <img src={axisLogo} alt="Axis" className="mx-auto h-8 w-8" width={32} height={32} />
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
