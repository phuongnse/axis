# Upgrade

This page says in what order a platform operator upgrades an Axis installation. It belongs to
[installation](installation.md), which says how to install Axis.

## Upgrade

An upgrade has a fixed order, because a host serves only the tenants on its own schema version. So
migrate first and roll out the hosts second. Only the latest version of Axis gets security fixes, so
an operator upgrades to get one, see [D108](../decisions/d108-reporting-a-vulnerability-and-security-fixes.md).

1. Back up the platform database and every tenant database. For example, run `pg_dump` from the admin
   image. Back up each tenant with [`tenant backup`](tenant-backup-and-restore.md#tenant-backup).
2. Run `migrate` with the new admin image.
3. Roll out the new server, the new worker, the new agent host and the new publisher.

Between steps 2 and 3, migrated tenants answer `503`. A tenant whose migration failed keeps its old
version. See [migrate](administration.md#migrate) for the window and for failed tenants.

The admin image checks the same connection strings as the hosts, see [TLS](installation.md#tls), so `migrate` in
step 2 needs them in their final form too.

**No downgrade.** Migrations are not reversed. To roll back, restore the backup from step 1 and run
the old images.
