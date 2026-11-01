# Security headers and rate limits

Detailed reference for the security headers the server sets on every response, and the rate limits
on the routes a caller can repeat. The conventions of [architecture.md](../architecture.md) apply:
sections marked _(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows [D85](../decisions/d85-rate-limits-and-security-headers.md). The security headers
and the rate limits are built.

## Security headers

The server sets these headers itself, so they are the same under `scripts/dev.sh`, Docker Compose
and the Helm chart, and behind any proxy.

| Header                      | Value                                                                                                                                                                                                     | On                                            |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| `Content-Security-Policy`   | `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'` | Every response                                |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains`                                                                                                                                                                     | Only when the request's scheme is `https`     |
| `X-Content-Type-Options`    | `nosniff`                                                                                                                                                                                                 | Every response                                |
| `Referrer-Policy`           | `strict-origin-when-cross-origin`                                                                                                                                                                         | Every response                                |
| `Cross-Origin-Opener-Policy` | `same-origin`                                                                                                                                                                                            | Every response                                |
| `Permissions-Policy`        | `camera=(), microphone=(), geolocation=(), payment=(), usb=()`                                                                                                                                            | Every response                                |
| `Cache-Control`             | `no-store`                                                                                                                                                                                                | Every response under `/api`                   |
| `Clear-Site-Data`           | `"cache", "storage"`                                                                                                                                                                                       | A successful `POST /api/auth/sign-out`        |

- **Scripts stay strict.** The SPA has no inline script and calls no `eval`. Scripts come only from
  the server's own origin. The one exception is the import map for
  [custom widgets](custom-widgets.md), whose `sha256` hash `script-src` lists when widgets are
  installed _(planned for M22)_.
- **Styles allow inline.** The UI library writes `<style>` tags at run time, so `style-src` allows
  `'unsafe-inline'`. An injected style cannot run code.
- **HSTS.** The scheme is the one after the
  [forwarded headers](host-settings.md#forwarded-headers) rules of
  [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md). The server never sends the
  header in `Development`, so a developer's browser does not remember it for `localhost`.
  `includeSubDomains` meets ASVS Level 2 and tells browsers to refuse plain http on every subdomain
  of a tenant's host, so an operator must serve those subdomains over https.
- **Clear-Site-Data.** A successful sign-out tells the browser to drop the site's HTTP cache,
  `localStorage`, `sessionStorage` and IndexedDB (ASVS V14.3.1). A refused sign-out, such as one
  without the CSRF header, sends no such header. See [security](security.md).
- **Base URI.** `base-uri 'none'` meets ASVS V3.4.3. The SPA has no `<base>` element.
- **Content-Type.** Text, JSON and XML answers name `charset=utf-8`, including problem details, the
  SPA page and static files. An answer that already names a charset keeps it. An attachment keeps
  the file's media type, because the server does not know the encoding of an uploaded file.
- **Static files.** The SPA's hashed files under `/assets/` get
  `Cache-Control: public, max-age=31536000, immutable`. `index.html` and the other files keep the
  revalidation by ETag. `no-store` applies only under `/api`, and replaces any `Cache-Control` an
  endpoint there sets.
- **Every answer.** The headers are written when the response starts, so error answers, `401`,
  `403` and the tenant `404` carry them too.
- **Under `scripts/dev.sh`.** Vite serves the SPA page on port 5173 without the policy, so only the
  API answers it proxies carry the headers. A violation then shows up only in the E2E run, Docker
  Compose or the Helm chart.
- **File download.** A file download keeps to this policy and adds `Content-Disposition: attachment`.
  A custom widget bundle is served from the server's own origin and calls no `eval`.
- **Tests.** Integration tests check the headers on the SPA page, a static file, an API response, a
  `401` and the `404` for an unknown host, and the exact `Content-Type` with its charset on those
  answers and on a `403`. A unit test shows which types get a charset. They check that HSTS is missing on plain http and in
  `Development`, and that HSTS follows the forwarded scheme only from a named proxy. The Playwright
  run uses the shared `test` of `tests/e2e/test.ts`, which fails any test whose pages report a
  Content Security Policy violation.

## Rate limits

The server uses the ASP.NET Core rate limiter.

- **Per replica.** Each replica counts on its own, in memory. A shared count would need a store
  that the stack does not have, or a database round trip on each request. So the limit an
  installation sees is the number of replicas times the setting.
- **No override per tenant.** Every tenant gets the same limits.
- **Where it runs.** After the tenant is resolved, the CSRF check and authentication. A refused write
  costs nothing, and the caller is known. A request that fails the CSRF check, or has no valid
  session or token, still gets its `403` or `401`, never a `429`. The one exception is the sign-in
  callback, see below.
- **Refused.** A request over a limit answers `429` problem details titled "Too many requests.", with
  a `Retry-After` header in seconds. The route never runs, so it writes nothing and no audit record.
  The server counts refusals in the metric `axis.rate_limits.refused`, by partition.

| Partition    | Routes                                                                  | Counted by                  | Setting                     |
| ------------ | ----------------------------------------------------------------------- | --------------------------- | --------------------------- |
| Sign-in      | `GET /api/auth/sign-in` and `GET /api/auth/callback`                    | Client address              | `RateLimits:SignIn`         |
| Session      | Every other route that takes a session                                  | Tenant and user             | `RateLimits:Session`        |
| Bearer       | Inbound endpoints and [service accounts](security.md#service-accounts)  | Tenant and caller id        | `RateLimits:Bearer`         |
| Publishing   | Studio validate, publish, preview, rollback and test requests           | Tenant and user             | `RateLimits:Publishing`     |
| MCP          | `/mcp`, the [MCP server](mcp-server.md) _(planned for M35)_             | Tenant and client           | `RateLimits:Mcp`            |

- **Client address.** It follows the [forwarded headers](host-settings.md#forwarded-headers) rules.
  Requests with no client address, such as those on a Unix socket, share one sign-in count.
- **Settings.** Each setting is a number of requests per minute for one replica, see
  [rate limit settings](host-settings.md#rate-limit-settings). A value of zero or less, or one that is
  not a number, stops the server's start, naming the setting. The defaults are set so that the
  [load test](performance.md#load-test) never reaches them.
- **Short burst.** Each caller has a bucket of request allowances. It holds one minute's worth, which
  is the setting, and gets a sixth of it back every 10 seconds. So a page that makes a burst of API
  calls passes, and `Retry-After` is at most about 10 seconds.
- **The partition follows the route and the kind of caller.** A bearer request on a route that also
  takes a session counts as bearer. The status poll `GET /api/studio/apps/{application}/requests/{id}`
  and `POST /api/auth/sign-out` count as session, not as publishing or sign-in.
- **No valid session or token.** A request on a session or bearer route with no valid session or token
  counts in that route's partition by tenant and client address. So it is not unlimited, and a user's
  empty bucket never turns an anonymous `401` into a `429`.
- **The callback.** The OIDC handler answers `GET /api/auth/callback` inside authentication, so the
  limiter counts the callback just before authentication. It shares the sign-in count of its client
  address.
- **The control plane.** _(planned for M33)_ The [control plane](control-plane.md) is another
  service with limits of its own: the `sign_up` partition of [sign-up](sign-up.md#bots-and-abuse),
  and the [billing portal](billing-portal.md#rules)'s.
- **Open routes.** `GET /api/site`, `GET /api/texts/{locale}`, `/health` and the SPA's static files
  and pages have no limit.
- **Metric.** `axis.rate_limits.refused` counts the refused requests, with the attribute
  `axis.partition`: `sign_in`, `session`, `bearer`, `publishing` or, _(planned for M35)_, `mcp`. It carries no tenant, user or
  address.
- **Tests.** An integration test sends requests past each partition's limit and expects the `429` with
  `Retry-After`, and a request in another partition, of another user, from another client address or
  of another tenant still passes. Another shows a bearer caller is refused past `RateLimits:Bearer`,
  and a refused write still gets its CSRF `403` first. A unit test reads every route from the server's
  endpoint data source and fails on a route that is not open and has no partition, or that takes a
  bearer token and has no bearer partition.
