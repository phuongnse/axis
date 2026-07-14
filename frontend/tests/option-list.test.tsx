import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { OptionList, OptionListItem } from '@/components/shared/OptionList';

describe('OptionList', () => {
  it('renders full-width, start-aligned options and reports selection', async () => {
    const user = userEvent.setup();
    const onValueChange = vi.fn();

    render(
      <OptionList label="Theme" value="system" onValueChange={onValueChange}>
        <OptionListItem value="system">System</OptionListItem>
        <OptionListItem value="dark">Dark</OptionListItem>
      </OptionList>,
    );

    const group = screen.getByRole('group', { name: 'Theme' });
    const system = screen.getByRole('button', { name: 'System' });
    const dark = screen.getByRole('button', { name: 'Dark' });

    expect(group).toHaveClass('w-full');
    expect(system).toHaveClass('w-full', 'justify-start');
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

    await user.click(dark);

    expect(onValueChange).toHaveBeenCalledWith('dark');
  });
});
