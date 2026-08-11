import { render, screen } from '@testing-library/react';
import { describe, expect, expectTypeOf, it, vi } from 'vitest';
import type { AccountSurfaceProps } from '../src/components/shared/AccountSurface';
import type { AppShellProps } from '../src/components/shared/AppShell';
import {
  AuthenticatedFrame,
  type AuthenticatedFrameProps,
} from '../src/components/shared/AuthenticatedFrame';
import { EntrySurface, type EntrySurfaceProps } from '../src/components/shared/EntrySurface';
import type { ManagedDialogProps } from '../src/components/shared/ManagedDialog';
import {
  ProcessWorkbench,
  type ProcessWorkbenchProps,
} from '../src/components/shared/ProcessWorkbench';
import type { ResourceWorkspaceProps } from '../src/components/shared/ResourceWorkspace';
import {
  activeSurfaceContracts,
  type SurfaceIdFor,
  surfaceContractAttributes,
} from '../src/lib/ui-foundation';

type VisualEscapeHatch<T> = Extract<
  keyof T,
  'className' | 'scrollMode' | 'variant' | `${string}ClassName`
>;

vi.mock('@/features/preferences', () => ({
  PreferencesMenu: () => <div data-slot="test-preferences">Preferences</div>,
}));

describe('surface contracts', () => {
  it('binds active surface ids to finite contracts at compile time', () => {
    expect(activeSurfaceContracts['account-actions']).toBe('account-surface');
    expect(activeSurfaceContracts['solution-delivery']).toBe('process-workbench');
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
        utilities={<div data-slot="test-utilities">Utilities</div>}
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

  it('owns workbench route scrolling and page hierarchy', () => {
    const { container } = render(
      <ProcessWorkbench
        surfaceId="solution-delivery"
        title="Solutions"
        description="Publish and install immutable releases."
      >
        <section aria-label="Release pipeline">Pipeline</section>
      </ProcessWorkbench>,
    );

    const layout = container.querySelector('[data-slot="page-layout"]');
    const workbench = container.querySelector('[data-slot="process-workbench"]');
    expect(layout).toHaveAttribute('data-scroll-mode', 'route');
    expect(workbench).toHaveAttribute('data-axis-surface-contract', 'process-workbench');
    expect(workbench).toHaveAttribute('data-axis-surface-id', 'solution-delivery');
    expect(layout).toContainElement(screen.getByRole('heading', { level: 1, name: 'Solutions' }));
    expect(layout).toContainElement(screen.getByRole('region', { name: 'Release pipeline' }));
  });

  it('keeps surface APIs semantic and closes visual escape hatches', () => {
    expectTypeOf<VisualEscapeHatch<AccountSurfaceProps>>().toEqualTypeOf<never>();
    expectTypeOf<VisualEscapeHatch<AppShellProps>>().toEqualTypeOf<never>();
    expectTypeOf<VisualEscapeHatch<AuthenticatedFrameProps>>().toEqualTypeOf<never>();
    expectTypeOf<VisualEscapeHatch<EntrySurfaceProps>>().toEqualTypeOf<never>();
    expectTypeOf<VisualEscapeHatch<ManagedDialogProps>>().toEqualTypeOf<never>();
    expectTypeOf<VisualEscapeHatch<ProcessWorkbenchProps>>().toEqualTypeOf<never>();
    expectTypeOf<VisualEscapeHatch<ResourceWorkspaceProps>>().toEqualTypeOf<never>();
  });
});
