# D93. An operator web console

Status: **Agreed**

The owner agreed this on 2026-10-25. M23 adds the web console for operators
that [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md)
left for later. D43 holds: there is still no HTTP API for provisioning, and
the server still holds no admin rights on the database server. M23 built everything
here. See [operator console](../reference/operator-console.md).

- **Who it is for:** the platform operator, who keeps a tenant's processes
  and deliveries running. It is not for application administrators.
- **Operator role:**
  - An installation-level role, not a `role` resource, like the author role.
    The setting `Console:OperatorRole` names the roles claim value. With no
    value, nobody may use the console.
  - Application policies play no part. The role gives read access to the
    console views across every application of the tenant, and the commands
    below.
- **One tenant:** the console is a platform route, `/console`, on each
  tenant's hosts. It shows only that tenant. There is no view across tenants.
- **Views:**
  - failed instances and calls with an unknown outcome, across every process;
  - dead letters, from M14's event delivery limit;
  - webhook deliveries;
  - schedule triggers, with their last and next occurrence;
  - publish requests and release history;
  - the tenant's status and each application's active release.
- **Commands:** retry, cancel and resolve, as
  [D35](d35-operator-retry-cancel-and-resolve.md) defines them, and redeliver
  and discard for dead letters. Each needs a reason and writes the same audit
  records, with the operator as actor. The console has no other command.
- **What it never shows:** a field marked `sensitive`, and record data beyond
  what process inspection holds.
- **What stays in `Axis.Admin`:** creating, suspending, backing up and
  restoring tenants, setting bindings, and migrating and activating releases.
  Publishing stays in Studio.

_Why:_ the process inspector shows one process per widget, and only to users
that application policies allow, so an operator has no single place to see
what failed in a tenant. A console limited to the commands that already exist
gives that place without new powers. Keeping provisioning in `Axis.Admin`
keeps admin rights on the database server out of every long-running host, as
D43 agreed.
