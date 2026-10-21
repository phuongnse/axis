# D49. Several server replicas behind a TLS proxy

Status: **Agreed**

M10 runs the server as several replicas behind a reverse proxy that ends TLS.
That is what a shared Studio server needs. The forwarded headers, the shared
key ring, the platform tables under both tenant sources, the Studio write
lock, the Helm chart's `studio.persistence` and the Compose example with two
replicas behind a TLS proxy and a shared Studio folder, with its smoke test,
are built. See the [installation guide](../reference/installation.md#several-server-replicas),
the [forwarded headers settings](../reference/host-settings.md#forwarded-headers),
the [platform database](../reference/storage.md#platform-database) and the
[authoring API](../reference/authoring.md#versions).
The owner confirmed these points on issue #706 on 2026-10-19.

- **Shared key ring:**
  - The server keeps its ASP.NET data protection keys in a new platform table,
    `platform.data_protection_keys`. Every replica reads the same keys.
  - Sessions survive a restart and work on every replica.
  - Each key is encrypted at rest with `Platform:SecretKey`, the way tenant
    secrets are
    ([D42](d42-tenant-directory-in-the-platform-database.md)).
  - The associated data of a key is an empty tenant id, one zero byte and the
    field name `data_protection_key:` followed by the key's id. This is the
    byte layout of tenant secrets. A tenant id is never empty, so a key and a
    tenant field cannot be swapped.
  - The server's read-only platform role gets `INSERT` on that table and on
    nothing else. Key rotation happens while the server runs, so the server
    must be able to add keys. Rows are only inserted, never updated or deleted.
  - The platform tables exist under both tenant sources. Under
    `Tenancy:Source=configuration` the server also applies the platform
    migrations. So the server needs `ConnectionStrings:PlatformSchema` and
    `Platform:SecretKey` there too. Development uses a committed development
    key. Development, Compose and the tests keep one code path.
- **Forwarded headers:**
  - Two new server settings name the proxies that count:
    `ForwardedHeaders:KnownProxies` lists IP addresses and
    `ForwardedHeaders:KnownNetworks` lists CIDR ranges.
  - The server reads `X-Forwarded-For`, `X-Forwarded-Proto` and
    `X-Forwarded-Host` only from those addresses. With both settings empty,
    which is the default, it reads none.
  - No proxy is trusted until it is named. Loopback is not trusted by default.
  - The server reads one hop, the nearest proxy. A client then cannot spoof the
    scheme or the host through a chain of proxies.
  - The server never reads `X-Forwarded-Prefix`.
  - Tenant resolution, cookie security and OIDC redirect addresses use the
    scheme and host that result.
  - An entry that cannot be read stops the start and names the setting. The
    worker reads neither setting.
- **Studio across replicas:**
  - Studio folders live on a volume that every replica mounts at the same
    path.
  - A write takes a PostgreSQL advisory lock in the platform database, over
    `ConnectionStrings:Platform`. The version check and the write happen under
    that lock, so it holds across replicas. An advisory lock needs no grant.
  - The lock is keyed by the folder alone. A create checks that its `id` is
    unused anywhere in the folder. A lock per file would let two creates with
    the same `id` both succeed.
  - A write that cannot take the lock answers `503` and writes nothing. It
    waits up to 10 seconds first. The snapshot of `validate` and `publish`
    takes the same lock.
- **Installs:**
  - The Helm chart drops the one-replica limit of the server.
    `studio.persistence` mounts a shared folder volume when Studio is on. In
    Kubernetes that needs a `ReadWriteMany` storage class.
  - The on-premises Compose install can run two server replicas behind a TLS
    proxy.
  - The on-premises smoke test does so. It checks that a session survives
    restarting one replica.
  - The Compose install mounts one shared Studio folder, `AXIS_STUDIO_DIR`, on
    both replicas. The smoke test saves through one replica and reads through
    the other.
- **Effect on earlier decisions:**
  - D23 keeps its session rules. The key ring and the forwarded headers change
    where the cookie key lives and which scheme and host the server sees.
  - D38 keeps its write rules. The write lock moves from the server process to
    the platform database.
  - D42 keeps its tenant directory. The read-only platform role gains `INSERT`
    on one table, and the platform tables now exist under `configuration` too.
  - D44 decides the key ring and the forwarded headers, which it left open.

*Why:* a shared Studio server needs more than one replica, and the server
cannot do that today. Its sessions end at every restart. A second replica
cannot read the first one's cookies. Behind a proxy that ends TLS, the server
sees plain HTTP, which breaks secure cookies and OIDC redirect addresses. Two
replicas that share a folder could both accept a write with the same version.
A key table in the platform database gives every replica the same keys without
a writable root file system, at the cost of one `INSERT` grant to a read-only
role. Platform tables under both tenant sources give one code path for keys and
locks, at the cost of platform tables in development databases. Trusting no
proxy until it is named keeps a client from setting the scheme or the host. A
shared volume for Studio folders keeps one copy of each file. In Kubernetes it
needs a `ReadWriteMany` storage class.
