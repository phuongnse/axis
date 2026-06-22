import type { LucideIcon } from 'lucide-react';
import { AlertCircle, AlertTriangle, CheckCircle2, Info } from 'lucide-react';
import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

type NoticeVariant = 'info' | 'success' | 'warning' | 'error';

interface NoticeProps {
  title?: ReactNode;
  children?: ReactNode;
  variant?: NoticeVariant;
  icon?: LucideIcon;
  className?: string;
}

const noticeStyles: Record<NoticeVariant, string> = {
  info: 'border-state-info-border bg-state-info-background text-state-info-foreground dark:text-state-info-foreground',
  success:
    'border-state-success-border bg-state-success-background text-state-success-foreground dark:text-state-success-foreground',
  warning:
    'border-state-warning-border bg-state-warning-background text-state-warning-foreground dark:text-state-warning-foreground',
  error: 'border-destructive/30 bg-destructive/10 text-destructive',
};

const noticeIcons = {
  info: Info,
  success: CheckCircle2,
  warning: AlertTriangle,
  error: AlertCircle,
} satisfies Record<NoticeVariant, typeof Info>;

function Notice({ title, children, variant = 'info', icon, className }: NoticeProps) {
  const Icon = icon ?? noticeIcons[variant];
  const role = variant === 'error' || variant === 'warning' ? 'alert' : 'status';

  return (
    <div
      className={cn('rounded-lg border px-3 py-2 text-sm', noticeStyles[variant], className)}
      role={role}
    >
      <div className="flex items-start gap-2">
        <Icon className="mt-0.5 size-4 shrink-0" aria-hidden />
        <div className="min-w-0">
          {title ? <p className="font-medium">{title}</p> : null}
          {children ? (
            <div className={cn('text-xs opacity-90', title ? 'mt-1' : undefined)}>{children}</div>
          ) : null}
        </div>
      </div>
    </div>
  );
}

export type { NoticeVariant };
export { Notice };
