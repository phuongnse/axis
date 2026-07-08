import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useDebouncedValue } from './use-debounced-value';

function DebouncedProbe({ value, delayMs = 500 }: { value: string; delayMs?: number }) {
  const debouncedValue = useDebouncedValue(value, delayMs);

  return <output aria-label="debounced value">{debouncedValue}</output>;
}

describe('useDebouncedValue', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('updates only after the debounce duration elapses', () => {
    vi.useFakeTimers();
    const { rerender } = render(<DebouncedProbe value="initial" />);

    expect(screen.getByLabelText('debounced value')).toHaveTextContent('initial');

    rerender(<DebouncedProbe value="edited" />);
    expect(screen.getByLabelText('debounced value')).toHaveTextContent('initial');

    act(() => {
      vi.advanceTimersByTime(499);
    });
    expect(screen.getByLabelText('debounced value')).toHaveTextContent('initial');

    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(screen.getByLabelText('debounced value')).toHaveTextContent('edited');
  });

  it('keeps only the latest value when changes happen before the debounce settles', () => {
    vi.useFakeTimers();
    const { rerender } = render(<DebouncedProbe value="initial" />);

    rerender(<DebouncedProbe value="first" />);
    act(() => {
      vi.advanceTimersByTime(250);
    });
    rerender(<DebouncedProbe value="second" />);

    act(() => {
      vi.advanceTimersByTime(499);
    });
    expect(screen.getByLabelText('debounced value')).toHaveTextContent('initial');

    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(screen.getByLabelText('debounced value')).toHaveTextContent('second');
  });
});
