# Form resource and form widget

Detailed reference for the `form` resource and the `form` widget of the SPA. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet,
and Dn refers to [decisions.md](../decisions.md). The rest of the frontend is in
[frontend.md](frontend.md).

- **Form resource**. A `form` resource lays out the fields
  of one entity in sections, and any field can be read-only. One form serves
  both a page and a task (D20).

  ```json
  {
    "id": "8b3e5c71-4d2a-4f6b-9c0e-1a2b3c4d5e6f",
    "kind": "form",
    "name": "PurchaseRequestReview",
    "formatVersion": 1,
    "entity": "PurchaseRequest",
    "sections": [
      {
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
        "title": { "textKey": "purchaseRequest.sections.decision" },
        "fields": [{ "field": "decisionComments" }]
      }
    ]
  }
  ```

  - Each section has a `title` label and its `fields`, in the order shown.
    Each entry names a `field` of the entity. `readOnly` is optional and
    `false` by default.
  - **Fields in columns**. A field entry may have `width`, a whole
    number from 1 to 12, in twelfths of the section. It is 12 when left out.
    Fields fill a row from left to right, and the next field that does not fit
    starts a new row. A row is as tall as its tallest field and its fields line
    up at the top, so a message under one field never shifts the rows below.
    Below 992 px every field takes the whole row, in file order. A detail
    lays out the same sections with the same widths _(planned)_. A `width` that is not a
    whole number from 1 to 12 breaks the schema (`AXC0004`). Studio's
    [form designer](form-designer.md) edits it
    ([D96](../decisions/d96-rules-from-the-design-system-review.md)).
    `{ "field": "quantity", "width": 4 }` takes a third of the row.
  - A field appears at most once in the form. A field the form does not list
    is not shown and never sent.
  - Computed, sequence and creator fields are always read-only, whatever
    `readOnly` says.
  - On a page, `readOnly` only shapes the UI. The record API enforces the
    user's field write grants, so it still accepts a read-only field that an
    `update` grant covers. In a task, the server
    enforces it: completing a task rejects a value for a field the form does
    not make editable (see [Task API](task-api.md)).
  - A section's `fields` and the form's `sections` hold at least one entry,
    and a section without a `title` breaks the schema (`AXC0004`).
  - Compile checks: an `entity` that names no loaded entity is `AXC0074`. A
    `field` the entity does not have is `AXC0075`. A field listed again, in
    any section and ignoring letter case, is `AXC0076`. A section title key
    that no locale has is `AXC0028`. See
    [Configuration pipeline](configuration.md#configuration-pipeline).
  - A form over a child entity is not checked yet.
  - Fields get `visibleWhen`, `requiredWhen` and `readOnlyWhen`, sections get
    `visibleWhen` and a `name`, and a form may have `steps`. The compiler
    checks them. The form widget and the task page follow their conditions
    through the server, and show `steps` as a wizard, see
    [form widget conditions](form-widget-conditions.md). See
    [Form conditions and steps](resource-files.md#form-conditions-and-steps).
- **Form widget.** A page whose widget is a `form` creates a record at
  `/{site}/{page}/new` and edits one at `/{site}/{page}/{id}`, through the
  record API. It adds no rules of its own: the server validates.
  - **Inputs.** There is one input per field, in declaration order, labelled
    with the field's label or the field name. A widget that names a `form`
    shows only the fields its form lists, in its order, under the section
    titles (see **Form binding**). A required field's label is
    marked, but the mark blocks nothing: the server still decides.
  - **Field look** _(planned)_. These rules are agreed in
    [D96](../decisions/d96-rules-from-the-design-system-review.md).
    - **Label.** A field label is weight 500 in `textMuted`, wherever a field
      is labelled: on a form, on a detail and as a KPI's label.
    - **Required mark.** A required field has a `*` after its label, in
      `dangerText`, and its input carries `aria-required`.
    - **Help text.** A field whose entity field has
      [`help`](resource-files.md#resource-file-shape) shows it between the
      label and the input, at 12px with a 20px line height in `textMuted`,
      tied to the input with `aria-describedby`. A message under the input
      never replaces it. It is one or two short lines: longer guidance belongs
      on the page or the section. Only a form shows it, not a table or a
      detail.
    - **Validation messages.** A message is 14px at a 20px line height, 4px
      under its input, followed by a whole field gap (`spacing-lg`, 24px)
      before the next field, so the layout grows when a message appears.
    - **Read-only values.** A field the form makes read-only, by `readOnly` or
      `readOnlyWhen`, and a `computed`, `sequence` or `creator` field show
      plain text, not an input. A boolean shows Yes or No. The value starts
      about 6px under its label, and the field keeps the height of an editable
      field. An empty value shows "Not set", see
      [shown values](frontend.md).
    - **Values.** Dates, date-times and numbers follow the **Shown values**
      rules of [frontend.md](frontend.md). A date picker fills its column, as
      every other input does.

    | Field type | Input |
    | --- | --- |
    | `text` | a text input limited to `maxLength`. With `minLength` it also has the `minlength` attribute and shows the character count against the limits, such as `0 / 3–10`, or `0 / ≥3` with no `maxLength` |
    | `integer`, `decimal` | a plain text input with a numeric keyboard hint. It keeps the typed text as it is. With `min` or `max` its placeholder shows the range, such as `1 – 10`, `≥ 1` or `≤ 10`, and a typed number outside the range is marked invalid. It is never blocked, because the server decides. A number input that takes the locale's decimal comma is not decided yet |
    | `money` | the input of a decimal with the currency code beside the box. The code is `currency`, or the current value of `currencyField` in the form, or in the row of a child collection. Without a code of three letters nothing is shown beside the box. It sends the typed text as a plain JSON number |
    | `boolean` | a checkbox |
    | `date` | a date picker. With `min` or `max` it disables the dates outside them and its placeholder shows the range. It shows and accepts the locale's short numeric date, such as `03/18/2026` in `en` and `18/03/2026` in `vi` |
    | `date-time` | a date and time picker to the minute, with no seconds column, that shows and accepts the date and the time on the locale's clock, such as `03/04/2026 4:30 PM` in `en` and `04/03/2026 16:30` in `vi`, in the browser's time zone. It sends the instant in UTC with zero seconds and no fraction. With `min` or `max` it disables the dates outside them and its placeholder shows the range |
    | `enum` | a choice of the declared values, as written in the entity file, or their labels with `valueLabels` |
    | `file` | the [file input](file-fields.md#in-the-spa): the current file's name as a download link, an Upload button and, when the field is not required, a Remove button. A read-only file shows its link only |
    | `reference` | the label of the chosen record, read-only, with a choose button that opens the lookup. A field that is not required and is set also has a clear button. Choose and Clear are icon buttons, a search and a close icon, with their names as tooltips _(planned)_ |
    | `references` | a list of the chosen records' labels in their order, each with a remove button, and an add button that opens the lookup, described under **Set of references** |
    | `child-collection` | a table of its rows, described under **Child collections** |
    | any type with `computed`, `sequence` or `creator`, and any field but a child collection that the form makes read-only | the value the server returned, formatted as a table cell shows it, in a read-only text input today and as plain text _(planned)_, see **Field look**. An empty value shows "Not set" in muted italics, the value of the input today. It changes only when the record is saved and loaded again |

  - **Lookup.** The choose button opens a dialog titled with the field's
    label. It lists the target entity's records through their record API,
    sorted ascending by the target's display field, in one column for that
    field. The record API lists only the target records that match the
    user's read filters. It pages 20 records at a time and keeps nothing in the URL.
    Loading, empty and error states use the table's texts. Clicking a row
    or pressing Enter on it picks the record and closes the dialog. The same
    dialog picks the record of a `reference` filter input.
  - **Lookup search.** A search box above the list filters the records to
    those whose display field contains the typed text, ignoring letter case.
    The search applies 300 ms after typing stops, so typing does not request
    records for every key. The SPA trims the text and sends it as the record
    API's `search` parameter, and leaves `search` out when the text is empty.
    A new search starts again at page 1. Like paging, the search stays out
    of the URL and is empty each time the dialog opens.
  - **Reference label.** On edit, the label starts from the record's
    `labels`. A picked record's display field value replaces it at once. A
    changed reference is sent as the record id, and a cleared one as `null`.
    Picking the record that was already set leaves the field unchanged.
  - **Set of references.** A `references` field shows the labels of its
    chosen records in their order, as a list named after the field's label.
    A label that is missing or null shows the record id instead, because the
    record API returns no label when the user may not read the target's display
    field.
    - Each item has a remove button, named `Remove` and the item's label. The
      add button, named `Add` and the field's label, opens the same lookup as
      a `reference`, with its paging and search. Picking a row appends that
      record and closes the dialog. Picking a record that is in the set
      already leaves the set unchanged. A set is reordered by removing records
      and adding them again.
    - A polite live region announces `Added <label>` or `Removed <label>`, so
      a screen reader hears each change. Every control is a button, so the
      list is used with the keyboard, and removing a record moves focus to the
      add button.
    - When the set changed, the form sends the whole array of ids in the shown
      order, as [record-api.md](record-api.md#record-shape) describes. A set is
      compared with the one the form started from, id by id and in order, so
      an unchanged set is left out of the body. A new record starts with an
      empty set, and an empty set that was never changed stays out of the
      create body.
    - A required set that is empty gets the server's message on the field.
      The form does not check the limit of 1,000 ids, and the server's message
      shows on the field.
    - A set that the form makes read-only shows its labels joined by a comma
      in a read-only text input today and as plain text _(planned)_, with no add or
      remove button.
  - **Child collections.** A child collection field shows its rows as a
    table under the field's label.
    - There is one column per child field, headed by its label or its name.
      Each cell uses the input the form uses for that field type, so number
      text, dates and enums behave as in the rest of the form. A cell
      input's accessible name is the column header and the one-based row
      number, such as `Quantity 2`. A file cell is the file input, and its
      Upload button is named like `Upload File 2`. A computed child field's cell is a
      read-only text input with the row's value, formatted as a table cell
      shows it. An empty read-only or computed cell shows a muted italic "—".
      Integer, decimal and money columns sit on the right, in the header and
      in the text and input cells, in tabular figures, see **Shown values** in
      [frontend.md](frontend.md).
    - _(planned)_ A read-only or computed cell shows plain text, not an
      input, and keeps an input's 32px height, so it lines up with the inputs
      beside it. A reference column is 240px wide.
    - Each row has a remove button. An add button below the table appends a
      row with every child field `null`. Rows cannot be reordered.
      _(planned)_ Remove row is a red trash icon button. Add row sits above
      the table on the right, outlined at the *main* level of
      [buttons](frontend.md). When a row is added, focus moves to its first
      input.
    - When rows were added, removed or edited, the form sends the whole row
      list, as [record-api.md](record-api.md#child-rows-computed-fields-and-validations) describes. Each row holds its
      non-null child fields in declaration order, without its computed fields. Rows are compared by value
      with the rows the form started from, so an edit that is typed and then
      undone leaves the collection out of the body.
    - A key `/values/<collection>/<index>/<field>` for an existing row and a
      child field shows its messages under that cell. A key
      `/values/<collection>` shows under the table. _(planned)_ A cell's
      message stays inline under the cell's input, and the cells of a row line
      up at the top, so the row's other inputs never move. The message is
      left-aligned, even in a right-aligned number cell. Any other key inside the
      collection, such as a whole row, shows above the form. Adding or
      removing a row clears the collection's cell errors, because their
      indexes would point at the wrong rows.
  - **Changed fields.** The form sends only the fields whose value differs
    from the value it started from. It never sends a computed, sequence, creator
    or read-only field, nor a field its form does not show. A new record starts with every field
    `null` and every child collection and set of references empty, so an untouched field stays out of a create and the server
    decides what is required. An edit also sends the `version` it read. An
    emptied text or number input is sent as `null`.
  - **Number text.** The body is written by hand, so an integer, decimal or money
    amount goes out as the typed text and no digit is lost. Text that is not a JSON number,
    such as `1,5`, goes out as a JSON string, and the server rejects it on its field. The form
    never turns number text into a JavaScript number.
  - **Errors.** A `400` or `409` problem is mapped by its JSON Pointer keys. A
    key `/values/<field>` for a field of the form shows its messages under
    that field. Every other key, such as `""`, `/values` or `/version`, shows
    above the form. A save that fails in any other way shows a shared error
    there too. Every message is looked up in the site texts first, so a
    failed validation, whose message is a text key, shows its text in the
    user's locale. A fixed message such as "Required." is no text key, so it
    shows unchanged.
  - **Conflicts.** A `409` on edit that names no field is a stale version. The
    form shows a conflict message with a reload button, which loads the
    current values and version and drops the user's changes. A duplicate
    unique value is a `409` keyed `/values/<field>`, so it shows under its
    field.
  - **Return address.** Save and cancel return to the table page that opened
    the form, with its paging and sorting, which the table passes in history
    state. Without it, such as in a new tab, they go to `/{site}`, which opens
    the site's first page. Only an address inside the same site is used.
  - **Keys.** Enter in a date or date-time picker only confirms the picked
    value and never sends the form. Enter in a text input sends the form, as
    in any form.
  - **History**. The form page of a saved record shows the record's history
    from the [history endpoint](record-api.md#audit-records-and-history) in a
    panel below the form, newest first. A new record has no history panel.
    - Each entry shows its action, who did it, its time and a short summary.
      The action is a platform text, such as "Updated" or "Task decided". An
      action without a text shows as it is.
    - Who did it is the stored `actorName`. Without one, it is "System" for
      `system`, "Anonymous" for `anonymous`, or else the raw user id.
    - The time uses the user's locale, with the medium date and time styles.
    - The summary is built from the details. It lists the labels of the
      fields it names, then the task outcome, then the step, joined by " · ".
      An entry whose details name none of them has no summary. Outcome and
      step names show as declared, such as `approve`. Comments and other
      field values are never shown, because audit records never hold them.
    - An entry whose `inspectPath` is not `null` links to the instance page
      of the first page of the site whose widget is a `processInspector` that
      names the entry's process. The link reads "Open instance". The process
      and the instance id are the segments of `inspectPath`. The panel looks
      at the pages of the site's navigation in order, and loads their page
      metadata only when an entry has an `inspectPath`. The entry shows no
      link when no such page is in the navigation.
    - The panel shows 20 entries per page, with a paging control when there
      are more. The page is the panel's own and stays out of the URL.
    - When the history fails to load, an error shows inside the panel, and
      the form still works.
  - **Sequence fields**. Page metadata marks a field that
    names a [sequence](resource-files.md#sequences), so the form shows it
    read-only. It is empty on a new record, and the form never sends it.
  - **Creator fields**. Page metadata marks a field that declares
    [`creator`](roles-and-policies.md#record-creator), so the form shows it
    read-only. It is empty on a new record, shows the creating user's id once
    saved, and the form never sends it.
  - **Form binding**. A `form` widget names a `form`
    resource, or an `entity` as shorthand for all its fields in declaration
    order. Naming both or neither is `AXC0060`, and a `form` that names no
    loaded form is `AXC0077`. With a `form`, the widget shows its sections in
    order, each under its title as a heading, and shows a read-only field as
    on a computed field. A read-only child collection shows its rows with
    every cell read-only, and has no add or remove buttons. The widget sends
    only the editable fields it shows. A server error on a field the form
    does not show appears above the form.
  - **Start process action**. A `form` widget can declare
    `startProcess` actions. On a saved record, each is shown as a button
    next to Save and Cancel, with its label. It is outlined at the *main*
    level of [buttons](frontend.md), and Save is the solid *finish* button
    _(planned)_. For a signed-in user, page
    metadata lists only the actions whose process the user holds a `start`
    grant on. A new record shows no action buttons, so a failed start never
    leaves a record behind on a form that still says new.

    ```json
    {
      "type": "form",
      "form": "PurchaseRequestEdit",
      "actions": [
        {
          "type": "startProcess",
          "process": "PurchaseRequestApproval",
          "label": { "textKey": "purchaseRequest.submit" }
        }
      ]
    }
    ```

    - The process must name the form's entity as its subject. An unknown
      process is `AXC0087`, and a process over another entity is `AXC0088`,
      both at `/widgets/{i}/actions/{j}/process`. `actions` on another
      widget type is `AXC0060`. The label joins the `AXC0028` check.
    - A click saves the changed fields first, as the save button does. If
      the save fails, its errors show as for a save, and nothing starts. When
      no field has changed, the save is skipped, so the record's version and
      history do not change for nothing.
    - Then it posts `{ "subjectId" }` to the
      [start endpoint](processes.md#start-endpoint) with an
      `Idempotency-Key`. When a click got no final answer, a network error or
      a `5xx`, the next click sends the same key, so a lost response cannot
      start a second instance. Any other answer ends the key, and the next
      click sends a new one.
    - On `201` it shows a success message and loads the record again. The
      first step runs on the worker after the `201`, so the record may still
      show its old status until it is loaded again later.
    - A `400`, such as a failed start condition, shows its messages above
      the form, looked up in the site texts first. A `409` shows a platform
      text saying the record is already in a running process. Any other
      answer shows a generic platform error.
    - The button is disabled while the save and the start run.
  - **Conditions and steps**. The widget asks the server which fields to
    show, require and lock, and which messages to show. The conditions and the
    steps are built. See
    [form-widget-conditions.md](form-widget-conditions.md).
  - **Form metadata**. Page metadata gives the widget's
    `entity` with all its fields, so the SPA knows every field's type. It
    adds:
    - `form`: `{ "name", "sections": [ { "name", "titleKey", "visibleWhen", "fields": [ { "name", "readOnly", "visibleWhen", "width" } ] } ], "steps": [ { "titleKey", "sections" } ] }`,
      or `null` when the widget names an `entity`. `readOnly` already
      includes computed, sequence and creator fields. Field names are as the entity
      declares them. Each field also has `width`, 12 when the form
      leaves it out, and each entity field has `helpKey`, the text key of its
      `help`, or `null`.
    - `actions`: `[ { "type", "process", "labelKey", "instancesPath", "visibleWhen" } ]`,
      or `[]`. `instancesPath` is the start endpoint, such as
      `/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances`,
      so the SPA never builds it itself. A `navigate` action is
      `{ "type": "navigate", "page", "labelKey", "parameters", "record", "opensForm", "paths", "visibleWhen" }`, with
      the target page's declared name. `parameters` maps each parameter name,
      as the target page declares it, to its expression, or is `{}`. `record`
      is the expression of the record id, or `null`. The expressions go to the
      browser as written. They may use a reference path such as
      `department.name`. `paths` maps each reference along such a path, by its
      declared field names up to that reference, such as `department` for
      `department.name` and `customer` and `customer.parent` for
      `customer.parent.name`, to the record API of that reference's target
      entity, so the SPA never builds it. It is `{}` when the action uses no
      path. The SPA resolves a path on click, see **Values**. `opensForm` is `true` when the target page holds a
      form widget, so the SPA opens `/new` without `record`. It follows file
      order. A saved record shows a button for each `navigate` action after
      its `startProcess` buttons, with values from the record as it was
      loaded, so unsaved edits are not used. A new record shows none.
    - `form` also holds `visibleWhen` on each field and section, as `true`
      when it has a condition and `false` otherwise. It holds `steps`, each
      with a `titleKey` and its section names, and `[]` when the form has
      none. Each section has a `name`, which is `null` when it has none. It
      holds no expressions, because they stay on the server. A user who may
      not read any field of a section gets no such section, and its name is
      left out of the steps. A step left with no section is left out.
