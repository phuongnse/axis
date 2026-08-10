import { act, render, screen } from '@testing-library/react';
import { Save } from 'lucide-react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AsyncButton } from '@/components/shared/AsyncButton';

describe('AsyncButton', () => {
  afterEach(() => vi.useRealTimers());

  it('locks immediately, keeps its label stable, and delays visual pending feedback', () => {
    vi.useFakeTimers();
    const { rerender } = render(
      <AsyncButton icon={<Save />} pending={false} pendingLabel="Saving">
        Save
      </AsyncButton>,
    );

    const button = screen.getByRole('button', { name: 'Save' });
    const iconSlot = button.querySelector('[data-slot="async-button-icon"]');
    expect(iconSlot?.querySelector('.lucide-save')).not.toBeNull();

    rerender(
      <AsyncButton icon={<Save />} pending pendingLabel="Saving">
        Save
      </AsyncButton>,
    );
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-busy', 'true');
    expect(button).toHaveAccessibleName('Save');
    expect(button).toHaveTextContent('Save');
    expect(screen.getByRole('status')).toHaveTextContent('Saving');
    expect(iconSlot?.querySelector('[data-slot="spinner"]')).toBeNull();

    act(() => vi.advanceTimersByTime(300));
    expect(iconSlot?.querySelector('[data-slot="spinner"]')).not.toBeNull();

    rerender(
      <AsyncButton icon={<Save />} pending={false} pendingLabel="Saving">
        Save
      </AsyncButton>,
    );
    act(() => vi.advanceTimersByTime(399));
    expect(button).toBeDisabled();
    expect(iconSlot?.querySelector('[data-slot="spinner"]')).not.toBeNull();

    act(() => vi.advanceTimersByTime(1));
    expect(button).not.toBeDisabled();
    expect(iconSlot?.querySelector('.lucide-save')).not.toBeNull();
  });
});
