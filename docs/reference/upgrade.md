# Upgrade

This page says in what order a platform operator upgrades an Axis installation. It belongs to
[installation](installation.md), which says how to install Axis.

## Upgrade

An upgrade has a fixed order, because a host serves only the tenants on its own schema version. So
migrate first and roll out the hosts second. Only the latest version of Axis gets security fixes, so
an operator upgrades to get one, see [D108](../decisions/d108-reporting-a-vulnerability-and-security-fixes.md).

1. Back up the platform database and every tenant database. For example, run `pg_dump` from the admin
   image. Back up each tenant with [`tenant backup`](tenant-backup-and-restore.md#tenant-backup).
2. If the installation has no key service yet, wrap its keys. A host outside Development and Testing does not start
   without `KeyService:Kind`, and the new admin image refuses a plain `Platform:SecretKey`, so do this before `migrate`.
   Make the key file, see [the key service](installation-on-premises.md#the-secret-files), or the AWS KMS key, see
   [the key service](installation-in-the-cloud.md#the-key-service). Wrap `Platform:SecretKey`, and
   `Platform:PreviousSecretKey` if it is set, with `platform key wrap --purpose installation-key` from the new admin image. Wrap the agent
   host's key file with `--purpose agent-host-key` when AI authoring is on. Put the wrapped values in place of the plain ones,
   and set `KeyService:Kind` with `KeyService:KeyFile` or `KeyService:KeyId` on every host that reads them. Roll the wrapped
   keys and the new images out together, because an old host does not read a wrapped key.
3. Run `migrate` with the new admin image.
4. Roll out the new server, the new worker, the new agent host and the new publisher.

Between steps 3 and 4, migrated tenants answer `503`. A tenant whose migration failed keeps its old
version. See [migrate](administration.md#migrate) for the window and for failed tenants.

The admin image checks the same connection strings as the hosts, see [TLS](installation.md#tls), so `migrate` in
step 3 needs them in their final form too.

**No downgrade.** Migrations are not reversed. To roll back, restore the backup from step 1 and run
the old images.
