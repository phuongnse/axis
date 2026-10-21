# D44. Packaging for on-premises and cloud installation

Status: **Agreed**

Axis ships as three production images, a Compose example for one machine and a
Helm chart for Kubernetes. The production images, the Compose example and the
chart are built. The development `Dockerfile` default
target and `compose.yaml` hold the samples and development credentials, and
they are not for production. See the
[installation guide](../reference/installation.md) for the steps and every
setting, and [administration](../reference/administration.md) for the admin
commands.
The owner confirmed these points on issue #641 on 2026-10-19.

- **Production images:**
  - There are three: `axis-server`, `axis-worker` and `axis-admin`. They come
    from new targets of the `Dockerfile`. The development images stay as they
    are.
  - They hold no sample, no sample extension package, no sample module, no
    development realm and no credential. They run the `Production`
    environment.
  - They run as a non-root user and work with a read-only root file system.
  - They read settings only from environment variables and from files in
    `/run/secrets`. Each file is named after a setting in its environment
    variable form, such as `Platform__SecretKey`, and its content is the value.
  - Extension packages are mounted at `/extensions` and modules at `/modules`.
    Application folders for `activate` are mounted into the admin container.
  - `axis-admin` holds the PostgreSQL 18 client tools, for backup and restore.
    PostgreSQL 18 is the supported server version.
  - Every image carries the Axis version in the OCI label
    `org.opencontainers.image.version`. `axis-admin version` prints it.
- **On premises:**
  - `deploy/compose/` holds a production Compose example: the server, the
    worker and a one-shot admin `migrate`. PostgreSQL and the OIDC provider
    are external. Secrets come from files under `secrets:`. It sets
    `Tenancy:Source` to `directory`.
  - A smoke test starts it with a test PostgreSQL and Keycloak. It creates a
    tenant with the admin tool and activates the leave request sample,
    `samples/apps/leave-requests`. It checks that the tenant's host serves its
    site at `/leave` and that both hosts are ready.
  - The leave request sample needs the `departments` module. The smoke test
    mounts it at `/modules` for the test only. The production images still hold
    no sample module.
- **Cloud:**
  - `deploy/helm/axis/` holds a Helm chart. It has a Deployment for the server
    and one for the worker, with their health probes. A pre-upgrade Job runs
    `migrate`.
  - PostgreSQL, the OIDC provider and the source of secrets are external.
  - `helm lint` and `helm template`, validated against the Kubernetes schemas,
    check the chart. Axis has no test against a live cluster.
- **Upgrades:**
  - An upgrade backs up the databases and runs `migrate` with the new admin
    image. Then it rolls out the new server and worker. This is the order of
    [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md).
  - A host serves only tenants on its own schema version, as D43 says. So an
    old host refuses a migrated tenant and does not corrupt it.
  - Migrations are never reversed. A rollback is restoring the backup and
    running the old images.
- **Publishing:** CI builds the production images on every run. Pushing them to
  a registry on a version tag is a workflow change, so it is a hand-made pull
  request.
- **Not in M9:**
  - A test against a live Kubernetes cluster.
  - A downgrade.
  - Publishing images by NexKit.
  - Deciding where the ASP.NET data protection key ring lives, and forwarded
    headers. Sessions across replicas and restarts need a shared key ring.
    [D49](d49-several-server-replicas-behind-a-tls-proxy.md) now decides
    both.

_Why:_ an image with development settings or sample credentials would weaken
every installation, so the production images hold none. Compose suits one
machine and Helm is the common package format for Kubernetes. The cost of Helm
is a new tool in `scripts/setup.sh`, so the NexKit `cache` setting and the CI
cache steps must follow in a hand-made pull request. The chart is checked only
statically, and the Compose smoke test is the end-to-end proof of the images.
Migrating first and refusing a host on the wrong schema version keeps an
upgrade safe without reversing migrations. A restored backup is the only
rollback. Pushing images is a workflow change, which NexKit may not make.
