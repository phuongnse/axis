# D21. Development test users, audit records and sequences

Status: **Agreed**

## Context

Processes need an actor and a trail that nobody can rewrite
([knowledge](../domain/knowledge.md#operations)). Business documents need numbers that never repeat
and never skip.

## Decision

- **No built-in test users.** People sign in through the identity provider, and integration tests
  through a scheme that only test hosts register
  ([D23](d23-sign-in-through-oidc-and-the-bff-session.md)). `GET /api/me` answers the session's user,
  or `401`.
- **Append-only audit records.** Each action that changes data writes one in the same transaction,
  with the actor: a user id, or `system` for the worker. The database refuses any change or removal.
  A record's audit records, newest first, are its history.
- **Sequences.** A field that names a `sequence` gets the next number of its format on create, in the
  create's transaction, and clients cannot write it. The counter restarts each year only when the
  format holds the year.

The detail is in [audit records](../reference/storage.md#audit-records-and-sequence-counters),
[history](../reference/record-audit.md#audit-records-and-history) and
[sequences](../reference/resource-files.md#sequences).

## Consequences

- A rolled-back create uses no number, so there are no gaps.
- Concurrent creates of one sequence wait on its counter row, which the expected volumes allow.
- An audit record stays forever, so
  [D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md) keeps names
  out of it.
