import type { LucideIcon } from 'lucide-react';
import { Database, FileText, LayoutDashboard, Settings, Workflow, Zap } from 'lucide-react';

export interface ShellNavItem {
  labelKey: string;
  icon: LucideIcon;
  to?: string;
  disabled?: boolean;
}

/** Matches docs/wireframes app shell nav + Dashboard route. */
export const shellNavItems: ShellNavItem[] = [
  { labelKey: 'shell.nav.dashboard', icon: LayoutDashboard, to: '/dashboard' },
  { labelKey: 'shell.nav.dataModels', icon: Database, disabled: true },
  { labelKey: 'shell.nav.workflows', icon: Workflow, disabled: true },
  { labelKey: 'shell.nav.forms', icon: FileText, disabled: true },
  { labelKey: 'shell.nav.executions', icon: Zap, disabled: true },
  { labelKey: 'shell.nav.settings', icon: Settings, disabled: true },
];

export function pageTitleKeyForPath(pathname: string): string {
  if (pathname.startsWith('/dashboard')) return 'shell.page.dashboardOverview';
  return 'shell.page.workspace';
}
