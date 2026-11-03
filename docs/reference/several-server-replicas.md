# Several server replicas

The server can run as several replicas behind a reverse proxy that ends TLS.
See [D49](../decisions/d49-several-server-replicas-behind-a-tls-proxy.md).
The shared key ring, the forwarded headers and the platform settings under both
tenant sources, the Studio write lock, the Helm chart's `studio.persistence`
and the Compose example with two replicas behind a TLS proxy and a shared
Studio folder, with its smoke test, are built.

- **A shared key ring.** The server keeps its data protection keys in the
  platform database, encrypted with `Platform:SecretKey`. See
  [platform database](storage.md#platform-database). Sessions survive a
  restart and work on every replica. Every replica needs the same
  `Platform:SecretKey`. A replica with another one fails to start, and the
  error names the setting. The server sets the data protection application name
  to `Axis`, so replicas installed at different paths share the keys. Changing
  `Platform:SecretKey` on an installation that holds keys follows the steps of
  the [installation key](tenant-setting-commands.md#installation-key) rotation,
  which signs nobody out. The first start after this change also signs
  everyone out once.
- **Forwarded headers.** Behind a proxy that ends TLS, the server does not see
  the client's connection, only the proxy's. Name your proxies so the server can
  read the real scheme and host:

  | Setting | Environment variable | Meaning |
  | --- | --- | --- |
  | `ForwardedHeaders:KnownProxies` | `ForwardedHeaders__KnownProxies__0` | IP addresses of the proxies. Use `__1`, `__2` and so on for more. |
  | `ForwardedHeaders:KnownNetworks` | `ForwardedHeaders__KnownNetworks__0` | CIDR ranges of the proxies, such as `10.0.0.0/8`. |

- **What the proxy must send.** It must set `X-Forwarded-For`,
  `X-Forwarded-Proto` and `X-Forwarded-Host`. It must replace any value a
  client sent. The server reads these headers only from a named address, one
  hop, and never reads `X-Forwarded-Prefix`. With both settings empty, it
  reads none. Loopback is not trusted by default. The rules are in
  [forwarded headers](host-settings.md#forwarded-headers).
- **Studio folders.** Put them on a shared volume. Mount it at the same path on
  every replica, because the write lock is keyed by the folder path. The lock
  is built. It lives in the platform database. In
  Kubernetes that needs a `ReadWriteMany` storage class, set with
  `studio.persistence`. In Compose, set `AXIS_STUDIO_DIR`. Both servers mount
  it. The volume must be writable by the non-root user of the image, UID 1654,
  and the chart sets no `fsGroup`. See [Studio](host-settings.md#studio-settings).
- **On premises.** The Compose example runs two server replicas behind a TLS
  proxy. See "TLS proxy and a second replica" in [on premises](installation.md#on-premises)
  and [TLS](installation.md#tls).
- **Platform settings under both tenant sources.** The server needs
  `ConnectionStrings:PlatformSchema` and `Platform:SecretKey` also under
  `Tenancy:Source=configuration`, because it creates the platform tables there
  too. A missing one stops the start. See [tenancy settings](host-settings.md#tenancy-settings).
