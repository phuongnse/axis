# Tenant setting commands

Detailed reference for the `Axis.Admin` commands that change a tenant after it is created, rotate
keys and passwords, and the platform audit records they write. The other commands are in
[administration](administration.md). The conventions of [architecture.md](../architecture.md) apply:
sections marked _(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows [D87](../decisions/d87-tenant-setting-commands-and-key-rotation.md). The
`platform.audit_records` table and `PlatformAuditRecords.WriteAsync` are built. The rest of this
page is _(planned for M16)_.

## Common rules

- **Directory only.** Every command here needs `Tenancy:Source=directory`, because it writes the
  [tenant directory](../architecture.md#tenant-directory). Under `configuration` the host settings
  hold the tenants, so the command exits with code 2.
- **Over the schema role.** The commands write over `ConnectionStrings:PlatformSchema`, as
  `tenant create` does. They need `Platform:SecretKey` to encrypt.
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

- `tenant binding list <id>` prints each binding's name and kind. It prints the value of a `url`
  binding, and never the value of a `secret` binding.
- `tenant binding set <id> <name> --url <value>` adds or replaces a `url` binding. The value is an
  absolute `http` or `https` URL.
- `tenant binding set <id> <name> --secret` adds or replaces a `secret` binding. The value comes from
  `AXIS_BINDING_SECRET`, or from standard input when the variable is not set.
- `tenant binding remove <id> <name>` removes a binding. A binding that does not exist exits with
  code 2.
- Names are unique per tenant, ignoring letter case, as in
  [platform database](storage.md#platform-database).
- A preview tenant's bindings are set the same way. Its main tenant's bindings stay untouched.
- **After a restore.** A restored tenant needs its secret bindings set again, and this command does
  it. See [tenant restore](administration.md#tenant-restore).

## Sign-in settings

`tenant oidc set <id>` changes one or more sign-in settings of a main tenant:

| Option                         | Meaning                                                                                       |
| ------------------------------ | --------------------------------------------------------------------------------------------- |
| `--authority <url>`            | The authority of the tenant's identity provider                                               |
| `--client-id <id>`             | The OIDC client id                                                                            |
| `--client-secret`              | Replaces the client secret, from `AXIS_OIDC_CLIENT_SECRET` or standard input                  |
| `--claim <name>=<claim>`       | A claim name: `UserId`, `DisplayName` or `Roles`                                              |
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
  [tenant restore](administration.md#tenant-restore).

## Database passwords

`tenant rotate-passwords <id>` sets new generated passwords for the tenant's runtime and schema
database roles, with `ALTER ROLE`, and stores them encrypted, in one step per role.

- **Admin connection.** It needs `Admin:ConnectionString`, as `tenant create` does.
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
  built. `PlatformAuditRecords.WriteAsync` in `Axis.Administration` writes one record. No command
  calls it yet, so the commands that write records are _(planned for M16)_.
- **What writes it.** Every `Axis.Admin` command that changes the directory, a tenant database, a key
  or a password. `tenant backup` writes one too, because an archive holds a tenant's data.
  `tenant list` and `version` write none.
- **A record.** The time, the command, the tenant id, the operating system user and the machine that
  ran it, and the names of what changed, such as a binding name or a setting name. It never holds a
  secret value.
- **Who writes.** Only `Axis.Admin`, over `ConnectionStrings:PlatformSchema`. The read-only role of
  the server and the worker gets no `SELECT` on this table, so neither host reads it.
