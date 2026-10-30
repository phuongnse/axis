# Data encryption

_(planned for M27)_ Axis encrypts the values of fields marked `sensitive` and tenant backup archives, as
[D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) agrees and
[D116](../decisions/d116-how-sensitive-fields-and-backups-are-encrypted.md) proposes. The keys are in the
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
- **What reads it.** Computed fields that are not stored, rules and process expressions read the decrypted value, as
  they read any field. Process inspection still hides it, see
  [sensitive fields](roles-and-policies.md#sensitive-fields).

## Exact lookup

A sensitive field may declare an exact lookup, so a filter can find a record by its exact value:

```json
{ "name": "nationalId", "type": "text", "maxLength": 20, "sensitive": true, "exactLookup": true }
```

- Axis stores an HMAC-SHA256 of the value's plain text in a second `bytea` column, named after the field's column
  with the suffix `_lookup`, with an index. The key is the tenant's lookup key.
- A data source filter, a policy's record filter or a caller's filter may test the field with `==` or `!=` against a
  value, and nothing else. Axis compares the hashes.
- Two records with the same value have the same hash, so whoever reads the table can see that they match. That is
  the cost of the lookup.

## What a sensitive field cannot do

Compilation refuses each of these on a sensitive field with [`AXC0307`](diagnostic-codes.md), at the path of the use.
The message names the field and the use.

- A filter, other than the equality of an exact lookup.
- A sort, in a data source or a table.
- An aggregate over it, and a data source column that groups by it.
- An index, `unique` or `searchable`.
- A stored computed field that reads it.
- An expression that runs as SQL, such as a data source `filter` or a policy grant's `filter`, other than the
  equality of an exact lookup.

`exactLookup` on a field that is not `sensitive` is [`AXC0308`](diagnostic-codes.md) at `/fields/{i}/exactLookup`. A
filter on a sensitive field that a caller sends at run time, through the record API or a data source's parameters,
is a `400` problem keyed at the filter. Import and export read and write the plain values, under the same grants as
the record API.

## Changing the flag

Adding or removing `sensitive` or `exactLookup` is a schema change that activation applies in its transaction, as
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
- **Lookup key.** Each tenant has one lookup key of 32 random bytes. It is not rotated in M27.
- **Under the directory source.**
  - `platform.tenant_data_keys` holds the keys, one row per key: the tenant id, the key's `number` (`0` for the
    lookup key) and the key, encrypted by the installation key as other secrets are.
  - The field name in the associated data is `data_key:` followed by the number.
  - `platform rekey` re-encrypts them with the other secrets.
- **Under the configuration source.** `Tenants:{id}:DataKeys:{number}` and `Tenants:{id}:LookupKey` hold the keys,
  base64-encoded. Development and the tests set them.
- **When they are made.** `tenant create` makes data key 1 and the lookup key. `tenant restore` takes them from the
  archive. `migrate` makes them for a tenant that has none.
- **No key, no write.** A host that finds no key for a tenant refuses to write a sensitive value, with a `500`
  problem and an error log line that names the tenant. It never writes a sensitive value in clear.
- **Refresh.** Each host reads a tenant's keys when it first needs them, and again when a value names a number it
  does not hold.

### Rotating a data key

`tenant data-key rotate <id>` adds the next data key, then re-encrypts every sensitive value under an older key, one
table at a time, each in a transaction of its own. It prints
`<id> data-key rotate ok: key <n>, <m> values re-encrypted`. A host that wrote with an older key during the rotation
leaves values that a second run re-encrypts.

`tenant data-key retire <id>` deletes every data key older than the newest that no stored value names. It refuses,
with exit code `1`, while a value still names one, and says how many. Run `rotate` again, then `retire`.

Both write a [platform audit record](tenant-setting-commands.md#platform-audit-records) with the key numbers and
never a key. The exit codes are those of [output and exit codes](administration.md#output-and-exit-codes).

## Encrypted backups

- **The backup key.** The operator makes a key of 32 random bytes, for example with `openssl rand -base64 32`, and
  keeps it outside the installation. It is never the installation key. `tenant backup` and `tenant restore` take it
  as `--backup-key-file <file>`, a file that holds the base64 key. The key never goes on the command line, in a
  setting or in a log. A lost backup key loses its archives.
- **The archive.** `tenant backup` writes the tar archive of [tenant backup](administration.md#tenant-backup)
  encrypted:
  - The file starts with the 8 bytes `AXISBAK1` and a random 8-byte prefix.
  - Chunks of 1 MiB of the tar follow, each encrypted with AES-256-GCM. A chunk's nonce is the prefix and the
    chunk's 4-byte counter. Its associated data marks whether it is the last chunk.
  - A cut, reordered or changed archive fails to decrypt.
- **The tenant's keys go with it.** The manifest holds the tenant's data keys and lookup key, base64-encoded, under
  `keys`. Only the backup key protects them, so a restored tenant can read its sensitive values. `tenant restore`
  stores them again under the installation key.
- **Restore refusals.** `tenant restore` refuses, before anything changes and with exit code `2`:
  - an archive that does not start with `AXISBAK1`, which includes every archive written before M27;
  - a key file that is missing or does not hold 32 bytes;
  - a key that does not open the first chunk.

  A chunk that fails to decrypt later fails the `restore` step, which restores nothing.
