import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Monitor, Moon } from 'lucide-react';
import { describe, expect, it, vi } from 'vitest';
import { OptionList, OptionListItem } from '@/components/shared/OptionList';
import { axisStyles } from '@/theme.generated';

describe('OptionList', () => {
  it('renders full-width, start-aligned options and reports selection', async () => {
    const user = userEvent.setup();
    const onValueChange = vi.fn();

    render(
      <OptionList label="Theme" value="system" onValueChange={onValueChange}>
        <OptionListItem icon={<Monitor />} value="system">
          System
        </OptionListItem>
        <OptionListItem icon={<Moon />} value="dark">
          Dark
        </OptionListItem>
      </OptionList>,
    );

    const group = screen.getByRole('group', { name: 'Theme' });
    const system = screen.getByRole('button', { name: 'System' });
    const dark = screen.getByRole('button', { name: 'Dark' });

    expect(group).toHaveClass('w-full');
    expect(system).toHaveClass(
      'w-full',
      'justify-start',
      axisStyles.density.minHeight.touchTarget,
      axisStyles.density.minHeight.compactControlAtSmall,
    );
    expect(system).toHaveClass(
      'aria-pressed:bg-secondary',
      'aria-pressed:text-secondary-foreground',
      'dark:aria-pressed:hover:bg-secondary',
    );
    expect(system).not.toHaveClass('aria-pressed:ring-1');
    expect(dark).toHaveClass(
      'w-full',
      'justify-start',
      'hover:bg-accent',
      'hover:text-accent-foreground',
    );
    expect(system).toHaveAttribute('aria-pressed', 'true');
    expect(system.firstElementChild).toHaveAttribute('data-slot', 'option-item-icon');
    expect(system.firstElementChild?.querySelector('.lucide-monitor')).not.toBeNull();
    expect(system.lastElementChild).toHaveAttribute('data-slot', 'option-item-label');
    expect(system.lastElementChild).toHaveClass(
      axisStyles.typography.scale.label,
      axisStyles.typography.weight.label,
      'whitespace-normal',
      'break-words',
    );
    expect(system.lastElementChild).not.toHaveClass('truncate');
    expect(system.querySelector('.lucide-check')).toBeNull();

    await user.click(dark);

    expect(onValueChange).toHaveBeenCalledWith('dark');
  });

  it('replaces the leading icon immediately for a pending user action', () => {
    const { rerender } = render(
      <OptionList label="Theme" value="dark" onValueChange={vi.fn()}>
        <OptionListItem icon={<Moon />} pending value="dark">
          Dark
        </OptionListItem>
      </OptionList>,
    );

    const icon = screen
      .getByRole('button', { name: 'Dark' })
      .querySelector('[data-slot="option-item-icon"]');
    expect(screen.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: 'Dark' })).toBeDisabled();
    expect(icon?.querySelector('[data-slot="spinner"]')).not.toBeNull();

    rerender(
      <OptionList label="Theme" value="dark" onValueChange={vi.fn()}>
        <OptionListItem icon={<Moon />} value="dark">
          Dark
        </OptionListItem>
      </OptionList>,
    );

    expect(screen.getByRole('button', { name: 'Dark' })).not.toHaveAttribute('aria-busy');
    expect(screen.getByRole('button', { name: 'Dark' })).toBeEnabled();
    expect(icon?.querySelector('.lucide-moon')).not.toBeNull();
  });
});
