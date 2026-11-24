# Entity designer

The entity designer is the Form tab of an `entity` resource in Studio, in place of the generic form.
It shows the entity's details, its fields, validations and indexes, and the JSON tab edits the same
draft. The rest of Studio is in [Studio](studio.md).

## Typing

A key redraws the box it went into and what shows its value, such as the save bar. The other field rows
and validations stay as they are. A resource of a dozen fields stays quick to type in because of this.
The expression editors read the draft when completion opens, not on each key.

## Sections

The Form tab is sections, as [the resource form](studio-resource-form.md#sections) has them: General,
Fields, Validations and Indexes. Sections suit an entity because its parts are small, and tabs are for
parts that are large and apart. The details are inputs on the page, with no step to edit them.

General holds the name, the label, the description and the display field, each with its help. The name
and the display field are an [`AxisField`](actions-and-controls.md#fields), and the label and the
description are an `AxisFieldGroup` of [label texts](studio.md#label-texts). The display field is a
[picker](studio.md#reference-pickers) of the draft's fields. The section does not show `id`, `kind` or
`formatVersion`.

## Fields

- **The table.** The fields are the [standard table](tables.md#tables), one row per field,
  in the file's order. An entity's field order is its order everywhere else: the file, the default form
  and the lists that name fields in the entity's field order.
- **Reordering** _(planned, #5049)_. Each row starts with a handle named Drag to reorder. Dragging it moves the
  row to its place, and the rows between make room. The keyboard uses the same handle: Space picks the row
  up, the arrow keys move it, Space drops it and Escape cancels, as [tables](tables.md#tables)
  say. While a search or a filter hides rows, the handles are off, and their tooltip says "Clear the search
  and the filter to reorder". The table has no sort.
- **Row menu** _(planned, #5049)_. Each row's "…" menu holds Move up, Move down and Delete, the way to move a row
  without a drag. Move up is off on the first row and Move down on the last, and Delete is destructive.
  Each entry replaces the `fields` array. A read-only designer has no menu.
- **Name.** The Name cell is a real link to the field's drawer, at `&field=<name>`, with the field's layer mark
  beside it. A plain click opens the drawer in place. Ctrl+click opens a new tab, and the address can be copied.
  A field with no name has no address, so its name reads "Unnamed field" as a subtle button that opens the drawer.
- **Label** _(planned, #5049)_. The Label cell is the label in the author's language, else in English, read from the
  draft's texts, so a label put in by Done shows before Save writes it. The dash shows where the field has no
  label. The table has no input.
- **Columns** _(planned, #5049)_. Its columns are Name, Label, Type, Required, Unique and Source. It has a search, and
  Add field sits in the section's heading. Add field opens a new field in the side panel, as
  [the field panel](#field-drawer) says.
- **Source** _(planned, #5049)_. The Source column shows where a field's value comes from, in the panel's words: Input,
  Computed, Sequence, Creator or Matched. Every field shows one, so a value people enter reads Input and not
  the dash. The column is filtered by the same ways.
- **Filter** _(planned, #5049)_. The table has a Filter on Name, Label, Type, Required, Unique and Source. Name and Label
  filter as text, Label as its cell shows it. Type filters as a choice of the field types, Person among them. Required
  and Unique filter as a choice of Yes and No, and Source as a choice of its ways. A field whose type does not take
  the setting counts as empty. The filter lives in the address as `fields.filter`. It only hides rows: every row
  shows on one page with no pager, and an edit to a hidden row stays in the draft. A field whose origin is new, one
  added since the last save, stays shown. The table has no sort or Columns.
- **Search**. The search box looks in the name and in the label as the Label cell shows it. It lives in
  the address as `fields.search`. It hides rows as the filter does, except that a field added since the last save
  is not kept, so a search can hide it until its name or label matches.
- **Type in words.** The cell reads the type's name, and for a reference, references or child collection the entity
  it points to, by its label, as "Reference to Department" or "Child collection of Line item". The name shows only
  where that entity has no label. Enum and every other type read as their name alone, with no count. A reference with
  no target yet reads "Reference to" and the dash in place of the target.
- **Person** _(planned, #5049)_. A `text` field with `userId` reads Person, in the Type cell and in the Type filter, as the
  [Type list](#settings) says.
- **The target is a link.** In the Type cell the target's label is a link that opens the entity, and a module's entity
  at its module view. The field's name stays the control that opens the panel.
- **Required and Unique read Yes or No**, the words a record's boolean reads, as a No is a value and the dash means
  none. They are columns because every field has them and an author compares them down the table. Searchable and
  Sensitive are in the panel, under Search and privacy.
- **Changing the type** drops the settings that the new type does not take and keeps the rest, except that `min`
  and `max` are dropped when the limit changes between a number, a date and a date-time. A `default` stays, and
  Validate reports a type mismatch at it. The designer adds no empty `values` or `target`, and Validate reports
  the missing setting.

## Field drawer

_(planned, #5049)_

A field's settings open in a side panel, `SidePanel`, in the shell's right column beside the page, so the table
stays in sight and clickable. At narrow width it stacks under the page instead, see [one side panel frame](shell.md#layout). The panel is titled by the field's name,
or "Unnamed field" while it has none. From the top it holds the migration warning, then the groups General, the
type's settings, Value, Validations, and Search and privacy, see [the drawer and its groups](#the-drawer-and-its-groups).

- **Adding.** Add field opens a new field in the panel, titled "New field". The panel holds a text field with no name
  and adds nothing yet. Its action bar holds Cancel and Add field. Add field in the panel puts the field at the end of
  the table and closes the panel. Cancel and Close add nothing.
- **Held edits.** The panel holds what is typed, the settings and the label texts alike, until its main action puts
  it into the draft: Done for an open field, Add field for a new one. Cancel, Close and opening another item drop what
  is held, so the page shows none of it. The panel asks first when that would drop typed text. Done and Add field
  put the edits in the draft, and Save writes them.
- **Checks.** Done and Add field check the label and the name before anything goes into the draft. The label needs
  words in at least one language, or the panel shows "Enter a label." under its boxes. The name must be there, or
  "Enter a name." shows. It starts with a letter and has only letters and digits, or "A name starts with a letter and
  has only letters and digits." shows. No other field of the entity may have it, ignoring letter case, or "Another
  field has this name." shows. The pattern is the one in the schema that Studio loads. The panel stays open while
  one is wrong, and the focus goes to the first box in error. Nothing is checked while a person types.
- **Names follow labels.** A new field's name follows its label until the name is changed by hand. The label's
  words lose their marks, "đ" reads as "d", and the words are joined in camel case, so "Ngày giao hàng" becomes
  `ngayGiaoHang` and "Amount with tax" becomes `amountWithTax`. An existing field keeps its name when its label changes.
- **Focus.** The panel opens with the focus on Label, its first box, whether a field opens from the table, from another
  item in the open panel, or as a new field. A focus path, such as a diagnostic's, goes to its own setting. Cancel and
  Close return the focus to the field's name in the table, and a read-only panel takes the focus itself.
- **One panel at a time.** Opening a field closes a validation's or an index's panel, and the other way round.
- **Address.** The panel has an address, `&field=<name>`, so Back closes it, Forward reopens it, and a link can open a
  field. Opening and closing it each add a history entry. Done renames the field in the address, and a field with no
  name has no `field`. A name that no field has opens nothing and is dropped from the address with a replace, see
  [route and address](studio.md#route-and-address).
- **Delete and move.** Delete closes the panel when it deletes the panel's own field. When it deletes another field,
  the panel stays on its field, which moves up a row if it came after the deleted one. Move up and Move down keep the
  open panel on its field, at the field's new row. The panel does not follow its field on Undo, Redo or an edit in the
  JSON tab: it stays on its row, and shows as closed when that row is gone.
- **Read-only.** A read-only designer opens the panel from the name too, with every input in it off.

### The drawer and its groups

_(planned, #5049)_

A field's settings are in groups. Each group is a `SectionPart`, with a heading and a line under it, and a
`borderSubtle` rule runs between the groups from the panel's edge to its scrollbar. Fields in a group sit
`spacing-md` apart. Each setting is an [`AxisField`](actions-and-controls.md#fields) or `AxisCheckboxField` with
its help, and the value labels are an `AxisFieldGroup` with a label for each value. A setting that the field's type
does not take does not show, as the [field types](resource-files.md#entity-field-types-and-constraints) say, and a
group with none is left out. What has an effect comes before what it affects: the type decides which settings there
are, and where the value comes from decides whether Required applies.

| Group              | Settings                                                                                                                                                                                  |
| ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| General            | Label (required), Name, Type and Help text, in that order                                                                                                                                  |
| The type's         | Titled by the type, as "Text settings": the target entity, `values` with their labels, the currency, `precision` and `scale`, `maxLength`, `minLength`, `min` and `max`, and a file's `maxSize` and `accept` |
| Value              | Source, then only the chosen way's settings: Required, Unique and Default for Input                                                                                                       |
| Validations        | The validations at this field, each a link to its panel, with Add validation on the title row                                                                                             |
| Search and privacy | `searchable`, `sensitive`, and Exact lookup once the field is sensitive. Folded and closed at first, its title followed by "· Searchable, Sensitive" or "· Off"                          |

- **A pair shares one row.** Two settings that belong together sit on one row under one title and one help, and take
  no help of their own, so their boxes line up: Length (Min length and Max length), Range (Min and Max) and Digits
  (Total digits and Decimal places).
- **A type's group holds every plain limit of a value.** A limit that holds only for an entered value, such as Min
  length, Min and Max, shows only for Input. Validation keeps the checks at the field, see Validations below.
- **Value's settings.** Required, Unique and Default show only for Input, and sit under the way the value comes from.
  Every box in the group is as wide as the others.
- **Search and privacy** folds, closed at first, and its settings show after its title, so a privacy setting is
  never out of sight unseen.
- **Validations** lists the validations whose `field` is this field, each a link that opens its panel. Add validation
  opens a new validation at this field. With none, the group says "No validation shows its message at this field yet."
  A new field that is not yet added has no validations and no Add validation.

### Settings

These settings take a control of their own:

| Setting             | Control                                                                                                                                                                                   |
| ------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `default`           | The [expression editor](studio.md#expression-editor), on text, integer, decimal, money, boolean, date, date-time and enum fields                                                          |
| `expression`        | The expression editor                                                                                                                                                                     |
| `min` and `max`     | A number input on integer, decimal and money fields, a date picker that writes `yyyy-MM-dd` on date fields, and a date-time picker that writes an RFC 3339 UTC string on date-time fields |
| `minLength` and `maxLength` | A number input, on text fields                                                                                                                                                    |
| `searchable`        | _(planned, #5049)_ A checkbox with its label on the right and its help under the label, on text fields                                                                                          |
| `maxSize`, `accept` | On a `file` field, the largest size in bytes and a list of media types                                                                                                                    |
| Currency            | On a `money` field, a choice between a fixed `currency` code and a `currencyField`, picked from the entity's `enum` and `text` fields, through `AxisChoiceField`                           |
| `valueLabels`       | On an `enum` field, rows of a value and its label, each label typed as [label texts](studio.md#label-texts) under "Label of {value}"                                                      |

- **The Type list** _(planned, #5049)_. Each type shows its name in words, with a short line of help under it, such as
  "Whole numbers, for counting." under Whole number and "Links to one record of another entity." under Reference.
  The list is in this order: Text, Whole number, Decimal, Money, Yes or no, Date, Date and time, Enum, Person,
  Reference, References, Child collection and File. It can be searched by name. The types include every
  [field type](resource-files.md#entity-field-types-and-constraints), money, file and references among them, and a
  child entity's fields may be `reference`. A `references` field has the same target picker as a `reference` field.
  A test compares the field types and settings of the designer with `entity.schema.json`, so a setting added to the
  schema cannot be left out of the designer by mistake.
- **Person** _(planned, #5049)_. Person is "Someone who uses the application, picked by name". It writes a `text` field with
  `userId: true` and no `maxLength`, `minLength` or `searchable`, and the panel has no Text settings for it. Choosing
  another type drops `userId`.
- **Source** _(planned, #5049)_. The Source select has a line of help under each way: Input, Computed, Sequence, Creator and,
  for a reference, Matched. The compiler refuses two ways together (`AXC0013`), so one way is chosen, and only its
  settings follow, at full width. Input shows Required, Unique and `default`. Computed shows the `expression`, Sequence
  the `sequence`, and Matched Find by and From, see below. A Person's Source offers Input and Creator, and a text
  field does not offer Creator. Creator writes `creator: true`, and it drops `userId` as the compiler takes either but
  not both. Choosing a way rewrites the field once, dropping the other ways' keys and those only an entered value may
  have, and writing an empty key for the chosen way: `expression`, `sequence`, `creator: true` or a `match` with an
  empty `field` and `from`. Choosing the way the field has changes nothing. Creator is only a way, never a checkbox.
  Exact lookup shows only once the field is sensitive, and unticking Sensitive removes `exactLookup` in the same change.
- **Matched**. Find by offers only the target entity's unique fields, from `unique` in the
  [names](authoring.md#names), and From only this entity's fields of the same type that the compiler
  allows (`AXC0362`): for a reference, the same target too, and never the field itself or a sensitive, matched or
  computed field. Both show the required mark. They are plain boxes until the names have loaded, and From offers
  nothing while Find by names no unique field of the target.
- **Maximum length is the type's**. `maxLength` is the size of the stored text, which a value the server
  sets keeps too, so it is under Text settings, as precision and scale are of a number. Only the checks on an entered
  value are Value's, and they show only for Input.
- **Label and help text** _(planned, #5049)_. Both are label texts, typed in words in the author's language, with the
  Translations button inside the box, as the entity's own label is. Studio shows words and never a text key, see
  [names](studio-workspace.md#names).
- **A default has no record in scope,** so its editor offers rules and functions but no field names and
  no aggregate functions, and a field name in it is reported as unknown (`AXC0046`).
- **A money field holds one currency.** From a field writes an empty `currencyField` and removes `currency`, and
  Fixed code removes `currencyField`. A typed code is turned into upper case.
- **An enum keeps a removed value's label.** Removing a value keeps its label in `valueLabels`, and
  Validate reports it (`AXC0242`).
- **A key already in the file stays.** A key that the chosen way hides, such as a `default`, `required`,
  `userId`, `min`, `max` or `minLength` on a field with an `expression`, a `sequence` or `creator`, or another
  way's key, stays in the file, hidden. Validate reports it.
- **Its diagnostic**. The key stays until the author changes the way, and the diagnostic opens the panel at the
  field, with focus on the panel, as the [diagnostics](#diagnostics) rule says for a setting the panel does not show.
- **Clearing removes the key.** Clearing a field's label, the display field, `maxLength`, `precision`,
  `scale`, `maxSize`, `accept`, `default`, `min`, `max`, `minLength`, the last
  enum value or the last value label removes the key. Clearing the chosen way's `expression` or `sequence`
  leaves it empty instead, so the field keeps its way, and Validate reports it. Turning `searchable` off and
  unticking Unique index also remove the key, rather than write `false`.

## Required keys

The keys that `entity.schema.json` requires show the shared `*` and are announced as required: the entity name,
an index's fields, and a validation's expression and message. So do the target of a reference field, the currency
field of a money field, the chosen way's expression and sequence, and a matched reference's Find by and From,
which cannot be emptied.

- **Names and labels** _(planned, #5049)_. The name of an index or a validation is typed in its panel and shows the
  required mark, as do a field's label, a validation's message and an index's fields. A validation's `field` is
  optional, so it shows no mark. A field's name and type, which the schema requires, show it in the
  [field panel](#field-drawer).

## Indexes

_(planned, #5049)_

The Indexes section, after Validations, is a table of the entity's `indexes`. Its columns are Name, Fields, by their
labels in the index's order, and Unique, Yes or No. Rows are ordered by dragging, as [the Fields table](#fields) is,
and each row's "…" menu holds Move up, Move down and Delete, with Move up off on the first index and Move down on the
last. Empty, it shows the one [empty state](actions-and-controls.md#showing-what-is-empty), "No indexes yet.". A name
opens the index's panel at `&index=<name>`.

The index's panel holds Name, Index fields and Unique index. Index fields is a multi-choice of the fields by their
labels, in the order picked. It does not offer `child-collection`, `references` and `file` fields, which have no column
(`AXC0276`). Add index opens a new index in the panel and adds it only by its own Add index. The author names the index,
with a name that starts with a letter, has only letters and digits and is not used by another index of the entity,
ignoring letter case. The errors read "Enter a name.", "A name starts with a letter and has only letters and digits."
and "Another index has this name.". An index needs at least one field, "Pick at least one field.". Each change writes
the whole `indexes` array, and deleting the last index removes the key.

## Validations

_(planned, #5049)_

The Validations section is a table of the entity's validations, after Fields. Its columns are Name, Message, in the
author's language, else in English, and Field, by its label. A validation of the whole record has no field, so its
Field cell shows the dash. Rows are ordered by dragging, and each row's "…" menu holds Move up, Move down and Delete.
Empty, it shows the one empty state, "No validations yet.". A name opens the validation's panel at
`&validation=<name>`.

The validation's panel holds Name, Validation expression, Message and Field. The expression is in the
[expression editor](studio.md#expression-editor). The message is typed as label texts, with its Translations button
inside the box. Field is picked by its label, and its help says "Leave it empty to show the message at the top of the
form". Add validation opens a new validation in the panel and adds it only by its own Add validation, which needs a
name and a message. Cancel and Close add nothing.

- **The author names a validation.** A name starts with a letter, has only letters and digits, and is not used by
  another validation of the entity, ignoring letter case. The errors read "Enter a name.", "A name starts with a letter
  and has only letters and digits." and "Another validation has this name.". Studio does not make a name for it.
- **A validation of the whole record names no field.** Its message shows at the top of the form, and its failure is
  reported at `/values`, see [child rows and validations](record-child-rows-and-validations.md#child-rows-computed-fields-and-validations).

## Diagnostics

Every input keeps its `studio-field-<pointer>` id, and each row has the id of its field, so a diagnostic
opens at its input, or at the row when the row does not show the setting.

- **In a field** _(planned, #5049)_. A diagnostic at a setting inside a field, such as `/fields/2/maxLength`, opens that
  field's [side panel](#field-drawer) with focus at the setting. A focus path inside the folded Search and privacy
  opens that group first. When the panel does not show the setting, focus goes to the panel.
- **In a validation or an index** _(planned, #5049)_. A diagnostic inside `/validations/{i}` or `/indexes/{i}` opens that
  item's panel at the setting, as a field's diagnostic opens its panel.

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
  soon as the author edits it in its panel, at the top of the panel, and the save adds no migration
  for it.
- **The JSON view.** A rename typed in the JSON view reads as a removal and a new field, because that
  view gives no row identity, so the removal question still guards the data.

## Module entities

A module entity, such as `organization/Department`, opens in the designer with the application's
[customization layer](customization-layers.md#in-studio) applied, from the
[module resource route](authoring.md#routes), and every input is on. An expression and a label show as
plain text inputs, because the module resource is not one of the folder's. There is no JSON tab, no
Delete, no migration questions and no Used by tab.

- **Its heading.** The page heading shows the entity's label in the author's language, with English as the fallback.
  Without a label it shows the name without the `<module>/` prefix, never `organization/Department`.
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
