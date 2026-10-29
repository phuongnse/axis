# D109. Sensitive fields and backups are encrypted, and history keeps no names

Status: **Agreed**

The owner agreed these points on 2026-10-29. Today Axis encrypts only secrets. A field marked
`sensitive` is stored in clear, a tenant backup can be read by whoever holds the file, and
append-only history keeps display names and full client addresses forever. Disk encryption only
helps against a stolen disk: anyone with a database login, a dump or a replica reads everything.
Everything here is _(planned for M27)_, see the [roadmap](../roadmap.md#milestones).

- **`sensitive` means encrypted at rest.** This changes what
  [sensitive fields](../reference/roles-and-policies.md#sensitive-fields) do.
  - It matches the sensitive personal data of Vietnam's personal data rules, such as health,
    finances, identity numbers and biometrics. Basic personal data, such as a name or an email,
    stays a normal field that can be searched.
  - The server and the worker encrypt the value before it is written and decrypt it after it is
    read, with AES-256-GCM under a data key of the tenant. The value is bound to its tenant,
    entity, record and field, so a value copied elsewhere does not decrypt.
  - Each tenant has its own data key, stored encrypted by the installation key. A command rotates
    it and re-encrypts the tenant's values, as `platform rekey` does for secrets.
  - The record API, forms and the detail widget show the value to users whose grants cover it,
    as today.
- **What a sensitive field cannot do,** checked at compile time with a new diagnostic code:
  filters, sorting, aggregates, an index, `unique`, a computed field that is stored, a data
  source column that sorts or groups, and an expression that runs as SQL.
  - **Exact lookup:** a field may also declare an exact lookup. Axis then stores an HMAC of the
    value under a second key of the tenant, and a filter may test the field for equality only.
    The cost is that two records with the same value can be seen to match.
- **Existing data:** a migration encrypts the values of fields that a release marks
  `sensitive`, and decrypts them when the flag is removed. None of the samples marks a field
  `sensitive` today.
- **Backups are encrypted.** `tenant backup` encrypts the archive with a backup key that the
  operator gives the admin tool and keeps outside the installation. It is not the installation
  key, so a stolen backup and a stolen installation do not open each other. `tenant restore`
  needs the same key. A lost backup key means a lost archive.
- **History keeps no names, and shorter addresses.** For rows written from then on:
  - Append-only history stores the user id and no display name: the tenant audit records, the
    release history and the publish requests. A history view reads the name from the user's
    profile, and shows the user id when the profile was removed.
  - The session audit records keep the client address with its last part removed: the last
    octet of IPv4 and the last 80 bits of IPv6.
- **Order:** M27 follows M18, because the key design rests on the threat model of
  [D106](d106-a-threat-model-kept-current-like-the-asvs-tracking.md). It runs beside M19 to
  M21, and M26 waits for it, because the built-in applications of M26 are the first that a
  customer runs, and `organization` holds personal data of employees. It must also finish
  before the first customer's installation.

_Why:_ a flag named `sensitive` that encrypts nothing invites authors to believe their data is
protected. The law Axis's first customers follow asks for stronger protection of sensitive
personal data than of basic personal data, so the flag follows the same line. Encrypting in the
application protects against what disk encryption does not: a leaked database login, a dump, a
replica, an operator's administrator. A backup holds the whole tenant and leaves the
installation, so it needs a key of its own. Audit records are kept forever and cannot be
changed, so keeping a name or a full address in them conflicts with a person's right to have
their personal data removed. This is the cheapest time to change, with no customer and no
sample field marked `sensitive`. The cost is that a sensitive field cannot be filtered, sorted
or aggregated, that an exact lookup shows matching values, and that a lost backup key loses the
archive.
