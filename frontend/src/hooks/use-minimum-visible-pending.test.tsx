import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useMinimumVisiblePending } from './use-minimum-visible-pending';

function PendingProbe({ pending, minimumMs = 400 }: { pending: boolean; minimumMs?: number }) {
  const visible = useMinimumVisiblePending(pending, { minimumMs });

  return <output aria-label="pending visibility">{visible ? 'visible' : 'hidden'}</output>;
}

describe('useMinimumVisiblePending', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('keeps a short pending state visible until the minimum duration elapses', () => {
    vi.useFakeTimers();
    const { rerender } = render(<PendingProbe pending={false} />);

    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('hidden');

    rerender(<PendingProbe pending />);
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('visible');

    rerender(<PendingProbe pending={false} />);
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('visible');

    act(() => {
      vi.advanceTimersByTime(399);
    });
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('visible');

    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('hidden');
  });

  it('hides immediately when pending ends after the minimum duration', () => {
    vi.useFakeTimers();
    const { rerender } = render(<PendingProbe pending />);

    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('visible');

    act(() => {
      vi.advanceTimersByTime(400);
    });
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('visible');

    rerender(<PendingProbe pending={false} />);
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('hidden');
  });
});
