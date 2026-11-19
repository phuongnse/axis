# Form resource and form widget

This page describes the `form` resource, which lays out the fields of one entity, and the `form`
widget, which creates and edits a record through it in the SPA. How the widget follows the form's
conditions and steps is in [form widget conditions](form-widget-conditions.md), the conditions
themselves in [form conditions](form-conditions.md), and the rest of the frontend in
[frontend](frontend.md).

## Form resource

A `form` resource lays out the fields of one entity in sections, and any field can be read-only. One
form serves both a page and a task.

```json
{
  "id": "8b3e5c71-4d2a-4f6b-9c0e-1a2b3c4d5e6f",
  "kind": "form",
  "name": "PurchaseRequestReview",
  "formatVersion": 1,
  "entity": "PurchaseRequest",
  "sections": [
    {
      "name": "request",
      "title": { "textKey": "purchaseRequest.sections.request" },
      "fields": [
        { "field": "title", "readOnly": true },
        { "field": "department", "readOnly": true },
        { "field": "supplier", "readOnly": true },
        { "field": "lineItems", "readOnly": true },
        { "field": "totalAmount", "readOnly": true }
      ]
    },
    {
      "name": "decision",
      "title": { "textKey": "purchaseRequest.sections.decision" },
      "fields": [{ "field": "decisionComments" }]
    }
  ]
}
```

- **Sections and fields.** Each section has a `name`, a `title` label and its `fields`, in the order
  shown. Each entry names a `field` of the entity, and `readOnly` is optional and `false` by default.
  A [customization layer](customization-layers.md#list-keys) finds a section by its `name` and a field
  entry by its `field`.
- **Each field once.** A field appears at most once in the form. A field the form does not list is not
  shown and never sent.
- **Always read-only.** Computed, sequence, creator and matched reference fields are always read-only,
  whatever `readOnly` says.
- **Who enforces `readOnly`.** On a page, `readOnly` only shapes the UI: the record API enforces the
  user's field write grants, so it still accepts a read-only field that an `update` grant covers. In a
  task, the server enforces it: completing a task refuses a value for a field the form does not make
  editable, see the [task API](task-api.md).
- **Conditions and steps.** Fields may have `visibleWhen`, `requiredWhen` and `readOnlyWhen`, sections
  may have `visibleWhen`, and a form may have `steps`, which the widget shows as a wizard, see
  [form conditions and steps](form-conditions.md#form-conditions-and-steps).
- **Tabs** _(planned for M42)_. `tabs` is an optional list of `{ "name", "title", "sections" }`, shaped like `steps`,
  as in `"tabs": [{ "name": "main", "title": { "textKey": "order.tabs.main" }, "sections": ["request"] }]`. When it is
  present, every section is in exactly one tab. Tabs show in list order, and a tab whose sections are all hidden is
  left out. After a failed save the form opens the first tab with an error. The open tab stays out of the URL, as the
  [current step](form-widget-conditions.md#steps) does. The tabs are drawn as a page's [tabs](container-widgets.md#tabs)
  are. The compiler refuses a repeated tab name, an unknown or repeated section, a section in no tab, and `tabs` beside
  `steps`. Each tab's `title` is a text key and joins the text key check. As [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md) agrees.
- **Folding** _(planned for M42)_. A section with `"collapsed": true` starts closed, as the `collapsed` property of a
  page's [section](container-widgets.md#sections) does, and the person can open and close it. Its open state stays out
  of the URL. A failed save opens a closed section that holds an error. As [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md) agrees.
- **Defaults from the context** _(planned for M42)_. A field entry may have `default`. `"user"` on a `text` field marked
  `userId` gives the signed-in person's id, and `"today"` on a `date` field gives today in the
  [application time zone](entity-logic-and-sequences.md#application-time-zone). It applies only to a new record, where
  it replaces the entity's default. A value that the page gives, see
  [first values of a new record](resource-files.md#first-values-of-a-new-record), wins over it. A field that the person
  may not set on create, because it is not in `createFields`, starts empty, as first values do. The compiler refuses
  any other value, a `default` on the wrong field type, and one on a read-only, computed, sequence, creator or matched
  field. As [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md) agrees.

### Fields in columns

A field entry may have `width`, a whole number from 1 to 12, in twelfths of the section, and it is 12
when left out. So `{ "field": "quantity", "width": 4 }` takes a third of the row.

- Fields fill a row from left to right, and the next field that does not fit starts a new row.
- A row is as tall as its tallest field and its fields line up at the top, so a message under one
  field never shifts the rows below.
- Fields of a row are `spacing-md` apart across. Rows and sections are `spacing-lg` apart.
- At narrow width every field takes the whole row, in file order.
- A detail lays out the same sections with the same widths, and Studio's
  [form designer](form-designer.md) edits them.

### Compile checks

The schema and the compiler refuse a form that cannot be shown, with these codes. See the
[configuration pipeline](configuration.md#configuration-pipeline).

| Problem                                                                                                                        | Code      |
| ------------------------------------------------------------------------------------------------------------------------------ | --------- |
| A section without a `name` or a `title`, an empty `sections` or `fields`, or a `width` that is not a whole number from 1 to 12 | `AXC0004` |
| An `entity` that names no loaded entity                                                                                        | `AXC0074` |
| A `field` the entity does not have                                                                                             | `AXC0075` |
| A field listed again, in any section and ignoring letter case                                                                  | `AXC0076` |
| A section title key that no locale has                                                                                         | `AXC0028` |

## Form widget

A page whose widget is a `form` creates a record at `/{site}/{page}/new` and edits one at
`/{site}/{page}/{id}`, through the record API. It adds no rules of its own, because the server
validates.

- **Form binding.** A `form` widget names a `form` resource, or an `entity` as shorthand for all its
  fields in declaration order. Naming both or neither is `AXC0060`, and a `form` that names no loaded
  form is `AXC0077`.
- **With a `form`,** the widget shows only the fields the form lists, in its order, and its sections in
  order, each under its title as a heading. It shows a read-only field as it shows a computed field. A
  read-only child collection shows its rows with every cell read-only, and has no add or remove
  buttons. The widget sends only the editable fields it shows, and a server error on a field the form
  does not show appears above the form.
- **Groups as parts** _(planned)_. A titled group of fields is a part of the page, as
  [parts a rule apart](shell.md#layout) says. Its title is drawn by `SectionPart` as a level-2 heading at the
  `heading5` size, with the part's spacing and no heading margin of its own. A `borderSubtle` rule sits above a
  group drawn after another, and none above the first. A form with one untitled group, such as an `entity`
  form, has no rule. A form too long for one page is split into [steps](form-widget-conditions.md#steps), which check
  each step before the next.
- **Conditions and steps.** The widget asks the server which fields to show, require and lock, and
  which messages to show, and shows `steps` as a wizard, see
  [form widget conditions](form-widget-conditions.md).

## Inputs

There is one input per field, in declaration order, labelled with the field's label or the field name.
A required field's label is marked, but the mark blocks nothing: the server still decides.

| Field type                                                                                                            | Input                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| --------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `text`                                                                                                                | A text input limited to `maxLength`. With `minLength` it also has the `minlength` attribute and shows the character count against the limits, such as `0 / 3–10`, or `0 / ≥3` with no `maxLength`                                                                                                                                                                                                                                     |
| `text` with `multiline` _(planned for M42)_                                                                           | A text area that grows with its text. Enter adds a line and never sends the form                                                                                                                                                                                                                                                                                                                                                      |
| `richText` _(planned for M42)_                                                                                        | An editor with a toolbar that offers only the allowed formats. A read-only value shows its formatting                                                                                                                                                                                                                                                                                                                                 |
| A `text` field marked `userId`                                                                                        | A searchable select of people by display name, from `usersPath`, which searches again as the person types. It sends the picked user id, and a field that is not required can be cleared, which sends null. A stored id shows the person's name, read through the route's `id` parameter, or the id itself when no profile has it. A failed search leaves the list empty                                                               |
| `integer`, `decimal`                                                                                                  | A plain text input with a numeric keyboard hint, which keeps the typed text as it is. With `min` or `max` its placeholder shows the range, such as `1 – 10`, `≥ 1` or `≤ 10`, and a typed number outside the range is marked invalid but never blocked, because the server decides                                                                                                                                                    |
| `money`                                                                                                               | The input of a decimal with the currency code beside the box. The code is `currency`, or the current value of `currencyField` in the form, or in the row of a child collection. Without a code of three letters nothing shows beside the box. It sends the typed text as a plain JSON number                                                                                                                                          |
| `boolean`                                                                                                             | A checkbox                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `date`                                                                                                                | A date picker that shows and accepts the locale's short numeric date, such as `03/18/2026` in `en` and `18/03/2026` in `vi`. With `min` or `max` it disables the dates outside them and its placeholder shows the range                                                                                                                                                                                                               |
| `date-time`                                                                                                           | A date and time picker to the minute, with no seconds column, that shows and accepts the date and the time on the locale's clock, such as `03/04/2026 4:30 PM` in `en` and `04/03/2026 16:30` in `vi`, in the browser's time zone. It sends the instant in UTC with zero seconds and no fraction. With `min` or `max` it disables the dates outside them and its placeholder shows the range                                          |
| `enum`                                                                                                                | A choice of the declared values, as written in the entity file, or their labels with `valueLabels`                                                                                                                                                                                                                                                                                                                                    |
| `file`                                                                                                                | The [file input](file-fields.md#in-the-spa): the current file's name as a download link, an Upload button and, when the field is not required, a Remove button. A read-only file shows its link only, or "Not set" when empty                                                                                                                                                                                                         |
| `reference`                                                                                                           | The label of the chosen record, read-only, with a choose button that opens the [lookup](#lookup), and a clear button when the field is not required and is set. Choose and Clear are icon buttons, a search and a close icon, with their names as tooltips on hover and on keyboard focus. They are bordered buttons joined to the input on its right, in the input's border colour, with the corner radius only on the outer corners |
| `references`                                                                                                          | A list of the chosen records' labels in their order, each with a remove button, and an add button that opens the lookup, see [set of references](#set-of-references)                                                                                                                                                                                                                                                                  |
| `child-collection`                                                                                                    | A table of its rows, see [child collections](#child-collections)                                                                                                                                                                                                                                                                                                                                                                      |
| Any type with `computed`, `sequence` or `creator`, and any field but a child collection that the form makes read-only | The value the server returned, formatted as a table cell shows it, see [field look](#field-look). An empty value shows "Not set" in muted italics. It changes only when the record is saved and loaded again                                                                                                                                                                                                           |

- **Sequence fields.** Page metadata marks a field that names a
  [sequence](entity-logic-and-sequences.md#sequences), so the form shows it read-only. It is empty on a new record,
  and the form never sends it.
- **Creator fields.** Page metadata marks a field that declares
  [`creator`](roles-and-policies.md#record-creator), so the form shows it read-only. It is empty on a
  new record, shows the creating user's id once saved, and the form never sends it.
- **Open a reference** _(planned)_. A `reference` field whose chosen record the person can open has an Open icon button
  with `ExportOutlined`, joined after Choose and before Clear. It is named "Open {label}" ("Mở {label}"), where Open is
  the text `shell.form.openRecord`, and it is a real link to the record's page. A read-only reference is a link too,
  see [opening things](actions-and-controls.md#opening-things). The unsaved changes guard asks before a link leaves a
  form with a change. A record the person cannot open is plain text, with no Open button.

## Field look

These rules hold for every field on a form.

- **Label.** On a form, a field label is medium weight in `textMuted`, as it is on a detail and as a
  KPI's label.
- **Required mark.** A required field has a `*` after its label, in `dangerText`, and the mark is
  hidden from screen readers. An editable input of a required field carries `aria-required`, and a
  read-only value does not. Required means the form state answer's `required`, or else the field's
  `required`.
- **Warning.** A field's warning message and its border take `warningText`, as an
  error's take `dangerText`, see [fields](actions-and-controls.md#fields).
- **Help text.** A field whose entity field has [`help`](resource-files.md#resource-file-shape) shows
  it between the label and the input, in small text in `textMuted`, `spacing-xs` above the input, and
  tied to the input with `aria-describedby`. A file input and a read-only value show it but are not
  tied to it. A message under the input never replaces it. It is one or two short lines, because longer
  guidance belongs on the page or the section. Only a form shows it, not a table or a detail.
- **Validation messages.** A message shows under its input, and the form's layout keeps `spacing-lg`
  between the message and the next field, so the layout grows when a message appears, as every form does,
  see [fields](actions-and-controls.md#fields).
- **Read-only values.** A field the form makes read-only, by `readOnly` or `readOnlyWhen`, and a
  `computed`, `sequence` or `creator` field show plain text, not an input, so nothing looks editable
  that is not. A boolean shows Yes or No. The field keeps the height of an editable field. An empty
  value shows "Not set" in muted italics, see the [shown values](frontend.md#shown-values).
- **Values.** Dates, date-times and numbers follow the [shown values](frontend.md#shown-values). A date
  picker fills its column, as every other input does.

## Lookup

A `reference` field's choose button, a `references` field's add button and a `reference` filter input
open one lookup dialog, which lists the records a person may pick.

- **List.** The dialog is titled with the field's label. It lists the target entity's records through
  their record API, sorted ascending by the target's display field, in one column for that field. The
  shared table draws it, with search and filter in its toolbar, and the pager hides when one page holds every
  record. The record API lists only the target records that match the user's read filters. It pages 20
  records at a time and keeps nothing in the URL. Loading, empty and error states use the table's
  texts.
- **Picking.** Clicking a row or pressing Enter on it picks the record and closes the dialog.
- **Search.** A search box in the table's toolbar filters the records to those whose display field contains
  the typed text, ignoring letter case. It is the table's own Search box, so the search applies on Enter,
  on the search button and when the box is cleared, never on each key. The SPA trims the text and sends
  it as the record API's `search` parameter, and leaves `search` out when the text is empty. A new search
  starts again at page 1. Like paging, the search stays in the dialog, out of the URL, and is empty each
  time the dialog opens.
- **Filter.** The display column has the table's Filter, with text conditions such as is, contains and is
  empty. The SPA sends the filter as the record API's `filter`, together with the search and the sort. A new
  filter starts again at page 1. Like paging and search, the filter stays in the dialog, out of the URL, and is
  empty each time the dialog opens. A target with no display field has no Filter.
- **Reference label.** On edit, the label starts from the record's `labels`, and a picked record's
  display field value replaces it at once. A changed reference is sent as the record id, and a cleared
  one as `null`. Picking the record that was already set leaves the field unchanged.
- **Filtered** _(planned for M42)_. For a field entry with `lookupFilter`, the lookup sends the form's current
  values with each list request, and the server lists only the records the filter allows, on top of the read
  filters. A pick that a later change makes disallowed stays in the field until the save refuses it. See
  [lookup filters](form-conditions.md#lookup-filters).

## Set of references

A `references` field shows the labels of its chosen records in their order, as a list named after the
field's label. A label that is missing or null shows the record id instead, because the record API
returns no label when the user may not read the target's display field.

- **Adding and removing.** Each item has a remove button, named `Remove` and the item's label. The add
  button, named `Add` and the field's label, opens the [lookup](#lookup), with its paging, search and filter.
  Picking a row appends that record and closes the dialog, and picking a record that is in the set
  already leaves the set unchanged. A set is reordered by removing records and adding them again.
- **Keyboard and screen readers.** A polite live region announces `Added <label>` or
  `Removed <label>`, so a screen reader hears each change. Every control is a button, so the list is
  used with the keyboard, and removing a record moves focus to the add button.
- **Sending.** When the set changed, the form sends the whole array of ids in the shown order, as the
  [record shape](record-api.md#record-shape) describes. A set is compared with the one the form started
  from, id by id and in order, so an unchanged set is left out of the body. A new record starts with an
  empty set, and an empty set that was never changed stays out of the create body.
- **Messages.** A required set that is empty gets the server's message on the field. The form does not
  check the limit of 1,000 ids, and the server's message shows on the field.
- **Read-only.** A set that the form makes read-only shows its labels joined by a comma,
  or "Not set" when it is empty, with no add or remove button.
- **Links** _(planned)_. Each entry's label is a link to its record page, and so is each label of a read-only set.

## Child collections

A child collection field shows its rows as a table under the field's label.

- **Label on the toolbar** _(planned)_. An editable collection's label, its required mark and its help sit on the
  left of its table's toolbar, on one row with Add row on the right. A read-only collection has no toolbar and keeps
  its label above its table.

- **Columns.** There is one column per child field, headed by its label or its name. Integer, decimal
  and money columns sit on the right, in the header and in every cell, in tabular figures, see the
  [shown values](frontend.md#shown-values).
- **Cells.** Each editable cell uses the input the form uses for that field type, so number text, dates
  and enums behave as in the rest of the form. A cell input's accessible name is the column header and
  the one-based row number, such as `Quantity 2`. A file cell is the file input, and its Upload button
  is named like `Upload File 2`.
- **Read-only cells.** A computed child field's cell, and every cell of a read-only collection, shows
  the row's value, not an input, formatted as a table cell shows it. It keeps an input's
  height, so it lines up with the inputs beside it. An empty read-only or computed cell shows a muted
  italic "—". A read-only file cell shows its download link, or a dash when it has no file.
- **References in cells** _(planned)_. A reference cell has the same Open button as a reference field, and a read-only
  reference cell is a link to its record page.
- **Table.** The table is the shared table, see [tables](actions-and-controls.md#tables). A reference
  cell's input keeps at least 240px and the other inputs at least 160px.
- **Rows.** Delete row is the row's icon action: a trash icon button in `dangerText`, with its name as
  a tooltip on hover and on keyboard focus. Below 992px it folds into the row's "…" menu. Add row is the table
  toolbar's main action, above the table on the right, outlined at the main level of
  [buttons](actions-and-controls.md#buttons). It appends a row with every child field `null`, and
  focus moves to the row's first input.
- **Order** _(planned for M42)_. Each editable row has a drag handle, and the keyboard moves a row with Move up and
  Move down in the row's menu. The form sends the rows in the shown order, because a new order counts as a change.
  The server keeps the order it is sent, see [child rows](record-child-rows-and-validations.md#child-rows-computed-fields-and-validations).
  A read-only collection has no handle. As [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md) agrees.
- **Sending.** When rows were added, removed or edited, the form sends the whole row list, as
  [child rows](record-child-rows-and-validations.md#child-rows-computed-fields-and-validations) describes. Each row holds its
  non-null child fields in declaration order, without its computed fields. Rows are compared by value
  with the rows the form started from, so an edit that is typed and then undone leaves the collection
  out of the body.
- **Messages.** A key `/values/<collection>/<index>/<field>` for an existing row and a child field
  shows its messages under that cell, and a key `/values/<collection>` under the table. A cell's
  message stays inline under the cell's input, and the cells of a row line up at the top, so the row's
  other inputs never move. The message is left-aligned, even in a right-aligned number cell. Any other
  key inside the collection, such as a whole row, shows above the form. Adding or removing a row clears
  the collection's cell errors, because their indexes would point at the wrong rows.

## Saving

The form sends only what changed, and shows the server's answer where it belongs.

- **Changed fields.** The form sends only the fields whose value differs from the value it started
  from. It never sends a computed, sequence, creator or read-only field, nor a field its form does not
  show. A new record starts with every child collection and set of references empty, and with every field
  `null` unless a start value below gives it one, so an untouched field stays out of a create and the server
  decides what is required. An edit also sends the `version` it read. An emptied text or number input is
  sent as `null`.
- **Start values** _(planned for M42)_. On a new record, a field that starts with a first value or a context
  default counts as changed. So the create sends it, and the server checks it against the person's
  `createFields`, as it checks any value.
- **Number text.** The body is written by hand, so an integer, decimal or money amount goes out as the
  typed text and no digit is lost. Text that is not a JSON number, such as `1,5`, goes out as a JSON
  string, and the server refuses it on its field. The form never turns number text into a JavaScript
  number.
- **Errors.** A `400` or `409` problem is mapped by its JSON Pointer keys. A key `/values/<field>` for
  a field of the form shows its messages under that field. Every other key, such as `""`, `/values` or
  `/version`, shows above the form, and so does a shared error for a save that fails in any other way.
  The page scrolls to an error above the form and focuses it, so a user at the foot of a long form sees
  it. A screen reader reads it as an alert.
  Every message is looked up in the site texts first, so a failed validation, whose message is a text
  key, shows its text in the user's locale. A fixed message such as "Required." is no text key, so it
  shows unchanged.
- **Conflicts.** A `409` on edit that names no field is a stale version. The form shows a conflict
  message with a reload button, which loads the current values and version and drops the user's
  changes. A duplicate unique value is a `409` keyed `/values/<field>`, so it shows under its field.
- **Return address.** Save and Cancel return to the table page that opened the form, with its paging
  and sorting, which the table passes in history state. Without it, such as in a new tab, they go to
  `/{site}`, which opens the site's first page. Only an address inside the same site is used.
- **Keys.** Enter in a date or date-time picker only confirms the picked value and never sends the
  form. Enter in a text input sends the form, as in any form.
- **The save bar** _(planned)_. A site's form has no Save or Cancel row of its own. While the form has a change to
  save, the [save bar](studio-resource-form.md#save-bar) shows Discard and Save. With no change it is not drawn, so a
  new record's form shows it once the person types. Discard asks first, then puts every value back as the form
  started. A form without the grant to save shows no bar. A person who leaves by the sidebar or the browser is asked
  first, by [Leave without saving?](studio.md#editor), while there is a change. A wizard's Back and Next stay under
  the step's fields, and the bar shows on its last step.

### Form drafts

_(planned for M42)_

A form for a new record keeps a form draft: the person's typing, saved on the server as they type, for
that person only. It is removed when the record is saved, and after 30 days. The routes, limits and
checks are in [form drafts](record-form-state.md#form-drafts). A form draft is not the authoring
[Draft](../domain/concepts.md).

- **Scope.** Only a widget that names a `form`, on a new record, for a person with a `create` grant on
  the form's entity. A widget that names only an `entity`, a saved record and the task page keep no
  draft.
- **Saving.** The widget sends a `PUT` 2 s after the last change. It sends the values that a create
  would send, but no file field and no file cell. It does not save before the first change, nor when
  nothing has changed since the last save. A failed save shows nothing, and the next change tries
  again.
- **Restoring.** When a new form opens, the widget reads the draft. Its values fill the fields that
  the form shows and the person may write, as typed values over the first values and the `defaults`.
  The widget then asks for the form state as it does on open. A notice above the form says that the
  draft was restored and when it was saved, and offers Discard.
- **Dropping.** A successful create removes the draft on the server. Discard asks first, by
  [undo rather than confirm](actions-and-controls.md#undo-rather-than-confirm), because a discarded
  draft cannot be brought back. It then deletes the draft and opens the form as new. Cancel keeps the
  draft.
- **Storage.** The draft is on the server only. Nothing goes in the URL or in browser storage.

## Start process actions

A `form` widget can declare `startProcess` actions, which save the record and start a process over it.

```json
{
  "type": "form",
  "name": "edit",
  "form": "PurchaseRequestEdit",
  "actions": [
    {
      "type": "startProcess",
      "name": "submit",
      "process": "PurchaseRequestApproval",
      "label": { "textKey": "purchaseRequest.submit" }
    }
  ]
}
```

- **Buttons.** On a saved record, each action is a button next to Save and Cancel, with its label.
  Save is the solid finish button, and beside it the action is a text button, see
  [buttons](actions-and-controls.md#buttons). For a signed-in user, page metadata lists only the
  actions whose process the user holds a `start` grant on. A new record shows no action buttons, so a
  failed start never leaves a record behind on a form that still says new.
- **On the page heading** _(planned)_. Each action on a saved record is the page's own: an outlined `main`
  button on the right of the page's heading, on its row, through `PageActions`, with the `navigate` buttons
  after the `startProcess` ones. A form drawn with no page heading, such as in a widget's preview, puts them at
  its top, on the right.
- **Compile checks.** The process must name the form's entity as its subject. An unknown process is
  `AXC0087`, and a process over another entity is `AXC0088`, both at
  `/widgets/{i}/actions/{j}/process`. `actions` on another widget type is `AXC0060`. The label joins
  the `AXC0028` check.

A click runs these steps, and the button is disabled while they run:

1. It saves the changed fields first, as the save button does. If the save fails, its errors show as
   for a save, and nothing starts. When no field has changed, the save is skipped, so the record's
   version and history do not change for nothing.
2. It posts `{ "subjectId", "via" }`, with the action's label text key as `via`, to the [start endpoint](processes.md#start-endpoint) with an
   `Idempotency-Key`. When a click got no final answer, a network error or a `5xx`, the next click
   sends the same key, so a lost response cannot start a second instance. Any other answer ends the
   key, and the next click sends a new one.
3. On `201` it shows a success message and loads the record again. The first step runs on the worker
   after the `201`, so the record may still show its old status until it is loaded again later.

A `400`, such as a failed start condition, shows its messages above the form, looked up in the site
texts first. A `409` shows a platform text saying the record is already in a running process. Any other
answer shows a generic platform error.

## History

The form page of a saved record shows the record's history from the
[history endpoint](record-audit.md#audit-records-and-history) in a panel below the form, newest first.
A new record has no history panel.

- **The form's last part** _(planned)_. The history is the last part of the form's page, titled History
  ("Lịch sử"), drawn by `SectionPart` with a `borderSubtle` rule above it and no card. Its loading, empty, error
  and paging states stay.
- **Entry.** Each entry shows its action, who did it, its time and a short summary. The action is a
  platform text, such as "Updated" or "Task decided", and an action without a text shows as it is.
- **Who.** Who did it is the `actorName` the history answers. Without one, it is "System" for
  `system`, "Anonymous" for `anonymous`, or else the raw user id.
- **Time.** The time uses the user's locale, with the medium date and time styles.
- **Summary.** The summary is built from the details: the labels of the fields it names, then the task
  outcome, then the step. An entry whose details name none of them has no summary. Outcome and step
  names show as declared, such as `approve`. Comments are never shown, because audit records never hold
  them.
- **Values** _(planned for M41)_. An entry that changed fields shows each field's label with its old and new
  value, as [values in history](record-audit.md#values-in-history) holds them. A field the person may not
  read shows only its label, so the person sees that it changed and not what it was.
- **Summary lines**. In the summary the field labels are joined by commas, and the
  task outcome and the step each take a line of their own, never joined by a separator, see
  [text](actions-and-controls.md#text).
- **Open process.** An entry whose `inspectPath` is not `null` links to the instance page of the first
  page of the site whose widget is a `processInspector` that names the entry's process. The link reads
  "Open process", and the process and the instance id are the segments of `inspectPath`. The panel
  looks at the pages of the site's navigation in order, and loads their page metadata only when an
  entry has an `inspectPath`. The entry shows no link when no such page is in the navigation.
- **Paging.** The panel shows 20 entries per page, with a paging control when there are more. The page
  is the panel's own and stays out of the URL.
- **Failure.** When the history fails to load, an error shows inside the panel, and the form still
  works.
- **Timeline.** _(planned for M43)_ The panel reads the [timeline](comments.md#the-timeline) route. A filter by kind
  shows the comments, changes, steps or emails, or all of them.
- **Comment box.** _(planned for M43)_ The box shows with the `comment` or `internalComments` grant. Its visibility is
  an `AxisChoiceField` of Internal and Shared, shown only to a person who holds both grants. Mentions use the
  reassign user picker, and files use the upload control of a file field. A person edits or deletes their own
  comment within `Comments:EditWindow`, see [editing and deleting](comments.md#editing-and-deleting).

## Form metadata

Page metadata gives the widget's `entity` with all its fields, so the SPA knows every field's type, and
each entity field has `helpKey`, the text key of its `help`, or `null`. It adds `form` and `actions`.

- **`form`** is
  `{ "name", "sections": [ { "name", "titleKey", "visibleWhen", "fields": [ { "name", "readOnly", "visibleWhen", "width" } ] } ], "steps": [ { "titleKey", "sections" } ] }`,
  or `null` when the widget names an `entity`.
  - `readOnly` already includes computed, sequence and creator fields. Field names are as the entity
    declares them. Each field has `width`, 12 when the form leaves it out.
  - `visibleWhen` on each field and section is `true` when it has a condition and `false` otherwise.
    `form` holds no expressions, because they stay on the server.
  - Each section has a `name`. `steps` holds each step with its `titleKey` and its section names, and
    is `[]` when the form has none.
  - A user who may not read any field of a section gets no such section, and its name is left out of
    the steps. A step left with no section is left out.
- **Tabs, folding and lookups** _(planned for M42)_. `form` also holds `tabs`, as `[{ "name", "titleKey", "sections" }]`
  or `[]`, each section's `collapsed` and each field's `lookupFilter`, as `true` or `false`. A tab left with no
  readable section is left out.
- **`actions`** is `[ { "type", "process", "labelKey", "instancesPath", "visibleWhen" } ]`, or `[]`,
  in file order.
  - `instancesPath` is the start endpoint, such as
    `/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances`, so the SPA never builds it
    itself.
  - A `navigate` action is
    `{ "type": "navigate", "page", "labelKey", "parameters", "record", "opensForm", "paths", "visibleWhen" }`,
    with the target page's declared name. `parameters` maps each parameter name, as the target page
    declares it, to its expression, or is `{}`. `record` is the expression of the record id, or
    `null`. The expressions go to the browser as written, and may use a reference path such as
    `department.name`.
  - `paths` maps each reference along such a path, by its declared field names up to that reference,
    to the record API of that reference's target entity, so the SPA never builds it: `department` for
    `department.name`, and `customer` and `customer.parent` for `customer.parent.name`. It is `{}` when
    the action uses no path. The SPA resolves a path on click, as the navigate actions of
    [frontend](frontend.md) say.
  - `opensForm` is `true` when the target page holds a form widget, so the SPA opens `/new` without
    `record`.
  - A saved record shows a button for each `navigate` action after its `startProcess` buttons, with
    values from the record as it was loaded, so unsaved edits are not used. A new record shows none.
