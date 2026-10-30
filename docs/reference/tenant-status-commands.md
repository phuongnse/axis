# Tenant status commands

Detailed reference for the `Axis.Admin` commands that suspend and resume a tenant and list the tenants. The other
commands are in [administration](administration.md). The conventions of [architecture.md](../architecture.md) apply.

## Tenant suspend

`tenant suspend <id>` sets the status of an `active` tenant to `suspended`
and raises its `version`.

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

`tenant resume <id>` sets the status of a `suspended` tenant to `active` and
raises its `version`.

| Option | Meaning |
| --- | --- |
| `<id>` | The tenant id. |

Hosts pick the change up at their next refresh. It writes one `status` line.
Resuming an `active` tenant is a no-op that exits `0` and does not raise the
`version`.

**Exit codes.** `0` on success. `1` when the tenant is `provisioning`, or the
write failed. `2` for an unknown tenant.

## Tenant list

`tenant list` prints a table with one row for each tenant, in ordinal order of
the id:

| Column | Meaning |
| --- | --- |
| `id` | The tenant id. |
| `status` | `provisioning`, `active` or `suspended`. |
| `hosts` | The tenant's hosts, separated by commas. A site host shows its path, such as `shop.example.test (shop)`. |
| `schema version` | The recorded schema version, or `-` when it is empty. |
| `preview of` | The id of the main tenant, for a preview tenant, or `-` for a main tenant. |

It takes no option. It never prints a password, an OIDC secret, a binding value
or a connection string.

**Exit codes.** `0` on success, also when there is no tenant. `1` when it
cannot read the directory.
