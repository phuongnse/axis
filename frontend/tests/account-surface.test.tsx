import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ComponentProps } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { AccountSurface } from '@/components/shared/AccountSurface';

const identity = {
  displayName: 'Ada Lovelace',
  initials: 'AL',
  secondaryLabel: 'ada@example.com',
  triggerKind: 'organization' as const,
  triggerLabel: 'Axis Reference Product',
};

function renderAccountSurface(overrides: Partial<ComponentProps<typeof AccountSurface>> = {}) {
  const props: ComponentProps<typeof AccountSurface> = {
    identity,
    onSignOut: vi.fn(),
    preferenceControls: (
      <>
        <button type="button">Language control</button>
        <button type="button">Theme control</button>
      </>
    ),
    surfaceId: 'account-actions',
    workspace: (
      <section aria-label="Workspace">
        <button type="button">Personal workspace</button>
      </section>
    ),
    ...overrides,
  };
  const view = render(<AccountSurface {...props} />);
  return { ...view, props };
}

describe('AccountSurface', () => {
  it('owns the ordered identity, Workspace, preferences, and sign-out composition', async () => {
    const user = userEvent.setup();
    renderAccountSurface();

    const trigger = screen.getByRole('button', { name: 'Account menu' });
    expect(trigger).toHaveTextContent('Axis Reference Product');
    await user.click(trigger);

    const surface = document.querySelector<HTMLElement>('[data-slot="account-surface"]');
    expect(surface).not.toBeNull();
    expect(surface).toHaveAttribute('aria-label', 'Account menu');
    expect(surface).toHaveAttribute('data-axis-surface-contract', 'account-surface');
    expect(surface).toHaveAttribute('data-axis-surface-id', 'account-actions');
    const accountIdentity = screen.getByRole('region', { name: 'Account' });
    const workspace = screen.getByRole('region', { name: 'Workspace' });
    const preferences = screen.getByRole('region', { name: 'Preferences' });
    const signOut = screen.getByRole('button', { name: 'Sign out' });

    expect(accountIdentity).toHaveTextContent('Ada Lovelace');
    expect(accountIdentity).toHaveTextContent('ada@example.com');
    expect(accountIdentity.compareDocumentPosition(workspace)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
    expect(workspace.compareDocumentPosition(preferences)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(preferences.compareDocumentPosition(signOut)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(screen.getByRole('button', { name: 'Language control' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Theme control' })).toBeVisible();
  });

  it('keeps the surface open while its authoritative context transition is locked', async () => {
    const user = userEvent.setup();
    renderAccountSurface({ transitionLocked: true });

    const trigger = screen.getByRole('button', { name: 'Account menu' });
    await user.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'true');

    await user.click(trigger);
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    await user.keyboard('{Escape}');
    expect(trigger).toHaveAttribute('aria-expanded', 'true');
    expect(document.querySelector('[data-slot="account-surface"]')).toBeVisible();
  });

  it('delegates sign-out and exposes pending and failure states without changing the action name', async () => {
    const user = userEvent.setup();
    const onSignOut = vi.fn();
    const view = renderAccountSurface({ onSignOut, signOutError: true });

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    const signOut = screen.getByRole('button', { name: 'Sign out' });
    expect(screen.getByRole('alert')).toHaveTextContent('Sign out did not complete. Try again.');
    await user.click(signOut);
    expect(onSignOut).toHaveBeenCalledOnce();

    view.rerender(<AccountSurface {...view.props} signOutError={false} signingOut />);
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Sign out' })).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('status')).toHaveTextContent('Signing out');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
