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
mkdir -p .dev-certs

openssl genrsa -out .dev-certs/rootCA-key.pem 4096
openssl req -x509 -new -nodes \
  -key .dev-certs/rootCA-key.pem \
  -sha256 -days 825 \
  -out .dev-certs/rootCA.pem \
  -subj "/CN=Axis Local Dev Root CA"
openssl x509 -outform der \
  -in .dev-certs/rootCA.pem \
  -out .dev-certs/rootCA.cer

openssl genrsa -out .dev-certs/localhost-key.pem 2048
cat > .dev-certs/localhost.ext <<'EOF'
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=@alt_names

[alt_names]
DNS.1=localhost
DNS.2=api
DNS.3=web
IP.1=127.0.0.1
IP.2=::1
EOF

openssl req -new \
  -key .dev-certs/localhost-key.pem \
  -out .dev-certs/localhost.csr \
  -subj "/CN=localhost"
openssl x509 -req \
  -in .dev-certs/localhost.csr \
  -CA .dev-certs/rootCA.pem \
  -CAkey .dev-certs/rootCA-key.pem \
  -CAcreateserial \
  -out .dev-certs/localhost.pem \
  -days 825 -sha256 \
  -extfile .dev-certs/localhost.ext
```

## Trust

Trust the root CA in the host OS that runs the browser. On WSL with a Windows
browser, import `.dev-certs/rootCA.cer` into **Current User -> Trusted Root
Certification Authorities** in Windows. Trusting only inside WSL is not enough
for a Windows browser.

Docker Playwright E2E passes `.dev-certs/rootCA.pem` to Node and imports it
into Chromium's NSS database before running browsers, so E2E tests do not use
`ignoreHTTPSErrors`.
