# D44. Packaging for on-premises and cloud installation

Status: **Agreed**

## Context

An organization must be able to install Axis on one machine or in Kubernetes and upgrade it safely.
The development images hold the samples and development credentials, which would weaken every
installation that ran them.

## Decision

- **Three production images,** `axis-server`, `axis-worker` and `axis-admin`, separate from the
  development images. They hold no sample and no credential, run as a non-root user on a read-only
  root file system, and read settings only from the environment and secret files. `axis-admin` holds
  the PostgreSQL client tools for backup and restore.
- **On premises, a Compose example** runs the server, the worker and a one-shot `migrate`, with
  PostgreSQL outside it. A smoke test installs it, as the end-to-end proof of the images.
- **In the cloud, a Helm chart** runs the server and the worker with their probes and runs `migrate`
  before an upgrade. It is checked statically, against the Kubernetes schemas.
- **Both run Axis's identity provider,** as [D99](d99-axis-ships-its-own-identity-provider.md) says.
- **An upgrade migrates first,** then rolls out the new hosts, as
  [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md) orders. Migrations are
  never reversed: a rollback restores the backup and runs the old images.
- **A release workflow publishes the images,** scanned and signed, as
  [D107](d107-the-dependency-scan-runs-now-and-images-are-scanned-and-signed.md) says.

Several replicas are [D49](d49-several-server-replicas-behind-a-tls-proxy.md). The steps and every
setting are in [installation](../reference/installation.md).

## Consequences

- No installation can start with a sample credential.
- A host refuses a tenant on another schema version, so an upgrade is safe without reversing
  migrations, and a restored backup is the only rollback.
- Helm is a new tool in `scripts/setup.sh`, and no test runs the chart on a live cluster.
- A workflow that publishes images is a change in `.github/`, which NexKit may not make.
