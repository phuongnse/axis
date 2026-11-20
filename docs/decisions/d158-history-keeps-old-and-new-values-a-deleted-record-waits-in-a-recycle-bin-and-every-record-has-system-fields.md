# D158. History keeps old and new values, a deleted record waits in a recycle bin, and every record has system fields

Status: **Agreed**

## Context

A record's history said who changed which fields and when, but not from what to what, and a deleted
record was gone unless the whole tenant was restored. An auditor asks what a supplier's bank account
was before someone changed it, a clerk deletes the wrong supplier, and another system asks what
changed since its last read. Dataverse, ServiceNow and Salesforce keep values in history, keep a
deleted record for a time, and give every record its created and modified fields.
[D154](d154-every-capability-an-organization-needs-has-a-home-and-a-capability-map-keeps-it-so.md)
puts this among the foundations.

The record tables stay the source of truth, and history stays the append-only trail beside them that
[D21](d21-development-test-users-audit-records-and-sequences.md) agrees, written in the same
transaction as the change. Event sourcing was weighed and not taken, for the reasons below.

## Decision

- **History keeps the old and the new value of each change.** A change to a field, a child row, a
  reference or a file is kept with its value before and after: a reference with its label at the
  time, a file with its name and version.
- **A sensitive value in history is encrypted** under the tenant's data key, as
  [D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) encrypts the
  field, and history still keeps user ids, not names.
- **History shows a value only to a person who may read its field.** Anyone else who may read the
  record's history sees that the field changed, not its values.
- **A record can be seen as it was at a time,** rebuilt from its history.
- **A tenant administrator searches and exports the tenant's audit,** every audit record of data
  changes, sign-ins, administration, publishes and operator commands, by person, entity, record and
  time.
- **A deleted record waits in a recycle bin for 30 days,** or the period the tenant sets. A person
  who may delete records of its entity, or an administrator, restores it with its child rows and its
  files. A restore is refused, with the reason, when another record now holds one of its unique values
  or a record it refers to is gone. At the end of the period the record is purged, and an
  administrator may purge it sooner. A record in the bin shows nowhere else. A restore raises a change
  that triggers can start on.
- **Every record has system fields:** when it was created and by whom, and when it was last changed
  and by whom. They can be read, filtered and sorted, and the record API answers what changed since a
  time.
- **History values can be erased under rule.** Erasing a person's data, which M47 builds, also clears
  their values from history, by a path of its own that is audited. A sensitive value is erased by
  dropping its key. Nothing else changes or removes an audit record.

The detail is in [history](../reference/record-audit.md#audit-records-and-history),
[audit records](../reference/audit-records-and-sequence-counters.md#audit-records-and-sequence-counters) and the
[record API](../reference/record-api.md).

## Consequences

- An auditor reads what a value was and who changed it, and a reader who may not see a field still
  sees that it changed.
- A wrong delete is undone in a minute instead of by a restore of the tenant.
- History grows with the values it keeps, and a sensitive value stays encrypted in it.
- Another system reads what changed since its last read, without copying timestamps onto each record.
- Records stay the source of truth, so a form, a filter, a unique value and a validation see a change
  at once, and a release changes an entity by a migration, as before.

## Alternatives

- **Event sourcing.** Each kind of query would need a projection that lags behind a save, events would
  have to stay readable across every release that an author changes, and erasing personal data from
  events that never change is hard. The platforms of this kind keep records as the source of truth and
  an audit trail beside them.
- **Values only for chosen fields.** An author would have to guess in advance which change an auditor
  will ask about.
- **Restore only from a tenant backup.** Undoing one delete would undo every other change since the
  backup.
