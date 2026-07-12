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
}

export function AuthNotice({ title, children, variant = 'default' }: AuthNoticeProps) {
  const Icon = noticeIcons[variant];
  const alertVariant = {
    default: 'default',
    destructive: 'destructive',
    success: 'default',
    warning: 'default',
  } as const;

  return (
    <Alert variant={alertVariant[variant]}>
      <Icon aria-hidden />
      {title ? <AlertTitle>{title}</AlertTitle> : null}
      {children ? <AlertDescription>{children}</AlertDescription> : null}
    </Alert>
  );
}
