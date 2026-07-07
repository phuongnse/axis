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
        labelKey: 'objects.nav.definitions',
        icon: 'objects',
        to: '/objects',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 20 },
        order: 10,
        isVisible: () => false,
      },
      {
        id: 'invalid',
        labelKey: '',
        icon: 'objects',
        to: '/objects',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 20 },
        order: 20,
      },
      {
        id: 'objects.second',
        labelKey: 'objects.nav.definitions',
        icon: 'objects',
        to: '/objects',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 10 },
        order: 20,
      },
      {
        id: 'objects.first',
        labelKey: 'objects.nav.definitions',
        icon: 'objects',
        to: '/objects',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 10 },
        order: 10,
      },
    ];

    const visible = visibleModuleNavigationContributions(contributions, { pathname: '/objects' });

    expect(visible.map((item) => item.id)).toEqual(['objects.first', 'objects.second']);
    expect(visible.every((item) => item.isActive({ pathname: '/objects/123' }))).toBe(true);
  });

  it('renders localized visible contributions with route target and active state', () => {
    const items = visibleModuleNavigationContributions(
      [
        {
          id: 'objects.definitions',
          labelKey: 'objects.nav.definitions',
          icon: 'objects',
          to: '/objects',
          group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 100 },
          order: 100,
        },
      ],
      { pathname: '/objects' },
    );

    render(<ModuleNavigation context={{ pathname: '/objects' }} items={items} />);

    expect(screen.getByRole('navigation', { name: 'Modules' })).toBeInTheDocument();
    expect(screen.getByText('Workspace')).toBeInTheDocument();
    const link = screen.getByRole('link', { name: 'Business objects' });
    expect(link).toHaveAttribute('href', '/objects');
    expect(link).toHaveAttribute('aria-current', 'page');
  });
});
