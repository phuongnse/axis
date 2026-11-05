# D116. How sensitive fields and backups are encrypted

Status: **Agreed**

## Context

[D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) encrypts
sensitive fields and backups and leaves their shapes open. Keys must rotate while the hosts keep
running, a restored tenant must read its own values, and a backup key must not leak.

## Decision

- **A stored value names its key.** The column is `bytea`: the data key's number, then the AES-256-GCM
  output, bound to the tenant, entity, record and field.
- **Keys are numbered.** Writes use the newest data key, reads the one the value names. Lookup keys
  work the same way.
- **Keys live with the secrets:** in the platform database under the installation key, or in tenant
  settings under the configuration source.
- **Rotate, then retire.** `tenant data-key rotate` adds keys and moves every value and hash to them.
  `tenant data-key retire` deletes older keys once nothing uses them.
- **Exact lookup** is `"exactLookup": true` beside `"sensitive": true`: an indexed HMAC-SHA256 column
  that a search matches under every lookup key the tenant holds.
- **Two diagnostic codes,** one for every use D109 forbids and one for `exactLookup` without
  `sensitive`. A caller's run-time filter on a sensitive field is a `400`.
- **Keys exist first.** `tenant create`, `tenant restore` and `migrate` make them, and a host without
  one never writes a sensitive value in clear.
- **A flag change runs in the activation's transaction.**
- **A backup key file.** The backup key, 32 random bytes in base64, comes from `--backup-key-file`,
  never from the command line. The whole archive is encrypted in chunks, and carries the tenant's
  keys.
- **Restore refuses** an unencrypted archive or a wrong key before anything changes.

The detail is in [data encryption](../reference/data-encryption.md).

## Consequences

- Rotation needs no stop, as client keys rotate under `fapi2`.
- A restored tenant reads its values, and a stolen archive is useless without its key.
- Archives from before encryption cannot be restored, at no cost while no customer has one.
