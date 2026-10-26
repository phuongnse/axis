# Tenant setting commands

Detailed reference for the `Axis.Admin` commands that change a tenant after it is created, rotate
keys and passwords, and the platform audit records they write. The other commands are in
[administration](administration.md). The conventions of [architecture.md](../architecture.md) apply:
sections marked _(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows [D87](../decisions/d87-tenant-setting-commands-and-key-rotation.md). The
`platform.audit_records` table, `PlatformAuditRecords.WriteAsync` and the records of the commands
that already exist are built, see [platform audit records](#platform-audit-records). The database
password rotation, [`tenant rotate-passwords`](#database-passwords), is built. The
[deployment binding](#deployment-bindings) and [host](#hosts) commands are built too. The sign-in
settings and the key rotation are _(planned for M16)_.

## Common rules

- **Directory only.** Every command here needs `Tenancy:Source=directory`, because it writes the
  [tenant directory](../architecture.md#tenant-directory). Under `configuration` the host settings
  hold the tenants, so the command exits with code 2.
- **Over the schema role.** The commands write over `ConnectionStrings:PlatformSchema`, as
  `tenant create` does. A command that encrypts needs `Platform:SecretKey`.
- **Picked up without a restart.** Every change raises the tenant `version`. The server and the
  worker pick it up at their next `Tenancy:RefreshInterval`.
- **Secrets never on the command line.** A secret value comes from an environment variable or from
  standard input, never from an argument. No command prints a secret.
- **Exit codes.** As in [output and exit codes](administration.md#output-and-exit-codes). An unknown
  tenant exits with code 2, and nothing changes.
- **One library.** The command line calls a library that holds the rules. A later web console calls
  the same library.
- **Audited.** Every command writes a [platform audit record](#platform-audit-records).

## Deployment bindings

- `tenant binding list <id>` prints a table of each binding's name, kind and value, ordered by
  name. A `url` binding shows its URL. A `secret` binding shows `(hidden)` when it has a value and
  `(not set)` when it has none, never the value. It also lists a tenant that is still
  `provisioning`.
- `tenant binding set <id> <name> --url <value>` adds or replaces a `url` binding. The value is an
  absolute `http` or `https` URL.
- `tenant binding set <id> <name> --secret` adds or replaces a `secret` binding. The value comes from
  `AXIS_BINDING_SECRET`, or from standard input when the variable is not set. The value is stored
  encrypted and never printed. An empty value exits with code 2. With the variable unset and a
  terminal for input, the command waits for end of input and shows no prompt.
- `tenant binding remove <id> <name>` removes a binding. A binding that does not exist exits with
  code 2.
- Names are unique per tenant, ignoring letter case, as in
  [platform database](storage.md#platform-database).
- A preview tenant's bindings are set the same way. Its main tenant's bindings stay untouched.
- Replacing a binding whose name differs only in letter case keeps the stored name.
- **Settings.** Only `tenant binding set --secret` needs `Platform:SecretKey`. The other commands
  need `ConnectionStrings:PlatformSchema` and `Tenancy:Source=directory`.
- **A provisioning tenant.** The commands that change something exit with code 2 for a tenant that
  is still `provisioning`. A rerun of `tenant create` or `tenant restore` replaces its bindings and
  hosts, so a change made before then would be lost.
- **After a restore.** A restored tenant needs its secret bindings set again, and this command does
  it. See [tenant restore](administration.md#tenant-restore).

## Sign-in settings

`tenant oidc set <id>` changes one or more sign-in settings of a main tenant:

| Option                         | Meaning                                                                                       |
| ------------------------------ | --------------------------------------------------------------------------------------------- |
| `--authority <url>`            | The authority of the tenant's identity provider                                               |
| `--client-id <id>`             | The OIDC client id                                                                            |
| `--client-secret`              | Replaces the client secret, from `AXIS_OIDC_CLIENT_SECRET` or standard input                  |
| `--claim <name>=<claim>`       | A claim name: `UserId`, `DisplayName`, `Roles`, `Email` or `Locale`                              |
| `--endpoint-audience <value>`  | `Oidc:EndpointAudience`, see [machine callers](security.md#machine-callers). Empty removes it |
| `--service-audience <value>`   | `Oidc:ServiceAudience`, see [service accounts](security.md#service-accounts). Empty removes it |
| `--profile standard\|fapi2`    | The sign-in profile, see [FAPI 2.0 profile](security.md#fapi-20-profile)                      |
| `--client-key`                 | Adds a client key for `private_key_jwt`, as a PEM from `AXIS_OIDC_CLIENT_KEY` or standard input |
| `--retire-client-key <kid>`    | Removes the client key with that key id                                                       |

- **Client key rotation.** A tenant holds at most two client keys. The server signs with the newest
  and publishes both, so the identity provider can accept either while it changes over. Retiring the
  last key of a `fapi2` tenant exits with code 2.
- **Checks.** `--profile fapi2` without a client key exits with code 2. A preview tenant has no
  sign-in settings of its own, so the command exits with code 2 for one.

## Hosts

- `tenant host add <id> <host>` adds a host. A host already used by any tenant exits with code 2.
- `tenant host remove <id> <host>` removes a host. Removing a tenant's last host exits with code 2.
- `tenant host move <host> --to <id>` moves a host to another tenant in one transaction. It is the
  last step of restoring over an existing tenant, see
  [tenant restore](administration.md#tenant-restore). A move may leave the source tenant with no
  hosts. Only `tenant host remove` refuses to remove a last host.
- Every host command exits with code 2 for a tenant that is still `provisioning`, as the binding
  commands do.
- A move raises the `version` of both tenants.

## Database passwords

`tenant rotate-passwords <id>` sets new generated passwords for the tenant's runtime and schema
database roles, `axis_<id>_runtime` and `axis_<id>_schema`, with `ALTER ROLE`, and stores them
encrypted, in one step per role. It prints one line per role, `<id> runtime-password ok` and
`<id> schema-password ok`, or `failed` with the message of the database. It never prints a password.

- **Admin connection.** It needs `Admin:ConnectionString`, as `tenant create` does, and checks the
  admin role the same way: it needs `CREATEROLE` and must not be a superuser. On PostgreSQL 16 and
  later a `CREATEROLE` role changes the password of only the roles it administers, so the role of
  `Admin:ConnectionString` must be the one that created the tenant's roles.
- **Order and steps.** The runtime role goes first, then the schema role. A step saves the new
  encrypted password and raises the tenant `version` in a transaction of the platform database, runs
  `ALTER ROLE`, and commits only when `ALTER ROLE` succeeded. A step that fails leaves the stored
  password of its role as it was, the command stops and exits with code 1, and running it again
  completes the rotation. A role that was rotated before the failure keeps its new password.
- **Which tenants.** An `active` or `suspended` tenant is rotated. A tenant that is still
  `provisioning` exits with code 2, as `migrate` does, because its roles may not exist yet.
- **Exit code 2, nothing changes.** `Tenancy:Source` is not `directory`, the tenant is unknown or
  still provisioning, `Admin:ConnectionString` is missing, or the admin role is unsuitable. No audit
  record is written.
- **The log of the database server.** `ALTER ROLE ... PASSWORD` cannot take a parameter. A server
  that logs DDL statements logs the new password, as it does for `CREATE ROLE` in `tenant create`.
- **During the change.** The server and the worker keep their open pooled connections. A new
  connection opened before their next refresh may fail once with the old password. The next refresh
  gives them the new one.

## Installation key

The installation key, `Platform:SecretKey`, encrypts every stored secret and the data protection
keys. It can be rotated without signing anyone out.

1. Generate a new key. Set it as `Platform:SecretKey` and the old one as
   `Platform:PreviousSecretKey` on every host, then restart them. Each host encrypts with the new
   key, and decrypts with the new key first, then with the previous key when the AES-GCM tag does
   not match.
2. Run `platform rekey`. It decrypts every tenant secret and every row of
   `platform.data_protection_keys`, and encrypts each with the new key, in one transaction.
3. Remove `Platform:PreviousSecretKey` from every host and restart them.

- **Format.** The [encrypted format](storage.md#platform-database) does not change, and neither does
  the associated data.
- **Settings.** `platform rekey` needs both keys. Without `Platform:PreviousSecretKey` it exits with
  code 2. A value that fails to decrypt with both keys stops the command, and nothing changes.
- **Data protection keys.** Rekey is the one action that updates rows of
  `platform.data_protection_keys`. The server's role still only inserts them.

## Platform audit records

- **Table.** `platform.audit_records` is append-only. The database rejects every update, delete and
  truncate with SQLSTATE `23001`, as it does for the audit table of a tenant database. The table is
  built. `PlatformAuditRecords.WriteAsync` in `Axis.Administration` writes one record.
- **What writes it.** Every `Axis.Admin` command that acts on a tenant: `tenant create`,
  `tenant suspend`, `tenant resume`, `tenant rotate-passwords`, `tenant backup`, `tenant restore`,
  `migrate` and `activate` are built. `tenant backup` writes one because an archive holds a tenant's
  data. `tenant binding set`, `tenant binding remove`, `tenant host add`, `tenant host remove` and
  `tenant host move` are built and write one each. The other setting commands and `platform rekey`
  are _(planned for M16)_. `tenant list`, `tenant binding list`, `version` and `publisher` write
  none. The dispatcher of `Axis.Admin` reads a table of commands, and each is
  marked as writing one record per tenant or none. A command without a mark fails a test.
- **When.** One record per tenant, when tenant work starts: after the checks of the command and
  before its first change for that tenant. A usage error, a missing setting, an unknown tenant or a
  host that another tenant holds writes none, because nothing started. A record means the command
  started work for the tenant, not that the work succeeded. The `changed` names of these commands
  are empty, because the command already says what changed. `tenant rotate-passwords` is the
  exception: its record names both roles, `axis_<id>_runtime` and `axis_<id>_schema`, and never a
  password.
  The binding and host commands differ: each is one small transaction that writes its record and
  makes its change together, so a record means the change was made. A command that is refused or
  fails rolls its record back too.
- **A record that cannot be written.** That tenant stops with an `audit` step, and nothing changes
  for it. The line is `<tenant> audit failed: <message>`, and the command exits `1`. The other
  tenants of `migrate` and `activate` go on. A record that is written prints no line.
- **A record.** The time, the command, the tenant id, the operating system user and the machine that
  ran it, and the names of what changed, such as a binding name or a setting name. It never holds a
  secret value. The binding and host commands write the names as `binding:<name>` and
  `host:<host>`, so one record can name things of more than one kind. `tenant host move` writes one
  record, on the target tenant, with `host:<host>` and `from:<source id>`.
- **Who writes.** Only `Axis.Admin`, over `ConnectionStrings:PlatformSchema`. The read-only role of
  the server and the worker gets no `SELECT` on this table, so neither host reads it.
