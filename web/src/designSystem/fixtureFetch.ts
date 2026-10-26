/**
 * Replaces `window.fetch` so a preview answers the widgets' API calls from fixtures. Only a request to
 * the page's own origin whose path starts with one of the scopes is a fixture's to answer. A route
 * matches the request path with its query, or the path alone. A scoped path with no route is logged as
 * a console error and answered with a 404, so a missing fixture fails the preview check. A route may
 * return a `Response`, which is the answer as it is, or a promise that never settles, which keeps the
 * request pending. Any other result is answered as JSON with status 200. Any other request goes to the
 * page's own `fetch`, unchanged, so a page that loads the bundle keeps working.
 */
export function installFixtureFetch(routes: Record<string, () => unknown>, scopes: readonly string[]): void {
  const original = window.fetch
  window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const href = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const url = new URL(href, window.location.href)
    if (url.origin !== window.location.origin || !scopes.some((scope) => url.pathname.startsWith(scope))) {
      return original.call(window, input, init)
    }
    const route = routes[url.pathname + url.search] ?? routes[url.pathname]
    if (!route) {
      console.error('No design-system fixture for', href)
      return new Response(null, { status: 404 })
    }
    const result = await route()
    if (result instanceof Response) {
      return result
    }
    return new Response(JSON.stringify(result), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
}
