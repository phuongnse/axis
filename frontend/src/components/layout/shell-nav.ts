import type { LucideIcon } from 'lucide-react';
import { Database, FileText, LayoutDashboard, Settings, Workflow, Zap } from 'lucide-react';

export interface ShellNavItem {
  label: string;
  icon: LucideIcon;
  to?: string;
  disabled?: boolean;
}

/** Matches docs/wireframes app shell nav + Dashboard route. */
export const shellNavItems: ShellNavItem[] = [
  { label: 'Dashboard', icon: LayoutDashboard, to: '/dashboard' },
  { label: 'Data Models', icon: Database, disabled: true },
  { label: 'Workflows', icon: Workflow, disabled: true },
  { label: 'Forms', icon: FileText, disabled: true },
  { label: 'Executions', icon: Zap, disabled: true },
  { label: 'Settings', icon: Settings, disabled: true },
];

export function pageTitleForPath(pathname: string): string {
  if (pathname.startsWith('/dashboard')) return 'Dashboard Overview';
  return 'Workspace';
}
