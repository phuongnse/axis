import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AsyncContent } from '@/components/shared/AsyncContent';

describe('AsyncContent', () => {
  afterEach(() => vi.useRealTimers());

  it('owns delayed pending feedback and reveals ready content without a flash', () => {
    vi.useFakeTimers();
    const { container, rerender } = render(
      <AsyncContent pending pendingLabel="Loading records">
        Ready records
      </AsyncContent>,
    );

    const boundary = container.querySelector('[data-slot="async-content"]');
    expect(boundary).toHaveAttribute('aria-busy', 'true');
    expect(screen.queryByText('Loading records')).not.toBeInTheDocument();
    expect(screen.queryByText('Ready records')).not.toBeInTheDocument();

    act(() => vi.advanceTimersByTime(300));
    expect(screen.getByRole('status')).toHaveTextContent('Loading records');

    rerender(
      <AsyncContent pending={false} pendingLabel="Loading records">
        Ready records
      </AsyncContent>,
    );
    act(() => vi.advanceTimersByTime(399));
    expect(screen.getByRole('status')).toHaveTextContent('Loading records');
    expect(screen.queryByText('Ready records')).not.toBeInTheDocument();

    act(() => vi.advanceTimersByTime(1));
    expect(screen.getByText('Ready records')).toBeInTheDocument();
    expect(boundary).not.toHaveAttribute('aria-busy');
  });

  it('lets an error replace pending feedback immediately', () => {
    vi.useFakeTimers();
    const { rerender } = render(
      <AsyncContent pending pendingLabel="Loading records">
        Error state
      </AsyncContent>,
    );
    act(() => vi.advanceTimersByTime(300));

    rerender(
      <AsyncContent error pending={false} pendingLabel="Loading records">
        Error state
      </AsyncContent>,
    );

    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    expect(screen.getByText('Error state')).toBeInTheDocument();
  });
});
