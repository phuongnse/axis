# D109. Sensitive fields and backups are encrypted, and history keeps no names

Status: **Agreed**

## Context

A flag named `sensitive` that encrypts nothing invites authors to believe their data is protected.
Disk encryption helps only against a stolen disk, not a leaked database login, a dump or a replica.
A backup holds a whole tenant and leaves the installation. Audit records are kept forever, so a name
or a full address in them conflicts with a person's right to have their data removed. The law of
Axis's first customers protects sensitive personal data more strongly than basic personal data.

## Decision

- **`sensitive` means encrypted at rest,** for sensitive personal data such as health, finances,
  identity numbers and biometrics. A name or an email stays a normal, searchable field.
- **The application encrypts.** The server and the worker encrypt with AES-256-GCM under a data key
  of the tenant, bound to the tenant, entity, record and field. The keys are encrypted by the
  installation key, and a command rotates them.
- **A sensitive field cannot be filtered, sorted, aggregated, indexed or unique,** nor read by a
  stored computed field or SQL. An optional exact lookup stores an HMAC under a second key, for
  equality filters only.
- **Marking a field encrypts its stored values,** and unmarking decrypts them.
- **Backups are encrypted** with a backup key that the operator keeps outside the installation, never
  the installation key.
- **History keeps no names.** Append-only history stores the user id and reads the name from the
  user's profile. Session audit records drop the last part of the client address.

The shapes are [D116](d116-how-sensitive-fields-and-backups-are-encrypted.md)'s. The detail is in
[data encryption](../reference/data-encryption.md) and
[data classification](../reference/keys-and-data-protection.md#data-classification).

## Consequences

- A login, a dump or a replica shows only ciphertext for a sensitive field.
- An exact lookup shows which records share a value.
- A lost backup key loses its archives.

## Alternatives

- **Disk encryption alone.** It leaves every database reader able to read everything.
