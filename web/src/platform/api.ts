const unsafeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

/**
 * `fetch` for `/api`. Unsafe requests carry the CSRF header from D23, which a page on another site
 * cannot set without a CORS preflight. Other requests pass through unchanged.
 */
export function apiFetch(input: string, init?: RequestInit): Promise<Response> {
  if (!unsafeMethods.has((init?.method ?? 'GET').toUpperCase())) {
    return fetch(input, init)
  }
  const headers = new Headers(init?.headers)
  headers.set('X-Axis-Csrf', '1')
  return fetch(input, { ...init, headers })
}
