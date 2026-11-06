# D42. Tenant directory in the platform database

Status: **Agreed**

## Context

A new tenant, a suspended tenant or a changed secret must take effect without editing the settings of
the server and the worker and restarting them. Provisioning creates database roles with generated
passwords that the hosts must read, and
[D25](d25-connectors-deployment-bindings-and-secrets.md) has no secret store to keep them in.

## Decision

- **Two tenant sources behind one contract.** `Tenancy:Source` is `configuration`, the default, which
  reads the tenants from the hosts' settings, or `directory`, which reads them only from the platform
  database. Request resolution, connections, bindings and sign-in do not know the source.
- **The directory is the `platform` schema** of the platform database. It holds each tenant's status,
  hosts, database, sign-in settings, bindings and main tenant if it is a preview, with a `version`
  that grows on every change. A host belongs to one tenant only.
- **Secrets are encrypted with an installation key,** AES-256-GCM under `Platform:SecretKey`, and
  bound to their tenant and field, so a copied value does not decrypt. A value is decrypted only at
  the moment of use. [D87](d87-tenant-setting-commands-and-key-rotation.md) rotates the key.
- **Changes apply without a restart.** Both hosts read the directory again at an interval. A tenant
  that is not `active` is not served and its new work is not claimed. A new tenant or changed
  database settings are served only once the runtime role check of
  [D31](d31-separate-database-roles.md) passes.
- **A failed refresh keeps the last good directory** and makes readiness Degraded. A failed first
  load stops the server, because no earlier directory is safe to serve, and the worker retries.
- **The hosts only read the directory,** apart from the few rows a host must write, such as the
  data protection keys of [D49](d49-several-server-replicas-behind-a-tls-proxy.md).
  [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md)'s `Axis.Admin` writes it.

What each host answers is in [tenant directory](../architecture.md#tenant-directory), the settings in
[tenancy settings](../reference/host-settings.md#tenancy-settings), and the tables and the encrypted
format in [platform database](../reference/storage.md#platform-database).

## Consequences

- An operator changes a tenant with a command, and the hosts follow within one refresh.
- Development and the tests keep `configuration`, so no fixture is rewritten, at the cost of two code
  paths behind one contract.
- A tenant suspended while the platform database is unreachable keeps running until a refresh works.
- Losing the installation key means setting every stored secret again.

## Alternatives

- **Tenants in the hosts' settings only.** Every change would need an edit on both hosts and a
  restart.
- **Unhealthy on a failed refresh.** It would pull the hosts out of service and defeat keeping the
  last good directory.
