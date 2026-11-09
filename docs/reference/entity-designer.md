# Entity designer

The entity designer is the Form tab of an `entity` resource in Studio, in place of the generic form.
It shows the entity's details, its fields as a table, its validations and its indexes, and the JSON tab
edits the same draft. The rest of Studio is in [Studio](studio.md).

## Sections

_(planned for M31)_

The Form tab is sections, as [the resource form](studio-resource-form.md#sections) has them: General,
Fields, Validations and Indexes. Sections suit an entity because its parts are small, and tabs are for
parts that are large and apart. The details are inputs on the page, with no step to edit them.

General holds the name, the label, the description and the display field, each with its help. The name
and the display field are an [`AxisField`](actions-and-controls.md#fields), and the label and the
description are an `AxisFieldGroup` of [label texts](studio.md#label-texts). The display field is a
[picker](studio.md#reference-pickers) of the draft's fields. The section does not show `id`, `kind` or
`formatVersion`.

## Fields

- **The table** _(planned for M31)_. The fields are the [standard table](actions-and-controls.md#tables),
  one row per field, in the file's order, so every field stays in sight however many settings it has. Its
  columns are Name, Label, Type, Required, Unique and Properties. It has a search, and Add field sits in
  the section's heading. Each row's menu has Move up, Move down and Remove, and each of these replaces
  the `fields` array. A field's name opens the [field drawer](#field-drawer), beside the table.
- **Type in words** _(planned for M31)_. The cell reads the type's name, and for a reference, references or child
  collection the entity it points to, by its label, as "Reference to Department" or "Child collection
  of Line item". The name shows only where that entity has no label. Enum and every other type read as
  their name alone, with no count.
- **The target is a link** _(planned for M31)_. In the Type cell the target's label is a link that opens the entity, and a
  module's entity at its module view. The field's name stays the control that opens the drawer.
- **Required and Unique read Yes or No** _(planned for M31)_, the words a record's boolean reads, as a No is a value and
  the dash means none. They are columns because every field has them and an author compares them down
  the table. A setting only some fields have is a tag under Properties.
- **Properties** _(planned for M31)_. The tags say where a field's value comes from, at most one of Computed, Sequence,
  Creator, Matched, User and Default, then Searchable and Sensitive.
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

## Field drawer

### The drawer and its groups

_(planned for M31)_

A field's settings open in a drawer of the `medium` width with the table still in sight, as
Salesforce's Object Manager, Dataverse and Strapi have it. The drawer has an address, `&field=<name>`,
so Back reopens it and a link can open a field. A migration warning is at the top of the drawer.

The settings are in groups, each setting an [`AxisField`](actions-and-controls.md#fields) with its help,
and the value labels an `AxisFieldGroup` with a label for each value. A setting that the field's type does not take does not show, as the
[field types](resource-files.md#entity-field-types-and-constraints) say, and a group with none is left
out. What has an effect comes before what it affects: the type decides which settings there are, and
where the value comes from decides whether Required applies.

| Group              | Settings                                                                                                                                                         |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| General            | Name, label, type and help text. The label and the help are [label texts](studio.md#label-texts)                                                                  |
| The type's         | Titled by the type, as "Reference settings": the target entity, `values` with `valueLabels`, the currency, `precision` and `scale`, and `maxLength`                |
| Value              | The value comes from, then only the chosen way's settings                                                                                                         |
| Validation         | Checks on an entered value: Required, Unique, `minLength`, `min` and `max`, a file's `maxSize` and `accept`, then the validations at this field                     |
| Search and privacy | `searchable`, `sensitive`, and Exact lookup once the field is sensitive                                                                                            |

Pairs that belong together share a row: `min` with `max`, and `precision` with `scale`.

### Settings

These settings take a control of their own:

| Setting             | Control                                                                                                                                                                                   |
| ------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `default`           | The [expression editor](studio.md#expression-editor), on text, integer, decimal, money, boolean, date, date-time and enum fields                                                                            |
| `expression`        | The expression editor                                                                                                                                                                     |
| `min` and `max`     | A number input on integer, decimal and money fields, a date picker that writes `yyyy-MM-dd` on date fields, and a date-time picker that writes an RFC 3339 UTC string on date-time fields |
| `minLength`         | A number input, on text fields                                                                                                                                                            |
| `searchable`        | A switch, on text fields                                                                                                                                                                  |
| `maxSize`, `accept` | On a `file` field, the largest size in bytes and a list of media types                                                                                                                    |
| Currency            | On a `money` field, a choice between a fixed `currency` code and a `currencyField`, picked from the entity's `enum` and `text` fields, through `AxisChoiceField`                           |
| `valueLabels`       | On an `enum` field, rows of a value and its label, typed as [label texts](studio.md#label-texts)                                                                                          |

- **The value comes from one choice** _(planned for M31)_. The compiler refuses two ways together (`AXC0013`), so the Value
  group is one `AxisChoiceField` of the ways the type takes, each with a line of help: Entered by people,
  Computed, Sequence, Creator, and Matched for a reference. Only the chosen way's settings follow: a
  `default` and Holds a user ([`userId`](resource-files.md#entity-field-types-and-constraints)) for an
  entered value, the `expression`, the `sequence`, or Find by and From for a
  [`match`](resource-files.md#matched-references). Required shows only for an entered value. Choosing a
  way rewrites the field once, dropping the other ways' keys and those only an entered value may have.
- **Matched** _(planned for M31)_. Find by offers only the target entity's unique fields, from `unique` in the
  [names](authoring.md#names), and From only this entity's fields of the same type that the compiler
  allows (`AXC0362`).
- **Maximum length is the type's** _(planned for M31)_. `maxLength` is the size of the stored text, which a value the server
  sets keeps too, so it is under Text settings, as precision and scale are of a number. Validation holds
  only checks on an entered value.
- **A validation at its field** _(planned for M31)_. A validation is a rule on the whole record, and its `field` only says
  where its message shows. The file stays as it is, and the designer shows it twice: the drawer's
  Validation group lists the validations at this field, each a box with its name, Delete validation, its
  expression and its message, typed as texts, and Add validation fills `field`. The entity's
  [Validations](#validations) section keeps every one.
- **A default has no record in scope,** so its editor offers rules and functions but no field names and
  no aggregate functions, and a field name in it is reported as unknown (`AXC0046`).
- **A money field holds one currency.** Field writes an empty `currencyField` and removes `currency`, and
  Fixed code removes `currencyField`. A typed code is turned into upper case.
- **An enum keeps a removed value's label.** Removing a value keeps its label in `valueLabels`, and
  Validate reports it (`AXC0242`).
- **A key already in the file stays** _(planned for M31)_. A `default`, `min`, `max` or `minLength` on a field with an
  `expression`, a `sequence` or `creator` stays until the author changes the way, and its diagnostic opens
  at the field.
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

- **Inside a field** _(planned for M31)_. A diagnostic inside a field opens the
  [field drawer](#field-drawer) at the setting.

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
- **Expressions.** A `convert` or `fill` question asks for the expression in a plain text box, a required
  field whose help is the question's hint. The button that adds it stays on, and a click
  [checks the field](actions-and-controls.md#fields): with the box empty it asks for a value and
  answers nothing. A Skip button adds nothing for that field, and Cancel stops the whole
  save and keeps the draft. Studio does not check the expression, and the save's diagnostics report a
  bad value at `/changes/{i}/value`.
- **Warnings.** A loaded field that switches to or from `enum`, or gains `unique`, shows a warning as
  soon as the author edits it, at the top of its [drawer](#field-drawer), and the save adds no migration
  for it.
- **The JSON view.** A rename typed in the JSON view reads as a removal and a new field, because that
  view gives no row identity, so the removal question still guards the data.

## Module entities

A module entity, such as `organization/Department`, opens in the designer with the application's
[customization layer](customization-layers.md#in-studio) applied, from the
[module resource route](authoring.md#routes), and every input is on. An expression and a label show as
plain text inputs, because the module resource is not one of the folder's. There is no JSON tab, no
Delete, no migration questions and no Used by tab.

- **Its heading** _(planned for M31)_. The page heading shows the entity's label, not
  `organization/Department`.
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
