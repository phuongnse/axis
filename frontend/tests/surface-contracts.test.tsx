import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, expectTypeOf, it, vi } from 'vitest';
import type {
  AccountPreferencesModel,
  AccountSurfaceProps,
  AccountWorkspaceModel,
} from '../src/components/shared/AccountSurface';
import type { AppShellProps } from '../src/components/shared/AppShell';
import {
  AuthenticatedFrame,
  type AuthenticatedFrameProps,
} from '../src/components/shared/AuthenticatedFrame';
import { EntrySurface, type EntrySurfaceProps } from '../src/components/shared/EntrySurface';
import type { ManagedDialogProps } from '../src/components/shared/ManagedDialog';
import type { ResourceWorkspaceProps } from '../src/components/shared/ResourceWorkspace';
import {
  activeSurfaceContracts,
  type EnforcedSurfaceContractId,
  type SurfaceIdFor,
  surfaceContractAttributes,
} from '../src/lib/ui-foundation';

type UnboundedContentSlot<T> = {
  [Key in keyof T]-?: ReactNode extends NonNullable<T[Key]> ? Key : never;
}[keyof T];

describe('surface contracts', () => {
  it('binds active surface ids to finite contracts at compile time', () => {
    expect(activeSurfaceContracts['account-actions']).toBe('account-surface');
    expect(activeSurfaceContracts['solution-delivery']).toBe('resource-workspace');
    expect(activeSurfaceContracts['solution-delivery-windows']).toBe('managed-task-window');
    expectTypeOf<EnforcedSurfaceContractId>().toEqualTypeOf<
      'account-surface' | 'authenticated-frame' | 'entry-surface' | 'managed-task-window'
    >();
    expectTypeOf<SurfaceIdFor<'entry-surface'>>().toEqualTypeOf<
      | 'email-confirmation'
      | 'invitation-acceptance'
      | 'registration'
      | 'session-unavailable'
      | 'sign-in'
      | 'verify-email'
    >();
    expect(() => surfaceContractAttributes('entry-surface', 'account-actions' as never)).toThrow(
      'Surface "account-actions" is not registered to contract "entry-surface".',
    );
  });

  it('owns the authenticated frame anatomy while application state stays in slots', () => {
    const { container } = render(
      <AuthenticatedFrame
        surfaceId="authenticated-frame"
        contentBlocked
        contentBusy
        contentObscured
        contextSurface={<p>Refreshing Workspace</p>}
        contextSurfaceVisible
        header={<header>Header</header>}
        navigation={<nav>Navigation</nav>}
        managedWindows={<div>Managed windows</div>}
        notifications={<div>Notifications</div>}
        footer={<footer>Footer</footer>}
      >
        <section>Route content</section>
      </AuthenticatedFrame>,
    );

    const frame = container.querySelector('[data-axis-surface-id="authenticated-frame"]');
    const navigation = container.querySelector('[data-slot="module-navigation-boundary"]');
    const route = container.querySelector('[data-slot="authenticated-route-content"]');
    const main = route?.parentElement;
    expect(frame).toHaveAttribute('data-axis-surface-contract', 'authenticated-frame');
    expect(frame).toContainElement(screen.getByText('Header'));
    expect(frame).toContainElement(screen.getByText('Footer'));
    expect(navigation).toHaveAttribute('inert');
    expect(route).toHaveAttribute('inert');
    expect(route).toHaveClass('invisible', 'pointer-events-none');
    expect(main).toHaveAttribute('aria-busy', 'true');
    expect(frame).toContainElement(screen.getByText('Refreshing Workspace'));
  });

  it('owns the entry hierarchy and its semantic slots', () => {
    const { container } = render(
      <EntrySurface
        surfaceId="sign-in"
        preferences={{
          label: 'Preferences',
          language: {
            label: 'Language',
            onSelect: vi.fn(),
            options: [{ icon: 'EN', label: 'English', value: 'en' }],
            value: 'en',
          },
          theme: {
            label: 'Theme',
            onSelect: vi.fn(),
            options: [{ icon: 'System', label: 'System', value: 'system' }],
            value: 'system',
          },
        }}
        title="Sign in"
        banner={<p role="alert">Session expired</p>}
        footer={<a href="/register">Create account</a>}
      >
        <form aria-label="Sign in form" />
      </EntrySurface>,
    );

    const layout = container.querySelector('[data-slot="entry-layout"]');
    const surface = container.querySelector('[data-slot="entry-surface"]');
    expect(layout).toContainElement(surface);
    expect(surface).toHaveAttribute('data-axis-surface-contract', 'entry-surface');
    expect(surface).toHaveAttribute('data-axis-surface-id', 'sign-in');
    expect(surface).toContainElement(screen.getByRole('heading', { level: 1, name: 'Sign in' }));
    expect(surface).toContainElement(screen.getByRole('alert'));
    expect(surface).toContainElement(screen.getByRole('form', { name: 'Sign in form' }));
    expect(surface).toContainElement(screen.getByRole('link', { name: 'Create account' }));
  });

  it('classifies generic content capabilities at every finite owner boundary', () => {
    expectTypeOf<AccountSurfaceProps['workspace']>().toEqualTypeOf<AccountWorkspaceModel>();
    expectTypeOf<AccountSurfaceProps['preferences']>().toEqualTypeOf<AccountPreferencesModel>();
    expectTypeOf<UnboundedContentSlot<AccountSurfaceProps>>().toEqualTypeOf<never>();
    expectTypeOf<UnboundedContentSlot<AppShellProps>>().toEqualTypeOf<'children'>();
    expectTypeOf<UnboundedContentSlot<AuthenticatedFrameProps>>().toEqualTypeOf<
      | 'children'
      | 'contextSurface'
      | 'footer'
      | 'header'
      | 'managedWindows'
      | 'navigation'
      | 'notifications'
    >();
    expectTypeOf<UnboundedContentSlot<EntrySurfaceProps>>().toEqualTypeOf<
      'banner' | 'children' | 'footer'
    >();
    expectTypeOf<UnboundedContentSlot<ManagedDialogProps>>().toEqualTypeOf<
      'children' | 'description' | 'footer' | 'titleAccessory'
    >();
    expectTypeOf<UnboundedContentSlot<ResourceWorkspaceProps>>().toEqualTypeOf<
      'children' | 'description' | 'status' | 'title'
    >();
  });
});
