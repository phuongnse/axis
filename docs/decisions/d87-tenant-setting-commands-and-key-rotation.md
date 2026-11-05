# D87. Tenant setting commands and key rotation

Status: **Agreed**

## Context

An installation must change a tenant without editing tables by hand, and recover from a leaked key
without signing everyone out and setting every secret again. Commands that change secrets are the
actions an attacker wants most.

## Decision

- **`Axis.Admin` commands change a tenant after it is created,** such as its bindings, sign-in
  settings and hosts. Which sign-in settings stay the tenant's follows
  [D100](d100-identity-providers-per-application-and-sign-in-per-site.md).
- **A secret never goes on the command line,** and no command prints one.
- **Every change raises the tenant `version`,** so the hosts pick it up at their next refresh, as
  [D42](d42-tenant-directory-in-the-platform-database.md) says.
- **The rules live in a library,** so a web console can call the same code.
- **The installation key rotates without signing anyone out.** The hosts accept the previous key
  while `platform rekey` re-encrypts every secret and data protection key in one transaction.
- **Database passwords and client keys rotate too,** a client key by holding the new key next to
  the old one.
- **Every command that changes the directory, a tenant database, a key or a password writes a
  platform audit record,** as does `tenant backup`. It names who ran what and what changed, never a
  secret value.

The commands and the audit records are in
[tenant setting commands](../reference/tenant-setting-commands.md).

## Consequences

- A leaked installation key is replaced without setting any secret again.
- A connection opened during a password rotation may fail once.
- The tenant commands apply only under the `directory` source.
