import type { LucideIcon } from 'lucide-react';
import { LayoutDashboard } from 'lucide-react';
import type { TranslationKey } from '@/features/preferences';

export interface ShellNavItem {
  labelKey: TranslationKey;
  icon: LucideIcon;
  to?: string;
  disabled?: boolean;
}

export const shellNavItems: ShellNavItem[] = [
  {
    labelKey: 'app.dashboard',
    icon: LayoutDashboard,
    to: '/dashboard',
  },
];

export function pageTitleKeyForPath(pathname: string): TranslationKey {
  if (pathname.startsWith('/dashboard')) return 'app.dashboard';
  return 'app.account';
}
