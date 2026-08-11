import { render, screen } from '@testing-library/react';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { ModuleNavigation } from '@/components/shared/ModuleNavigation';
import {
  type ModuleNavigationContribution,
  visibleModuleNavigationContributions,
} from '@/lib/module-navigation';
import { moduleNavigationContributions } from '@/lib/module-navigation-registry';
import { axisStyles } from '@/theme.generated';

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
  getRouteApi: () => ({
    useSearch: () => ({}),
    useNavigate: () => vi.fn(),
  }),
}));

describe('module navigation', () => {
  it('filters hidden and invalid contributions while preserving group and item order', () => {
    const contributions: ModuleNavigationContribution[] = [
      {
        id: 'hidden',
        labelKey: 'businessObjects.nav.definitions',
        icon: 'businessObjects',
        to: '/business-objects',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 20 },
        order: 10,
        isVisible: () => false,
      },
      {
        id: 'invalid',
        labelKey: '',
        icon: 'businessObjects',
        to: '/business-objects',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 20 },
        order: 20,
      },
      {
        id: 'businessObjects.second',
        labelKey: 'businessObjects.nav.definitions',
        icon: 'businessObjects',
        to: '/business-objects',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 10 },
        order: 20,
      },
      {
        id: 'businessObjects.first',
        labelKey: 'businessObjects.nav.definitions',
        icon: 'businessObjects',
        to: '/business-objects',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 10 },
        order: 10,
      },
      {
        id: 'rules.catalog',
        labelKey: 'rules.nav.definitions',
        icon: 'rules',
        to: '/rules',
        group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 10 },
        order: 30,
      },
    ];

    const visible = visibleModuleNavigationContributions(contributions, {
      pathname: '/business-objects',
    });

    expect(visible.map((item) => item.id)).toEqual([
      'businessObjects.first',
      'businessObjects.second',
      'rules.catalog',
    ]);
    expect(visible[0].isActive({ pathname: '/business-objects/123' })).toBe(true);
    expect(visible[1].isActive({ pathname: '/business-objects/123' })).toBe(true);
    expect(visible[2].isActive({ pathname: '/rules' })).toBe(true);
  });

  it('renders localized visible contributions with route target and active state', () => {
    const items = visibleModuleNavigationContributions(
      [
        {
          id: 'businessObjects.definitions',
          labelKey: 'businessObjects.nav.definitions',
          icon: 'businessObjects',
          to: '/business-objects',
          group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 100 },
          order: 100,
        },
        {
          id: 'rules.fieldDefinitions',
          labelKey: 'rules.nav.definitions',
          icon: 'rules',
          to: '/rules',
          group: { id: 'workspace', labelKey: 'nav.group.workspace', order: 100 },
          order: 110,
        },
      ],
      { pathname: '/rules' },
    );

    render(<ModuleNavigation context={{ pathname: '/rules' }} items={items} />);

    const navigation = screen.getByRole('navigation', { name: 'Modules' });
    expect(navigation).toHaveClass('bg-card');
    expect(navigation).not.toHaveClass('bg-card/80', 'backdrop-blur');
    expect(screen.getByText('Workspace')).toBeInTheDocument();
    const businessObjectsLink = screen.getByRole('link', { name: 'Business objects' });
    expect(businessObjectsLink).toHaveAttribute('href', '/business-objects');
    expect(businessObjectsLink).toHaveClass(
      axisStyles.density.minHeight.touchTarget,
      axisStyles.density.minWidth.touchTarget,
      'md:min-h-0',
      'md:min-w-0',
      'md:w-full',
    );
    const navigationIcon = businessObjectsLink.querySelector('svg');
    expect(navigationIcon).toHaveClass(axisStyles.icon.size.navigation);
    expect(businessObjectsLink).toHaveClass(
      'hover:bg-accent',
      'hover:text-accent-foreground',
      'dark:hover:bg-accent',
      'transition-colors',
      axisStyles.motion.duration.state,
      axisStyles.motion.easing.state,
      'motion-reduce:transition-none',
    );
    expect(businessObjectsLink).not.toHaveClass('dark:hover:bg-muted/50');
    const rulesLink = screen.getByRole('link', { name: 'Rules' });
    expect(rulesLink).toHaveClass(
      'md:w-full',
      'md:justify-start',
      'bg-secondary',
      'text-secondary-foreground',
    );
    expect(rulesLink).not.toHaveClass('bg-accent');
    expect(rulesLink).toHaveAttribute('href', '/rules');
    expect(rulesLink).toHaveAttribute('aria-current', 'page');
  });

  it('registers all workspace module navigation contributions in registry order', () => {
    expect(moduleNavigationContributions.map((item) => item.id)).toEqual([
      'identity.memberships',
      'identity.service-identities',
      'authorization.product-roles',
      'businessObjects.definitions',
      'rules.fieldDefinitions',
      'solutions.management',
    ]);
  });

  it('renders only production contributions returned by the server availability projection', () => {
    const visible = visibleModuleNavigationContributions(moduleNavigationContributions, {
      pathname: '/business-objects',
      availableContributionIds: new Set(['identity.memberships', 'businessObjects.definitions']),
    });

    expect(visible.map((item) => item.id)).toEqual([
      'identity.memberships',
      'businessObjects.definitions',
    ]);
  });

  it('reveals the active item when the responsive navigation viewport changes', () => {
    const items = visibleModuleNavigationContributions(moduleNavigationContributions, {
      pathname: '/rules',
      availableContributionIds: new Set(['businessObjects.definitions', 'rules.fieldDefinitions']),
    });
    const scrollIntoView = vi.fn();
    const originalScrollIntoView = Object.getOwnPropertyDescriptor(
      HTMLElement.prototype,
      'scrollIntoView',
    );
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: scrollIntoView,
    });

    try {
      render(<ModuleNavigation context={{ pathname: '/rules' }} items={items} />);

      expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', inline: 'center' });
      expect(screen.getByRole('link', { name: 'Rules' })).toHaveAttribute('aria-current', 'page');
    } finally {
      if (originalScrollIntoView) {
        Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', originalScrollIntoView);
      } else {
        Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView');
      }
    }
  });
});
