# D85. Rate limits and security headers

Status: **Agreed**

M16 adds security headers to every response and rate limits to the routes a
caller can repeat. The rate limits are _(planned for M16)_. See
[security headers and rate limits](../reference/headers-and-rate-limits.md)
for the detail. The owner agreed these points on 2026-10-25.

- **Headers from the server:**
  - The server sets the headers itself, so they are the same under
    `scripts/dev.sh`, Docker Compose and the Helm chart, and behind any proxy.
  - Content Security Policy: scripts only from the server's own origin, with
    no inline script and no `eval`. Styles from the own origin and inline,
    because the UI library writes `<style>` tags. No plugins, no `<base>`
    other than the own origin, and no framing by other sites.
  - HTTP Strict Transport Security, only when the request's scheme is `https`
    after the forwarded headers of
    [D49](d49-several-server-replicas-behind-a-tls-proxy.md), and never in
    Development.
  - `X-Content-Type-Options: nosniff`,
    `Referrer-Policy: strict-origin-when-cross-origin`,
    `Cross-Origin-Opener-Policy: same-origin` and a `Permissions-Policy` that
    turns off what the SPA does not use.
  - `Cache-Control: no-store` on every response under `/api`.
- **Rate limits:**
  - The server uses the ASP.NET Core rate limiter. Each replica counts on its
    own, in memory. A shared count would need a store that
    [D3](d3-technology-stack.md) does not have, or a database round trip on
    every request.
  - The limit an installation sees is the number of replicas times the
    setting. The docs say so.
  - There is no override per tenant.
  - Partitions:
    - sign-in and its callback, by client address after the forwarded
      headers;
    - routes that take a session, by tenant and user;
    - routes that take a bearer token, by tenant and caller id;
    - Studio publish and test requests, by tenant and user.
  - The limiter runs after the tenant, the CSRF check and authentication, so a
    refused write costs nothing and a caller is known.
  - A refused request is a `429` problem details response with
    `Retry-After`.
  - The limits are host settings under `RateLimits`, with defaults that the
    load test of [D82](d82-performance-slos-and-load-tests.md) never reaches.
- **Tests:** an integration test checks the headers on a page, a static file
  and an API response. Another sends requests past a limit and expects the
  `429`.
- **Later milestones:** routes that M14, M15 and M17 add take a partition from
  the list above. File downloads and custom widget bundles must keep to the
  Content Security Policy.

_Why:_ headers stop whole classes of attack in the browser, such as framing,
content sniffing and injected scripts, at no cost to the server. Setting them
in the server means no install can forget them. A limit per replica needs no
new store and still stops a single caller from exhausting a replica. The cost
is that the limit grows with the replicas. Allowing inline styles keeps the UI
library working, and scripts, where the real risk lies, stay strict.
