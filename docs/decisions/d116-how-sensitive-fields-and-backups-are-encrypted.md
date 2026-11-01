# D116. How sensitive fields and backups are encrypted

Status: **Agreed**

The owner agreed this on 2026-10-30. It sets the shapes that [D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) left
open, so that M27 can be broken into issues. It adds to D109 and changes none of its points. It is built in M27, except the search that reads the keys again after a rotation. That is carried over to M24,
with the other items listed in [D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md). [Data encryption](../reference/data-encryption.md) holds the detail.

- **Stored value:** a `sensitive` field's column is `bytea`. It holds the number of the data key that encrypted it,
  then a 12-byte nonce, the ciphertext and a 16-byte tag. The plain text is the value's text form in the record API.
  The associated data binds it to the tenant, entity, record and field.
- **Keys are numbered:**
  - A tenant has one or more data keys, numbered from 1. Writes use the newest one. Reads use the one that the
    value names.
  - So a rotation never leaves a value that no key can read, even while a host still writes with the key it read
    before.
  - Under the directory source the keys are stored in `platform.tenant_data_keys`, encrypted by the installation
    key, and `platform rekey` moves them too.
  - Under the configuration source they are tenant settings.
- **Two commands:**
  - `tenant data-key rotate <id>` adds a data key and a lookup key, re-encrypts every value under older data keys
    and recomputes every lookup hash under older lookup keys.
  - `tenant data-key retire <id>` deletes the older keys that no value or hash uses any more, and refuses while
    one does.
- **Exact lookup:**
  - A field declares `"exactLookup": true` next to `"sensitive": true`. Axis then stores an HMAC-SHA256 of the
    value in a second column, with an index, and a filter may only test the field for equality.
  - Lookup keys are numbered as data keys are, and each hash names its key. A search hashes the value under every
    lookup key the tenant holds, so it stays right while a rotation runs.
  - `exactLookup` without `sensitive` is an error.
- **Refusals:** one new code covers every use that D109 forbids on a sensitive field: filter, sort, aggregate,
  index, `unique`, `searchable`, a stored computed field, a data source column that sorts or groups, and an
  expression that runs as SQL, such as a policy's record filter. A second code covers `exactLookup` without
  `sensitive`. A filter on a sensitive field that a caller sends at run time is a `400`.
- **Keys exist before they are needed:**
  - `tenant create` and `tenant restore` make the keys.
  - `migrate` makes them for a tenant that has none.
  - A host that finds no key for a tenant refuses to write a sensitive value. It never writes one in clear.
- **Flag changes are migrations:** activation encrypts the stored values of a field that a release newly marks
  `sensitive`, and decrypts them when the flag is removed. It does this in the activation's transaction, as it
  applies other schema changes.
- **Backup key:**
  - The operator gives `tenant backup` and `tenant restore` the option `--backup-key-file <file>`. The file holds
    32 random bytes, base64-encoded, in the form of `Platform:SecretKey`. The key never goes on the command line.
  - The whole archive is encrypted, in chunks of 1 MiB with AES-256-GCM, so a cut or reordered archive fails to
    decrypt.
  - The archive carries the tenant's data keys and lookup keys, so its sensitive values can be read after a restore.
    Restore stores them again under the target installation's key.
  - Restore refuses an archive that is not encrypted, or a key that does not open it, before anything changes.

_Why:_ numbered keys make rotation safe without stopping the hosts, for the lookup hashes as much as for the values, the same way client keys rotate under `fapi2`.
A key file keeps the backup key out of the process list and the shell history. The archive has to carry the data
keys, or a restored tenant could not read its own sensitive values. The backup key protects them, so a stolen
archive is still useless without it. Refusing archives without encryption is cheap now, because no customer has
one.
