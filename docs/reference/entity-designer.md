# Entity designer

The entity designer is the Form tab of an `entity` resource in Studio, in place of the generic form.
It shows the entity's header, its fields as a table, its indexes and its validations, and the JSON tab
edits the same draft. The rest of Studio is in [Studio](studio.md).

## Header

The header shows the entity's name, its label with a text box for each locale, and a display field
picker that lists the field names of the draft. It does not show `id`, `kind` or `formatVersion`.

## Fields

The fields are a table with one row per field, whose columns are name, label, type, required, unique
and settings. A field's label is a picker of the existing text keys. Fields can be added, removed and
moved up or down, and each of these replaces the `fields` array.

- **Types.** The type picker offers every
  [field type](resource-files.md#entity-field-types-and-constraints), `money`, `file` and `references`
  among them, and a child entity's fields may be `reference`. A `references` field has the same target
  picker as a `reference` field. A test compares the field types and properties of the designer with
  `entity.schema.json`, so a property added to the schema cannot be left out of the designer by
  mistake.
- **Changing the type** drops the settings that the new type does not take and keeps the rest, except
  that `min` and `max` are dropped when the limit changes between a number, a date and a date-time. A
  `default` stays, and Validate reports a type mismatch at it. The designer adds no empty `values` or
  `target`, and Validate reports the missing setting.
- **Left to the JSON tab.** A `reference` field's [`match`](resource-files.md#matched-references) and a
  `text` field's [`userId`](resource-files.md#entity-field-types-and-constraints) are edited in the
  JSON tab.

## Field settings

The settings column shows only the settings that the row's type takes, as the
[field types](resource-files.md#entity-field-types-and-constraints) say, so a setting that a type does
not take cannot be edited. They are `maxLength`, `minLength`, `min` and `max`, `precision` and `scale`,
`values`, `target`, `expression`, `default`, `sequence`, `creator`, `searchable` and `sensitive`, and a
field's [`help`](resource-files.md#resource-file-shape) on every type, a picker of the existing text
keys like the label. Some take a control of their own:

| Setting             | Control                                                                                                                                                                                   |
| ------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `default`           | The [expression editor](studio.md#expression-editor), on text, integer, decimal, money, boolean, date, date-time and enum fields                                                                            |
| `expression`        | The expression editor                                                                                                                                                                     |
| `min` and `max`     | A number input on integer, decimal and money fields, a date picker that writes `yyyy-MM-dd` on date fields, and a date-time picker that writes an RFC 3339 UTC string on date-time fields |
| `minLength`         | A number input, on text fields                                                                                                                                                            |
| `searchable`        | A switch, on text fields                                                                                                                                                                  |
| `maxSize`, `accept` | On a `file` field, the largest size in bytes and a list of media types                                                                                                                    |
| Currency            | On a `money` field, a choice between a fixed `currency` code and a `currencyField`, picked from the entity's `enum` and `text` fields                                                     |
| `valueLabels`       | On an `enum` field, a label picker for each value                                                                                                                                         |

- **A default has no record in scope,** so its editor offers rules and functions but no field names and
  no aggregate functions, and a field name in it is reported as unknown (`AXC0046`).
- **A money field holds one currency.** Field writes an empty `currencyField` and removes `currency`, and
  Fixed code removes `currencyField`. A typed code is turned into upper case.
- **An enum keeps a removed value's label.** Removing a value keeps its label in `valueLabels`, and
  Validate reports it (`AXC0242`).
- **A computed field takes no default.** A field with an `expression`, a `sequence` or `creator` does not
  show `default`, `min`, `max` and `minLength`, because the compiler refuses the combination
  (`AXC0013`). Such a key that is already in the file stays, and its diagnostic opens at the field's
  row.
- **Clearing removes the key.** Clearing a field's label, the display field, `maxLength`, `precision`,
  `scale`, `maxSize`, `accept`, `sequence`, `expression`, `default`, `min`, `max`, `minLength`, the last
  enum value or the last value label removes the key. Turning `searchable` off and unticking an index's
  Unique also remove the key, rather than write `false`.

## Indexes

An Indexes list under the fields adds, moves and removes the entity's `indexes`. Each index shows its
name as read-only text, picks the entity's fields in order and has a Unique checkbox. Add index writes
the name `index1`, `index2` and so on, the first one that no index of the entity has, ignoring letter
case. The picker offers only fields that have a column, so it leaves out `child-collection`,
`references` and `file` fields (`AXC0276`). Each change writes the whole `indexes` array, and removing
the last index removes the key.

## Validations

Each validation shows its name as read-only text, its expression in the expression editor, a message
key picker and a field picker, and can be added and removed. Add validation writes the name
`validation1`, `validation2` and so on, the first one that no validation of the entity has, ignoring
letter case.

## Diagnostics

Every input keeps its `studio-field-<pointer>` id, and each row has the id of its field, so a diagnostic
opens at its input, or at the row when the row does not show the setting.

## Migrations

When a save of the designer changes a field that existed when Studio
loaded the entity, the designer asks what to do with its data and adds the migration change that goes
with the answer, so data is never lost without a choice. "Loaded" means as Studio read the entity, not
the active release. See [migrations](migrations.md#migrations).

| Change to a loaded field                       | Question                                             | Migration change                                          |
| ---------------------------------------------- | ---------------------------------------------------- | --------------------------------------------------------- |
| Rename                                         | Keep its data?                                       | `renameField` with `from` and `to`                        |
| Remove                                         | Drop its data?                                       | `dropField`, or none when the field is kept               |
| Type change                                    | A `convert` expression                               | `convert` with the author's expression                    |
| Narrower `maxLength`                           | A `convert` expression                               | `convert`                                                 |
| Removed enum values                            | A `convert` expression that maps them to kept values | `convert`                                                 |
| Required, or a new required field              | Fill the nulls? A `fill` expression                  | `fill`                                                    |
| To or from `enum`, or `unique` added           | None                                                 | None: the designer warns, and Validate reports `AXC0016`  |
| Rename or removal of a `child-collection` field | None                                                | None, because it has no column                            |

- **The migration resource** is named `<Entity>Change<UTC yyyyMMddHHmmss>`, such as
  `PurchaseRequestChange20261020093000` in `migrations/purchase-request-change20261020093000.json`,
  and holds the changes of one save in edit order. The designer never edits an existing migration.
- **One call.** The save sends the entity patch and the migration create in one
  [`changes` call](authoring.md#changes), and a `409` keeps the draft, as for a single save. Validate
  still runs every activation check, so a missing migration shows before a publish.
- **Order.** The questions come one at a time when the author clicks Save, in the order in which the
  author first edited each loaded field. A chain of renames, such as a to b to c, counts as one rename
  from a to c. A field added in the same session asks nothing, except a `fill` question when it is
  required, and those questions come after the loaded fields, in row order. A new row with the name of
  a removed loaded field counts as that loaded field.
- **Rename and removal.** A rename asks Keep data, Drop data or Cancel, the last two as text buttons
  beside the solid one. Keep data adds `renameField`, Drop data adds `dropField` for the old name, and
  Cancel stops the whole save and keeps the draft. A removal asks Drop data or Keep field, and Keep
  field puts the field back where it was, so the save goes on with no migration for it.
- **One field's questions stay together:** its rename or removal, then its `convert`, then its `fill`.
  A `convert` or `fill` after a rename uses the new name, and several edits of one field ask once. When
  a rename is answered Drop data, the `convert` question is skipped and a required field counts as
  new, so its `fill` is still asked. When a required field is renamed and keeps its data, no `fill` is
  asked.
- **Expressions.** A `convert` or `fill` question asks for the expression in a plain text box, with the
  button that adds it, a Skip button that adds nothing for that field, and Cancel, which stops the whole
  save and keeps the draft. Studio does not check the expression, and the save's diagnostics report a
  bad value at `/changes/{i}/value`.
- **Warnings.** A loaded field that switches to or from `enum`, or gains `unique`, shows a warning in
  its row as soon as the author edits it, and the save adds no migration for it.
- **The JSON view.** A rename typed in the JSON view reads as a removal and a new field, because that
  view gives no row identity, so the removal question still guards the data.

## Module entities

A module entity, such as `organization/Department`, opens in the designer with the application's
[customization layer](customization-layers.md#in-studio) applied, from the
[module resource route](authoring.md#routes), and every input is on. An expression and a label show as
plain text inputs, because the module resource is not one of the folder's. There is no JSON tab, no
Delete, no migration questions and no Uses or Used by lists.

- **Save writes the layer.** Save sends the whole entity to the
  [module save route](module-resource-saves.md), which writes only the difference as the layer. It is
  off until the draft differs from what was loaded. A save that the server refuses shows the server's
  reason, and a `409` shows the conflict alert with Reload. A save that adds a field may need a
  migration, which Validate or Publish reports.
- **Customized and Reset all.** When the entity has a layer, a Customized tag and a Reset all button show
  in the header. Reset all asks first, then deletes the layer file, and the designer shows the module's
  version again.
- **Marks.** A field, an index or a validation that the layer added or changed shows **Added** or
  **Changed**, found by its name. One that the layer removed shows in a muted **Removed** row under its
  list. Each has **Reset to module**, which calls the
  [element reset](module-resource-saves.md#element-reset) route and reloads the entity. It is off while
  the draft has unsaved edits.
