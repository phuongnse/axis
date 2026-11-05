# D43. Tenant provisioning and migrations across tenant databases

Status: **Agreed**

## Context

Under the tenant directory of [D42](d42-tenant-directory-in-the-platform-database.md), something must
create a tenant's database and roles, migrate every tenant database on an upgrade and activate
releases. That needs admin rights on the database server and every tenant's schema role, which
[D31](d31-separate-database-roles.md) keeps out of the hosts that serve requests.

## Decision

- **A command-line admin host.** `Axis.Admin`, run by the platform operator, creates tenants,
  migrates tenant databases and activates releases. It alone holds the admin connection, a role
  that may create databases and roles and is never a superuser, and the tenants' schema roles under
  the `directory` source. It writes the directory over the `platform` schema's owner, not the admin
  role.
- **A cell offers the same operations through an API.** The provisioning API of
  [D123](d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md) queues jobs that
  `Axis.Admin` runs with the same code. A self-hosted installation has no provisioning API, and the
  server and the worker never hold admin rights.
- **`tenant create`** creates the roles `axis_<id>_schema` and `axis_<id>_runtime` with generated
  passwords and the database `axis_<id>`, migrates it and only then sets the tenant `active`.
  Running it again finishes what is missing.
- **No tenant is deleted outside a cell.** It is suspended instead.
- **Each tenant migrates on its own.** A failed tenant keeps its old schema and does not stop the
  others. The operator migrates first, then deploys the new hosts.
- **A host serves only tenants on its own schema version,** the id of the newest migration applied.
- **`activate` activates a release per tenant and runs no migration.** Startup activation stays only
  for the `configuration` source.

The commands and exit codes are in [administration](../reference/administration.md), the connections
in [database roles](../reference/storage.md#under-the-directory-source), and the cell's API in
[tenant lifecycle](../reference/tenant-lifecycle.md#provisioning-api).

## Consequences

- No long-running host holds database admin rights.
- One broken tenant database no longer stops the others, but tenants can sit on different schema
  versions, and migrated tenants answer `503` until the new hosts are deployed.
- A version taken from the code needs no number that someone must remember to raise.
- Role names derived from the tenant id are easy to find, but an id with a hyphen needs quoting.

## Alternatives

- **Migrate in the server.** It would hold every tenant's schema role, and one failed tenant would
  stop its start.
- **A tenant delete.** Deleting destroys data, so suspending is used instead.
