# Local HTTPS

> **Navigation**: [← local-dev.md](./local-dev.md) · [← docs/README.md](../README.md) · [← AGENTS.md](../../AGENTS.md)

Axis local dev uses HTTPS for browser-facing services by default. Compose mounts
only the certificate files each service needs; `.dev-certs/` is ignored, and the
local root private key stays on the host.

## Files

- `.dev-certs/rootCA.pem` - local root CA used by containers.
- `.dev-certs/rootCA.cer` - same root CA in DER format for host OS import.
- `.dev-certs/localhost.pem` - leaf certificate for `localhost`, `127.0.0.1`,
  `::1`, `api`, and `web`.
- `.dev-certs/localhost-key.pem` - leaf private key.

## Generate

Run from the repo root:

```bash
python scripts/axis.py local-dev certs
```

## Trust

Trust the root CA in the host OS that runs the browser. On WSL with a Windows
browser, import `.dev-certs/rootCA.cer` into **Current User -> Trusted Root
Certification Authorities** in Windows. Trusting only inside WSL is not enough
for a Windows browser.

Docker Playwright E2E passes `.dev-certs/rootCA.pem` to Node and imports it
into Chromium's NSS database before running browsers, so E2E tests do not use
`ignoreHTTPSErrors`.
