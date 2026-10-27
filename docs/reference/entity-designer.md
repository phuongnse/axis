# Entity designer

Detailed reference for the entity designer of Studio: the Form tab of an `entity` resource. The rest of Studio
is in [studio.md](studio.md). The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

- **Entity designer** _(built)_. An `entity` resource opens in a designer on
  the Form tab in place of the generic form. The JSON tab stays and edits the
  same draft. See
  [D54](../decisions/d54-the-entity-designer-and-the-policy-matrix.md).
  - The header shows the entity's name, its label with a text box per locale,
    and a display field picker. The picker lists the field names of the draft.
    It does not show `id`, `kind` or `formatVersion`.
  - The fields are a table. Its columns are name, label, type, required,
    unique and a settings column. A field's label is a picker of the existing
    text keys. The settings column shows only what the row's type takes:
    `maxLength`, `minLength`, `min` and `max`, `precision` and `scale`,
    `values`, `target`, `expression`, `default`, `sequence`, `creator`,
    `searchable` and `sensitive`. The settings follow the
    [field types](resource-files.md#entity-field-types-and-constraints), so a
    setting a type does not take cannot be edited. The settings
    column also has a field's [`help`](resource-files.md#resource-file-shape)
    on every type, a picker of the existing text keys like the label.
  - The type picker also offers `money`, `file` and `references`, and a child
    entity's fields may be `reference`. A `references` field has the same
    target picker as a `reference` field. The settings column adds, for a
    `file`, `maxSize` in bytes and an `accept` list of media types. For a
    `money` field it adds a choice between a fixed `currency` code and a
    `currencyField`, picked from the entity's `enum` and `text` fields. The
    field holds only the one that is chosen: Field writes an empty
    `currencyField` and removes `currency`, and Fixed code removes
    `currencyField`. A typed code is turned into upper case. For an `enum` it
    adds a label picker for each value, which writes `valueLabels`. Removing a
    value keeps its label, and Validate reports it (`AXC0242`). A test compares
    the field types and properties of the designer with `entity.schema.json`,
    so a property that a later issue adds cannot be left out by mistake. See
    [types and properties added in M15](resource-files.md#types-and-properties-added-in-m15).
  - `default` uses the [expression editor](../decisions/d52-reference-pickers-label-texts-and-the-expression-editor.md)
    on text, integer, decimal, money, boolean, date, date-time and enum fields.
    A default has no record in scope, so the editor offers rules and functions
    but no field names and no aggregate functions, and a field name in it is
    reported as unknown (`AXC0046`). `min` and `max` are a number input on
    integer, decimal and money fields, a date picker that writes `yyyy-MM-dd`
    on date fields and a date-time picker that writes an RFC 3339 UTC string on
    date-time fields. `minLength` is a number input and `searchable` is a
    switch, both on text fields. See
    [D77](../decisions/d77-indexes-defaults-and-ranges.md) and
    [D80](../decisions/d80-search-more-aggregates-and-query-timeouts.md).
  - A field with an `expression`, a `sequence` or `creator` does not show
    `default`, `min`, `max` and `minLength`, because the compiler refuses the
    combination (`AXC0013`). A key that is already in the file stays, and its
    diagnostic opens at the field's row.
  - An Indexes list under the fields adds, moves and removes the entity's
    `indexes`. Each index picks the entity's fields in order and has a Unique
    checkbox. The picker offers only fields that have a column, so it leaves
    out `child-collection`, `references` and `file` fields (`AXC0276`). Each
    change writes the whole `indexes` array, and removing the last index
    removes the key.
  - Changing a field's type drops the settings the new type does not take and
    keeps the rest, except that `min` and `max` are dropped when the limit
    changes between a number, a date and a date-time. A `default` stays, and
    Validate reports a type mismatch at it. It does not add an empty `values` or `target`, and Validate
    reports the missing setting.
  - Clearing a field's label, the display field, `maxLength`, `precision`,
    `scale`, `maxSize`, `accept`, `sequence`, `expression`, `default`, `min`,
    `max`, `minLength`, the last enum value or the last value label removes the
    key. Turning `searchable` off and unchecking an index's Unique also remove
    the key, they do not write `false`.
  - A computed field's `expression` and each validation's expression use the
    [expression editor](../decisions/d52-reference-pickers-label-texts-and-the-expression-editor.md).
    A validation also has a message key picker and a field picker, and can be
    added and removed.
  - Fields can be added, removed and moved up or down. Each of these replaces
    the `fields` array.
  - Every input keeps its `studio-field-<pointer>` id, and each row has the id
    of its field, so a diagnostic opens at its input, or at the row when the
    row does not show the setting.
  - A module entity, such as `departments/Department`, opens in the designer
    read-only, from the [module resource route](authoring.md#routes). Every input is
    off, and it has no Add field, Remove, Move, Add validation, Save or Delete
    button, no JSON tab and no Uses or Used by lists. A label shows its text key.
