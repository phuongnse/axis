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

const preferences = {
  language: {
    feedback: null,
    label: 'Language',
    onRetry: vi.fn(),
    onSelect: vi.fn(),
    options: [
      { icon: 'EN', label: 'English', value: 'en' },
      { icon: 'VI', label: 'Vietnamese', value: 'vi' },
    ],
    pendingLabel: 'Saving...',
    value: 'en',
  },
  theme: {
    feedback: null,
    label: 'Theme',
    onRetry: vi.fn(),
    onSelect: vi.fn(),
    options: [
      { icon: <span aria-hidden>S</span>, label: 'System', value: 'system' },
      { icon: <span aria-hidden>L</span>, label: 'Light', value: 'light' },
      { icon: <span aria-hidden>D</span>, label: 'Dark', value: 'dark' },
    ],
    pendingLabel: 'Saving...',
    value: 'system',
  },
};

function renderAccountSurface(overrides: Partial<ComponentProps<typeof AccountSurface>> = {}) {
  const props: ComponentProps<typeof AccountSurface> = {
    identity,
    onSignOut: vi.fn(),
    preferences,
    surfaceId: 'account-actions',
    workspace: {
      feedback: null,
      loadState: 'ready',
      onCreate: vi.fn(),
      onRetryContext: vi.fn(),
      onRetryLoad: vi.fn(),
      onSelect: vi.fn(),
      options: [
        {
          current: true,
          id: 'personal-workspace',
          kind: 'person',
          label: 'Personal workspace',
        },
        {
          current: false,
          id: 'axis-reference-product',
          kind: 'organization',
          label: 'Axis Reference Product',
        },
      ],
    },
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
    const createOrganization = screen.getByRole('button', { name: 'Create Organization' });
    const signOut = screen.getByRole('button', { name: 'Sign out' });

    expect(accountIdentity).toHaveTextContent('Ada Lovelace');
    expect(accountIdentity).toHaveTextContent('ada@example.com');
    expect(accountIdentity.compareDocumentPosition(workspace)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
    expect(workspace.compareDocumentPosition(preferences)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(preferences.compareDocumentPosition(signOut)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(screen.getByRole('group', { name: 'Language' })).toBeVisible();
    expect(screen.getByRole('group', { name: 'Theme' })).toBeVisible();
    expect(createOrganization).toHaveAttribute('data-axis-account-role', 'section-action');
    expect(signOut).toHaveAttribute('data-axis-account-role', 'section-action');
    const regions = Array.from(
      surface?.querySelectorAll<HTMLElement>('[data-axis-account-region]') ?? [],
    );
    expect(regions.map((region) => region.dataset.axisAccountRegion)).toEqual([
      'identity',
      'workspace',
      'preferences',
      'actions',
    ]);
    expect(screen.getByRole('button', { name: 'Personal workspace' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(screen.getByRole('button', { name: 'Axis Reference Product' })).toBeEnabled();
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

  it('renders preference state from typed models and delegates selection and recovery', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    const onRetry = vi.fn();
    const view = renderAccountSurface({
      preferences: {
        ...preferences,
        language: { ...preferences.language, onRetry, onSelect },
      },
    });

    await user.click(screen.getByRole('button', { name: 'Account menu' }));
    await user.click(screen.getByRole('button', { name: 'Vietnamese' }));
    expect(onSelect).toHaveBeenCalledWith('vi');

    view.rerender(
      <AccountSurface
        {...view.props}
        preferences={{
          ...preferences,
          language: {
            ...preferences.language,
            onRetry,
            onSelect,
            options: preferences.language.options.map((option) => ({
              ...option,
              pending: option.value === 'vi',
            })),
          },
        }}
      />,
    );
    expect(screen.getByRole('region', { name: 'Language' })).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: 'Vietnamese' })).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent('Saving...');

    view.rerender(
      <AccountSurface
        {...view.props}
        preferences={{
          ...preferences,
          language: {
            ...preferences.language,
            feedback: { message: 'Language could not be saved.', retryLabel: 'Retry' },
            onRetry,
            onSelect,
          },
        }}
      />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Language could not be saved.');
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    expect(onRetry).toHaveBeenCalledOnce();
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
