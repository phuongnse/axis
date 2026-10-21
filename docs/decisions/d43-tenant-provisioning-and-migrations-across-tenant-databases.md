# D43. Tenant provisioning and migrations across tenant databases

Status: **Agreed**

A platform operator creates tenants, migrates every tenant database and
activates releases with a command-line admin host, `Axis.Admin`. Tenants can still come from the `Tenants` section, where the
server migrates and activates at start, as in
[D31](d31-separate-database-roles.md). See
[administration](../reference/administration.md) for every command,
[database roles](../reference/storage.md#database-roles) for who holds which
connection and [admin settings](../reference/host-settings.md#admin-settings)
for `Admin:ConnectionString`.
The owner confirmed these points on issue #640 on 2026-10-19.

- **Admin host:**
  - `Axis.Admin` is a console host that the platform operator runs. There is
    no HTTP API for provisioning.
  - It is the only host that writes the tenant directory of
    [D42](d42-tenant-directory-in-the-platform-database.md). It writes
    it over `ConnectionStrings:PlatformSchema`, the role that owns the
    `platform` schema. It does not use the admin role for that.
  - It is the only host that holds an admin connection to the database server,
    and the only one that holds the schema database role of a tenant when
    tenants come from the directory.
  - `Admin:ConnectionString` names a database role with `CREATEDB` and
    `CREATEROLE`. It is not a superuser. The tool refuses a superuser and
    exits with code 2.
  - Every command writes one line per tenant and step. No command prints a
    password or a connection string.
  - Exit code 0 means every tenant succeeded. Code 1 means at least one tenant
    failed. Code 2 means a usage or settings error before any tenant work: an
    unknown option, a missing setting, a superuser admin role or an unknown
    tenant.
- **Creating a tenant:**
  - `tenant create <id> --host <host>... --oidc-authority <url>
--oidc-client-id <id>` takes the OIDC client secret from an environment
    variable.
  - It writes the directory entry as `provisioning` first. It creates the
    roles `axis_<id>_schema` and `axis_<id>_runtime` with generated
    passwords, and the database `axis_<id>`, owned by the schema role. It runs
    the grants of D31 and the migrations, checks the runtime role, records the
    schema version and sets the entry `active`.
  - A tenant id may be at most 50 characters. PostgreSQL names hold at most 63
    bytes, and `axis_<id>_runtime` adds 13 characters. A tenant id with a
    hyphen gives a quoted role and database name.
  - The new database goes on the same database server as
    `Admin:ConnectionString`. The directory records that server's host and
    port. Axis has one database server.
  - `--preview-of <main>` creates a preview tenant of the
    main tenant, and `--binding <name>=<value>` sets its `url` bindings. See
    [D48](d48-preview-tenants.md).
  - Running it again for a `provisioning` tenant finishes what is missing. For
    an `active` or `suspended` tenant it fails and changes nothing.
- **Suspend, resume and list:**
  - `tenant suspend <id>` and `tenant resume <id>` set the status. Hosts pick
    the change up at their next refresh.
  - `tenant list` prints each tenant's id, status, hosts and schema version.
  - There is no tenant delete.
- **Migrations across tenant databases:**
  - `migrate` applies the grants and the configuration, data and processes
    migrations to every tenant database, or to one with `--tenant <id>`. It
    skips a tenant that is still `provisioning`.
  - Each tenant migrates on its own, in id order. A failed tenant keeps its old
    schema version and does not stop the others. `--parallel <n>` migrates up to
    n tenants at a time, and defaults to 1.
  - The operator runs `migrate` first and then deploys the new server and
    worker.
  - The server no longer migrates tenant databases under
    `Tenancy:Source=directory`.
- **Schema version:**
  - A tenant's schema version is the id of the newest EF Core migration across
    the configuration, data and processes contexts. Migration ids sort by time,
    so the newest one names the schema.
  - The directory stores it as text in `platform.tenants`. It is empty until
    the first migration.
  - Under `directory`, the server and the worker serve a tenant only when its
    schema version is the one they were built for. Otherwise the tenant's hosts
    answer `503`, as for a suspended tenant, and the worker claims none of its
    work. Between `migrate` and the deploy of the new hosts, tenants answer
    `503`.
- **Activation:**
  - `activate <folder> [--tenant <id>]` compiles a folder and activates its
    release for every `active` tenant, or for the one named tenant. It uses the
    same diagnostics, packages, modules, recompute limit and seeding as
    [startup activation](../reference/host-settings.md#startup-activation).
  - Each tenant succeeds or fails on its own.
  - It runs no EF Core migrations. A tenant whose schema version is not the
    admin host's fails with a message to run `migrate` first.
  - `ActivateOnStartup` is only for `Tenancy:Source=configuration`, where
    startup activation stays as it is, for Development, Compose and the tests.
    Under `directory` a non-empty setting stops the start.
- **Connections per host:**
  - Under `directory`, the server and the worker hold only runtime database
    roles for tenant databases. The server holds no tenant schema connection
    and runs no tenant migration or grant step.
  - The server keeps `ConnectionStrings:PlatformSchema` at start, for the
    platform migrations, as D42 says. "No schema connection" means no tenant
    schema connection.
  - The worker holds no schema connection of any kind.
- **Not in M9:** deleting a tenant, an HTTP provisioning API, rotating the
  generated database passwords and moving a tenant to another database server.

_Why:_ a command-line tool keeps database admin rights out of every
long-running host, and a web console for operators can come later. Tenants
migrate on their own, so one broken tenant database no longer stops the
others. The cost is that tenants can sit on different schema versions, so a
host refuses a tenant whose version it does not expect. A version taken from
the code needs no number that someone must remember to raise. Deleting a tenant
destroys data, so suspend is used instead. Role names that derive from the tenant id
make the roles easy to find, at the cost of quoted names for ids with a hyphen.
Writing the directory over the schema role of the `platform` schema keeps the
admin role limited to creating databases and roles.
