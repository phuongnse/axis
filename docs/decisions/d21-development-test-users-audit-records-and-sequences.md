# D21. Development test users, audit records and sequences

Status: **Agreed**

M3 adds three platform pieces before processes run. Test users, their
sign-in, the current user, the SPA user picker, sequences, the audit records
of record API writes, process starts, process steps and task decisions, and
record history are built.

- **Test users:** replaced by
  [D23](d23-sign-in-through-oidc-and-the-bff-session.md), which removed
  them in M4.
- **Current user:** `GET /api/me` returns the session user from
  [D23](d23-sign-in-through-oidc-and-the-bff-session.md), or `401`
  when there is none. Before M4, record writes and process starts stayed
  open with no signed-in user, and the actor was then `anonymous`.
- **Audit records:** rows in `axis.audit_records` are append-only.
  - Each one commits in the same transaction as its action.
  - M3 records these actions: record create, update and delete through the
    record API, process start, each process step and each task decision.
  - Each holds the time, the actor (a user id, or `system` for the worker),
    the action, the application, the entity, the record id, the process
    instance id and a details object.
  - A database trigger rejects every `UPDATE` and `DELETE`.

  See [storage](../reference/storage.md#audit-records-and-sequence-counters)
  and [the record API](../reference/record-audit.md#audit-records-and-history).

- **Record history:** an endpoint returns the audit records of one record,
  newest first. The form page shows them. See
  [the record API](../reference/record-audit.md#audit-records-and-history).
- **Sequences:** a `sequence` resource has a format, such as
  `PR-{yyyy}-{n:5}`. See
  [configuration](../reference/resource-files.md#sequences).
  - The counter restarts each year, but only when the format has `{yyyy}`.
    The year is UTC unless the application names a time zone, see
    [D75](d75-the-application-time-zone.md).
  - An entity `text` field names a `sequence`. Its value is assigned on
    create, and clients cannot write it.
  - The counter row is locked and updated in the caller's transaction, so a
    create that rolls back uses no number and there are no gaps. Concurrent
    creates of the same sequence wait on that row lock, which is accepted at
    the expected volumes.

_Why:_ see [knowledge](../domain/knowledge.md#operations). Processes need an
actor to record and a trail that cannot be rewritten. Real authentication
waits for M4, so a fixed list of test users stands in, and it must never
reach Production. Business numbers come from the same transaction as the
record, so they never repeat and never skip.
