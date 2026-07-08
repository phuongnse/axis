import { useEffect, useRef, useState } from 'react';

export const defaultMinimumPendingIndicatorMs = 400;

interface MinimumVisiblePendingOptions {
  minimumMs?: number;
}

export function useMinimumVisiblePending(
  pending: boolean,
  options: MinimumVisiblePendingOptions = {},
): boolean {
  const minimumMs = Math.max(0, options.minimumMs ?? defaultMinimumPendingIndicatorMs);
  const [visible, setVisible] = useState(pending);
  const [minimumElapsed, setMinimumElapsed] = useState(!pending);
  const timeoutRef = useRef<number | null>(null);

  useEffect(() => {
    if (!pending) return;

    if (timeoutRef.current != null) {
      window.clearTimeout(timeoutRef.current);
    }

    setVisible(true);
    setMinimumElapsed(false);
    timeoutRef.current = window.setTimeout(() => {
      timeoutRef.current = null;
      setMinimumElapsed(true);
    }, minimumMs);
  }, [pending, minimumMs]);

  useEffect(
    () => () => {
      if (timeoutRef.current != null) {
        window.clearTimeout(timeoutRef.current);
      }
    },
    [],
  );

  useEffect(() => {
    if (!pending && minimumElapsed) {
      setVisible(false);
    }
  }, [pending, minimumElapsed]);

  return visible;
}
