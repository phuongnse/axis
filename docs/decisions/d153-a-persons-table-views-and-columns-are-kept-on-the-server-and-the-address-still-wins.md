# D153. A person's table views and columns are kept on the server, and the address still wins

Status: **Agreed**

## Context

[D151](d151-tables-whose-rows-grow-have-the-full-toolbar-and-a-view-is-a-filter-kept-in-the-address.md)
keeps a table's view in the address, so a link opens the same rows for anyone. The address does not
keep a person's own choice, though. A person who comes back without the link loses their columns and
their views. Browser storage does not follow a person to another browser, and sign-out clears it, see
[sign-in routes](../reference/security.md#sign-in-routes).

## Decision

- The platform keeps each person's columns, and each person's named views of search, filter and sort,
  on the server. The rows are kept for each person and each table key, in the tenant database.
- A kept choice applies only through the address. An address that already carries a value wins, so a
  link opens the same rows for anyone who follows it.
- A view belongs to the person who saved it, and is never shared with others.
- [D151](d151-tables-whose-rows-grow-have-the-full-toolbar-and-a-view-is-a-filter-kept-in-the-address.md)
  still holds: a view is still a filter kept in the address. The rules of the tables are in
  [actions and controls](../reference/actions-and-controls.md#tables), and the routes and their limits
  are in [table views and columns](../reference/frontend.md#table-views-and-columns).

## Consequences

- A person finds their columns and views again on any browser they sign in on.
- A table's link still opens the same rows for everyone, so sharing keeps working.
- The server stores a small amount of data for each person and table, and the limits of the routes
  keep it bounded.
- The routes, their status codes and their limits become a contract that the code must build.

## Alternatives

- **Browser storage.** It does not follow the person to another browser, and sign-out clears it.
- **Views shared with a team.** Nobody asked for it, and each shared view would need its own grants.
- **A view held inside the table.** D151 rejected it, because a view that lives in the table cannot be
  linked to, so a person cannot share the rows they see.
