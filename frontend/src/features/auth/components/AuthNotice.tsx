import { CircleAlert, CircleCheck, Info, type LucideIcon, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';

type AuthNoticeVariant = 'default' | 'destructive' | 'success' | 'warning';

const noticeIcons = {
  default: Info,
  destructive: CircleAlert,
  success: CircleCheck,
  warning: TriangleAlert,
} satisfies Record<AuthNoticeVariant, LucideIcon>;

interface AuthNoticeProps {
  title?: ReactNode;
  children?: ReactNode;
  variant?: AuthNoticeVariant;
  className?: string;
}

export function AuthNotice({ title, children, variant = 'default', className }: AuthNoticeProps) {
  const Icon = noticeIcons[variant];
  const alertVariant = variant === 'destructive' ? 'destructive' : 'default';

  return (
    <Alert variant={alertVariant} className={className}>
      <Icon aria-hidden />
      {title ? <AlertTitle>{title}</AlertTitle> : null}
      {children ? <AlertDescription>{children}</AlertDescription> : null}
    </Alert>
  );
}
