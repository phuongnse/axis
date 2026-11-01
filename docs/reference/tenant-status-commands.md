# Tenant status commands

Detailed reference for the `Axis.Admin` commands that suspend and resume a tenant and list the tenants. The other
commands are in [administration](administration.md). The conventions of [architecture.md](../architecture.md) apply.

## Tenant suspend

`tenant suspend <id>` sets the status of an `active` tenant, or _(planned for M33)_ a `readOnly`
one, to `suspended` and raises its `version`.

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |

Hosts pick the change up at their next refresh. After that the tenant's hosts
answer `503`, and the worker claims none of its new work. Work already claimed
finishes. It writes one `status` line. Suspending a `suspended` tenant is a no-op
that exits `0` and does not raise the `version`. The status changes with one
conditional update, so a `tenant create` that finishes at the same moment is
never overwritten.

**Exit codes.** `0` on success. `1` when the tenant is `provisioning`, or the
write failed. `2` for an unknown tenant.

## Tenant resume

`tenant resume <id>` sets the status of a `suspended` tenant, or _(planned for M33)_ a `readOnly`
one, to `active` and raises its `version`.

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |

Hosts pick the change up at their next refresh. It writes one `status` line.
Resuming an `active` tenant is a no-op that exits `0` and does not raise the
`version`.

**Exit codes.** `0` on success. `1` when the tenant is `provisioning`, or the
write failed. `2` for an unknown tenant.

## Tenant read-only

_(planned for M33)_ `tenant read-only <id>` sets the status of an `active` tenant to `readOnly` and
raises its `version`, as [D126](../decisions/d126-sign-up-trials-and-the-control-plane.md) agrees.
The control plane sets it when a trial ends or a payment's grace ends, through the
[provisioning API](tenant-lifecycle.md#provisioning-api).

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |

Hosts pick the change up at their next refresh. After that people still sign in and read, and every
write answers `403` with the reason `tenant-read-only`. The worker claims none of the tenant's new
work, and work already claimed finishes. What it allows and refuses is in
[the read-only tenant](subscriptions.md#the-read-only-tenant). `tenant resume` sets it `active` again,
and `tenant suspend` suspends it. Making a `readOnly` tenant read-only is a no-op that exits `0`.

**Exit codes.** `0` on success. `1` when the tenant is `provisioning` or `suspended`, or the write
failed. `2` for an unknown tenant.

## Tenant list

`tenant list` prints a table with one row for each tenant, in ordinal order of
the id:

| Column | Meaning |
| --- | --- |
| `id` | The tenant id. |
| `status` | `provisioning`, `active` or `suspended`, and _(planned for M33)_ `readOnly`. |
| `hosts` | The tenant's hosts, separated by commas. A site host shows its path, such as `shop.example.test (shop)`. |
| `schema version` | The recorded schema version, or `-` when it is empty. |
| `preview of` | The id of the main tenant, for a preview tenant, or `-` for a main tenant. |

It takes no option. It never prints a password, an OIDC secret, a binding value
or a connection string.

**Exit codes.** `0` on success, also when there is no tenant. `1` when it
cannot read the directory.
