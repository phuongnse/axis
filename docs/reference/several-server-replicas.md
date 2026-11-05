# Several server replicas

The server can run as several replicas behind a reverse proxy that ends TLS. This page says what the
replicas share and what the proxy must do. The reasons are in
[D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).

## Shared state

Every replica serves every request, so the replicas keep no state of their own that a request needs.

- **A shared key ring.** The server keeps its data protection keys in the platform database, encrypted
  with `Platform:SecretKey`, see [platform database](storage.md#platform-database). So sessions
  survive a restart and work on every replica. Every replica needs the same `Platform:SecretKey`: a
  replica with another one fails to start, and the error names the setting. The server sets the data
  protection application name to `Axis`, so replicas installed at different paths share the keys. To
  change `Platform:SecretKey` on an installation that holds keys, follow the
  [installation key](tenant-setting-commands.md#installation-key) rotation, which signs nobody out.
- **Studio folders.** Put them on a shared volume, and mount it at the same path on every replica,
  because the Studio write lock lives in the platform database and is keyed by the folder path. In
  Kubernetes that needs a `ReadWriteMany` storage class, set with `studio.persistence`. In Compose, set
  `AXIS_STUDIO_DIR`, which both servers mount. The volume must be writable by the non-root user of the
  image, UID 1654, and the chart sets no `fsGroup`. See [Studio settings](host-settings.md#studio-settings).
- **Platform settings under both tenant sources.** The server needs
  `ConnectionStrings:PlatformSchema` and `Platform:SecretKey` under `Tenancy:Source=configuration`
  too, because it creates the platform tables there as well. A missing one stops the start. See
  [tenancy settings](host-settings.md#tenancy-settings).

## Behind a TLS proxy

Behind a proxy that ends TLS, the server does not see the client's connection, only the proxy's. So
the server reads the real scheme and host from forwarded headers, and only from the proxies you name:

| Setting | Environment variable | Meaning |
| --- | --- | --- |
| `ForwardedHeaders:KnownProxies` | `ForwardedHeaders__KnownProxies__0` | IP addresses of the proxies. Use `__1`, `__2` and so on for more. |
| `ForwardedHeaders:KnownNetworks` | `ForwardedHeaders__KnownNetworks__0` | CIDR ranges of the proxies, such as `10.0.0.0/8`. |

- **What the proxy must send.** It must set `X-Forwarded-For`, `X-Forwarded-Proto` and
  `X-Forwarded-Host`, and replace any value that a client sent. The server reads these headers only
  from a named address, one hop, and never reads `X-Forwarded-Prefix`. With both settings empty, it
  reads none. Loopback is not trusted by default. The rules are in
  [forwarded headers](host-settings.md#forwarded-headers).
- **On premises.** The Compose example runs two server replicas behind a TLS proxy, see
  [TLS proxy and a second replica](installation.md#tls-proxy-and-a-second-replica) and
  [TLS](installation.md#tls).
