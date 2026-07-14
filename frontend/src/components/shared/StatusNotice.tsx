import { CircleAlert, CircleCheck, Info, type LucideIcon, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';

type StatusNoticeTone = 'info' | 'success' | 'warning' | 'destructive';

const noticeStyles = {
  info: {
    icon: Info,
    alert: 'border-info/25 bg-info/10 text-info',
    description: 'text-info/90',
  },
  success: {
    icon: CircleCheck,
    alert: 'border-success/25 bg-success/10 text-success',
    description: 'text-success/90',
  },
  warning: {
    icon: TriangleAlert,
    alert: 'border-warning/25 bg-warning/10 text-warning',
    description: 'text-warning/90',
  },
  destructive: {
    icon: CircleAlert,
    alert: 'border-destructive/25 bg-destructive/10 text-destructive',
    description: 'text-destructive/90',
  },
} satisfies Record<StatusNoticeTone, { icon: LucideIcon; alert: string; description: string }>;

interface StatusNoticeProps {
  title?: ReactNode;
  children?: ReactNode;
  tone?: StatusNoticeTone;
}

function StatusNotice({ title, children, tone = 'info' }: StatusNoticeProps) {
  const style = noticeStyles[tone];
  const Icon = style.icon;

  return (
    <Alert className={style.alert}>
      <Icon aria-hidden />
      {title ? <AlertTitle>{title}</AlertTitle> : null}
      {children ? (
        <AlertDescription className={style.description}>{children}</AlertDescription>
      ) : null}
    </Alert>
  );
}

export { StatusNotice, type StatusNoticeTone };
