import type { ReactNode } from 'react';

import { Notice, type NoticeVariant } from '@/components/ui/notice';

type AuthNoticeVariant = NoticeVariant;

interface AuthNoticeProps {
  title?: ReactNode;
  children?: ReactNode;
  variant?: AuthNoticeVariant;
  className?: string;
}

export function AuthNotice({ title, children, variant = 'info', className }: AuthNoticeProps) {
  return (
    <Notice title={title} variant={variant} className={className}>
      {children}
    </Notice>
  );
}
