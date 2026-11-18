# D157. Files reach calls and emails, show in the page, keep their versions, and documents come from Word templates

Status: **Agreed**

## Context

A file lived in the tenant database, was always downloaded as an attachment, kept no earlier version,
and could not reach a connector, a package or an email. An organization sends a purchase order as a
PDF, prints a record, reads a scanned invoice in the page, sends a contract for an e-signature and
mails a branded notice with an attachment. [Knowledge](../domain/knowledge.md#what-applications-use-most)
names documents made from templates as a utility the platform owes its applications, and
[D154](d154-every-capability-an-organization-needs-has-a-home-and-a-capability-map-keeps-it-so.md)
puts them among the foundations. This decision replaces [D76](https://github.com/phuongnse/axis/blob/11688aa43b8dfa85639ba00e0b14cd9baea8a54f/docs/decisions/d76-file-money-and-enum-label-field-types.md)
and keeps every part of it that still holds.

## Decision

- **A `file` field holds one file.** Several files are a child collection whose child entity has a
  `file` field, so there is no list type.
- **Upload first, attach by a record write.** An upload stores a pending file that belongs to the
  user, the entity and the field it was uploaded for, and needs a `create` or `update` grant there. A
  record write attaches it by its id, and only that user's pending file for that field.
- **A download goes through the owner record.** It checks the read grant, the record filter and the
  field as a read of that record would.
- **Images and PDFs show in the page,** with a thumbnail, from an origin of their own that runs no
  script of Axis's, and every other file comes as an attachment with `nosniff`. SVG and HTML never
  show inline.
- **A file keeps its earlier versions.** Replacing a file keeps the one before, up to a number of
  versions, and a person who may update the record lists them and restores one.
- **Bytes go where a `storage` binding says.** `storage` is a kind of
  [deployment binding](d25-connectors-deployment-bindings-and-secrets.md), named by the application
  manifest's `fileStorage`. Its value is `database`, the default, which keeps the bytes in the tenant
  database, or an object storage that speaks the S3 protocol, which keeps them outside it. A command
  moves a tenant's files from one to the other.
- **No malware scanning in Axis.** Axis refuses executables and SVG. An operator that needs scanning
  puts a scanning proxy in front of the upload route.
- **A file is a value that calls, packages and emails carry.** A connector input may send a file, in
  a multipart body or encoded in JSON. A package operation takes and gives files. A file that a call
  or a package gives back is written to a `file` field. A `notify` email carries files as attachments.
- **Documents come from Word templates.** A `documentTemplate` resource holds a Word file with places
  for values, tables that repeat over child rows, conditions and images, one for each language, as
  Dynamics, Power Automate and Docmosis shape them. A process step makes a PDF or a Word file from a
  record with it and writes the file to a field or sends it in an email, and a page offers it as a
  download. The documents are made in a runner with a sandbox, never in the server.
- **A record page prints,** with a layout for paper, and a report gives a PDF through the same
  templates.
- **Emails may be HTML.** An email template has an HTML body with a plain text one beside it, the
  site's logo and attachments, under a size limit, and its HTML is cleaned before it is sent.
- **An e-signature goes through a connector.** A process sends a document to a signing service, such
  as a Vietnamese certificate authority or DocuSign, waits for the call that says it is signed, and
  keeps the signed file.
- **`money` is a decimal with a currency.** The currency is a fixed ISO 4217 code or the value of a
  field of the same record. Its expression type is `decimal`, and there is no conversion between
  currencies, so a total over amounts whose currency comes from a field must group by that field.
- **Enum values get labels apart from the values.** `valueLabels` maps each value to a text key.
  Screens show the label, while the record API, data sources, exports and expressions keep the value.

The detail is in [file fields](../reference/file-fields.md),
[entity field types](../reference/resource-files.md#entity-field-types-and-constraints),
[deployment bindings](../reference/integration-resources.md#deployment-bindings),
[notifications](../reference/notifications.md) and
[data source aggregates](../reference/data-sources.md#aggregates).

## Consequences

- Record writes stay JSON, and nobody can attach another user's file by guessing its id.
- A file shown in the page cannot run script in Axis's origin, because it comes from its own.
- Files in the tenant database share its isolation, transactions and backups. A storage outside the
  database has to say how its files are backed up, and moving bytes changes a tenant's binding, not
  the release.
- A purchase order, a contract or a certificate is made from a Word file the business already uses,
  in each language of the application.
- Making documents needs a runner with a sandbox beside the worker.
- An installation that must scan uploads runs and updates the scanner itself.
- A total across currencies is refused rather than silently wrong.
- An enum value's wording changes without a migration.

## Alternatives

- **Templates in HTML.** A developer writes them easily, but the people who own a business's forms
  write them in Word.
- **Files sent inside the record write.** Record writes would stop being JSON.
- **Scanning in Axis.** Signatures need constant updates that an on-premises installation cannot
  promise.
- **A money type in the expression language.** Correct display needs only the field's currency, so a
  new expression type would add work without a gain.
