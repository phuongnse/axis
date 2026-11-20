# Audit records and sequence counters in storage

This page holds the audit records of each tenant database, the audit actions they record and the sequence
counters that number records. The rest of storage, such as the system tables and the database roles, is in
[storage](storage.md).

## Audit records and sequence counters

`Axis.Data` owns the audit records, the trail of who did what, and the sequence counters that number
records. Both are system tables in the `axis` schema of the tenant database.

### Audit records

`axis.audit_records` is append-only, and each row is written in the same transaction as the action
it records. The record API, the process [start endpoint](processes.md#start-endpoint),
[process steps](process-execution.md#running-a-step), [task completions](task-api.md#completing-a-task) and
[saves](task-api.md#saving-a-task) and the [operator commands](process-operator-commands.md#operator-commands) write them, the last with the
user or service account as actor, and so do the actions that [audit actions](#audit-actions) lists.

| Column                | Type                             | Meaning                                                                                                                                                                                                                           |
| --------------------- | -------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id`                  | `uuid`                           | A version 7 UUID                                                                                                                                                                                                                  |
| `occurred_at`         | `timestamptz`                    | When the action happened: the transaction time, `now()`, so the audit records of one transaction share it                                                                                                                         |
| `actor`               | `text`                           | A user id, `system` for the worker, or `anonymous` for `session.signInRefused`. The record API and the process start endpoint need a session, so they never write `anonymous`                                                     |
| `actor_name`          | `text`, null                     | Null in every row that Axis writes, because history keeps no names: a history view reads the name from the user's profile, see [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) |
| `action`              | `text`                           | The action, such as `record.created`                                                                                                                                                                                              |
| `application_id`      | `uuid`, null for session actions | The application `id`. A session action belongs to no application                                                                                                                                                                  |
| `entity_id`           | `uuid`, null                     | The entity `id`, when the action concerns a record                                                                                                                                                                                |
| `record_id`           | `uuid`, null                     | The record id                                                                                                                                                                                                                     |
| `process_instance_id` | `uuid`, null                     | The process instance id                                                                                                                                                                                                           |
| `details`             | `jsonb`                          | An object that depends on the action                                                                                                                                                                                              |

- **Stable ids.** Application and entity are stored by their stable resource ids, never by name, so
  a rename keeps the history.
- **History index.** An index on `(entity_id, record_id, occurred_at desc, id desc)` serves the
  [record history](record-audit.md#audit-records-and-history).
- **Append only.** A `BEFORE UPDATE OR DELETE` row trigger and a `BEFORE TRUNCATE` statement trigger
  raise an exception with SQLSTATE `23001` (`restrict_violation`), so no row can be changed or
  removed, even by raw SQL.
- **Values** _(planned for M41)_. The `details` of a record action hold `changes`, the old and new value of each
  change, with a sensitive value as ciphertext, see [values in history](record-audit.md#values-in-history).
- **The erasure path** _(planned for M41)_. The append-only triggers let one path through, the update that the
  [erasure of history values](record-audit.md#erasing-values-from-history) makes to `details`, and nothing else.
- **Search index** _(planned for M41)_. An index on `(occurred_at desc, id desc)` and one on `(actor, occurred_at desc)`
  serve the [tenant's audit](record-audit.md#the-tenants-audit), which filters by person, application, entity,
  record, action and time.
- **Recycle bin and history actions** _(planned for M41)_. These actions write audit records beside those of
  [audit actions](#audit-actions), each in the transaction of its change. `record.restored` and `record.purged` set `application_id`, `entity_id` and `record_id`
  and have `{ "version" }` as `details`, with the user as `actor`, or `system` for a purge at the end of the period.
  `auditRecords.exported` has the administrator as `actor`, `{ "format", "rowCount", "truncated" }` as `details` and
  no other id. `history.erased` has the administrator as `actor`, no other id and `{ "auditRecords" }` as `details`,
  the number of audit records changed. None holds a value.

### Audit actions

Besides the record and process actions, these actions write audit records. A `publish` and an
`activate` write theirs as [audit records of publishing](publish-request-storage.md#audit-records-of-publishing) says.

- **Import actions.** The commit of a [record import](import-export.md#import) writes
  `records.imported`, after the `record.created` or `record.updated` audit record of each row, in the
  transaction that writes the rows. The actor is the uploader's user id. `entity_id` is set, and
  `record_id` and `process_instance_id` are null. `details` is `{ "created", "updated" }`, the
  counts of records, and holds no value.
- **Session actions.** The BFF session writes three actions, each in the tenant database of the
  resolved host, see [sign-in and the session](security.md#sign-in-and-the-session). Their
  `application_id`, `entity_id`, `record_id` and `process_instance_id` are null.

  | Action                  | When                                     | `actor`     | `details`                       |
  | ----------------------- | ---------------------------------------- | ----------- | ------------------------------- |
  | `session.signedIn`      | The callback creates a session           | The user id | `{ "clientAddress" }`           |
  | `session.signedOut`     | `POST /api/auth/sign-out` with a session | The user id | `{ "clientAddress" }`           |
  | `session.signInRefused` | The callback is refused                  | `anonymous` | `{ "reason", "clientAddress" }` |
  - `reason` is one of these:

    | Reason                | The callback                                                                                                                                                                       |
    | --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
    | `otherTenant`         | Has a `state` that came from a sign-in that another tenant's client started                                                                                                        |
    | `invalidToken`        | Has an invalid ID token, including one with a nonce that the sign-in did not send                                                                                                  |
    | `invalidState`        | Has a `state` that is missing, unreadable or fails the correlation check                                                                                                           |
    | `invalidIssuer`       | Has provider metadata that names an issuer other than the tenant's `Authority`, in any profile, or is a `fapi2` callback whose `iss` is missing or is not the tenant's `Authority` |
    | `providerError`       | Reports a provider error, including a person who cancels at the identity provider                                                                                                  |
    | `providerNotAccepted` | Has a valid token whose provider the tenant's platform sign-in does not accept and no active site that shares the tenant's host allows                                             |
    | `providerNotAllowed`  | Has a valid token whose provider the site that started the sign-in does not allow, or comes from a site that is no longer active                                                   |
    | `acrNotAccepted`      | Has a valid token whose provider the site allows, but whose upstream `acr` the provider's `acr` list does not accept or the token lacks                                            |

  - `clientAddress` is always present. It is the address as text, with the last octet of IPv4 and
    the last 80 bits of IPv6 set to zero, such as `203.0.113.0` and `2001:db8:1::`. An IPv4-mapped
    IPv6 address is written as IPv4 first. It is `null` when the server sees no address.
  - A `session.signedIn` or `session.signedOut` record commits before its action takes effect. If
    it cannot be written, the callback fails and creates no session, or sign-out fails with a `500`
    and the session stays. If a `session.signInRefused` record cannot be written, the server logs a
    warning and the callback still redirects to the shell's sign-in page.
  - They never hold a token, a cookie or the user agent. A session that expires writes nothing, and
    neither does a sign-out with no session.

- **User actions.** An administrator's command on a user of the tenant's realm writes one action,
  see [user administration](user-administration.md#audit): `user.invited`,
  `user.invitationResent`, `user.disabled`, `user.enabled`, `user.authenticatorReset`,
  `user.roleGiven` or `user.roleRemoved`.
  - The `actor` is the administrator's user id. `entity_id`, `record_id` and `process_instance_id`
    are null, and so is `application_id`, except that a role action on an application role holds
    it.
  - `details` is `{ "userId" }`, the id of the realm user the command acted on. A role action adds
    `role`, and a platform role action adds `"platform": true` and has the kind as its `role`. An
    invitation adds `email`, and a resend adds `previousUserId`, the id before the realm user was
    created again. No record holds the invitation link.
  - A role action commits with the row it changes. An invitation commits with its given roles before
    the email is sent, and a resend with the move of the given roles. The others commit with the end
    of the user's Axis sessions, after the realm steps succeeded.
- **Webhook actions.** Each [webhook](process-events.md#webhook-delivery) request that a receiver
  answers `2xx` writes `webhook.delivered`, with actor `system`. The `application_id`, the event's
  `entity_id`, the subject `record_id` and the `process_instance_id` of the raising instance are
  set, so the delivery shows in the record's history. The `details` are
  `{ "webhookId", "webhook", "eventId", "statusCode" }`, and never hold the body, the URL or a
  payload value.
- **Dead letter actions.** A redeliver or a discard of a [dead letter](process-events.md#routes)
  writes `deadLetter.redelivered` or `deadLetter.discarded`, with the user as `actor`. The
  `application_id` is set, and `entity_id`, `record_id` and `process_instance_id` are null, because a
  dead letter has no subject record. The `details` are
  `{ "deadLetterId", "kind", "triggerId", "eventId", "occurrenceAt", "reason" }`, with null for the
  ids that the kind lacks.
- **Export actions.** An [export](import-export.md#export) of an entity or a data source writes
  `records.exported`, with the user as `actor`. The `application_id` is set, and `record_id` and
  `process_instance_id` are null. An entity export sets `entity_id`, and a data source export leaves
  it null and puts the data source's id in the `details`. The `details` are
  `{ "format", "rowCount", "truncated" }` and, for a data source, `"dataSourceId"`. `format` is
  `csv` or `xlsx`, and `truncated` says whether the file stopped at `Data:ExportMaxRows`. They never
  hold a field value.

- **Application setting actions.** A change of an [application setting](authoring.md#application-settings)
  writes `application.autoAcceptChanged`, with the user as `actor`. The `application_id` is set, and
  `entity_id`, `record_id` and `process_instance_id` are null. The `details` are `{ "autoAccept" }`,
  the new value. It is written only when the value changes, in the transaction that saves the value.

- **Authoring request actions.** Sending, approving and dropping an
  [authoring request](authoring-requests.md#routes), and the accept of its draft, write
  `authoringRequest.sent`, `authoringRequest.approved`, `authoringRequest.dropped` and
  `authoringRequest.accepted`, with the author as `actor`. The `application_id` is set, `details` is
  `{ "requestId" }`, and the other ids are null. Each commits with the row it changes. The accepted
  record commits with the final status of the accept's publish request.

- **MCP tool actions.** A successful `create_draft`, `write_resource`, `write_layer`, `delete_resource`, `write_package_file` or `delete_package_file` call of the
  [MCP server](mcp-server.md#tools), and an `accept_draft` call that queues an accept, writes `mcp.toolCalled`, with the person as `actor`. The
  `application_id` is set, and `entity_id`, `record_id` and `process_instance_id` are null. The `details`
  are `{ "tool", "clientId", "draftId", "resourceId" }`, with a null `resourceId` for `create_draft`, `accept_draft`, `write_package_file` and `delete_package_file`. For `write_layer` it is the id of the module resource the
  call edited, also when the edit leaves no layer. They
  hold no content, name or path. The record commits after the draft's write, or after the accept's publish request is stored, and a refused call writes none.
- **MCP consent actions.** A [revoke of an outside MCP client's consent](user-administration.md#outside-mcp-clients) writes
  `mcp.consentRevoked`, with the administrator as `actor`. The `application_id`, `entity_id`, `record_id` and
  `process_instance_id` are null. The `details` are `{ "clientId", "userIds" }`, the people whose consent was listed
  before the revoke. The record commits with `revoked_at` of the client, before the consents are deleted.
- **Package runner actions.** A [create or revoke of a package runner](user-administration.md#package-runners) writes
  `packageRunner.created` or `packageRunner.revoked`, with the administrator as `actor`. The `application_id`,
  `entity_id`, `record_id` and `process_instance_id` are null. The `details` are `{ "id", "clientId" }`, and never the
  secret. The record commits with the row, or with `revoked_at` before the client is deleted.

### Sequence counters

`axis.sequence_counters` holds one row per [sequence](entity-logic-and-sequences.md#sequences) and period, which
the first number of the period creates.

| Column           | Type      | Meaning                                              |
| ---------------- | --------- | ---------------------------------------------------- |
| `sequence_id`    | `uuid`    | The `sequence` resource `id`                         |
| `application_id` | `uuid`    | The application `id`                                 |
| `period`         | `integer` | The local year, or 0 when the format has no `{yyyy}` |
| `last_value`     | `bigint`  | The last number handed out                           |

- **Key.** The primary key is `(sequence_id, period)`.
- **Module sequences.** For a module sequence, `sequence_id` is the id derived for the application,
  as [module resource ids](entity-tables.md#physical-names) says, so two applications that use one module count on
  their own.
- **Taking a number.** Taking a number is one
  `INSERT ... ON CONFLICT DO UPDATE SET last_value = last_value + 1 RETURNING last_value` in the
  caller's transaction. Its row lock makes concurrent creates of the same sequence wait for each
  other, and a create that rolls back uses no number, so there are no gaps.
