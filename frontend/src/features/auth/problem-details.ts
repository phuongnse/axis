import type { ApiError } from '@/lib/api';

export function getProblemDetail(error: ApiError): string {
  const data = error.data;
  if (typeof data === 'object' && data !== null) {
    const record = data as Record<string, unknown>;
    if (typeof record.detail === 'string') return record.detail;
    if (typeof record.message === 'string') return record.message;
    if (typeof record.title === 'string') return record.title;
  }
  return error.message;
}
