export class ApiError extends Error {
  status: number;
  data: any;

  constructor(
    status: number,
    data: any,
    message?: string
  ) {
    super(message || `API Error: ${status}`);
    this.status = status;
    this.data = data;
    this.name = 'ApiError';
  }
}

const BASE_URL = import.meta.env.VITE_API_URL || '/api';

export async function fetchApi<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  const url = `${BASE_URL}${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}`;

  const defaultHeaders: HeadersInit = {
    'Content-Type': 'application/json',
    'Accept': 'application/json',
  };

  const response = await fetch(url, {
    ...options,
    headers: {
      ...defaultHeaders,
      ...options.headers,
    },
    // Ensure credentials (cookies) are always sent
    credentials: options.credentials || 'include',
  });

  if (!response.ok) {
    let errorData;
    try {
      errorData = await response.json();
    } catch {
      errorData = { message: response.statusText };
    }

    if (response.status === 401) {
      // Handled globally (e.g. redirect to login)
      // Usually intercepted at the Router loader level
    }

    throw new ApiError(response.status, errorData);
  }

  // Handle empty responses (like 204 No Content)
  if (response.status === 204) {
    return null as any;
  }

  return response.json();
}