import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { type PendingFeedbackKind, usePendingVisibility } from './usePendingVisibility';

function PendingProbe({
  pending,
  kind = 'feedback',
}: {
  pending: boolean;
  kind?: PendingFeedbackKind;
}) {
  const visible = usePendingVisibility(pending, kind);

  return <output aria-label="pending visibility">{visible ? 'visible' : 'hidden'}</output>;
}

describe('usePendingVisibility', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('never reveals feedback when work finishes inside the shared delay', () => {
    vi.useFakeTimers();
    const { rerender } = render(<PendingProbe pending={false} />);

    rerender(<PendingProbe pending />);
    act(() => vi.advanceTimersByTime(299));
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('hidden');

    rerender(<PendingProbe pending={false} />);
    act(() => vi.advanceTimersByTime(500));
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('hidden');
  });

  it('keeps revealed feedback stable for the shared minimum duration', () => {
    vi.useFakeTimers();
    const { rerender } = render(<PendingProbe pending />);

    act(() => vi.advanceTimersByTime(300));
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('visible');

    rerender(<PendingProbe pending={false} />);
    act(() => vi.advanceTimersByTime(399));
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('visible');

    act(() => vi.advanceTimersByTime(1));
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('hidden');
  });

  it('uses the longer shared threshold for context transitions', () => {
    vi.useFakeTimers();
    const { rerender } = render(<PendingProbe pending kind="context-transition" />);

    act(() => vi.advanceTimersByTime(499));
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('hidden');

    act(() => vi.advanceTimersByTime(1));
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('visible');

    rerender(<PendingProbe pending={false} kind="context-transition" />);
    act(() => vi.advanceTimersByTime(599));
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('visible');

    act(() => vi.advanceTimersByTime(1));
    expect(screen.getByLabelText('pending visibility')).toHaveTextContent('hidden');
  });
});
