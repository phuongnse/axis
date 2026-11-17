# Calendar, board, cards and tree widgets

_(planned for M42)_

The calendar, board, cards and tree widgets show an entity's records in four views. A calendar places them by a date, a
board groups them, cards show them as tiles and a tree shows them under the record they refer to. This page says what
each view shows, what it keeps in the address and what the compiler checks, as [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md)
agrees. The rest of the frontend is in [frontend](frontend.md).

## Every view

The four views share their source, their access and their address rules. Each view adds only what its own kind of
display needs.

- **Source.** A view names an `entity`, not a `dataSource`. Each item is one record of that entity, read through the
  record API under the user's read grants, as a table's rows are.
- **Title.** An item's title is the value of the entity's `displayField`, as a table's identifying column shows it.
- **Actions.** A view may list `rowActions` and `toolbarActions`, as a table does, see
  [row and toolbar actions](table-widget.md#row-and-toolbar-actions). The first `navigate` row action opens an item from
  its title, see [opening a row](table-widget.md#opening-a-row).
- **Access.** A view shows to a person who may read its entity, as any widget over an entity does, see
  [policies](frontend.md#policies).
- **Address.** A calendar, a board and cards take an optional `prefix`, as a table does, see
  [address prefix](table-widget.md#address-prefix). A page has at most one table, calendar, board or cards widget
  without a prefix. A tree keeps nothing in the address and has no `prefix`.
- **Compile.** The compiler checks that each field a view names exists on its entity and has the type the view needs.
  It checks a `prefix` as it checks a table's.
- **Look.** The look is left to design reviews, as [design changes](../delivery.md#design-changes) says.

## A drag writes the record

A calendar and a board move an item by a drag. A drop is an update through the record API, as a save of a form is, so
the same rules decide whether it succeeds.

- **The request.** A drop sends `PATCH` to the record, with the `version` the view read and only the moved field in
  `values`, such as `{ "version": 3, "values": { "startDate": "2026-11-20" } }`. See
  [create, update and delete](record-api.md#create-update-and-delete).
- **Offered only where writable.** A drag is offered only where the user may write the field. The record API refuses a
  write to a field the user may not write, as it refuses a save.
- **Refusals.** A drop can be refused by a grant, a field write grant, a validation, a module contract or a stale
  version. The record API gives each refusal its answer, see [concurrency](record-api.md#concurrency) and
  [when a write is refused](module-contracts.md#when-a-write-is-refused).
- **Result.** A refused drop puts the item back where it was and shows the message that the record API gives.
- **Triggers.** A dropped update starts the data change triggers of the entity, as any update does, see
  [create, update and delete](record-api.md#create-update-and-delete).

## Calendar

A calendar shows the records of an entity by a date, one month at a time. A drag moves a record to another day.

```json
{ "type": "calendar", "name": "leave", "entity": "LeaveRequest", "date": "startDate", "prefix": "leave" }
```

- **Date field.** `date` names a `date` or a `date-time` field of the entity.
- **Month.** The view shows one month, kept in the address as `month` in the form `yyyy-MM`, such as `?month=2026-11`.
  Without it, the view shows the current month, as the application time zone counts it, see
  [application time zone](entity-logic-and-sequences.md#application-time-zone).
- **Reserved name.** `month` is a key of the calendar's address, so a page parameter named `month` is refused on a page
  with an unprefixed calendar, as a table's query names are reserved.
- **Day.** A `date-time` is placed on the day that the SPA shows it on, in the browser's time zone, as every
  `date-time` is shown, see [shown values](frontend.md#shown-values).
- **Drag.** A drag to another day writes the new date to the field. A `date-time` keeps the time of day that the calendar
  shows, so the drop moves the record by whole days.

## Board

A board groups the records of an entity into columns, one for each group. A drag moves a record from one group to
another.

```json
{ "type": "board", "name": "tickets", "entity": "Ticket", "groupBy": "status", "prefix": "tickets" }
```

- **Group field.** `groupBy` names an `enum` or a `reference` field of the entity.
- **Groups of an enum.** There is one group for each value, in the order the enum declares them.
- **Groups of a reference.** There is one group for each record of the target entity that the user may read, in the
  order of that entity's display field.
- **No value.** A field that is not required also gets a group for the records with no value. A drop on that group
  clears the field.
- **Drag.** A drag between groups writes the field, as [a drag writes the record](#a-drag-writes-the-record) says.
- **Address.** A board keeps `search` and `filter`, as a table does, see [search](table-widget.md#search) and
  [filter](table-widget.md#filter). With a `prefix`, they are `tickets.search` and `tickets.filter`.

## Cards

Cards show the records of an entity as tiles, one tile for each record.

```json
{ "type": "cards", "name": "products", "entity": "Product", "fields": ["category", "price"], "prefix": "products" }
```

- **Fields.** `fields` names the fields that each tile shows, under its title.
- **Address.** Cards keep `search`, `filter`, `page`, `pageSize` and `sort`, as a table does, and follow the same rules,
  see [paging and sorting](table-widget.md#paging-and-sorting).
- **No drag.** A tile cannot be dragged. It opens its item through the first `navigate` row action, as every view does.

## Tree

A tree shows the records of an entity under the record that each one refers to.

```json
{ "type": "tree", "name": "categories", "entity": "Category", "parent": "parentCategory" }
```

- **Parent.** `parent` names a `reference` field of the same entity. A record with an empty parent is a root.
- **Children.** A parent's children are read when the parent is opened in the tree, so the tree loads one level at a
  time.
- **Address.** A tree keeps nothing in the address and has no `prefix`.
- **No drag.** A tree does not move a record, so it has no drag.
