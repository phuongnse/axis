import { AlertCircle, AlertTriangle, CheckCircle2, Info } from 'lucide-react';
import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

type AuthNoticeVariant = 'info' | 'success' | 'warning' | 'error';

interface AuthNoticeProps {
  title?: ReactNode;
  children?: ReactNode;
  variant?: AuthNoticeVariant;
  className?: string;
}

const noticeStyles: Record<AuthNoticeVariant, string> = {
  info: 'border-sky-500/25 bg-sky-500/5 text-sky-800 dark:text-sky-300',
  success: 'border-emerald-500/25 bg-emerald-500/5 text-emerald-700 dark:text-emerald-400',
  warning: 'border-amber-500/25 bg-amber-500/5 text-amber-800 dark:text-amber-300',
  error: 'border-destructive/30 bg-destructive/5 text-destructive',
};

const noticeIcons = {
  info: Info,
  success: CheckCircle2,
  warning: AlertTriangle,
  error: AlertCircle,
} satisfies Record<AuthNoticeVariant, typeof Info>;

export function AuthNotice({ title, children, variant = 'info', className }: AuthNoticeProps) {
  const Icon = noticeIcons[variant];
  const role = variant === 'error' || variant === 'warning' ? 'alert' : 'status';

  return (
    <div
      className={cn('rounded-lg border px-3 py-2 text-sm', noticeStyles[variant], className)}
      role={role}
    >
      <div className="flex items-start gap-2">
        <Icon className="mt-0.5 size-4 shrink-0" aria-hidden />
        <div>
          {title ? <p className="font-medium">{title}</p> : null}
          {children ? (
            <div className={cn('text-xs opacity-90', title ? 'mt-1' : undefined)}>{children}</div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
