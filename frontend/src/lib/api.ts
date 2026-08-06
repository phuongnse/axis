import {
  applyBrowserSessionResponse,
  getCsrfToken,
  useAuthStore,
} from '@/features/auth/auth-store';
import type { AxisBrowserSessionDto } from '@/lib/api-generated';
import { queryClient } from '@/lib/query-client';

export class ApiError extends Error {
  status: number;
  data: unknown;

  constructor(status: number, data: unknown, message?: string) {
    super(message || `API Error: ${status}`);
    this.status = status;
    this.data = data;
    this.name = 'ApiError';
  }
}

export class ClientRequestSessionChangedError extends Error {
  constructor() {
    super('The client request session changed.');
    this.name = 'ClientRequestSessionChangedError';
  }
}

const BASE_URL = import.meta.env.VITE_API_URL || '/api';

interface FetchApiOptions extends RequestInit {
  timeout?: number;
}

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);
let csrfBootstrapInFlight: Promise<string> | null = null;
let clientRequestSessionGeneration = 0;

export function invalidateClientRequestSession(): void {
  clientRequestSessionGeneration += 1;
}

export async function fetchApi<T>(endpoint: string, options: FetchApiOptions = {}): Promise<T> {
  const requestSessionGeneration = clientRequestSessionGeneration;
  const url = `${BASE_URL}${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}`;

  const headers = new Headers(options.headers);
  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json');
  }

  const method = (options.method ?? 'GET').toUpperCase();
  if (!SAFE_METHODS.has(method) && !headers.has('Authorization')) {
    headers.set('X-CSRF-TOKEN', await ensureCsrfToken());
  }

  if (options.body instanceof FormData) {
    // Let the browser set the multipart boundary.
    headers.delete('Content-Type');
  } else if (options.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  assertCurrentClientRequestSession(requestSessionGeneration);

  const timeoutMs = options.timeout || 30000;
  const controller = new AbortController();
  const id = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetch(url, {
      ...options,
      headers,
      signal: options.signal || controller.signal,
      credentials: options.credentials || 'include',
    });

    clearTimeout(id);
    assertCurrentClientRequestSession(requestSessionGeneration);

    if (!response.ok) {
      let errorData: unknown;
      try {
        errorData = await response.json();
      } catch {
        errorData = { message: response.statusText };
      }

      assertCurrentClientRequestSession(requestSessionGeneration);

      if (response.status === 401) {
        useAuthStore.getState().markBrowserSessionGuest();
        queryClient.clear();
        if (typeof window !== 'undefined' && !window.location.pathname.startsWith('/sign-in')) {
          window.location.href = '/sign-in';
        }
      }

      throw new ApiError(response.status, errorData);
    }

    if (response.status === 204 || response.status === 205) {
      return null as T;
    }

    const text = await response.text();
    assertCurrentClientRequestSession(requestSessionGeneration);
    if (!text) {
      return null as T;
    }

    return JSON.parse(text);
  } catch (error: unknown) {
    clearTimeout(id);
    if (error instanceof Error && error.name === 'AbortError') {
      throw new Error('The operation was aborted');
    }
    throw error;
  }
}

function assertCurrentClientRequestSession(requestSessionGeneration: number): void {
  if (requestSessionGeneration !== clientRequestSessionGeneration) {
    throw new ClientRequestSessionChangedError();
  }
}

async function ensureCsrfToken(): Promise<string> {
  const current = getCsrfToken();
  if (current) return current;

  if (!csrfBootstrapInFlight) {
    csrfBootstrapInFlight = fetchBrowserSessionForCsrf().finally(() => {
      csrfBootstrapInFlight = null;
    });
  }
  return csrfBootstrapInFlight;
}

async function fetchBrowserSessionForCsrf(): Promise<string> {
  const response = await fetch(`${BASE_URL}/auth/session`, {
    method: 'GET',
    headers: { Accept: 'application/json' },
    credentials: 'include',
  });
  if (!response.ok) {
    throw new ApiError(response.status, await readErrorData(response));
  }

  const session = (await response.json()) as AxisBrowserSessionDto;
  applyBrowserSessionResponse(session);
  const csrfToken = getCsrfToken();
  if (!csrfToken) throw new Error('Browser session response did not provide a CSRF token.');
  return csrfToken;
}

async function readErrorData(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return { message: response.statusText };
  }
}
