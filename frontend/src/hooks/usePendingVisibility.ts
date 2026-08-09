import { useEffect, useRef, useState } from 'react';

export type PendingFeedbackKind = 'feedback' | 'context-transition';

const pendingFeedbackTimings = {
  feedback: { delayMs: 300, minimumMs: 400 },
  'context-transition': { delayMs: 500, minimumMs: 600 },
} as const satisfies Record<PendingFeedbackKind, { delayMs: number; minimumMs: number }>;

export function usePendingVisibility(
  pending: boolean,
  kind: PendingFeedbackKind = 'feedback',
): boolean {
  const { delayMs, minimumMs } = pendingFeedbackTimings[kind];
  const [visible, setVisible] = useState(false);
  const visibleSinceRef = useRef<number | null>(null);

  useEffect(() => {
    let timeout: number | undefined;

    if (pending && !visible) {
      timeout = window.setTimeout(() => {
        visibleSinceRef.current = Date.now();
        setVisible(true);
      }, delayMs);
    } else if (!pending && visible) {
      const elapsed = Date.now() - (visibleSinceRef.current ?? Date.now());
      const remaining = Math.max(0, minimumMs - elapsed);
      timeout = window.setTimeout(() => {
        visibleSinceRef.current = null;
        setVisible(false);
      }, remaining);
    }

    return () => {
      if (timeout !== undefined) window.clearTimeout(timeout);
    };
  }, [delayMs, minimumMs, pending, visible]);

  return visible;
}
