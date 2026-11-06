# D45. Tenant backup and restore

Status: **Agreed**

## Context

A tenant is the unit of isolation, so it is the unit of backup. Restoring sends the tenant's work
back in time, which can repeat calls to external systems, and an archive leaves the installation, so
it must not carry the installation's secrets.

## Decision

- **`tenant backup` archives one tenant** from one consistent snapshot of its database, with a
  manifest of the versions its releases pin. The tenant keeps running.
- **The archive holds no secret of the installation** and is encrypted with a backup key kept
  outside it, as [D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md)
  and [D116](d116-how-sensitive-fields-and-backups-are-encrypted.md) say.
- **`tenant restore` restores only into a new tenant,** with new roles and passwords, and rebuilds the
  grants of [D31](d31-separate-database-roles.md). An older schema is migrated after the restore.
- **A restored tenant stays suspended** until its secrets are set again. Restoring over a tenant is
  a restore under a new id and a move of the hosts.
- **A restore refuses before it changes anything,** and never for a missing module, because a stored
  release holds its module files.
- **Work goes back to backup time,** and the worker runs it again once the tenant resumes.
- **The operator backs up the platform database and keeps the keys** outside Axis.
- **A cell backs up every tenant on a schedule,** as
  [D123](d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md) says.

The commands and refusals are in [tenant backup and restore](../reference/tenant-backup-and-restore.md),
and the effect on running work in [execution](../domain/knowledge.md#execution).

## Consequences

- An archive can be stored like any data file, and a lost backup key loses it.
- Restoring never overwrites live data.
- A call staged after the backup gets a new idempotency key, so an external system may act twice.
- Point-in-time recovery of one tenant is not offered. An archive restores into another installation
  too, where the tenant gets a realm of its own, but nothing keeps two installations' data in step, as
  the [product brief](../product-brief.md#out-of-scope) keeps migrating live data out of scope.
