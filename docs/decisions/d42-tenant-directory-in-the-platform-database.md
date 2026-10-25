# D42. Tenant directory in the platform database

Status: **Agreed**

The server and the worker learn their tenants from a tenant directory in the
platform database, and pick up changes without a restart. The tenant catalog contract, the `platform` schema and the directory source
of the server and the worker are built. Under `directory` the server creates the
schema at start and reads the directory every `Tenancy:RefreshInterval`. It
serves new tenants, answers `503` for tenants that are not `active` and `404`
for removed hosts, and runs the role check of D31 on new tenants and on changed
database settings. The worker reads its `active` tenants, and retries the
first read until it works. It then reads the directory every
`Tenancy:RefreshInterval`, runs the role check of D31 on new tenants and on
changed database settings, claims no new work of tenants that are not `active`,
and is Degraded for a failed refresh. The server's Degraded readiness for a
failed refresh is built.
Both hosts,
under `configuration`, take tenants from the `Tenants` section, as in
[D10](d10-tenant-aware-from-day-one-full-tenancy-later.md).
See [tenant directory](../architecture.md#tenant-directory) for what each host
answers, [tenancy settings](../reference/host-settings.md#tenancy-settings) for
the settings and [platform database](../reference/storage.md#platform-database)
for the tables and the encryption.
The owner confirmed these points on issue #639 on 2026-10-18.

- **Two tenant sources:**
  - The setting `Tenancy:Source` is `configuration` or `directory`. It
    defaults to `configuration`, which is today's behaviour. Development,
    `scripts/dev.sh`, Docker Compose and the tests keep it.
  - With `directory`, both hosts read tenants only from the platform
    database. A `Tenants` section is then ignored, and the start logs a
    warning when one is present.
  - Both sources answer through one tenant catalog contract, so request
    resolution, tenant connections, deployment bindings and sign-in do not
    know the source.
- **What the directory holds:**
  - It is a `platform` schema in the platform database, with EF Core
    migrations shipped with Axis. The server applies them at start, as it does
    for tenant databases.
  - A tenant has an `id` under the same rule as today, a `status`
    (`provisioning`, `active` or `suspended`), its hosts, its database, its
    OIDC settings and its deployment bindings.
  - Hosts are unique across all tenants, ignoring letter case.
  - An entry may name its main tenant, when it is a
    preview tenant. See [D48](d48-preview-tenants.md).
  - The database is the server host, the port, the database name, and the
    runtime and schema user names.
  - Every entry has a `version` that grows on each change.
- **Secrets:**
  - Database passwords, the OIDC client secret and `secret` binding values are
    stored encrypted, never in plain text.
  - Encryption is AES-256-GCM with an installation key from the setting
    `Platform:SecretKey`, 32 bytes in base64. It comes from the environment or
    a mounted secret file, never from a committed file outside Development. A
    missing or invalid key stops the start.
  - Each encrypted value is bound to its tenant and field through the
    AES-GCM associated data: the tenant id and the field name. Someone with
    write access to the table cannot copy one tenant's secret onto another
    tenant, because it no longer decrypts.
  - No API, log, problem details or backup manifest holds a decrypted secret.
    A value is decrypted only at the moment of use, as `SecretValue` does
    today.
  - Rotating the installation key is out of scope. After a leaked key,
    every stored secret must be set again by hand.
    - [D87](d87-tenant-setting-commands-and-key-rotation.md) replaces this
      point: the installation key can be rotated.
- **Changes without restart:**
  - Both hosts read the directory again every `Tenancy:RefreshInterval`, 30
    seconds by default.
  - A new `active` tenant is served after the next refresh, and its OIDC
    client is registered then.
  - A `suspended` or `provisioning` tenant answers `503` problem details
    titled "This tenant is not available.". The worker claims none of its new
    work, and work already claimed finishes.
  - A removed host answers `404`, as an unknown host does today.
  - A refresh that cannot reach the platform database keeps the last good
    directory and logs an error. A tenant suspended during that outage keeps
    running until the next successful refresh.
  - A tenant that first appears at a refresh is served only after the runtime
    database role check of
    [D31](d31-separate-database-roles.md) passes. Until then it answers
    `503`, like a suspended tenant.
  - A served tenant whose database host, port, name, runtime user or runtime
    password changes at a refresh answers `503` until the D31 check passes on
    the new settings. Then it gets a new connection pool.
- **Who writes the directory:** the server and the worker only read it. The
  admin host of
  [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md)
  writes it over `ConnectionStrings:PlatformSchema`, and migrates, grants and
  activates the databases of tenants.
- **Platform connection strings:**
  - `ConnectionStrings:Platform` becomes a role that may only read the
    `platform` schema. Under `directory`, both hosts need it. The worker has
    no platform connection today.
  - The server applies the platform migrations over a separate setting,
    `ConnectionStrings:PlatformSchema`. It uses it only at start, as D31 does
    for the tenant databases.
  - The read-only role gains `INSERT` on the data protection key table, and
    under `configuration` the server now creates the platform tables too. See
    [D49](d49-several-server-replicas-behind-a-tls-proxy.md).
- **First load:** the server stops its start when it cannot load the directory
  the first time, because there is no earlier directory that is safe to serve.
  The worker waits and retries, and it is not ready until the first load
  works.
- **Readiness:**
  - When a refresh fails, `/health/ready` answers Degraded (`200`) on both
    hosts. Unhealthy would pull the hosts out of service, which defeats keeping
    the last good directory.
  - This lifts the rule of
    [D37](d37-health-checks-for-the-server-and-the-worker.md) that the
    worker is never Degraded.

_Why:_ a new tenant, a suspended tenant or a changed secret must take effect
without editing the configuration of both hosts and restarting them.
[D25](d25-connectors-deployment-bindings-and-secrets.md) has no secret
store, and provisioning creates database roles with generated passwords that
the hosts must read without a restart. Keeping `configuration` as the default
avoids rewriting every test fixture and development script, at the cost of two
code paths behind one contract. Encrypting each value with its tenant and field
bound in makes a copied value useless. Degraded readiness keeps the hosts
serving from the last good directory while it still shows the problem.
