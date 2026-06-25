import type { LucideIcon } from 'lucide-react';
import { LayoutDashboard } from 'lucide-react';

export interface ShellNavItem {
  label: string;
  icon: LucideIcon;
  to?: string;
  disabled?: boolean;
}

export const shellNavItems: ShellNavItem[] = [
  { label: 'Dashboard', icon: LayoutDashboard, to: '/dashboard' },
];

export function pageTitleForPath(pathname: string): string {
  if (pathname.startsWith('/dashboard')) return 'Dashboard';
  return 'Axis';
}
