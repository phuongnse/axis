# D93. An operator web console

Status: **Agreed**

## Context

The process inspector shows one process per widget, and only to users that application policies allow,
so the platform operator has no single place to see what failed in a tenant.
[D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md) left a web console for later.

## Decision

- **For the platform operator,** through an installation-level role that a host setting names.
  Application policies play no part.
- **One tenant.** The console is a platform route on each tenant's hosts.
- **What it shows:** failed work, dead letters, webhook deliveries, schedules, publishing and the
  active releases, never a `sensitive` field or more than process inspection holds.
- **Only the commands that exist:** retry, cancel and resolve of
  [D35](d35-operator-retry-cancel-and-resolve.md), and redeliver and discard of
  [D72](d72-a-delivery-limit-and-dead-letters.md), each with a reason.
- **No provisioning.** That stays in `Axis.Admin` and, in a cell, its
  [provisioning API](../reference/tenant-lifecycle.md#provisioning-api), and publishing stays in Studio.

The detail is in the [operator console](../reference/operator-console.md).

## Consequences

- The operator gets one place to look without new powers.
- The view across a cell's tenants is the cell console of
  [D123](d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md).
- The role cannot be given for one application only.
