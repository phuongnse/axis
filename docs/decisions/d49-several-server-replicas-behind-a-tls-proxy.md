# D49. Several server replicas behind a TLS proxy

Status: **Agreed**

## Context

A shared Studio server needs several server replicas behind a reverse proxy that ends TLS. A lone
server's sessions end at every restart and another replica cannot read its cookies. Behind the proxy
it sees plain HTTP, which breaks secure cookies and sign-in addresses. Two replicas that share a
Studio folder could both accept a write with the same version.

## Decision

- **A shared key ring in the platform database.** The data protection keys live in a platform table
  that every replica reads, encrypted with the installation key as the tenant secrets of
  [D42](d42-tenant-directory-in-the-platform-database.md) are. The server's read-only platform role
  may insert keys and nothing more.
- **The platform tables exist under both tenant sources,** so keys and locks have one code path.
- **No proxy is trusted until it is named.** The server reads the forwarded headers of one hop and
  only from the proxies its settings name.
- **Studio writes take an advisory lock in the platform database,** keyed by the folder, so the
  version check of [D38](d38-studio-edits-application-folders-through-an-authoring-api.md) holds
  across replicas. A write that cannot take it in time is refused.
- **Studio folders live on a volume that every replica mounts.**
- **The Helm chart and the Compose example can run several replicas.**

The steps are in [several server replicas](../reference/several-server-replicas.md), the settings in
[forwarded headers](../reference/host-settings.md#forwarded-headers) and the lock in the
[authoring API](../reference/authoring.md#versions).

## Consequences

- Sessions survive a restart and work on every replica, with no writable root file system.
- A read-only platform role gains one insert grant, and development databases hold the platform
  tables.
- An operator must name every proxy, a local one included.
- In Kubernetes, Studio needs a `ReadWriteMany` storage class.

## Alternatives

- **A lock per file.** Two creates with the same `id` in different files could both succeed.
