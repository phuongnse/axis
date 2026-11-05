# D59. Table row and toolbar actions

Status: **Agreed**

## Context

A user acts on the records a table lists: opens one, starts a process on it, deletes it. A delete can
remove data that should stay, and only the server can refuse a request
([D12](d12-authorization-is-server-side-and-default-deny.md)).

## Decision

- **Two lists.** A `table` has `rowActions` and `toolbarActions`. A `navigate` action
  ([D63](d63-navigate-actions-and-page-parameters.md)) may sit in either, and `startProcess` and
  `delete` act on a row. A group row has no actions.
- **Delete is confirmed, then checked.** The button shows only to a user with the `delete` grant, and
  the server checks the grant and its record filter again ([D24](d24-roles-and-policies.md)).
- **A running process keeps its record.** A record that a running process holds cannot be deleted.
- **`visibleWhen` only shapes the UI.**
- **An action acts on one row.**

The actions are in [tables](../reference/actions-and-controls.md#tables) and
[frontend](../reference/frontend.md), and the delete in
[the record API](../reference/record-api.md#create-update-and-delete).

## Consequences

- Every running instance keeps its subject.
- One row at a time keeps the confirmation and the messages simple.
