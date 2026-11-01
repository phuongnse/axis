# Data encryption

Axis encrypts tenant backup archives, and _(planned for M27)_ the values of fields marked `sensitive`, as
[D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) and
[D116](../decisions/d116-how-sensitive-fields-and-backups-are-encrypted.md) agree. The keys are in the
[cryptographic inventory](keys-and-data-protection.md#cryptographic-inventory). Secrets in the platform tables are
encrypted as [storage](storage.md#platform-database) describes, and nothing here changes that.

## Sensitive field values

- **Who encrypts.** The server and the worker encrypt a value before they write it and decrypt it after they read
  it. PostgreSQL never sees the plain value, so a database login, a dump or a replica shows only ciphertext. The
  record API, forms and the detail widget show the value to users whose grants cover the field, as for any field.
- **Column.** The field's column is `bytea`, whatever the field's type. A value is `null` when the field is empty.
- **Value format.** A stored value is:
  - the data key's number, a 4-byte big-endian integer;
  - a 12-byte random nonce;
  - the ciphertext;
  - a 16-byte tag.

  The plain text is the UTF-8 bytes of the value's text form in the
  [record API](record-api.md#record-shape), so a date is `2026-11-02` and a decimal keeps its scale. The cipher is
  AES-256-GCM.
- **Associated data.** The UTF-8 bytes of the tenant id, the entity `id`, the record id and the field name in lower
  case, each followed by one zero byte. A value copied to another tenant, entity, record or field does not decrypt.
  A field rename is a [migration](migrations.md#migrations), and it re-encrypts the field's values under the new
  name.
- **Child entities.** A sensitive field of a child entity is bound to the child row's id.
- **What reads it.** A computed field is always stored, and it cannot read a sensitive field. A computed field may be
  sensitive itself. Data sources, a computed field that a read works out on the spot because activation has not
  recomputed it yet, and an aggregate over a `references` field read the decrypted value, as they read any field. So do
  rules and process expressions. Process inspection still hides it, see
  [sensitive fields](roles-and-policies.md#sensitive-fields).

## Exact lookup

A sensitive field may declare an exact lookup, so a filter can find a record by its exact value:

```json
{ "name": "nationalId", "type": "text", "maxLength": 20, "sensitive": true, "exactLookup": true }
```

- Axis stores a lookup hash in a second `bytea` column, named after the field's column with the suffix `_lookup`,
  with an index. The hash is the lookup key's number, a 4-byte big-endian integer, then the 32-byte HMAC-SHA256 of
  the value's plain text under that key. The HMAC's message starts with the same associated data as the value,
  without the record id, so equal values in two fields hash differently.
- A write uses the newest lookup key. Every write that encrypts the value stores the hash in the same statement: the
  record API, child rows, imports, seeds, process steps and the computed recompute. A null value stores `null`. A
  tenant with no lookup key refuses a value like one with no data key, and nothing is written in clear.
- The lookup column, its index and the hash on every write are built. So is the filter on the hash for a policy
  grant's `filter`, a data source `filter` and a measure's `filter`. A name longer than 54 characters is `AXC0309`, because `f_`, the name and `_lookup` must fit
  PostgreSQL's 63 bytes.
- A policy grant's `filter` may test the field with `==` or `!=` against a text, integer, decimal or boolean value,
  in either order, and nothing else. Axis hashes the value under every lookup key the tenant holds, as far as the
  server has read them, and matches any of the hashes. A server that read the keys before a rotation can miss a record that the
  rotation re-hashed in an exact lookup search until it reads a value under the new key or restarts, and a search that reads
  the keys again _(planned for M27)_ closes that window, see [rotating the keys](#rotating-the-keys). `!=` also holds for a record whose field is empty.
  The record list and its export, the data source rows and their export, process inspection and every
  single-record check apply the grant this way. A filter that reaches Axis without the tenant's keys throws, and
  never compares plain text. A tenant with no lookup key has no hashes, so `==` matches no record and `!=` matches
  every record.
- A data source `filter` and a measure's `filter` may test the field with `==` or `!=` against a text, integer,
  decimal or boolean literal, or against a data source parameter of one of those types or an enum, in either order,
  and nothing else. A date or date-time parameter is `AXC0308`. Axis hashes the literal, or the parameter's value,
  under every lookup key the tenant holds, as for a grant. A parameter that was not given is `null` in the `filter`,
  so `==` matches the records whose field is empty and `!=` matches the records whose field has a value. The rows,
  their export and the trigger fan-out of a schedule trigger apply the `filter` this way, and a read that reaches it
  without the tenant's keys throws. A caller's filter may test the field the same way _(planned for M27)_.
- Two records with the same value have the same hash, so whoever reads the table can see that they match. That is
  the cost of the lookup.

## What a sensitive field cannot do

Compilation refuses each of these on a sensitive field with [`AXC0308`](diagnostic-codes.md), at the path of the use.
The message names the field and the use. The refusals and the run-time `400` for `sort` below are built, and hold
before the values are encrypted. The exact lookup exception is built for a policy grant's `filter`, a data source
`filter` and a measure's `filter`.

- A filter, other than the equality of an exact lookup in a policy grant's, data source's or measure's `filter`.
- A sort, in a data source or a table.
- An aggregate over it, and a data source column that groups by it.
- An index, `unique` or `searchable`.
- A reference, references or file field. These cannot be `sensitive` at all.
- The entity's `displayField`, which shows as a record's label in lists and links.
- A stored computed field that reads it.
- A migration `fill` or `convert` whose target is a sensitive field, at `/changes/{i}/field`. Its SQL would write
  plain text into the encrypted column.
- An expression that runs as SQL, such as a data source `filter` or a policy grant's `filter`, other than the
  equality of an exact lookup in a policy grant's, data source's or measure's `filter`. A migration `fill` or `convert` value is one, so it may not read a sensitive field.

`exactLookup` on a field that is not `sensitive` is [`AXC0309`](diagnostic-codes.md) at `/fields/{i}/exactLookup`. A
caller's `sort` on a sensitive field, in the record API or a data source, is a `400` problem keyed `sort`. The record
API has no other filter, and a data source's parameters only reach its `filter`, which tests an exact lookup field
against a parameter by its hash and cannot read a sensitive field.
Import and export read and write the plain values, under the same grants as
the record API.

## Changing the flag

A sensitive field's column is `bytea`, in a new table and in a new column, whatever the field's type. Adding or removing
`sensitive` on a field that already has a column is a schema change that activation applies in its transaction, as
[schema planning](storage.md#schema-planning) describes for other changes:

- **Marked `sensitive`.** Activation adds a `bytea` column, writes each row's encrypted value into it, drops the
  plain column and renames the new one. A field index or unique constraint goes with the plain column. The new column
  is not unique, so a field that is also `unique` is `AXC0016` at `/fields/{i}/unique`.
- **No longer `sensitive`.** Activation does the same the other way, and decrypts each value into a column of the
  field's type.
- **Renamed.** A `renameField` migration of a sensitive field encrypts each value again under the new field name,
  because the field name is part of the associated data.
- **Exact lookup.** Adding `exactLookup` to a sensitive field that has a column adds the `_lookup` column and its
  index, and a marked step that fills it. The step decrypts each stored value in C# and writes its hash under the newest
  lookup key, in batches. A field that becomes `sensitive` and `exactLookup` in one release is encrypted first and then
  hashed, in the same activation. Removing `exactLookup` drops the `_lookup` column, and its index with it. A
  `renameField` of an exact lookup field renames the `_lookup` column and its index and hashes each value again under
  the new name, because the field name is part of the hash. A `dropField` of an exact lookup field drops its `_lookup`
  column too.
- **Keys.** The planner does not see keys or values. It plans the new column, a marked step that fills it, the drop and
  the rename, and activation runs the marked step in the same transaction with the tenant's keys: it reads each row,
  encrypts, decrypts or hashes the value in C# and writes it back in batches. Every new encryption uses the newest data
  key, and every new hash the newest lookup key. PostgreSQL never sees a key or a plain sensitive value.
- **Failure.** A stored value that does not decrypt fails the activation with a
  `SensitiveValueDecryptionException` that names the tenant, entity, field and key number, never a value. A decrypted
  value that does not fit the field's type fails it too. Activation without the tenant's data keys fails when a flag
  change or a sensitive rename needs them, and without the newest lookup key when a step hashes. A plain value is never
  written into an encrypted column. The transaction rolls back, so the table and the active release stay as they were.
- **One change at a time.** A field that becomes `sensitive` and changes its type in the same release is `AXC0016` at
  `/fields/{i}/type`, except that text may widen. The old type behind a `bytea` column is not known, so when the mark
  goes the planner cannot see a type change. The cast of each decrypted value fails the activation when it does not
  fit, and converts a value that does. Change the type in a release of its own.
- **A required field.** A field that becomes `required` during the change follows the rules of
  [schema planning](storage.md#schema-planning): the column must already be `NOT NULL`.
- **Limits.** The rewrite runs inline in the activation transaction with no row limit, so a large table stays locked for
  as long as the encryption takes. Process instances pinned to an older release are not checked, so one that reads the
  field after the change fails with a type error. Values that record history or exports already hold in plain text stay
  plain after a field is marked.

Test: `SensitiveFlagActivationTests` for the flag change, the exact lookup fill and drop, the rename, a value that does
not decrypt and a missing key, and `SchemaPlannerTests` for the planned statements.

Test: `ExactLookupWriteTests` for the hash a create, an update and a child row store, the newest lookup key and the
refusal without one, `SensitiveValuesTests` for the hash, and `SchemaPlannerTests` for the lookup column and index.

## Tenant keys

- **Data keys.** Each tenant has one or more data keys of 32 random bytes, numbered from 1. A write uses the newest
  one. A read uses the one that the value names, so a host that still holds an older list keeps working.
- **Lookup keys.** Each tenant has one or more lookup keys of 32 random bytes, numbered from 1, used the same way:
  a write uses the newest, and a hash names the key that made it.
- **Under the directory source.**
  - `platform.tenant_data_keys` holds the keys, one row per key: the tenant id, the `kind` (`data` or `lookup`), the
    key's `number` and the key, encrypted by the installation key as other secrets are.
  - The field name in the associated data is `data_key:` or `lookup_key:`, followed by the number.
  - The stored text is the key's base64, and the field name makes the row fit its tenant, kind and number only.
  - `platform rekey` re-encrypts them with the other secrets.
- **Under the configuration source.** `Tenants:{id}:DataKeys:{number}` and `Tenants:{id}:LookupKeys:{number}` hold the keys,
  base64-encoded. Development and the tests set them.
- **When they are made.** `tenant create` makes data key 1 and lookup key 1. `migrate` makes them for a tenant that has
  none. `tenant restore` takes them from the archive, with the same numbers, and makes number 1 of each kind for an
  archive that has none.
- **No key, no write.** A host that finds no key for a tenant refuses to write a sensitive value, with a `500`
  problem and an error log line that names the tenant. It never writes a sensitive value in clear. The `500` problem
  and the log line are built for the record API. The other write paths fail closed, see below.
- **Refresh.** Each host reads a tenant's keys when it first needs them, and again when a value names a number it
  does not hold. The server and the worker do this through one key service. It gives the newest key of a kind for a
  write and a key by number for a read, and it never logs a key.

The key store, the settings, `tenant create`, `migrate`, `tenant restore`, the rekey, the refresh, the rotation and the cipher that
encrypts and decrypts a sensitive value under the data keys are built, and so is the `bytea` column of a sensitive field.
The record API, forms, the detail widget, record imports (the dry run and the commit), inbound endpoints, process steps,
connector and package call outputs, the process start endpoint, task completion, the task form state and the operator's call resolve write and read `sensitive` values with the cipher, also in the rows of a child
collection. A row's value is bound to that row's id, so it reads back only in that row. A stored value copied to another
record, row or field does not decrypt, and the read is a `500`. When the tenant has no data key, a record API write of a
sensitive value is a `500` problem, an error log line names the tenant, and no row is stored. A process step that
finds no key fails with the same error and stores nothing in clear. An import by key checks and updates a record that
holds a stored sensitive value, and an inbound endpoint reads the record for its output with the cipher. Test:
`RecordSensitiveValueTests` for the record API, `RecordImportSensitiveValueTests` for imports and inbound endpoints,
`ProcessSensitiveValueTests` for process steps, the process start endpoint and the task form state, and `ConnectorCallSensitiveOutputTests` for connector call outputs.

A data source decrypts the value of a projected sensitive field, on the root entity and through a reference path, in its
rows and its export, for a user whose grants cover the field. A value that does not decrypt is a `500`. Test:
`DataSourceSensitiveValueTests`. A rule or form requirement that aggregates over a `references` field reads the
target's decrypted values, and the server reads a pending computed field, and writes a sensitive computed value, with
the cipher. Test: `SensitiveComputedReadTests`.

Seeds encrypt a sensitive value with the tenant's data key, at server start, in `axis-admin activate` and in the
publisher. A synced seed decrypts the stored value and compares it with the declared one, so an unchanged value writes
nothing. A seed with a sensitive value fails the whole seed run when the tenant has no data key, and stores no row. Test:
`StartupActivationTests`.

`SensitiveValueSourceTests` guards the rest of the code: it fails on a method in `src/` that builds SQL over an entity
column without the cipher, unless an allow-list entry says why that method touches no sensitive value.

A scenario's tenant gets a random data key and lookup key, held in memory for the run only and never stored. Its record
steps, task completions, seeds and process steps use the cipher, so a value is never written in clear, and a read under a
grant whose `filter` tests an exact lookup field matches through the lookup hashes. Test: `ScenarioRunnerTests`. A data source `filter` and a measure's `filter` that test an exact lookup field match the same
way, in the rows, the export and the id read of the trigger fan-out. Test: `ExactLookupFilterTests`. The
trigger and starter reads do not use it yet _(planned for
M27)_: a data change trigger, an event trigger start, a task escalation, or an inbound endpoint's start condition
that reads a record holding a stored sensitive value fails. Activation's inline recompute of computed fields and the worker's
`computed.backfill` do not use the cipher either: on an entity that holds a stored sensitive value they fail and write
nothing, until they do _(planned for M27)_.

The leave request sample marks its `reason` `sensitive` and shows each request by a required `title`, its display
field, because a sensitive field cannot be one. The department manager reads the reason on the review task, and another
employee gets no record, task or task form state. Test: `tests/e2e/specs/leave-request.spec.ts`. No text, `bytea` or JSON column that
the tenant's runtime role reads holds a submitted reason, and the tenant's backup restores with its backup key, keeps the
reason as ciphertext and is refused without the key. Test: `LeaveRequestReasonStorageTests`.

### Rotating the keys

`tenant data-key rotate <id>` adds the next data key and the next lookup key. Then, one table at a time and each in a
transaction of its own, it re-encrypts every sensitive value under an older data key and recomputes every lookup hash
under an older lookup key. It prints `<id> data-key rotate ok: data key <n>, lookup key <k>, <m> values updated`. `<m>`
counts the values it re-encrypted and the hashes it recomputed, so `0` means that nothing used an older key.

- **Which columns.** Rotate reads the tenant database's catalogue, not the active release. It takes every `bytea` column named
  `f_` and a field name in the `entities` schema, and the `_lookup` column beside it. That includes child tables, and tables
  that no active release uses any more.
- **A host that writes meanwhile.** Rotate writes a value back only if the row still holds the bytes it read, so a row that a
  host changed while rotate ran stays as the host wrote it. A host that read the keys before the rotation writes with its older
  key until it reads a value under the new one, or restarts. Those values name an older key, and a second `rotate` updates them.
  A server that read the keys before the rotation still reads a rewritten value, because the new number is one it does not
  hold, so it reads the keys again.
- **A stale search.** A server with cached keys hashes an exact lookup search value only under the lookup keys it has read, so
  until it reads a value under the new key or restarts, its search can miss a record that rotate re-hashed. The search that
  reads the keys again _(planned for M27)_ closes the window, and [D116](../decisions/d116-how-sensitive-fields-and-backups-are-encrypted.md)
  stays the agreed target.
- **A failure.** A value that does not decrypt stops rotate at its table, with the line
  `<id> data-key rotate failed: <message>` and exit code `1`. The message holds no value and no key. The tables before it stay
  rewritten and the new keys stay, so running `rotate` again finishes the work.

`tenant data-key retire <id>` deletes every data key and lookup key older than the newest, once no stored value or hash names
one. While any value or hash still names an older key, it deletes no key at all, prints
`<id> data-key retire refused: <c> stored values still name an older key. Run tenant data-key rotate again.` and exits with
code `1`. Run `rotate` again, then `retire`. Otherwise it prints `<id> data-key retire ok: <d> data keys, <l> lookup keys deleted`.
A tenant with no older key prints it with `0` and writes no record. Restart the hosts, or wait until they have read the new
keys, before `retire`: a host that still writes with a deleted key stores values that no host can read after it restarts.

Both write a [platform audit record](tenant-setting-commands.md#platform-audit-records) with the key numbers and
never a key. A refused `retire` writes none. Both need `Tenancy:Source=directory`, and the exit codes are those of
[output and exit codes](administration.md#output-and-exit-codes). Test: `AdminTenantDataKeyTests`.

## Encrypted backups

- **The backup key.** The operator makes a key of 32 random bytes, for example with `openssl rand -base64 32`, and
  keeps it outside the installation. It is never the installation key. `tenant backup` and `tenant restore` take it
  as `--backup-key-file <file>`, a file that holds the base64 key. The tool trims white space around it. The key
  never goes on the command line, in a setting or in a log. A lost backup key loses its archives. The tool cannot
  tell whether the backup key equals the installation key, so keeping the two apart is the operator's job.
- **The archive.** `tenant backup` writes the tar archive of [tenant backup](tenant-backup-and-restore.md#tenant-backup)
  encrypted:
  - The file starts with the 8 bytes `AXISBAK1` and a random 8-byte prefix.
  - Chunks of 1 MiB of the tar follow, each encrypted with AES-256-GCM and followed by its 16-byte tag. A chunk's
    nonce is the prefix and the chunk's 4-byte counter, big-endian, from 0. Its associated data is one byte, 1 for the
    last chunk and 0 for the others.
  - A cut, reordered or changed archive fails to decrypt.
  - `tenant backup` writes the dump to a temporary file that only its owner can read, then deletes it. Only the final
    archive is encrypted.
- **The tenant's keys go with it.** The manifest holds the tenant's data keys and lookup keys, base64-encoded, under
  `keys`, each with its `kind`, `number` and `key`. Only the backup key protects them, so a restored tenant can read
  its sensitive values. `tenant restore` stores them again under the installation key, with the same numbers.
- **Restore refusals.** `tenant restore` refuses, before anything changes and with exit code `2`:
  - an archive that does not start with `AXISBAK1`, which includes every archive written before archives were
    encrypted, so none of them can be restored;
  - a key file that is missing or does not hold 32 bytes in base64;
  - a key that does not open the first chunk.

  The `check` step decrypts only as far as the manifest and the start of `tenant.dump`. The `restore` step decrypts to
  the end of the file, so a chunk that fails to decrypt later fails that step. The entry stays `provisioning`, nothing
  is restored, and a rerun with a good archive finishes the restore.
- **What the encryption does not do yet.** A `sensitive` field has a `bytea` column. The record API, forms and the
  detail widget, an import, an inbound endpoint, a seed, a process step, a connector or package call output, the process
  start endpoint, task completion, the task form state, a scenario and the operator's call resolve encrypt and decrypt a value with the tenant keys. A value's associated data names the tenant id, so that work
  has to handle a restore under a new id.
