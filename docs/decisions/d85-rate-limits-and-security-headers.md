# D85. Rate limits and security headers

Status: **Agreed**

## Context

Security headers stop framing, content sniffing and injected scripts at no cost, and ASVS Level 2
([D84](d84-asvs-level-2-and-a-server-side-session.md)) asks for them. Routes a caller can repeat need
limits, so one caller cannot exhaust a server.

## Decision

- **The server sets the headers itself** on every response.
- **Scripts strict, styles inline.** The Content Security Policy allows scripts only from the own
  origin, with no inline script or `eval`. It allows inline styles, because the UI library writes
  `<style>` tags. No `<base>`, plugins or framing.
- **HSTS with `includeSubDomains`,** only over `https` after the forwarded headers of
  [D49](d49-several-server-replicas-behind-a-tls-proxy.md), never in Development.
- **Rate limits per replica,** in memory, with the ASP.NET Core rate limiter, after the tenant, the
  CSRF check and authentication.
- **A partition per kind of caller:** sign-in by client address, sessions and Studio publishing by
  tenant and user, bearer tokens by tenant and caller. Every new route takes one.
- **A refusal is a `429`** with `Retry-After`. The limits are host settings, the same for every
  tenant.

The detail is in [security headers and rate limits](../reference/headers-and-rate-limits.md).

## Consequences

- No install can forget the headers.
- Subdomains of a tenant's host must serve https.
- The limit an installation sees grows with its replicas.

## Alternatives

- **Headers at the proxy.** An install could leave them out.
- **A shared count.** It needs a store [D3](d3-technology-stack.md) lacks, or a database round trip
  per request.
