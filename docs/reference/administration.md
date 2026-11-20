# Administration

This page describes `Axis.Admin`, the command-line host that a platform operator runs to create
tenants, migrate tenant databases, activate releases and run Studio's publish requests, and the
commands that look after a cell. On this page, "database role" always means the PostgreSQL role.

| Commands                                                                                                                               | Described in                                                          |
| -------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| `tenant create`                                                                                                                        | [Tenant create](#tenant-create)                                       |
| `tenant backup`, `tenant restore`                                                                                                      | [Tenant backup and restore](tenant-backup-and-restore.md)             |
| `tenant suspend`, `tenant resume`, `tenant read-only`, `tenant list`                                                                   | [Tenant status commands](tenant-status-commands.md)                   |
| `tenant oidc set`, `tenant sign-in set`                                                                                                | [Sign-in settings](tenant-setting-commands.md#sign-in-settings)       |
| `tenant binding list`, `tenant binding set` and `tenant binding remove`                                                                | [Deployment bindings](tenant-setting-commands.md#deployment-bindings) |
| `tenant host add`, `tenant host remove` and `tenant host move`                                                                         | [Hosts](tenant-setting-commands.md#hosts)                             |
| `tenant email set` and `tenant email clear`                                                                                            | [Mail settings](tenant-setting-commands.md#mail-settings)             |
| `tenant legal set` and `tenant legal clear`                                                                                            | [Legal links](tenant-setting-commands.md#legal-links)                 |
| `tenant session end`                                                                                                                   | [Sessions](tenant-setting-commands.md#sessions)                       |
| `tenant rotate-passwords`                                                                                                              | [Database passwords](tenant-setting-commands.md#database-passwords)   |
| `tenant data-key rotate` and `tenant data-key retire`                                                                                  | [Rotating the keys](data-encryption.md#rotating-the-keys)             |
| `platform rekey`                                                                                                                       | [Installation key](tenant-setting-commands.md#installation-key)       |
| `migrate`, `activate`, `cell backup`, `cell restore-check`, `version`                                                                  | The sections of this page                                             |
| `publisher`                                                                                                                            | [Publisher](publisher.md#publisher)                                   |
| `cell operator invite`, `cell operator client add`                                                                                     | [Cell operator commands](cell-operator-commands.md)                   |

`Axis.Admin` is the only host that writes the [tenant directory](../architecture.md#tenant-directory),
the only one that holds an admin connection to the database server, and the only one that holds the
schema database role of a tenant under `Tenancy:Source=directory`. The [publisher](publisher.md#publisher) is a
command of this host, so it holds that role as well. See [database roles](storage.md#database-roles)
for which host holds which connection. A self-hosted installation has no HTTP API for provisioning.
A cell offers provisioning through its [provisioning API](tenant-lifecycle.md#provisioning-api),
which queues jobs that the publisher runs with the code of these commands. A tenant can also come from the `Tenants` section, where the
server migrates and activates at start, see [startup activation](host-settings.md#startup-activation).

## Settings

`Axis.Admin` reads its settings from the environment, as the other hosts do. Each command reads only
the settings it needs, so a routine command needs no admin role and no secret it does not use.

| Setting                                     | Environment variable                         | Meaning                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| ------------------------------------------- | -------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Admin:ConnectionString`                    | `Admin__ConnectionString`                    | A database role with `CREATEDB` and `CREATEROLE`, not a superuser, on the database server that holds the tenant databases. It creates the tenant databases and roles. Only `tenant create`, `tenant restore`, `tenant rotate-passwords` and `cell restore-check` read it, and `publisher` reads it in a cell for the [create](publisher.md#create-job) and [purge](publisher.md#purge-job) jobs. `migrate` needs no admin role.                                                                                                                                                                                                                                                                                                         |
| `ConnectionStrings:PlatformSchema`          | `ConnectionStrings__PlatformSchema`          | The role that owns the `platform` schema. The tool writes the tenant directory over it.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `Platform:SecretKey`                        | `Platform__SecretKey`                        | The installation key that encrypts the generated passwords and the OIDC client secret, see [platform database](storage.md#platform-database).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| `Platform:PreviousSecretKey`                | `Platform__PreviousSecretKey`                | Optional. The old installation key during a rotation. Every command decrypts with it when `Platform:SecretKey` does not match. `platform rekey` needs it. A malformed value exits with code 2, naming the setting. See [installation key](tenant-setting-commands.md#installation-key).                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `IdentityProvider:Url`                      | `IdentityProvider__Url`                      | The base address of [Axis's identity provider](identity-provider.md), such as `https://idp.example.com/`. `tenant create` of a main tenant, `cell operator invite` and `cell operator client add` read it, and `tenant restore` reads it when it is set. In a cell, `publisher` reads it for the [create](publisher.md#create-job) and [purge](publisher.md#purge-job) jobs. `tenant restore` needs it when the archive's tenant signed in through its own realm, see [tenant restore](tenant-backup-and-restore.md#tenant-restore). It must be an absolute URL, and `https` outside `Development` and `Testing`. A path in it is kept. `cell restore-check` reads and requires it, because its scratch tenant always gets a new realm. |
| `IdentityProvider:AdminClientId`            | `IdentityProvider__AdminClientId`            | The client of the identity provider's `master` realm that `tenant create` signs in as, with the client credentials grant, to create a tenant's realm. `tenant create` of a main tenant, `cell operator invite` and `cell operator client add` read it, and so do `tenant restore` when it is set and `cell restore-check`. In a cell, `publisher` reads it for the [create](publisher.md#create-job) and [purge](publisher.md#purge-job) jobs.                                                                                                                                                                                                                                                                                          |
| `IdentityProvider:AdminClientSecret`        | `IdentityProvider__AdminClientSecret`        | The secret of that client. `tenant create` of a main tenant, `cell operator invite` and `cell operator client add` read it, and so do `tenant restore` when `IdentityProvider:Url` is set and `cell restore-check`. In a cell, `publisher` reads it for the [create](publisher.md#create-job) and [purge](publisher.md#purge-job) jobs. It is never stored and never printed.                                                                                                                                                                                                                                                                                                                                                           |
| `IdentityProvider:DatabaseConnectionString` | `IdentityProvider__DatabaseConnectionString` | Optional. A connection string to the identity provider's database, for a role that is read-only and has `SELECT` on Keycloak's `credential` table and no other right. Only `tenant backup` and `cell backup` read it, to put the password hashes and authenticator secrets of a realm's users into `realm.json`, see [tenant backup](tenant-backup-and-restore.md#tenant-backup). A `tenant restore` into a new realm imports that file and needs no such setting. It is never printed. Without it, no archive holds a `realm.json`. A value that is not a connection string exits with code 2, naming the setting.                                                                                                                     |
| `Email:*`                                   | `Email__...`                                 | The host's mail settings, with the same keys as the worker's, see [mail settings](notifications.md#mail-settings). A tenant without mail settings of its own gets them in its realm's mail server, see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). `tenant create`, `tenant restore`, `activate`, `publisher`, `cell operator invite`, `cell restore-check` and `tenant email set` and `clear` read them. A value that is not valid exits with code 2, naming the setting. Without them, a tenant that has no mail settings of its own gets no mail server in its realm.                                                                                                                               |
| `Tenants`                                   | `Tenants__<id>__...`                         | The tenants of `Tenancy:Source=configuration`, with each `SchemaConnectionString` and `ConnectionString` and its bindings. Only `publisher` reads them, and only under that source, see [publisher](publisher.md#publisher).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `DOTNET_ENVIRONMENT`                        | `DOTNET_ENVIRONMENT`                         | `Development` lets a `url` binding use `http` and a `secret` binding take its value from a configuration file, as on the server and the worker. Only `publisher` under the configuration source reads that. Every command reads it for the TLS check: outside `Development` and `Testing`, a command exits with code 2, naming the setting, when `Admin:ConnectionString`, `ConnectionStrings:PlatformSchema`, `ConnectionStrings:Platform`, `Scenarios:ServerConnectionString` or `IdentityProvider:DatabaseConnectionString` is set with an `SSL Mode` below `VerifyFull` or none. An unset value is `Production`.                                                                                                                    |
| `Scenarios:ServerConnectionString`          | `Scenarios__ServerConnectionString`          | A database role with `CREATEDB`, not a superuser, that owns no tenant database. `publisher` reads it, to run `test` requests and the contract scenarios of `publish` and `validate` requests. `cell restore-check` reads it, to run the test scenarios of a restored tenant's Studio applications, see [cell restore check](#cell-restore-check). It creates and drops the `axis_scenario_<uuid>` databases of the scenarios. Empty means a `test` request, or a `publish` or `validate` with contract scenarios to run, fails with `AXC0217`, which names this setting. A value that is not a connection string exits with code 2. See [database roles](storage.md#database-roles).                                                    |
| `Extensions:Path`                           | `Extensions__Path`                           | The installed extension packages. Only `activate`, `publisher`, `tenant restore` and `cell restore-check` read it.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `Modules:Path`                              | `Modules__Path`                              | The installed modules. Only `activate`, `publisher` and `cell restore-check` read it. `tenant restore` never reads it, because a missing module refuses nothing.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `Tenancy:Source`                            | `Tenancy__Source`                            | `directory` for the tenant setting commands, which exit with code 2 under any other value. `publisher` reads it too, see [publisher](publisher.md#publisher).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| none                                        | `AXIS_BINDING_SECRET`                        | The value of the `secret` binding that `tenant binding set --secret` sets. When it is unset, the value comes from standard input. It is never a command-line option. Only that command reads it.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| none                                        | `AXIS_SMTP_PASSWORD`                         | The SMTP password that `tenant email set --password` stores for the tenant. When it is unset, the password comes from the first line of standard input. It is never a command-line option. Only that command reads it.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| none                                        | `AXIS_OIDC_CLIENT_SECRET`                    | The OIDC client secret of a restored tenant that keeps the archive's sign-in. `tenant restore` reads it and needs it only then, because a new realm generates its own client secrets. `tenant create` does not read it. It is never a command-line option, so it does not appear in a process list.                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| none                                        | `AXIS_OIDC_CLIENT_KEY`                       | The client key as a PEM that `tenant oidc set --client-key` adds, read from standard input when the variable is unset. See [sign-in settings](tenant-setting-commands.md#sign-in-settings).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| none                                        | `PATH`                                       | `tenant backup` and `tenant restore` run `pg_dump` and `pg_restore`, so both must be on `PATH`. Their version must match the database server. A missing tool exits with code 2.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |

A missing or invalid setting exits with code 2 before any tenant work. The message names the setting
and never holds its value. The commands of the other pages read these:

- **`tenant suspend`, `tenant resume`, `tenant read-only` and `tenant list`** read only
  `ConnectionStrings:PlatformSchema`. They decrypt nothing and create no role or database, so they
  need none of `Platform:SecretKey`, `Admin:ConnectionString`, the `IdentityProvider` settings or
  `AXIS_OIDC_CLIENT_SECRET`.
- **`platform rekey`** reads only `ConnectionStrings:PlatformSchema`, `Platform:SecretKey` and
  `Platform:PreviousSecretKey`. It needs no admin role, no client secret and no `Tenancy:Source`.
- **`tenant binding`, `tenant host` and `tenant email`** read `ConnectionStrings:PlatformSchema` and
  need `Tenancy:Source=directory`. Only `tenant binding set --secret`, the three `tenant host`
  commands and the two `tenant email` commands also read `Platform:SecretKey`: the first encrypts the
  value, and the others decrypt the realm's activation secret to write the realm. See
  [tenant setting commands](tenant-setting-commands.md).
- **`tenant session end`, `tenant data-key rotate` and `tenant data-key retire`** read
  `ConnectionStrings:PlatformSchema` and `Platform:SecretKey`, because they connect to the tenant
  database as the schema role, and need `Tenancy:Source=directory` and no admin role. The data key
  commands read and write the tenant's keys there.
- **`tenant backup`** reads only `ConnectionStrings:PlatformSchema`, `Platform:SecretKey` and `PATH`,
  in whose directories it looks for `pg_dump`. It creates no role or database, so it needs neither
  `Admin:ConnectionString` nor `AXIS_OIDC_CLIENT_SECRET`.
- **`tenant restore`** reads the settings of `tenant create`, `AXIS_OIDC_CLIENT_SECRET`, `PATH` to
  find `pg_restore`, and `Extensions:Path` to check the packages that the archive requires. It needs
  `Extensions:Path` only when the archive requires a package, and `AXIS_OIDC_CLIENT_SECRET` only when
  the restored tenant keeps the archive's sign-in. It reads the `IdentityProvider` settings as
  optional: it needs them only when the archive's tenant signed in through its own realm, and a value
  that is given must still be valid. It reads the OIDC authority and client id from the archive, so
  it takes no `--oidc-*` option.

A new tenant's database goes on the same database server as `Admin:ConnectionString`, and the
directory records that server's host and port. Axis supports a single database server.

## Output and exit codes

Every command reports in the same form, so a script can follow each tenant and tell "some tenants
failed" from "nothing ran". Each command writes one line per tenant and step to standard output:

```text
<tenant> <step> ok
<tenant> <step> failed: <message>
```

Each command's section on this page, or the page that it links to, names that command's steps. An
`audit` step prints only when it fails, see [platform audit records](tenant-setting-commands.md#platform-audit-records).
The steps of the [publisher](publisher.md#publisher) are in its section, and those of its [create](publisher.md#create-job),
[export](publisher.md#export-job) and [purge](publisher.md#purge-job) jobs are in theirs.

A line, and a `failed` message, names the tenant and never a database host, a password or a
connection string. A restore refusal does name the tenant host the operator gave on the command line,
as typed. A failed `dump` step says only `pg_dump exited with code <n>` and never repeats the text
that `pg_dump` wrote, because that text can name the database host; to see the cause, run `pg_dump` by
hand. A command that does no tenant work, such as a usage error, writes its message to standard
error.

| Exit code | Meaning                                                                                                                                           |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `0`       | Every tenant succeeded.                                                                                                                           |
| `1`       | At least one tenant failed. The other tenants still ran.                                                                                          |
| `2`       | A usage or settings error before any tenant work: an unknown option, a missing setting, a superuser admin role or an unknown tenant. Nothing ran. |

## Tenant create

`tenant create` creates a tenant: its directory entry, its realm, its database roles and its
database. It has two forms:

- `tenant create <id> --host <host>... [--administrator <email>]` creates a main tenant and its realm.
- `tenant create <id> --preview-of <main> --host <host>... [--binding <name>=<value>]...` creates a
  [preview tenant](../architecture.md#preview-tenants) of a main tenant.

| Option                     | Meaning                                                                                                                                                                                                                                                                                                                                                            |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `<id>`                     | The tenant id. It follows the rule of the `Tenants` section: lowercase letters, digits and hyphens, at most 50 characters. It is also the name of the tenant's realm, so `master`, the identity provider's own admin realm, is refused. The id of a deleted tenant, one with a row in `platform.deleted_tenants`, is refused with exit code 2 at the `entry` step. |
| `--host <host>`            | A host of the tenant. Repeat it for more hosts. At least one is needed. A host that a tenant deleted less than 90 days ago had is refused with exit code 2, see [deleting a tenant](tenant-lifecycle.md#deleting-a-tenant).                                                                                                                                        |
| `--administrator <email>`  | Invites the person with this email as the tenant's first administrator, see [the first administrator](user-administration.md#the-first-administrator). Give it once. The email must be a plain address, such as `name@example.com`. It is a usage error together with `--preview-of`.                                                                              |
| `--preview-of <main>`      | Makes the tenant the preview tenant of the main tenant `<main>`.                                                                                                                                                                                                                                                                                                   |
| `--binding <name>=<value>` | A `url` binding of the new tenant. Repeat it for more. The value is an absolute `http` or `https` URL. It is accepted only together with `--preview-of`.                                                                                                                                                                                                           |

**Realm.** The command creates the tenant's realm in
[Axis's identity provider](identity-provider.md#keeping-the-realm-in-step), with its four clients,
and sets the tenant's `Oidc` block to the realm itself: the authority
`<IdentityProvider:Url>/realms/<id>` and the client `axis`. The admin host's `IdentityProvider`
settings give it the identity provider's admin client. The command takes no `--oidc-*` option, and
any one is a usage error, with or without `--preview-of`. It does not read
`AXIS_OIDC_CLIENT_SECRET`. It sets the platform client's redirect addresses from the hosts it is
given, so a person can finish a sign-in on each of them, and `tenant host add`, `remove` and `move`
keep those addresses, and the audiences of the realm's scope `axis:authoring`, in step. It also turns accounts on in the tenant's
[platform sign-in](identity-provider.md#platform-sign-in). With `--administrator <email>`, the realm
gets one invited account, see [the first administrator](user-administration.md#the-first-administrator).
Its emailed link names no address to return to, so the person sets a password and an authenticator
and then signs in on one of the tenant's hosts.

**Preview tenant.** With `--preview-of`, the steps below stay the same, and the entry also names its
main tenant.

- It gets no realm and has no `realm` step. It needs none of the `IdentityProvider` settings,
  because the preview uses its main tenant's sign-in settings.
- `--binding` without `--preview-of` is a usage error. A binding that is not `<name>=<value>`, or
  whose value is not an absolute `http` or `https` URL, is a usage error, and so is a binding name
  that repeats, ignoring letter case. No binding value is printed.
- Without `--binding` the preview has no bindings, see
  [preview tenants](host-settings.md#preview-tenants).
- The command exits `2` when `<main>` is not in the directory, is itself a preview tenant, already has
  a preview tenant, or is the tenant itself, and the directory stays unchanged. Two operators who
  create a preview of one main tenant at the same moment cannot both succeed: the second also exits
  `2`.
- The entry stores no sign-in settings of its own. The server reads them from the main tenant at
  every refresh, so a change to the main tenant's settings reaches the preview, and the client secret
  is stored only once. The preview never takes the main tenant's endpoint audience or service
  audience, so it accepts no machine-caller token. The preview uses the main tenant's settings
  whatever the main tenant's status.
- A rerun on a `provisioning` preview replaces its main tenant and its bindings, as it replaces its
  hosts.

**Steps, in order.** Each step writes its own line.

1. `entry`: validate the id and check that the hosts are unique across all tenants, ignoring letter
   case. Write the directory entry with status `provisioning` and, in the same transaction, the
   tenant's data key 1 and lookup key 1, see [tenant keys](data-encryption.md#tenant-keys).
2. `realm`: generate the secrets of the realm's four clients, store them encrypted in the directory
   and point the entry's `Oidc` block at the realm. Then create the tenant's realm in the identity
   provider, or bring an existing one up to date. A preview tenant has no such step.
3. `roles`: create `axis_<id>_schema` and `axis_<id>_runtime`. Each gets a 32-byte random password,
   stored encrypted in the directory.
4. `database`: create `axis_<id>`, owned by the schema role.
5. `grants`: run the grant step.
6. `migrations`: run the configuration, data and processes EF Core migrations.
7. `grants`: run the grant step again. Steps 5 to 7 run as the schema role.
8. `role-check`: run the runtime role check over the runtime role, see
   [database roles](storage.md#database-roles).
9. `version`: record the schema version and, without `--administrator`, set the entry `active`.
10. `administrator`: only with `--administrator`. Create the invited account, give it the
    `administrator` platform role, write the `user.invited` and `user.roleGiven` records, have the
    realm email the link and set the entry `active`.

The server serves a new tenant, with its sign-in, and the worker runs its work, after their next
refresh, see [tenant directory](../architecture.md#tenant-directory). A new tenant has no application
until `activate` has run for it.

**Settings and the platform tables.** The command checks its settings before any tenant work. It
refuses an admin role that is a superuser, or a member of one, and an admin role without `CREATEDB`
and `CREATEROLE`. It needs the `platform` tables to exist, because it does not create them: start the
server once under `Tenancy:Source=directory`, which creates them. Without the tables it exits with
code 2.

**What the steps do.**

- `realm` saves four random secrets, each 32 bytes written as 64 lowercase hex characters,
  encrypted, before it calls the identity provider: the secret of the `axis` client, which is the
  entry's `OidcClientSecret`, and those of `axis-activation`, `axis-users` and `axis-agent`. It saves the entry's
  `Oidc` authority and client id `axis` at the same time, so a stop between the save and the call
  never loses a secret. The realm's name and its display name are the tenant id. The step signs in to
  the `master` realm with the admin client, creates the realm with sessions of at most 8 hours, `en`
  and `vi` and the four clients, and gives the activation client `manage-realm` among its rights,
  and the realm its mail server, see
  [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). A failure names the
  step of the identity provider and its status code, never a response body or a secret. After a
  server error (5xx) the step reads the realm again and repeats, up to three attempts, before it
  fails.
- `roles` saves each generated password, encrypted, before it creates the role. It never alters a
  role that exists. A role that exists with no password stored for this tenant fails the step.
- `database` first gives the admin role `SET` membership, without `INHERIT`, in the schema role,
  because PostgreSQL 16 and later need this before a role that is not a superuser can create a
  database owned by another role. The admin role already holds `ADMIN OPTION` on the roles it
  creates, so this adds no power. An existing database must already be owned by the schema role.
- `grants` first revokes `CONNECT` and `TEMPORARY` on the database from `PUBLIC`, and grants both only
  to the runtime role. Without this, any login role of the database server could connect to the
  tenant database.
- The command sets no claim names, endpoint audience or service audience. They stay empty, so the
  hosts use their defaults, and [`tenant oidc set`](tenant-setting-commands.md#sign-in-settings) sets
  the audiences later. It sets no deployment binding either, except the `url` bindings that
  `--binding` gives a preview tenant.

**Names.** The role and database names come from the id. PostgreSQL names hold at most 63 bytes, and
`axis_<id>_runtime` adds 13 characters, hence the limit of 50. An id with a hyphen, such as
`acme-eu`, gives the quoted names `"axis_acme-eu_schema"`, `"axis_acme-eu_runtime"` and
`"axis_acme-eu"`. Quote them in any SQL you run by hand.

**Running it again.**

- On a `provisioning` entry, it skips every step already done and finishes the rest. The `roles` step
  does not generate new passwords for roles that exist. The `entry` step replaces the entry's hosts,
  main tenant and bindings with the new arguments, so an operator can fix a typo before the tenant
  goes live, which is safe because a `provisioning` tenant is never served. It never changes the
  stored passwords or tenant keys, and the `realm` step never changes the stored client secrets. It
  updates the realm that it made.
- A stop, such as Ctrl+C, writes a `failed` line for the current step and exits with code 1. The
  entry stays `provisioning`, and a rerun finishes it.
- With `--administrator`, the entry stays `provisioning` until the invitation has gone out, so a rerun
  retries a failed `administrator` step. The rerun finds the account by its email, so there is never a
  second account or a second `user.invited` record. It sends the email again only when the account
  has no password yet.
- On an `active` or `suspended` tenant, it fails with exit code 1 and changes nothing.

**Exit codes.** `0` when the tenant is `active`. `1` when a step failed, and the entry stays
`provisioning`. `2` for:

- an invalid id, the id `master`, a missing option or an `--oidc-*` option;
- a missing or invalid `IdentityProvider` setting of a main tenant, a superuser admin role or missing
  `platform` tables;
- a host that another tenant holds, written as an `entry failed` line that names no host, with no
  entry, role or database created;
- an `--administrator` that is not a valid email address, that repeats or that comes with
  `--preview-of`;
- for a preview tenant, a usage error of `--preview-of` or `--binding`, and a main tenant that is
  unknown, is itself a preview tenant, already has a preview tenant or is the tenant itself.

## Tenant backup and restore

`tenant backup` and `tenant restore` have their own page:
[tenant backup and restore](tenant-backup-and-restore.md).

## Migrate

`migrate [--tenant <id>] [--parallel <n>]` brings tenant databases to the schema that this build of
`Axis.Admin` ships. An upgrade runs it before the new hosts start.

| Option           | Meaning                                                                       |
| ---------------- | ----------------------------------------------------------------------------- |
| `--tenant <id>`  | Migrate this tenant only. Without it, migrate every tenant.                   |
| `--parallel <n>` | Migrate up to `n` tenants at a time. It defaults to 1 and must be at least 1. |

It covers every tenant except `provisioning` ones, in ordinal order of the id, `n` at a time. A
`suspended` tenant is migrated too, so it is current when it is resumed.

**Platform migrations first.** Before it reads the tenants, `migrate` applies the pending EF Core
migrations of the `platform` schema over `ConnectionStrings:PlatformSchema`, so an installation that
upgrades with `migrate` before the new server starts has the new platform tables in time. It does not
run the grants for the reader role, which the server applies at its next start. A failed platform
migration exits `2`, with the message of the setting that could not be used.

**Steps for each tenant.**

0. `audit`: write the [platform audit record](tenant-setting-commands.md#platform-audit-records) of
   the tenant. It prints a line only when it fails, and that tenant then stops.
1. `grants`: run the grant step.
2. `migrations`: run the configuration, data and processes EF Core migrations.
3. `grants`: run the grant step again.
4. `role-check`: run the runtime role check, see [database roles](storage.md#database-roles).
5. `version`: record the schema version in the directory, and make any missing data key 1 or lookup
   key 1.

Steps 1 to 3 run as the tenant's schema role and step 4 as its runtime role. Step 5 writes over
`ConnectionStrings:PlatformSchema`. `migrate` reaches each tenant database on the host and port that
the directory records for it. It reads only `ConnectionStrings:PlatformSchema` and
`Platform:SecretKey`, and not `Admin:ConnectionString` or `AXIS_OIDC_CLIENT_SECRET`, so a routine
upgrade needs no admin role. A tenant stops at its first failed step. Each step writes one line, and
with `--parallel` the lines of different tenants can interleave.

**No platform tables.** When the `platform` tables do not exist, there is no tenant to migrate.
`migrate` writes a note to standard error and exits `0`, so the install can run it before the first
server start, see [installation](installation.md).

**Schema version.** A tenant's schema version is the id of the newest EF Core migration across the
configuration, data and processes contexts. It is stored as text in the `schema_version` column of
`platform.tenants`, and is empty until the first migration. Migration ids sort by time, so the newest
one names the schema. It comes from the code, so no one raises a number by hand.

**Failures.** Each tenant migrates on its own. A failed tenant keeps its old schema version, writes a
`failed` line and does not stop the others, and the command exits `1` at the end. Running it again
retries the failed tenants, and the migrations that already ran do nothing.

**Upgrade order.** A host serves a tenant only when the tenant's schema version is the host's own,
see [tenant directory](../architecture.md#tenant-directory). So an upgrade is:

1. Run `migrate`.
2. Deploy the new server and worker.

Between the two, every migrated tenant answers `503`, because its schema version is not that of the
old hosts, and the worker claims none of its work. The window ends when the new hosts run. A tenant
whose migration failed stays at its old version, so the old hosts keep serving it, and the new hosts
answer `503` for it until `migrate` works for it.

**Exit codes.** `0` when every tenant succeeded, also when there is none or when the `platform`
tables do not exist. `1` when at least one tenant failed. `2` for an unknown `--tenant`, a `--tenant`
that is `provisioning`, a `--parallel` below 1 or a missing setting.

## Activate

`activate <folder> [--tenant <id>]` compiles an application folder and activates its release. A
running server serves the new release at its next request, with no restart.

| Option          | Meaning                                                                                                           |
| --------------- | ----------------------------------------------------------------------------------------------------------------- |
| `<folder>`      | The application folder. A relative path is resolved against the working directory.                                |
| `--tenant <id>` | Activate for this tenant only. It may be `active` or `suspended`. Without it, activate for every `active` tenant. |

It uses the same compile diagnostics, extension packages, modules, recompute limit and seeding as
[startup activation](host-settings.md#startup-activation).

- **Settings.** Packages come from `Extensions:Path` and modules from `Modules:Path`. An unset path
  gives an empty catalog, as on the server, and a set path whose folder does not exist is a settings
  error that names the setting and not the path. A problem of an installed package or module is
  written to standard error. `Activation:InlineRecomputeLimit` applies as it does at startup, and a
  value that is not a positive whole number is a settings error. `Data:ActivationTimeout` limits each
  statement of a tenant's activation transaction, and a value that is not a duration of at least one
  millisecond is a settings error.
- **Bindings.** The tool always refuses an `http` URL binding, because `http` is allowed only in
  Development, which uses startup activation. On a
  [preview tenant](../architecture.md#preview-tenants), a missing binding is an `AXC0100` warning and
  not an error, see [preview tenants](host-settings.md#preview-tenants).

**Steps for each tenant.**

1. `version`: check that the tenant's schema version is the one of this build. If it is not, the
   tenant fails with a message to run `migrate` first, and nothing else runs for it.
2. `compile`: compile the folder against the tenant database and store its release.
3. `activate`: activate the release. This adds a row with the actor `operator` to the
   [release history](publish-request-storage.md#release-history), unless the release is already the active one.
   After the activation has committed, the step brings the tenant's realm in step with the tenant's
   applications: it adds, changes and removes the realm's `site-` clients and `oidc-` and `saml-`
   providers, and writes the realm's mail server from the tenant's mail settings or the host's `Email`
   settings, see [keeping the realm in step](identity-provider.md#keeping-the-realm-in-step). The
   update writes no line of its own, and a tenant without an activation secret, such as a preview
   tenant, skips it. The command reads the identity provider's address and the activation secret from
   the tenant's own entry, so it needs no `IdentityProvider` settings. It reaches the identity
   provider as `tenant create` does, so the admin host must trust its certificate. A failure of the
   identity provider fails the step with a line that names the identity provider step and its status
   code, and never a secret.
4. `seed`: insert the seed records and write the schedule trigger state.
5. `indexes`: build the non-unique indexes that activation left out of a table with more than
   `Activation:InlineRecomputeLimit` rows, with `CREATE INDEX CONCURRENTLY`. An index that PostgreSQL
   marks invalid, for example after a cancelled build, is dropped and built again. The step holds the
   tenant's schema lock while it builds, so other activations of the tenant wait. See
   [schema planning](storage.md#schema-planning).

Steps 2, 3 and 5 run as the tenant's schema role, and step 4 as its runtime role. The tool runs no EF
Core migrations. Each step writes one `ok` line. Each error diagnostic of a failed step is written as
its own line, `<tenant> <step> failed: <code> <file> <path>: <message>`, so one run shows every
problem. The warnings of a step that succeeds go to standard error, as
`<tenant> warning <code> <file> <path>: <message>`. A line never names a host, a password or a
connection string.

**Failures.** Each tenant succeeds or fails on its own, and the other tenants continue, so tenants
can run different releases until `activate` works for all of them. The command exits `1` at the end.
Only a `compile` or `activate` failure keeps the previously active release. A failure of the realm
update, of `seed` or of `indexes` happens after activation has committed, so the new release stays
active, as it does with startup activation. After a failed realm update, `seed` and `indexes` do not
run until `activate` is run again, which updates the realm again and then runs them, and the next
activation builds a missing index again.

**Exit codes.** `0` when every tenant succeeded, also when none is `active`. `1` when at least one
tenant failed. `2` for a folder that does not exist, an unknown `--tenant`, a `--tenant` that is
`provisioning`, a bad option or a missing or bad setting.
## What else to back up

A tenant archive is not a backup of the installation. The operator also keeps these safe:

- **The platform database.** It holds the tenant directory. Back it up with standard PostgreSQL
  tools, such as `pg_dump` or continuous archiving. Axis does not back it up.
- **The installation key.** `Platform:SecretKey` decrypts every stored secret. Without it, no
  database password, OIDC client secret or `secret` binding value can be read, and every one must be
  set again by hand. Keep it apart from the backups, so a stolen backup does not hold its own key.
  See [platform database](storage.md#platform-database).
- **The backup key.** The file that `--backup-key-file` names decrypts every archive made with it.
  Keep it outside the installation and apart from the archives, so a stolen archive is useless. Keep
  it apart from the installation key too, because the tool cannot tell whether the two are the same.
  Without the backup key an archive cannot be restored, and nothing in Axis can recover it.
- **Installed packages and modules.** The folders `Extensions:Path` and `Modules:Path` hold the
  installed extension packages and modules. A restore needs the package versions that the archive
  marks `required`.

Point-in-time recovery of one tenant is out of scope, see [out of scope](#out-of-scope).

## Platform rekey

`platform rekey` re-encrypts every tenant secret and every stored data protection key with
`Platform:SecretKey`, after the installation key changed. It works under both tenancy sources, takes
no other argument and writes one platform audit record with no tenant. It prints one line,
`platform rekey ok: <n> tenant secrets, <m> data protection keys`. The steps, the exit codes and the
locks are in [installation key](tenant-setting-commands.md#installation-key).

## Cell backup

`cell backup` is the daily backup of a cell: it backs up every tenant once and uploads each archive to
object storage. It takes no argument and runs only in a cell, where `Cell:Id` is set. What it backs
up, its output, its audit records and its metric are in
[daily backup](tenant-backup-and-restore.md#daily-backup).

- **Settings.** `ConnectionStrings:PlatformSchema`, `Platform:SecretKey`, `Cell:BackupKeyFile`, the
  five `Cell:BackupStorage` settings, `IdentityProvider:DatabaseConnectionString`, which puts
  `realm.json` into the archive of a tenant with its own realm, and `PATH`, to find `pg_dump`. The
  chart sets `IdentityProvider:DatabaseConnectionString` for its CronJob from a Secret and requires it.
  Without the setting a run writes archives with no `realm.json`. It needs no `Admin:ConnectionString`.
- **Exit codes.** `0` when every tenant succeeded. `1` when a tenant failed, the run reached its
  6-hour limit or the gauge could not be recorded. `2`, before anything is uploaded or audited,
  outside a cell, with an argument, for a missing setting or `pg_dump`, or for an OTLP endpoint
  setting that is not an absolute `https` URL outside Development.

## Cell restore check

`cell restore-check` checks once that a recent archive restores: it restores the newest archive of a
random tenant into a scratch tenant, checks it and removes it. It takes no argument and runs only in
a cell, where `Cell:Id` is set. Its steps, output, audit record, stored result and metric are in
[weekly restore check](tenant-backup-and-restore.md#weekly-restore-check), and the weekly job is the
chart's [cell restore-check CronJob](installation.md#settings-and-secrets).

- **Settings.** `ConnectionStrings:PlatformSchema`, `Platform:SecretKey`, `Admin:ConnectionString`,
  `Cell:BackupKeyFile`, `Cell:InternalDomain`, the five `Cell:BackupStorage` settings, the
  `IdentityProvider` settings, `IdentityProvider:Url` included, the `Email` settings, `Studio:ApplicationsPath` when an
  archive holds Studio files, `Scenarios:ServerConnectionString`, which a tenant with a Studio
  application needs, `Extensions:Path` and `Modules:Path`, and `PATH`, to find `pg_restore`.
- **Exit codes.** `0` when the check passed. `1` when any step after the pick failed, the result could
  not be stored or no tenant has an archive of today or yesterday. `2`, with nothing changed and no audit record written, outside a
  cell, with an argument, for a missing setting or `pg_restore`, for a
  `Scenarios:ServerConnectionString` that is not a connection string or an `Extensions:Path` or
  `Modules:Path` folder that does not exist, or for an OTLP endpoint setting that is not an absolute
  `https` URL outside Development.

## Version

`version` prints the Axis version on one line. It takes no option, needs no settings and always exits
`0`. The production image of the admin host carries the same version in its OCI label, see
[installation](installation.md#images).

## Out of scope

These operations are outside what `Axis.Admin` does. An operator who needs one uses the database
server's own tools.

- Moving a tenant to another database server. Axis supports a single database server.
- Point-in-time recovery of one tenant. A cell keeps point-in-time recovery of its whole cluster, see
  [backups in a cell](tenant-backup-and-restore.md#backups-in-a-cell). An archive does restore into
  another installation, where the tenant gets a new realm, see
  [the new realm](tenant-backup-and-restore.md#the-new-realm).
- Overwriting a tenant in place with a restore.
