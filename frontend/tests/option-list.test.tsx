import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Monitor, Moon } from 'lucide-react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { OptionList, OptionListItem } from '@/components/shared/OptionList';

describe('OptionList', () => {
  afterEach(() => vi.useRealTimers());

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
      'min-h-axis-touch-target',
      'sm:min-h-axis-compact-control',
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
    expect(system.querySelector('.lucide-check')).toBeNull();

    await user.click(dark);

    expect(onValueChange).toHaveBeenCalledWith('dark');
  });

  it('replaces the leading icon only after the shared pending threshold', () => {
    vi.useFakeTimers();
    render(
      <OptionList label="Theme" value="dark" onValueChange={vi.fn()}>
        <OptionListItem icon={<Moon />} pending value="dark">
          Dark
        </OptionListItem>
      </OptionList>,
    );

    const icon = screen
      .getByRole('button', { name: 'Dark' })
      .querySelector('[data-slot="option-item-icon"]');
    expect(icon?.querySelector('.lucide-moon')).not.toBeNull();

    act(() => vi.advanceTimersByTime(300));
    expect(icon?.querySelector('[data-slot="spinner"]')).not.toBeNull();
  });
});
