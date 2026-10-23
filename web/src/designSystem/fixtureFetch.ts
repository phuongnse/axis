/**
 * Replaces `window.fetch` so a preview answers the widgets' API calls from fixtures. A route matches
 * the request path with its query, or the path alone. Any other URL is logged as a console error and
 * answered with a 404, so a missing fixture fails the preview check.
 */
export function installFixtureFetch(routes: Record<string, () => unknown>): void {
  window.fetch = async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const route = routes[url] ?? routes[url.split('?')[0]]
    if (!route) {
      console.error('No design-system fixture for', url)
      return new Response(null, { status: 404 })
    }
    return new Response(JSON.stringify(route()), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
}
