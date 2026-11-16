# D151. Tables whose rows grow have the full toolbar, and a view is a filter kept in the address

Status: **Agreed**

## Context

A person learns a table once only if every table whose rows grow works the same way. Tables that each
keep their own controls drift apart, and a fix reaches only one of them. A view that a table holds for
itself cannot be shared or reloaded either, because nothing in the address shows it.

## Decision

- A table whose rows grow has the full standard toolbar, as
  [actions and controls](../reference/actions-and-controls.md#tables) draws it.
- A table that is part of one thing, such as the lines of a child collection on a form, is the named
  exception. Its rows belong to that one thing, so a search or a filter of its own would split it.
- A table has no control of its own for a view. A view is a filter kept in the address, so a link opens
  the same rows for anyone who follows it.

## Consequences

- A person finds the same controls on every table whose rows grow, and a fix to the toolbar reaches all
  of them.
- A new table whose rows grow uses the shared toolbar. A table that is part of one thing needs its
  reason written down.
- A table's view can be shared and reloaded, because it lives in the address.

## Alternatives

- **Each table keeps the controls it needs.** Every table then works its own way, which is the drift
  this decision avoids.
- **A view held inside the table.** A view that lives in the table cannot be linked to, so a person
  cannot share the rows they see.
