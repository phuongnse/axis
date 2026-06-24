import type { ReactNode } from 'react';

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';

type AuthNoticeVariant = 'default' | 'destructive';

interface AuthNoticeProps {
  title?: ReactNode;
  children?: ReactNode;
  variant?: AuthNoticeVariant;
  className?: string;
}

export function AuthNotice({ title, children, variant = 'default', className }: AuthNoticeProps) {
  return (
    <Alert variant={variant} className={className}>
      {title ? <AlertTitle>{title}</AlertTitle> : null}
      {children ? <AlertDescription>{children}</AlertDescription> : null}
    </Alert>
  );
}
