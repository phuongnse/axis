import { useEffect, useState } from 'react';

export const defaultDebounceMs = 500;

export function useDebouncedValue<T>(value: T, delayMs = defaultDebounceMs): T {
  const normalizedDelayMs = Math.max(0, delayMs);
  const [debouncedValue, setDebouncedValue] = useState(value);

  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      setDebouncedValue(value);
    }, normalizedDelayMs);

    return () => window.clearTimeout(timeoutId);
  }, [value, normalizedDelayMs]);

  return debouncedValue;
}
