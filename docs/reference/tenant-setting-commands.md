# Tenant setting commands

Detailed reference for the `Axis.Admin` commands that change a tenant after it is created, rotate
keys and passwords, and the platform audit records they write. The other commands are in
[administration](administration.md). The conventions of [architecture.md](../architecture.md) apply:
sections marked _(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows [D87](../decisions/d87-tenant-setting-commands-and-key-rotation.md). The
`platform.audit_records` table, `PlatformAuditRecords.WriteAsync` and the records of the commands
that already exist are built, see [platform audit records](#platform-audit-records). The database
password rotation, [`tenant rotate-passwords`](#database-passwords), is built. The key rotation is
built, see [installation key](#installation-key). The
[deployment binding](#deployment-bindings) and [host](#hosts) commands are built too. So is
[`tenant oidc set`](#sign-in-settings), with its client key options, and the
[mail settings](#mail-settings) commands. So is ending [sessions](#sessions).

## Common rules

- **Directory only.** Every command here needs `Tenancy:Source=directory`, because it writes the
  [tenant directory](../architecture.md#tenant-directory). Under `configuration` the host settings
  hold the tenants, so the command exits with code 2. `platform rekey` is the exception: it works
  under both sources, see [installation key](#installation-key).
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
  name. A `url` binding shows its URL and a `storage` binding shows its value, `database`. A `secret` binding shows `(hidden)` when it has a value and
  `(not set)` when it has none, never the value. It also lists a tenant that is still
  `provisioning`.
- `tenant binding set <id> <name> --url <value>` adds or replaces a `url` binding. The value is an
  absolute `http` or `https` URL.
- `tenant binding set <id> <name> --storage database` adds or replaces a `storage` binding. Its only
  value is `database`. Any other value exits with code 2 and writes nothing, not even an audit
  record.
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

A successful run prints `<id> oidc ok`. With `--client-key` it then prints `<id> client-key <kid>`
and the public key as a PEM.

- **The secret.** `--client-secret` takes no value. The secret comes from `AXIS_OIDC_CLIENT_SECRET`,
  or from the first line of standard input when the variable is not set, as in
  `printf %s "$SECRET" | tenant oidc set <id> --client-secret`. An empty secret exits with code 2.
  The secret is stored encrypted and never printed.
- **Empty values.** An empty `--endpoint-audience` or `--service-audience` removes the audience. A
  tenant without a service audience refuses service account tokens again after the next refresh. An
  empty claim, as in `--claim Email=`, removes the stored claim name, so the default claim applies
  again.
- **Any status.** The command works on a tenant in any status, including `provisioning` and
  `suspended`, because sign-in settings need neither the tenant's database nor its roles. It needs
  `ConnectionStrings:PlatformSchema` and `Platform:SecretKey`, and not `Admin:ConnectionString`.
- **Audit.** One [platform audit record](#platform-audit-records) names every setting the command
  was given, even when the new value equals the stored one. The names are `authority`, `clientId`,
  `clientSecret`, `claimUserId`, `claimDisplayName`, `claimRoles`, `claimEmail`, `claimLocale`,
  `endpointAudience`, `serviceAudience` and `profile`, in that order. A name is never a value. Then
  come `retireClientKey:<kid>` and `clientKey:<kid>` for the key options, with the key id and never
  the key.
- **The client key.** `--client-key` takes no value. The key is an EC P-256 private key as a PEM. It
  comes from `AXIS_OIDC_CLIENT_KEY`, or from the whole of standard input when the variable is not
  set, as in `tenant oidc set <id> --client-key < key.pem`. The command stores the key encrypted, in
  PKCS#8 form, as the tenant's newest key. It never prints the private key. A PEM that is empty or
  is not an EC P-256 private key exits with code 2.
  `--client-key` and `--client-secret` together need `AXIS_OIDC_CLIENT_SECRET` or
  `AXIS_OIDC_CLIENT_KEY`. When neither is set, both would read standard input, so the command exits
  with code 2.
- **The key id.** The key id is the [RFC 7638](https://www.rfc-editor.org/rfc/rfc7638) JWK
  thumbprint of the public key, with SHA-256, in base64url. Axis works it out from the key, so the
  same key always has the same key id. The command prints it, with the public key as a PEM. An operator
  whose tenant signs in through an outside identity provider uses both there. The client assertion carries it as its `kid`.
- **Client key rotation.** A tenant holds at most two client keys. After its next refresh the server
  signs every client assertion with the newest key. For a tenant that signs in through its own realm,
  the command registers the keys in the realm itself, see the next bullet. The order is: run
  `--client-key`, wait for the servers' refresh, then run `--retire-client-key <old kid>`. For a tenant
  that signs in through an outside identity provider, the operator registers the new public key there
  first, because Axis has no JWKS address. If the key is added before that identity provider knows it,
  sign-in fails until it is registered or the new key is retired. Keycloak's admin console imports one
  public key per client. To let it hold two, give the client a JWKS, set through the admin API or a
  JWKS URL.
- **The tenant's own realm.** When the command is given `--profile`, `--client-key` or
  `--retire-client-key`, and the tenant signs in through its own realm, it updates the realm before it
  saves the change. The tenant has realm details when it has an activation secret and its authority is
  its own realm, see [tenant realm](host-settings.md#tenant-realm). The platform client `axis` and every
  `site-` client of the realm then list every key the tenant will hold after the run, each under its key
  id. Under `fapi2` they authenticate with `private_key_jwt` and require PAR, PKCE `S256` and
  DPoP-bound tokens. Under `standard` they use the client secret again and require neither PAR nor
  DPoP. Activation keeps a site client in step with `axis`, see
  [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). A tenant that signs in
  through an outside identity provider has no realm, so nothing changes there.
- **Retiring a key.** `--retire-client-key <kid>` removes the key with that key id. When it removes
  the newest key, the other key becomes the one the server signs with.
- **The tenant after the change.** The checks look at the tenant as it will be after the whole run,
  and one run may combine `--client-key`, `--retire-client-key` and `--profile`. So
  `--profile fapi2 --client-key` turns on `fapi2` for a tenant without a key, and
  `--profile standard --retire-client-key <kid>` removes the last key of a tenant that leaves `fapi2`.
  When both key options are given, the key is retired first and then the new one is added.
- **Exit code 2, nothing changes.** `Tenancy:Source` is not `directory`, the tenant is unknown, the
  tenant is a preview tenant, which has no sign-in settings of its own, no setting is given, a claim
  name or the profile is unknown, the tenant would be `fapi2` without a client key, the
  authority is not an absolute `http` or `https` URL, the client id is blank, the secret is
  empty, the client key is empty or not an EC P-256 private key, the tenant would hold a third
  client key, the key is already held, the key id to retire is unknown or blank, or both the secret
  and the key would read standard input. The `version` stays and no audit record is written.
- **Exit code 1 when the realm cannot be updated.** The command prints `<id> realm failed: <message>`.
  The message names the identity provider step and its status code, never a body or a secret. The
  tenant's keys, profile and `version` stay as they were, so running the command again completes the
  change. The audit record is already written.

- _(planned for M17)_ Every tenant signs in through its realm in
  [Axis's identity provider](identity-provider.md), which `tenant create` sets up. `--authority`, `--client-id`,
  `--client-secret` and `--claim` go, because the realm gives the authority and the clients, and each
  `identityProvider` resource names its own claims. The audiences, the profile and the client keys stay. Axis registers a client key in the realm itself, as
  `tenant oidc set` does today for a tenant with a realm.
- _(planned for M17)_ `tenant sign-in set <id>` sets the tenant's
  [platform sign-in](identity-provider.md#platform-sign-in): `--accounts true|false`, and one
  `--provider <name>` for each brokered provider, with its type, its bindings, its claims and its role mapping, as an
  `identityProvider` resource gives them. A run replaces the whole platform sign-in, brings the realm in step, and
  writes one platform audit record. A platform sign-in with no provider and no accounts exits with code 2.

## Mail settings

`tenant email set <id>` gives a tenant its own mail server. The worker sends the tenant's emails
through it, and the settings replace the host's `Email` settings as a whole, see
[mail settings](notifications.md#mail-settings).

| Option                  | Meaning                                                                          |
| ----------------------- | -------------------------------------------------------------------------------- |
| `--host <host>`         | The SMTP server. Required                                                        |
| `--from <address>`      | The sender address of every email. Required                                      |
| `--port <n>`            | The SMTP port, 1 to 65535. The default is 587                                    |
| `--tls true\|false`     | Whether to encrypt the connection. The default is `true`                         |
| `--user-name <name>`    | The user to sign in as. Without it the worker sends without signing in           |
| `--password`            | The password of the user, from `AXIS_SMTP_PASSWORD` or standard input            |

`tenant email clear <id>` removes the tenant's own settings, so the tenant uses the host's again. A
successful run of either command prints `<id> email ok`.

- **The whole block.** Each `set` replaces the tenant's whole mail block. An option left out is
  reset to its default or to empty, so a changed host never inherits an old user name or password.
  This is why `--host` and `--from` are required.
- **The password.** `--password` takes no value. The password comes from `AXIS_SMTP_PASSWORD`, or
  from the first line of standard input when the variable is not set, as in
  `printf %s "$PASSWORD" | tenant email set <id> --host <host> --from <address> --user-name <name> --password`.
  It is stored encrypted, bound to the tenant, and never printed. An empty password exits with
  code 2, and so does a password without `--user-name`.
- **Own settings.** A tenant has its own mail settings exactly when its SMTP host is set. A tenant
  without them uses the host's `Email` settings. Under the `directory` source the tenant directory
  holds them, and under `configuration` the tenant's `Email` block does.
- **Which tenants.** The commands work on an `active`, `suspended` or preview tenant. A preview
  tenant has its own mail settings, as it has its own bindings. A tenant that is still
  `provisioning` exits with code 2, as the binding commands do.
- **Settings.** Only `--password` needs `Platform:SecretKey`. Both commands need
  `ConnectionStrings:PlatformSchema` and `Tenancy:Source=directory`.
- **Audit.** One [platform audit record](#platform-audit-records) is written with the change, in one
  transaction. For `set` it names the settings that were given, in this order: `smtpHost`,
  `smtpPort`, `smtpUseTls`, `smtpUserName`, `smtpPassword` and `from`. For `clear` it names `email`.
  A name is never a value.
- **After a restore.** `tenant backup` and `tenant restore` do not carry the mail settings, so a
  restored tenant needs `tenant email set` again, as it needs its secret bindings.
- **Exit code 2, nothing changes.** `Tenancy:Source` is not `directory`, the tenant is unknown or
  still `provisioning`, `--host` or `--from` is missing or blank, the port is not a number from 1 to
  65535, the user name is blank, the password is empty, the password has no user name, `--tls false` is
  given outside Development, or an option is given twice or is unknown. The `version` stays and no audit record is written.

## Hosts

- `tenant host add <id> <host>` adds a host. A host already used by any tenant exits with code 2.
- `tenant host add <id> <host> --site <path>` gives the host to one site of the tenant. The path is stored in lower
  case in `platform.tenant_hosts.site_path`. A path with bad syntax, or a reserved path such as `api`, exits with
  code 2. The command does not check that a release has the site. A site has at most one host per tenant, so a
  second `--site` host for the same path exits with code 2. The audit record lists `host:<host>` and `site:<path>`.
- `tenant host remove <id> <host>` removes a host. Removing a tenant's last host exits with code 2. So does
  removing the tenant's last host that belongs to no site. Removing a site's host succeeds.
- `tenant host move <host> --to <id>` moves a host to another tenant in one transaction. It is the
  last step of restoring over an existing tenant, see
  [tenant restore](administration.md#tenant-restore). A move may leave the source tenant with no
  hosts. Only `tenant host remove` refuses to remove a last host. A move keeps the host's site, and it exits with
  code 2 when the target tenant already has a host for that site.
- `tenant backup` and `tenant restore` keep the host's site, and `tenant list` shows it, see
  [tenant backup](administration.md#tenant-backup) and [tenant list](administration.md#tenant-list).
- Every host command exits with code 2 for a tenant that is still `provisioning`, as the binding
  commands do.
- A move raises the `version` of both tenants.
- _(planned for M17)_ A host that belongs to a site serves only that site, and the tenant's other hosts send the
  site's path to it, see [sites and sign-in](identity-provider.md#sites-and-sign-in). Until then, the server does
  not read `site_path`, and a site host serves the whole tenant.

## Sessions

`tenant session end <id> --user <user>` deletes every session of one person in the tenant.
`tenant session end <id> --all` deletes every session of the tenant. The next request on each
deleted session answers 401, on every replica, because every request reads its row in
`axis.sessions`. A successful run prints one line, `<id> sessions ok: <n> ended`, and exits with
code 0, also when `n` is 0. A failure prints `<id> sessions failed: <message>` and exits with code 1.

- **Settings.** `ConnectionStrings:PlatformSchema`, `Platform:SecretKey` and
  `Tenancy:Source=directory`. It needs no admin role.
- **Schema role.** It connects to the tenant database as the tenant's schema role, as `migrate`
  does, and decrypts that password with `Platform:SecretKey`. That role owns `axis.sessions`.
- **Which tenants.** An `active`, `suspended` or preview tenant. A tenant that is still
  `provisioning` exits with code 2, because its database may not exist yet.
- **The user id.** It must match the stored user id exactly, with letter case. A person with no
  sessions is not an error: the run prints `0 ended`.
- **Audit.** One [platform audit record](#platform-audit-records) is written after the checks and
  before the delete. Its `changed` holds `user:<user>` or `all`. If the delete then fails, the
  record stays, the failure is printed, and running the command again completes it.
- **Exit code 2, nothing changes.** `Tenancy:Source` is not `directory`, a setting is missing, the
  tenant is unknown or still provisioning, or the options are wrong: none, both, a blank `--user`
  or an unknown option. No audit record is written.
- **What it does not do.** It does not disable the person's account. They can sign in again at
  once through the identity provider.

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
- **Both tenancy sources.** `platform rekey` does not read `Tenancy:Source`. The server keeps its
  data protection keys in the platform database under both sources, so both need the rotation.
  Under `configuration` the tenant directory is just empty.
- **Exit codes.** `0` when every value is re-encrypted. `2` when the usage is wrong, a setting is
  missing or malformed, the platform tables do not exist, `Platform:PreviousSecretKey` is not set
  (a blank value counts as not set), or a value decrypts with neither key. Then nothing changes and
  no audit record is written. A message for a value that decrypts with neither key names the tenant
  and the field, or the key id, and never a value. `1` when the audit record or the update fails,
  and nothing changes.
- **Summary line.** `platform rekey ok: <n> tenant secrets, <m> data protection keys`.
- **Every non-empty value.** Rekey re-encrypts each non-empty value, even one that the new key
  already encrypts, and skips empty values, such as the client secret of a preview tenant. Running
  it twice is harmless.
- **Version.** Rekey raises the `version` of each tenant whose secrets it re-encrypted, so each
  host loads the new ciphertext at its next `Tenancy:RefreshInterval`. That refresh rebuilds the
  database pool and the sign-in client of each tenant once.
- **Locks.** The transaction takes an `EXCLUSIVE` lock on `platform.tenants`,
  `platform.tenant_bindings` and `platform.data_protection_keys`. Another admin command, and a
  server that stores a new data protection key, wait for the commit. Readers are not blocked.
- **Audit record.** One record with no tenant. Its `changed` holds `tenant secrets: <n>` and
  `data protection keys: <m>`, never a value. It is written after every value has decrypted and
  before the first update.

## Platform audit records

- **Table.** `platform.audit_records` is append-only. The database rejects every update, delete and
  truncate with SQLSTATE `23001`, as it does for the audit table of a tenant database. The table is
  built. `PlatformAuditRecords.WriteAsync` in `Axis.Administration` writes one record.
- **What writes it.** Every `Axis.Admin` command that acts on a tenant: `tenant create`,
  `tenant suspend`, `tenant resume`, `tenant rotate-passwords`, `tenant oidc set`, `tenant backup`,
  `tenant restore`, `migrate` and `activate` are built. So is `tenant session end`. `tenant backup` writes one because an
  archive holds a tenant's data. `tenant binding set`, `tenant binding remove`, `tenant host add`,
  `tenant host remove` and `tenant host move` are built and write one each. So are `tenant email set`
  and `tenant email clear`, which name the [mail settings](#mail-settings) they changed. `platform rekey` is
  built and writes one record for the whole run, with no tenant. `tenant list`, `tenant binding list`, `version` and `publisher`
  write none. The dispatcher of `Axis.Admin` reads a table of commands, and each is marked as
  writing one record per tenant, one per run or none. A command without a mark fails a test.
- **When.** One record per tenant, when tenant work starts: after the checks of the command and
  before its first change for that tenant. `platform rekey` writes its one record after every value
  has decrypted and before the first update, and `changed` holds the counts. A usage error, a missing setting, an unknown tenant or a
  host that another tenant holds writes none, because nothing started. A record means the command
  started work for the tenant, not that the work succeeded. The `changed` names of these commands
  are empty, because the command already says what changed. `tenant rotate-passwords`,
  `tenant oidc set` and `tenant session end` are the exceptions. The record of `tenant rotate-passwords` names both roles,
  `axis_<id>_runtime` and `axis_<id>_schema`, and never a password. The record of `tenant oidc set`
  names the [settings](#sign-in-settings) it was given and never a value. `tenant oidc set` writes
  its record after its checks and before the change. The record of `tenant session end` holds
  `user:<user>` or `all`, and is written after its checks and before the delete.
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
