# Studio form editors

This page describes the editors of Studio that keep the generic form and add one part to it: the seed's grid of
records, the rows of an endpoint's `values` and the steps of a form. What Studio's other editors do is in
[Studio](studio.md), and the form they share is in [Studio resource form](studio-resource-form.md).

## Seed editor

The seed editor's Form tab shows the seed's name, entity and `sync`, then its records as a grid in a part
headed Records, with a rule above it. The grid has a row for each record and a column for each field of the
seed's `entity`, and the JSON view stays as the second view of the same draft.

- **Columns.** The input fits the field's type: a text box for `text`, `date`, `date-time` and
  `reference`, a text box that stores a number for `integer`, `decimal` and `money`, a true or false
  choice for `boolean`, and a choice of `values` for `enum`. A `reference` box checks the UUID form.
  Each input follows the [record API's value rules](record-api.md#request-bodies-and-values).
- **No column.** A computed field, a `sequence` field, a `creator` field and a `child-collection` field
  get no column, because seeding rejects a value for the first three, and child collections stay as the
  file has them. A value the grid does not show, an unknown key included, is saved unchanged.
- **Rows.** Add record adds a row with a new record id, and Remove deletes the record from the draft.
  Clearing a cell removes its key from the record's `values`, so the grid never writes `null`.
- **Filter.** The grid has a Filter on the Id and every column, each by its kind of value: text for `text`,
  `reference` and the Id, number for `integer`, `decimal` and `money`, a choice of Yes or No for `boolean`, a choice
  of `values` for `enum`, and date for `date` and `date-time`. A `date-time` filters on its first ten characters, its
  day, and a value of the wrong type counts as empty. The filter lives in the address as `records.filter`. It only
  hides rows: every row shows on one page with no pager, and an edit to a hidden row stays in the draft. A record
  whose id is not among the ids as last saved stays shown, so a record just added never disappears. The grid has no
  search, sort or Columns.
- **Save.** A cell whose value would fail seeding, because it has the wrong type, is out of bounds or
  is missing a required value, shows an error, and a click on Save focuses the first such cell and sends
  nothing, also when the value comes from the JSON view.
- **Fallback.** The entity is found by name among the folder's `entity` resources, ignoring letter
  case. When it is not there, as for a module entity or a typo, the seed uses the generic form and the
  JSON view.

## Endpoint rows

An endpoint keeps the generic form for its entity, process, input and output, and shows its `values`
as rows of a field and an expression in the [expression editor](studio.md#expression-editor). The rows sit in a
part headed Values, with help that says what they are. An endpoint with no values says so in one line, above
Add mapping. Its `event` sits in a part headed Event, below the values.

The field is picked from the fields of the endpoint's entity, found by name among the application's
names, and is typed while those are unknown. Add mapping lists the fields that have no row. Renaming a
row's field keeps its place in the file, and a value that is not an object is left to the JSON view.

## Form steps

A form keeps the generic form for its entity, sections and fields, with an
[expression editor](studio.md#expression-editor) for each `visibleWhen`, `requiredWhen` and `readOnlyWhen`, and
shows its `steps` as cards. The [form designer](form-designer.md) edits the same draft on a canvas.

The cards sit in a part headed Steps. Each step card has a title, a section picker, Move up, Move down and
Remove, and Add step appends a card. The picker lists the sections that have a `name`, so an unnamed section is
not offered. Removing the last step removes `steps`, and the form goes back to one page. A condition offers the
fields that the form lists, because a condition reads only those. A `steps` value that is not a list is left to
the JSON view.
