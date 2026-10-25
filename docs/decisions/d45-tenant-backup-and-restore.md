# D45. Tenant backup and restore

Status: **Agreed**

A platform operator backs up one tenant into an archive that holds no secret,
and restores it into a new, suspended tenant. The backup and the restore are built. See [tenant backup](../reference/administration.md#tenant-backup) and
[tenant restore](../reference/administration.md#tenant-restore) for the commands,
the manifest and every refusal.
The owner confirmed these points on issue #642 on 2026-10-19.

- **Backup:**
  - `tenant backup <id> --out <file>` in `Axis.Admin` writes one archive. It
    holds a `pg_dump` of the tenant database in custom format and a manifest.
  - [D76](d76-file-money-and-enum-label-field-types.md) stores the files of
    file fields in the tenant database, so the dump holds them.
  - The manifest holds the tenant id, the Axis version, the tenant's schema
    version, the directory entry without any secret, and every extension
    package and module version that a stored release pins.
  - The dump is one consistent snapshot, so the tenant keeps running during a
    backup.
  - The archive holds no secret. Database passwords, the OIDC client secret and
    `secret` binding values are not in it.
- **Archive and manifest:**
  - The archive is one tar file with `manifest.json` and `tenant.dump`.
  - The manifest lists the module versions that stored releases pin. It lists
    the package versions too, and marks which of them a restore requires.
- **Restore:**
  - `tenant restore <file> --tenant <id> --host <host>...` restores into a new
    tenant. It creates the database and both database roles with new
    passwords, as `tenant create` does, restores the dump and runs the grants
    again.
  - The dump is taken and restored without owners and privileges. The grant
    step of [D31](d31-separate-database-roles.md) builds every privilege
    again for the new roles.
  - An archive with an older schema version is restored and then migrated.
  - The tenant stays `suspended` after a restore. The operator sets the
    secrets again and resumes it.
  - The OIDC client secret comes from `AXIS_OIDC_CLIENT_SECRET`, as for
    `tenant create`. Each `secret` binding is restored by name and kind,
    without a value.
  - Restoring over an existing tenant is a suspend, a restore under a new id
    and a host move. There is no in-place overwrite.
  - A failed restore leaves the entry `provisioning`. Running the same restore
    again finishes it.
- **Refusals:**
  - A restore refuses, before it changes anything, with exit code 2 and a
    message that names what is missing.
  - It refuses an archive it cannot read or does not know, an archive whose
    schema version is newer than the admin tool's, an id that exists in the
    directory as `active` or `suspended`, a host that another tenant holds and
    a missing `AXIS_OIDC_CLIENT_SECRET`.
  - It refuses an archive whose required package versions are not installed.
    Required versions are the ones that an active release, or a release with a
    running or waiting process instance, pins. That is the start check
    `AXC0146`.
  - It never refuses a missing module version. A stored release holds its
    module files, so it keeps working without the module installed.
- **Work after a restore:**
  - Process instances, tasks, work items and connector calls are as they were at
    backup time. The worker runs them again once the tenant resumes.
  - A call that was staged before the backup and sent after it is sent again,
    with the same idempotency key, so a system that honours the key does not
    act twice.
  - A call that was staged after the backup is not in the archive. Its step
    runs again and stages a new call with a new operation identity, so a new
    idempotency key. The system may already have acted on the first call, so
    even a system that honours the key can act twice.
  - A connector without `idempotency` sends no key, so the system may act twice
    in either case.
  - Sequence counters, inbound endpoint receipts and schedule state also go
    back to backup time. Numbers issued after the backup can be issued again,
    and inbound requests accepted after it are lost.
  - This is a known effect. See
    [execution](../domain/knowledge.md#execution).
- **Outside Axis:**
  - The platform database and the installation key are backed up by the
    database operator with standard PostgreSQL tools.
  - Point-in-time recovery and restoring into another installation are out of
    scope. The product brief keeps migrating live business data between
    installations
    [out of scope](../product-brief.md#out-of-scope-for-now).
- **Not in M9:** deleting a tenant, an in-place overwrite, point-in-time
  recovery, restoring into another installation, and a command that moves a host
  between tenants or sets a binding.

_Why:_ a tenant is the unit of isolation, so it is the unit of backup. An
archive without secrets can be stored like any data file, at the cost that a
restored tenant stays suspended until its secrets are set again. Restoring only
into a new tenant never overwrites live data, and moving the hosts is a
separate step. Work goes back to an earlier state, so a call sent after the
backup is sent again. Only the idempotency key protects the external system
from a second effect, and only for a call staged before the backup. A call
staged after it gets a new key. A stored release holds its module files, so a missing
module would not stop a restored tenant. Refusing on it would block restores for
no reason.
