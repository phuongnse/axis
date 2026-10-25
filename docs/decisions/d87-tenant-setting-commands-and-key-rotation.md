# D87. Tenant setting commands and key rotation

Status: **Agreed**

M16 gives `Axis.Admin` the commands that change a tenant after it is created,
and the commands that rotate keys and passwords. Each command leaves a record
in a new platform audit table. The installation key rotation is built. The rest is
_(planned for M16)_. See
[tenant setting commands](../reference/tenant-setting-commands.md) for the
detail. The owner agreed these points on 2026-10-25.

- **Replaces a point of D42:**
  [D42](d42-tenant-directory-in-the-platform-database.md) says that rotating
  the installation key is out of scope. This decision adds it.
- **Tenant setting commands:**
  - `tenant binding set`, `tenant binding remove` and `tenant binding list`
    change and show a tenant's deployment bindings. A `secret` value comes
    from an environment variable or standard input, never from an argument,
    and is never printed.
  - `tenant oidc set` changes the authority, the client id, the client
    secret, the claim names, both audiences, the sign-in profile of
    [D83](d83-the-fapi-20-profile-per-tenant.md) and its client key.
  - `tenant host add`, `tenant host remove` and `tenant host move`. A move
    takes a host from one tenant to another in one transaction.
  - Every command raises the tenant `version`, so the server and the worker
    pick the change up at their next refresh, as D42 describes.
  - Every command needs `Tenancy:Source=directory`. Under `configuration` the
    host settings hold the tenants, and the commands exit with code 2.
  - The logic lives in a library that the command line calls. A later web
    console calls the same code.
- **Installation key rotation:**
  - A new setting, `Platform:PreviousSecretKey`, holds the old key during a
    rotation.
  - The hosts encrypt with `Platform:SecretKey`. To decrypt they try it first,
    then the previous key when the AES-GCM tag does not match.
  - `platform rekey` re-encrypts every tenant secret and every data protection
    key row with the new key, in one transaction.
  - Then the operator removes `Platform:PreviousSecretKey`. Nobody is signed
    out, and no secret has to be set again.
  - The encrypted format does not change.
  - Built: `platform rekey` works under both tenancy sources, because the server
    keeps its data protection keys in the platform database under both. It
    writes one platform audit record with no tenant, and it raises the `version`
    of each tenant whose secrets it re-encrypts.
- **Database password rotation:** `tenant rotate-passwords` sets new runtime
  and schema passwords for a tenant's database roles and stores them
  encrypted. A connection opened before the next refresh may fail once. Open
  pooled connections keep working.
- **Client key rotation:** `tenant oidc set` can hold a new client key next to
  the old one, so the identity provider can accept both while it changes over.
- **Platform audit table:**
  - A new table, `platform.audit_records`, is append-only, like the audit
    table of a tenant database.
  - Every `Axis.Admin` command that changes the directory, a tenant database,
    a key or a password writes one record. So does `tenant backup`, because an
    archive holds a tenant's data.
  - A record holds the time, the command, the tenant, the operating system
    user and machine that ran it, and the names of what changed. It never
    holds a secret value.
  - Only `Axis.Admin` writes it, over `ConnectionStrings:PlatformSchema`.
- **Restore:** a restored tenant still needs its secret bindings set again.
  `tenant binding set` is now the way to do it.

_Why:_ an installation must be able to change a tenant without editing tables
by hand, and must recover from a leaked key without signing everyone out and
setting every secret again. Accepting two keys for a while lets the hosts keep
running during the change. Commands that change secrets are the actions an
attacker wants most, so each one leaves a record that the operator can read
later. Putting the logic in a library lets a web console reuse it without a
second set of rules.
