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

const BASE_URL = import.meta.env.VITE_API_URL || '/api';

interface FetchApiOptions extends RequestInit {
  timeout?: number;
}

export async function fetchApi<T>(endpoint: string, options: FetchApiOptions = {}): Promise<T> {
  const url = `${BASE_URL}${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}`;

  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...((options.headers as Record<string, string>) || {}),
  };

  // Only set Content-Type to JSON if it's not FormData
  if (!(options.body instanceof FormData)) {
    if (!headers['Content-Type']) {
      headers['Content-Type'] = 'application/json';
    }
  } else {
    // If body is FormData, ensure Content-Type is NOT set so the browser
    // automatically sets it with the correct multipart boundary.
    delete headers['Content-Type'];
  }

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

    if (!response.ok) {
      let errorData: unknown;
      try {
        errorData = await response.json();
      } catch {
        errorData = { message: response.statusText };
      }

      if (response.status === 401) {
        // Handled globally (e.g. redirect to login)
      }

      throw new ApiError(response.status, errorData);
    }

    if (response.status === 204 || response.status === 205) {
      return null as T;
    }

    // Handle 200/201 that might surprisingly have no body
    const text = await response.text();
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
