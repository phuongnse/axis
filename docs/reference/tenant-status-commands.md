# Tenant status commands

The `Axis.Admin` commands on this page change a tenant's status and list the tenants. A status
decides whether the tenant's hosts answer and whether the worker claims its work. The other commands
are in [administration](administration.md).

## Common rules

The three status commands share these rules, and differ only in the statuses they start from:

| Command                          | From                     | To          | Exit code 1 for                |
| -------------------------------- | ------------------------ | ----------- | ------------------------------ |
| [`tenant suspend <id>`](#tenant-suspend)     | `active` or `readOnly`   | `suspended` | `provisioning`                 |
| [`tenant resume <id>`](#tenant-resume)       | `suspended` or `readOnly` | `active`    | `provisioning`                 |
| [`tenant read-only <id>`](#tenant-read-only) | `active`                 | `readOnly`  | `provisioning` or `suspended`  |

- **One option.** Each takes only `<id>`, the tenant id.
- **Picked up at the next refresh.** A change raises the tenant's `version`, and the hosts pick it up at
  their next refresh.
- **One line.** Each writes one `status` line.
- **A no-op.** A tenant that already has the wanted status is left alone: the command exits `0` and does
  not raise the `version`.
- **One conditional update.** The status changes with one conditional update, so a `tenant create` that
  finishes at the same moment is never overwritten.
- **Exit codes.** `0` on success, `1` for a status the table refuses or a failed write, and `2` for an
  unknown tenant.

## Tenant suspend

`tenant suspend <id>` suspends an `active` or a `readOnly` tenant. Once the hosts have picked the change
up, the tenant's hosts answer `503` and the worker claims none of its new work. Work already claimed
finishes.

## Tenant resume

`tenant resume <id>` makes a `suspended` or a `readOnly` tenant `active` again, so its hosts serve it
and the worker claims its work.

## Tenant read-only

`tenant read-only <id>` makes an `active` tenant `readOnly`. What a read-only tenant allows and
refuses is in [the read-only tenant](subscriptions.md#the-read-only-tenant).

- **Hosts keep serving.** Once the hosts have picked the change up, the tenant's hosts still answer,
  and each request learns that the tenant is read-only. The worker claims none of the tenant's new
  work, and work already claimed finishes.
- **Writes are refused** _(planned for M33)_. People still sign in and read, and every write answers
  `403` with the reason `tenant-read-only`.
- **Ending it.** `tenant resume` makes the tenant `active` again, and `tenant suspend` suspends it.
  After a resume the worker skips the schedule occurrences that fell while the tenant was read-only.
- **The provisioning API.** The same change is a job of the
  [provisioning API](tenant-lifecycle.md#provisioning-api).
- **When a trial ends** _(planned for M33)_. The [control plane](control-plane.md) makes the tenant
  read-only through the provisioning API.
- **When a payment's grace ends** _(planned for M34)_. The control plane makes the tenant read-only
  through the provisioning API.

## Tenant list

`tenant list` prints a table with one row for each tenant, in ordinal order of the id. It takes no
option.

| Column           | Meaning                                                                                                    |
| ---------------- | ---------------------------------------------------------------------------------------------------------- |
| `id`             | The tenant id.                                                                                             |
| `status`         | `provisioning`, `active`, `suspended` or `readOnly`.                                                       |
| `hosts`          | The tenant's hosts, separated by commas. A site host shows its path, such as `shop.example.test (shop)`. |
| `schema version` | The recorded schema version, or `-` when it is empty.                                                      |
| `preview of`     | The id of the main tenant, for a preview tenant, or `-` for a main tenant.                                 |

It never prints a password, an OIDC secret, a binding value or a connection string. It exits `0` on
success, also when there is no tenant, and `1` when it cannot read the directory.
