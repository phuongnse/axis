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
- A write uses the newest lookup key.
- A data source filter, a policy grant's `filter` or a caller's filter may test the field with `==` or `!=` against
  a value, and nothing else. Axis hashes the value under every lookup key the tenant holds and matches any of the
  hashes, so a search stays right while a rotation runs.
- Two records with the same value have the same hash, so whoever reads the table can see that they match. That is
  the cost of the lookup.

## What a sensitive field cannot do

Compilation refuses each of these on a sensitive field with [`AXC0308`](diagnostic-codes.md), at the path of the use.
The message names the field and the use. The refusals and the run-time `400` for `sort` below are built, and hold
before the values are encrypted. The exact lookup exception is planned.

- A filter, other than the equality of an exact lookup.
- A sort, in a data source or a table.
- An aggregate over it, and a data source column that groups by it.
- An index, `unique` or `searchable`.
- A reference, references or file field. These cannot be `sensitive` at all.
- The entity's `displayField`, which shows as a record's label in lists and links.
- A stored computed field that reads it.
- A migration `fill` or `convert` whose target is a sensitive field, at `/changes/{i}/field`. Its SQL would write
  plain text into the encrypted column.
- An expression that runs as SQL, such as a data source `filter` or a policy grant's `filter`, other than the
  equality of an exact lookup. A migration `fill` or `convert` value is one, so it may not read a sensitive field.

`exactLookup` on a field that is not `sensitive` is [`AXC0309`](diagnostic-codes.md) at `/fields/{i}/exactLookup`. A
caller's `sort` on a sensitive field, in the record API or a data source, is a `400` problem keyed `sort`. The record
API has no other filter, and a data source's parameters only reach its `filter`, which cannot read a sensitive field.
Import and export read and write the plain values, under the same grants as
the record API.

## Changing the flag

A sensitive field's column is `bytea`, in a new table and in a new column, whatever the field's type. This is built.
Until the change below is built, the planner refuses to add or remove `sensitive` on a field that already has a column.
It reports `AXC0016` at `/fields/{i}/type`, even when a `convert` migration covers the field, so plain values are never
cast into a `bytea` column.

_(planned for M27)_ Adding or removing `sensitive` or `exactLookup` will be a schema change that activation applies in its transaction, as
[schema planning](storage.md#schema-planning) describes for other changes:

- **Marked `sensitive`.** Activation adds a `bytea` column, writes each row's encrypted value into it, drops the
  plain column and renames the new one. A field index or unique constraint goes with the plain column.
- **No longer `sensitive`.** Activation does the same the other way, and decrypts each value into a column of the
  field's type.
- **`exactLookup` added or removed.** Activation adds the lookup column and fills it, or drops it.
- A stored value that does not decrypt fails the activation, and nothing changes.

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

The key store, the settings, `tenant create`, `migrate`, `tenant restore`, the rekey, the refresh and the cipher that
encrypts and decrypts a sensitive value under the data keys are built, and so is the `bytea` column of a sensitive field.
The record API, forms and the detail widget write and read `sensitive` values with the cipher, also in the rows of a child
collection. A row's value is bound to that row's id, so it reads back only in that row. A stored value copied to another
record, row or field does not decrypt, and the read is a `500`. When the tenant has no data key, a record API write of a
sensitive value is a `500` problem, an error log line names the tenant, and no row is stored. Test:
`RecordSensitiveValueTests`.

A data source decrypts the value of a projected sensitive field, on the root entity and through a reference path, in its
rows and its export, for a user whose grants cover the field. A value that does not decrypt is a `500`. Test:
`DataSourceSensitiveValueTests`. A rule or form requirement that aggregates over a `references` field reads the
target's decrypted values, and the server reads a pending computed field, and writes a sensitive computed value, with
the cipher. Test: `SensitiveComputedReadTests`.

Seeds encrypt a sensitive value with the tenant's data key, at server start, in `axis-admin activate` and in the
publisher. A synced seed decrypts the stored value and compares it with the declared one, so an unchanged value writes
nothing. A seed with a sensitive value fails the whole seed run when the tenant has no data key, and stores no row. Test:
`StartupActivationTests`.

Process steps, connector and package calls, imports and scenarios do not use the cipher yet _(planned for M27)_.
A write of a sensitive value on those paths fails with a `500`, and a value is never written in clear. A process
started on an entity that holds a stored sensitive value fails in the worker when it reads the record, until the worker
uses the cipher. The same holds for the task form state and the task completion of a process. Activation's inline
recompute of computed fields and the worker's `computed.backfill` do not use the cipher either: on an entity that
holds a stored sensitive value they fail and write nothing, until they do _(planned for M27)_. The rotation below is
also _(planned for M27)_.

### Rotating the keys

`tenant data-key rotate <id>` adds the next data key and the next lookup key. Then, one table at a time and each in a
transaction of its own, it re-encrypts every sensitive value under an older data key and recomputes every lookup hash
under an older lookup key. It prints `<id> data-key rotate ok: data key <n>, lookup key <k>, <m> values updated`. A
host that wrote with an older key during the rotation leaves values that a second run updates.

`tenant data-key retire <id>` deletes every data key and lookup key older than the newest that no stored value or
hash names. It refuses,
with exit code `1`, while a value still names one, and says how many. Run `rotate` again, then `retire`.

Both write a [platform audit record](tenant-setting-commands.md#platform-audit-records) with the key numbers and
never a key. The exit codes are those of [output and exit codes](administration.md#output-and-exit-codes).

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
  detail widget encrypt and decrypt a value with the tenant keys. A process step, a connector or package call, an
  import or a scenario does not yet _(planned for M27)_, and a write of a value there fails. The
  planner refuses a flag change until the [changing the flag](data-encryption.md#changing-the-flag) migration is built. A value's associated data names the tenant id, so that work has to handle
  a restore under a new id.
