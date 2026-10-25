# Security headers and rate limits

Detailed reference for the security headers the server sets on every response, and the rate limits
on the routes a caller can repeat. The conventions of [architecture.md](../architecture.md) apply:
sections marked _(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows [D85](../decisions/d85-rate-limits-and-security-headers.md). All of this page is
_(planned for M16)_.

## Security headers

The server sets these headers itself, so they are the same under `scripts/dev.sh`, Docker Compose
and the Helm chart, and behind any proxy.

| Header                      | Value                                                                                                                                                                                                     | On                                            |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| `Content-Security-Policy`   | `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'` | Every response                                |
| `Strict-Transport-Security` | `max-age=31536000`                                                                                                                                                                                        | Only when the request's scheme is `https`     |
| `X-Content-Type-Options`    | `nosniff`                                                                                                                                                                                                 | Every response                                |
| `Referrer-Policy`           | `strict-origin-when-cross-origin`                                                                                                                                                                         | Every response                                |
| `Cross-Origin-Opener-Policy` | `same-origin`                                                                                                                                                                                            | Every response                                |
| `Permissions-Policy`        | Turns off camera, microphone, geolocation, payment and USB                                                                                                                                                | Every response                                |
| `Cache-Control`             | `no-store`                                                                                                                                                                                                | Every response under `/api`                   |

- **Scripts stay strict.** The SPA has no inline script and calls no `eval`. Scripts come only from
  the server's own origin.
- **Styles allow inline.** The UI library writes `<style>` tags at run time, so `style-src` allows
  `'unsafe-inline'`. An injected style cannot run code.
- **HSTS.** The scheme is the one after the
  [forwarded headers](host-settings.md#forwarded-headers) rules of
  [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md). The server never sends the
  header in `Development`, so a developer's browser does not remember it for `localhost`.
- **Static files.** The SPA's hashed files keep their long cache lifetime. `no-store` applies only
  under `/api`.
- **Later routes.** A file download keeps to this policy and adds `Content-Disposition: attachment`.
  A custom widget bundle is served from the server's own origin and calls no `eval`.
- **Tests.** An integration test checks the headers on the SPA page, a static file and an API
  response, and that HSTS is missing on plain http.

## Rate limits

The server uses the ASP.NET Core rate limiter.

- **Per replica.** Each replica counts on its own, in memory. A shared count would need a store
  that the stack does not have, or a database round trip on each request. So the limit an
  installation sees is the number of replicas times the setting.
- **No override per tenant.** Every tenant gets the same limits.
- **Where it runs.** After the tenant is resolved, the CSRF check and authentication. A refused write
  costs nothing, and the caller is known.
- **Refused.** A request over a limit answers `429` problem details titled "Too many requests.", with
  a `Retry-After` header in seconds. It writes no audit record. The server counts refusals in a
  metric, by partition.

| Partition    | Routes                                                                  | Counted by                  | Setting                     |
| ------------ | ----------------------------------------------------------------------- | --------------------------- | --------------------------- |
| Sign-in      | `GET /api/auth/sign-in` and `GET /api/auth/callback`                    | Client address              | `RateLimits:SignIn`         |
| Session      | Every other route that takes a session                                  | Tenant and user             | `RateLimits:Session`        |
| Bearer       | Inbound endpoints and [service accounts](security.md#service-accounts)  | Tenant and caller id        | `RateLimits:Bearer`         |
| Publishing   | Studio validate, publish, preview, rollback and test requests           | Tenant and user             | `RateLimits:Publishing`     |

- **Client address.** It follows the [forwarded headers](host-settings.md#forwarded-headers) rules.
- **Settings.** Each setting is a number of requests per minute, with a short burst allowance. The
  defaults are set so that the [load test](performance.md#load-test) never reaches them. A value of
  zero or less stops the server's start, naming the setting.
- **Open routes.** `GET /api/site`, `GET /api/texts/{locale}`, `/health` and the SPA's static files
  have no limit.
- **Tests.** An integration test sends requests past a limit and expects the `429` with
  `Retry-After`, and a request in another partition still passes.
