import { render, screen } from '@testing-library/react';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { ModuleNavigation } from '@/components/shared/ModuleNavigation';
import {
  type ModuleNavigationContribution,
  visibleModuleNavigationContributions,
} from '@/lib/module-navigation';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    to,
    children,
    ...props
  }: AnchorHTMLAttributes<HTMLAnchorElement> & { to: string; children: ReactNode }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}));

describe('module navigation', () => {
  it('filters hidden and invalid contributions while preserving group and item order', () => {
    const contributions: ModuleNavigationContribution[] = [
      {
        id: 'hidden',
        labelKey: 'nav.modules',
        icon: 'module',
        to: '/dashboard',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 20 },
        order: 10,
        isVisible: () => false,
      },
      {
        id: 'invalid',
        labelKey: '',
        icon: 'module',
        to: '/dashboard',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 20 },
        order: 20,
      },
      {
        id: 'module.second',
        labelKey: 'nav.modules',
        icon: 'module',
        to: '/dashboard',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 10 },
        order: 20,
      },
      {
        id: 'module.first',
        labelKey: 'nav.modules',
        icon: 'module',
        to: '/dashboard',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 10 },
        order: 10,
      },
    ];

    const visible = visibleModuleNavigationContributions(contributions, { pathname: '/dashboard' });

    expect(visible.map((item) => item.id)).toEqual(['module.first', 'module.second']);
    expect(visible.every((item) => item.isActive({ pathname: '/dashboard' }))).toBe(true);
  });

  it('renders localized visible contributions with route target and active state', () => {
    const items = visibleModuleNavigationContributions(
      [
        {
          id: 'module.dashboard',
          labelKey: 'nav.modules',
          icon: 'module',
          to: '/dashboard',
          group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 100 },
          order: 100,
        },
      ],
      { pathname: '/dashboard' },
    );

    render(<ModuleNavigation context={{ pathname: '/dashboard' }} items={items} />);

    expect(screen.getByRole('navigation', { name: 'Modules' })).toBeInTheDocument();
    expect(screen.getByText('Workspace')).toBeInTheDocument();
    const link = screen.getByRole('link', { name: 'Modules' });
    expect(link).toHaveAttribute('href', '/dashboard');
    expect(link).toHaveAttribute('aria-current', 'page');
  });
});
