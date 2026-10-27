# D59. Table row and toolbar actions

Status: **Agreed**

A `table` widget offers actions on its rows and in its toolbar, delete
included. The owner confirmed these points on issue #932 on 2026-10-21.

- **Lists:** a `table` widget has `rowActions` and `toolbarActions`. `navigate`
  may sit in either list. `startProcess` and `delete` sit in `rowActions` only,
  and the row is their subject.
- **Delete:**
  - It asks the user to confirm, then calls the record API `DELETE`.
  - It shows only when the widget's `access.actions` holds `delete`.
  - The server still checks the `delete` grant and its record filter
    ([D12](d12-authorization-is-server-side-and-default-deny.md),
    [D24](d24-roles-and-policies.md)).
- **After a delete:** a success reloads the current page of rows. A `403`,
  `404` or `409` shows a message and keeps the row.
- **Visibility:** an action may have a `visibleWhen` expression over the row.
  It only shapes the UI. The server decides.
- **Audit:** a delete writes an audit record, as the record API does today.
- **Layout:** toolbar actions sit next to the create button. Row actions show
  as buttons and fold into a menu at phone width.
  - [D96](d96-rules-from-the-design-system-review.md) replaces this point:
    the first `navigate` row action opens a row from its identifying column,
    which is a link, and has no button. A delete is a red trash icon. With
    three or more other actions, or at narrow width, they fold into one "…"
    menu. The toolbar's first action is its main action and sits last, after
    Refresh and the secondary actions.
- **Running process:** a delete of a record that a running process holds is
  refused with `409`.
- **Bulk actions:** actions on selected rows come after M12. M12 acts on one
  row at a time.

The `navigate` action has the shape that
[D63](d63-navigate-actions-and-page-parameters.md) defines. A row of a table
bound to a data source acts on its root record, and a group row has no
actions.

_Why:_ a delete can remove stored data that should stay, so the user confirms
it and the server checks it again. The UI can hide a button, but only the
server can refuse a request ([D12](d12-authorization-is-server-side-and-default-deny.md)).
Refusing the delete of a record in a running process keeps every running
instance its subject. One row at a time keeps the confirmation and the
messages simple, and bulk actions can follow on the same record API.
