# Frontend

Detailed reference for the frontend: the SPA, its rendering from metadata and
its widgets. The conventions of [architecture.md](../architecture.md) apply:
sections marked *(planned for Mx)* are not built yet, and Dn refers to
[decisions.md](../decisions.md).

- **One SPA for all applications.** The SPA renders from metadata served by
  the server: site navigation, pages and their widgets, data source schemas
  and text resources. Publishing an application never
  rebuilds the frontend.
- **Shared look and feel.** A page is a route in a site, and its widgets are
  its content. Each widget type, such as `table` and `form`, has one shared
  component, and layout comes from container widgets rather than per-page
  templates. Those components and a single token-based theme with light and
  dark modes live in `web/src/platform/`. Feature code assembles them and
  adds no styling of its own.
- **How pages and widgets grow.** A page has one widget. A form binds an entity
  or a `form` resource, which lays the entity's fields out in sections, and a
  table binds an entity or a data source. Container widgets such as tabs, sections and columns will hold
  other widgets. Data sources are agreed in
  [D18](../decisions.md#d18-data-sources--agreed) and described in
  [data-sources.md](data-sources.md). Shared `form` resources, the task inbox
  and the `startProcess` action are agreed in
  [D20](../decisions.md#d20-human-tasks-task-inbox-and-forms--agreed). The
  `form` resource is built. The task inbox and the `startProcess` action are
  *(planned for M3)*. Navigate actions are expected to replace the table's
  `formPage` link; see
  [D15](../decisions.md#d15-presentation-model--agreed), where that part is
  still **Proposed**.
- **Text.** All UI strings come from text resources.
- **Sites and texts.** The server describes sites to the SPA through
  read-only endpoints. Like every other `/api` path, they need a known tenant
  host.
- **Platform site.** The built-in platform site in `Axis.Presentation` serves
  the shell through two endpoints, `GET /api/site` and
  `GET /api/texts/{locale}`. The home page also uses `GET /api/sites` (see
  Application sites below) to list the tenant's sites.
  - `GET /api/site` returns the site name, its title key, the locales
    (default, fallback and available) and the navigation items. Navigation
    labels are text keys, never literal text.
  - `GET /api/texts/{locale}` returns a flat map from text key to text for
    one locale. A locale without texts gets a 404 problem response.
  - The platform site offers English (`en`, default and fallback) and
    Vietnamese (`vi`). The platform site stays: application sites never
    replace or merge into it. The SPA reaches them through the site endpoints
    below. A test checks that every locale has exactly the same keys as
    English.
- **Application sites.** The sites of the tenant's active applications are
  found by path, not by application name, because the path is what users see
  and one application can have several sites. Four endpoints describe them.

  | Method and path | Response |
  | --- | --- |
  | `GET /api/sites` | `{ "sites": [ { "path", "titleKey", "titles" } ] }`, every site of every active application, in path order |
  | `GET /api/sites/{path}` | `{ "path", "titleKey", "locales": { "default", "fallback", "available" }, "navigation": [ { "page", "labelKey" } ] }` |
  | `GET /api/sites/{path}/texts/{locale}` | `{ "locale", "texts" }`, the texts of the site's application for one locale, in the shape of the platform texts |
  | `GET /api/sites/{path}/pages/{page}` | the page metadata shown below |

  A page response looks like this:

  ```json
  {
    "name": "Items",
    "titleKey": "items.title",
    "widgets": [
      {
        "type": "table",
        "formPage": "ItemForm",
        "entity": {
          "name": "Item",
          "labelKey": "item.label",
          "displayField": "name",
          "recordsPath": "/api/apps/RecordsApp/entities/Item/records",
          "fields": [
            {
              "name": "name", "type": "text", "labelKey": null,
              "required": true, "unique": false, "computed": false, "sequence": false,
              "maxLength": 100,
              "precision": null, "scale": null, "values": null, "target": null,
              "fields": null
            },
            {
              "name": "department", "type": "reference", "labelKey": "item.department",
              "required": false, "unique": false, "computed": false, "sequence": false,
              "maxLength": null,
              "precision": null, "scale": null, "values": null,
              "target": {
                "entity": "Department",
                "displayField": "name",
                "recordsPath": "/api/apps/RecordsApp/entities/Department/records"
              },
              "fields": null
            }
          ]
        },
        "dataSource": null,
        "form": null
      }
    ]
  }
  ```

  - `titles` in the site list has one entry per available locale of the site,
    from the locale as the site declares it to the title text. The home page
    can show each site without one more request per site.
  - `type` of a widget is `table` or `form`, and `taskInbox` *(planned for
    M3)*. `formPage` is the page holding the form for a table's records, and
    `null` otherwise. On a `table` or `form`, exactly one of `entity` and
    `dataSource` is set, and the other is `null`. A `taskInbox` has both
    `null`. The data source binding of the table widget below describes
    `dataSource`.
  - Every widget also has `form`, never left out. It is the widget's form
    resource on a `form` widget that names one, and `null` otherwise. It is
    described under **Form metadata** of the form widget.
  - *(planned for M3)* Every widget also has `actions` and `tasksPath`, never
    left out. `actions` is described under the form widget, and `tasksPath`
    under the task inbox widget. On a widget they do not apply to, they are
    `[]` and `null`.
  - `entity` is the widget's entity: its name, its label key, its display
    field, the path of its record API in `recordsPath`, and its fields in
    declaration order. A `child-collection` field is listed too. Tables show
    no column for it, and forms show its rows.
  - Each field has its name, its `type` as written in entity files (such as
    `date-time`), its label key, `required`, `unique`, `computed` and `sequence`, `maxLength` for
    text, `precision` and `scale` for decimal, `values` for enum, `target`
    for reference, and `fields` for child collection. A property the field's
    type does not have, or a label the file leaves out, is `null`; it is
    never left out.
  - `computed` is `true` for a [computed field](configuration.md#entity-logic).
    The server sets its value on every write, so the form shows it read-only
    and never sends it.
  - `sequence` is `true` for a text field that names a
    [sequence](configuration.md#sequences). The server numbers it on create,
    so the form shows it read-only and never sends it.
  - `target` names the referenced entity, its display field and the path of
    its record API, so the SPA never builds a record URL itself. It is only
    for a reference. A child collection's `target` is `null`, because a child
    entity has no record API of its own.
  - `fields` lists a child collection's child entity fields in declaration
    order, each in the same shape as an entity field. The form builds the
    columns and inputs of the collection's table from them.
  - Names in responses (page, form page, entity, and the application in
    `recordsPath`) are the model's declared names, not the letter case of the
    request.
  - The texts endpoint serves any locale the site's application has texts
    for, and the page endpoint any page of the site's application, not only
    pages in the site's navigation, because form pages opened from a table
    are not in navigation.
  - `{path}`, `{locale}` and `{page}` match ignoring letter case. A path
    outside the pattern (an ASCII letter, then ASCII letters, digits or
    hyphens, at most 60 characters, matched ignoring letter case) is a 404
    without a query. A reserved path such as `api` is a 404 because no site
    can hold it.
  - An unknown site, page or locale is a 404 problem with a fixed title. A
    site active only in another tenant does not exist for the request.
  - The endpoints have no authorization yet, like the record API.
- **Routing.** The SPA routes on the client with `react-router` v7. The
  server answers every other path with the SPA, so each address below
  answers 200.

  | Address | Shows |
  | --- | --- |
  | `/` | the platform home page: the server status and the tenant's sites as links, each titled in the current locale, or in the site's first locale when it lacks the current one |
  | `/{site}` | the site's first navigation entry, by redirect |
  | `/{site}/{page}` | the page inside the site shell, with the page title. A table page also takes `?page=&pageSize=&sort=`. A table bound to a data source also takes its parameter values |
  | `/{site}/{page}/new` | the form of a form page, to create a record |
  | `/{site}/{page}/{id}` | the form of a form page, to edit the record with that id |
  | `/{site}/{inboxpage}/{taskId}` | *(planned for M3)* the task page of the task with that id, under the page of a task inbox |

  - `{site}` is the site path. `{page}` is the page name in lower case, as
    navigation links write it. The server matches both ignoring letter case.
  - An unknown site shows the not-found page inside the platform shell. An
    unknown page shows it inside the site shell.
    An address with more than three segments, such as `/e2e/notes/42/edit`,
    matches no route and shows the not-found page inside the platform shell.
  - A form page without `new` or an id, `new` or an id on a table page, an
    id that is not in the hyphenated 8-4-4-4-12 hex form, and an id the
    entity has no record for show the not-found page inside the site shell.
    An id in the wrong form is not requested.
  - *(planned for M3)* `new` on a task inbox page, and a task id that is not
    in the hyphenated 8-4-4-4-12 hex form, show the not-found page inside the
    site shell. A task id in the wrong form is not requested. A well-formed
    task id that the task API answers `404` for shows the not-found page
    inside the site shell, as for a record.
  - Inside a site, the shell shows the site's title, navigation and locales.
    The theme toggle and the locale switch work as they do on the platform
    site.
- **Table widget.** A page whose widget is a `table` lists the records of its
  entity through the record API, or the rows of its data source, in the
  shared Ant Design table.
  - **Columns.** There is one column per field, in declaration order. The
    header is the field's label, or the field name when it has no label. A
    child collection has no column.
  - **URL state.** Paging and sorting live in the URL as the record API's own
    `page`, `pageSize` and `sort` (`-` and the field name for descending), so
    reload, sharing and the back button keep them. The widget writes them
    after every other parameter, always in the order `page`, `pageSize`,
    `sort`, and leaves out defaults (page 1, 20 a page or the data source's
    page size, no sort). It changes
    only its own three parameters and, for a data source, the parameter
    values described below: any other parameter, such as `x=1`, stays as it
    is.
  - **Data source binding.** A table may name a
    `dataSource` instead of an `entity`. See
    [data-sources.md](data-sources.md#widget-binding). Page metadata then
    gives the widget `"entity": null` and a `dataSource` object. An entity
    widget has `"dataSource": null`.

    ```json
    "dataSource": {
      "name": "OpenRequests",
      "rowsPath": "/api/apps/PurchasingApp/data-sources/OpenRequests/rows",
      "entity": "PurchaseRequest",
      "parameters": [
        {
          "name": "statusFilter", "type": "enum", "required": false,
          "labelKey": "requests.statusFilter",
          "values": ["draft", "submitted", "approved", "rejected"], "target": null
        }
      ],
      "pageSize": 20,
      "columns": [
        {
          "name": "status", "type": "enum", "labelKey": "request.status",
          "values": ["draft", "submitted", "approved", "rejected"], "target": null
        }
      ]
    }
    ```

    - **Parameters.** A parameter's `labelKey` is the text key of its label, or
      `null` when it has none. `values` and `target` are `null` when the type
      has none.
    - **Columns.** There is one column per projected field, in order. For a
      grouped data source, the columns are the group fields in `groupBy`
      order, then the measures. The header is the label of the field the
      path ends at, else the projected name. A measure has no `labelKey`, so
      its header is its name. A `count` is an `integer` column, a `sum` is a
      `decimal` column, and a `min` or `max` has its field's type. Sorting,
      values and labels follow the rules below, by the column's type.
    - **Page size.** The data source's `pageSize` is the table's default page
      size. It is offered with 10, 20, 50 and 100 when it is not one of them,
      and the URL leaves it out.
    - **Filter inputs.** A filter bar above the rows has one input per
      parameter, in declaration order. It shows the parameter's label, or its
      name when `labelKey` is `null`. Each input is the kind the form widget
      uses for that type, with two exceptions. A `boolean` is a Yes/No
      choice that can be cleared, because an optional parameter has three
      states: true, false and not given. A `reference` has a choose button
      that opens the lookup, and a clear button once it has a value. The
      SPA does not check the values: the server does.
    - **Applying a value.** A `text`, `integer` or `decimal` value applies on
      Enter or when the input loses focus, so typing does not request rows
      and add a history entry for every key. A date, a choice, a Yes/No and
      a picked record apply at once. A cleared value is removed.
    - **URL state.** The parameter values live in the URL under the
      parameter names, and the table sends them to `rowsPath`. The widget
      writes them after every other parameter and before `page`, `pageSize`
      and `sort`, in declaration order. A new value starts again at page 1
      and adds a history entry, so reload, sharing and the back button keep
      the filter. An empty value is left out, and a repeated parameter keeps
      its first value. Both are rewritten without a history entry, as for
      paging and sorting.
    - **Reference label.** The URL holds only the record id. After a reload
      the table loads that record of the target entity to show its display
      field. When the record is missing or cannot be loaded, the input shows
      the id.
    - **Rejected values.** When the server rejects a parameter value, such as
      a hand-edited `?statusFilter=bogus`, its message shows under that
      input and the table shows no rows. The load error alert is for other
      failures.
    - **Rows.** The table requests its rows from `rowsPath`. A row carries
      the id of its root record, so its open link and the create button go
      to the `formPage` as for an entity table. A group row of a grouped data
      source has `id: null`. A table over a grouped data source has no form
      page, so it has no open link and no create button.
  - **Changes.** A new sort or page size starts again at page 1, because the
    old page number means nothing under a new order or size. Only the
    pagination control moves between pages. Each change adds a history entry.
  - **Invalid values.** A `page` that is not a positive integer, a `pageSize`
    other than the offered sizes, and a `sort` that names no
    field, a reference field or a child collection field are ignored. The default is requested instead,
    and the URL is rewritten without them and without a history entry. Once
    the total is known, a `page` past the last page (at least 1) becomes the
    last page in the same way, so the URL and the pagination agree. With no
    records the last page is 1, so `page` is removed.
  - **Sorting.** Every column but a reference is sortable. The record API
    sorts a reference by the stored id, which does not match the label people
    see. Repeated clicks on a header sort ascending, then descending, then
    not at all.
  - **Values.** Values are formatted for display and never changed:

    | Field type | Shown as |
    | --- | --- |
    | `date-time` | date and time to the second in the UI locale and the browser's time zone. The fraction is dropped |
    | `date` | the date in the UI locale, without a time-zone shift |
    | `integer`, `decimal` | exactly as the API wrote them, right-aligned |
    | `boolean` | a localized yes or no |
    | `reference` | the label from `labels` |
    | `text`, `enum` | the value |
    | `null` | an empty cell |

  - **Number source text.** The SPA parses record responses with the source
    text of each JSON number, so `values` holds integers and decimals as
    strings. A plain parse would show `1250.50` as `1250.5` and lose digits
    beyond double precision. `version`, `page`, `pageSize` and `totalCount`
    are record metadata, not field values, so they are turned back into
    numbers. A browser without `JSON.parse` source text access (older than
    Chromium 114, Firefox 135 or Safari 18.4) falls back to the parsed
    number, so `1250.50` shows as `1250.5` there. The web unit tests need
    Node 22 or later for the same reason. The API never coerces a string to a
    number, so the form widget sends numbers back as JSON numbers.
  - **Links.** When the widget names a form page, a create button links to
    `/{site}/{formpage}/new` and each row has an open link to
    `/{site}/{formpage}/{id}`, with the page name in lower case. The create
    button is a link: a click with a modifier key or the middle button opens
    the form in a new tab or window. Both pass the table's address, with
    its paging and sorting, to the form in history state. Without a form
    page there is no create button and no open column.
  - **States.** Loading, empty and error states use the shared table and
    alert with platform texts. While the next page loads, the current rows
    stay under the loading overlay.
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
  - A field appears at most once in the form. A field the form does not list
    is not shown and never sent.
  - Computed fields and sequence fields are always read-only, whatever
    `readOnly` says.
  - On a page, `readOnly` only shapes the UI until policies arrive in M4,
    because the record API still accepts those fields. In a task, the server
    enforces it: completing a task rejects a value for a field the form does
    not make editable (see [Task API](processes.md#task-api)).
  - A section's `fields` and the form's `sections` hold at least one entry,
    and a section without a `title` breaks the schema (`AXC0004`).
  - Compile checks: an `entity` that names no loaded entity is `AXC0074`. A
    `field` the entity does not have is `AXC0075`. A field listed again, in
    any section and ignoring letter case, is `AXC0076`. A section title key
    that no locale has is `AXC0028`. See
    [Configuration pipeline](configuration.md#configuration-pipeline).
  - A form over a child entity is not checked yet.
- **Form widget.** A page whose widget is a `form` creates a record at
  `/{site}/{page}/new` and edits one at `/{site}/{page}/{id}`, through the
  record API. It adds no rules of its own: the server validates.
  - **Inputs.** There is one input per field, in declaration order, labelled
    with the field's label or the field name. A widget that names a `form`
    shows only the fields its form lists, in its order, under the section
    titles (see **Form binding**). A required field's label is
    marked, but the mark blocks nothing: the server still decides.

    | Field type | Input |
    | --- | --- |
    | `text` | a text input limited to `maxLength` |
    | `integer`, `decimal` | a plain text input with a numeric keyboard hint. It keeps the typed text as it is |
    | `boolean` | a checkbox |
    | `date` | a date picker |
    | `date-time` | a date and time picker to the second, in the browser's time zone. It sends the instant in UTC with no fraction |
    | `enum` | a choice of the declared values, as written in the entity file |
    | `reference` | the label of the chosen record, read-only, with a choose button that opens the lookup. A field that is not required and is set also has a clear button |
    | `child-collection` | a table of its rows, described under **Child collections** |
    | any type with `computed` or `sequence`, and any field but a child collection that the form makes read-only | a read-only text input with the value the server returned, formatted as a table cell shows it. It changes only when the record is saved and loaded again |

  - **Lookup.** The choose button opens a dialog titled with the field's
    label. It lists the target entity's records through their record API,
    sorted ascending by the target's display field, in one column for that
    field. It pages 20 records at a time and keeps nothing in the URL.
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
  - **Child collections.** A child collection field shows its rows as a
    table under the field's label.
    - There is one column per child field, headed by its label or its name.
      Each cell uses the input the form uses for that field type, so number
      text, dates and enums behave as in the rest of the form. A cell
      input's accessible name is the column header and the one-based row
      number, such as `Quantity 2`. A computed child field's cell is a
      read-only text input with the row's value, formatted as a table cell
      shows it.
    - Each row has a remove button. An add button below the table appends a
      row with every child field `null`. Rows cannot be reordered.
    - When rows were added, removed or edited, the form sends the whole row
      list, as [record-api.md](record-api.md#child-rows-computed-fields-and-validations) describes. Each row holds its
      non-null child fields in declaration order, without its computed fields. Rows are compared by value
      with the rows the form started from, so an edit that is typed and then
      undone leaves the collection out of the body.
    - A key `/values/<collection>/<index>/<field>` for an existing row and a
      child field shows its messages under that cell. A key
      `/values/<collection>` shows under the table. Any other key inside the
      collection, such as a whole row, shows above the form. Adding or
      removing a row clears the collection's cell errors, because their
      indexes would point at the wrong rows.
  - **Changed fields.** The form sends only the fields whose value differs
    from the value it started from. It never sends a computed, sequence or
    read-only field, nor a field its form does not show. A new record starts with every field
    `null` and every child collection empty, so an untouched field stays out of a create and the server
    decides what is required. An edit also sends the `version` it read. An
    emptied text or number input is sent as `null`.
  - **Number text.** The body is written by hand, so an integer or decimal
    goes out as the typed text and no digit is lost. Text that is not a JSON
    number, such as `1,5`, goes out as a JSON string, and the server rejects
    it on its field. The form never turns number text into a JavaScript
    number.
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
  - **History** *(planned for M3)*. The form page of an existing record shows
    the record's history from the
    [history endpoint](record-api.md#audit-records-and-history), newest
    first.
  - **Sequence fields**. Page metadata marks a field that
    names a [sequence](configuration.md#sequences), so the form shows it
    read-only. It is empty on a new record, and the form never sends it.
  - **Form binding**. A `form` widget names a `form`
    resource, or an `entity` as shorthand for all its fields in declaration
    order. Naming both or neither is `AXC0060`, and a `form` that names no
    loaded form is `AXC0077`. With a `form`, the widget shows its sections in
    order, each under its title as a heading, and shows a read-only field as
    on a computed field. A read-only child collection shows its rows with
    every cell read-only, and has no add or remove buttons. The widget sends
    only the editable fields it shows. A server error on a field the form
    does not show appears above the form.
  - **Start process action** *(planned for M3)*. A `form` widget can declare
    `startProcess` actions. Each is shown as a button below the form, with
    its label.

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

    - The process must name the form's entity as its subject.
    - A click saves the record first: a create on a new record, or the
      changed fields on an existing one, as the save button does. If the save
      fails, its errors show as for a save, and nothing starts.
    - Then it posts `{ "subjectId" }` to the
      [start endpoint](processes.md#start-endpoint), with a new
      `Idempotency-Key` for each click.
    - On `201` it shows a success message and loads the record again, at its
      edit address when it was new. A `400` or `409` shows its message above
      the form, looked up in the site texts first.
    - The button is disabled while the save and the start run.
  - **Form metadata**. Page metadata gives the widget's
    `entity` with all its fields, so the SPA knows every field's type. It
    adds:
    - `form`: `{ "name", "sections": [ { "titleKey", "fields": [ { "name", "readOnly" } ] } ] }`,
      or `null` when the widget names an `entity`. `readOnly` already
      includes computed and sequence fields. Field names are as the entity
      declares them.
    - *(planned for M3)* `actions`: `[ { "type", "process", "labelKey", "instancesPath" } ]`,
      or `[]`. `instancesPath` is the start endpoint, such as
      `/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances`,
      so the SPA never builds it itself.
- **Task inbox widget** *(planned for M3)*. A page whose widget is a
  `taskInbox` lists the signed-in user's open tasks in the application,
  through the [task API](processes.md#task-api).

  ```json
  {
    "id": "e1a2b3c4-d5e6-4f70-8a9b-0c1d2e3f4a5b",
    "kind": "page",
    "name": "MyTasks",
    "formatVersion": 1,
    "title": { "textKey": "pages.myTasks.title" },
    "widgets": [{ "type": "taskInbox" }]
  }
  ```

  - **Metadata.** The widget has `"entity": null`, `"dataSource": null` and
    a `tasksPath`, such as `"/api/apps/PurchasingApp/tasks"`. `tasksPath` is
    `null` on every other widget and never left out.
  - **Columns.** The task's label, the subject's label and the due date,
    formatted as a `date-time` cell. An empty due date is an empty cell.
  - **Paging.** `page` and `pageSize` live in the URL, as for a table. There
    is no sort.
  - **Links.** Each row opens the task page at
    `/{site}/{inboxpage}/{taskId}`, with the inbox page name in lower case.
  - **Signed out.** With nobody signed in, the task API answers `401`, and
    the widget shows a sign-in prompt in place of the rows.
- **Task page** *(planned for M3)*. The address `/{site}/{inboxpage}/{taskId}`
  shows one task, under the page of its task inbox.
  - It shows the step's label, the subject's label, the due date and the
    form's sections over the subject record. The layout and the field types
    come from the task read's `form`, and the values from the subject
    record, read through the record API.
  - It has one button per outcome, with the outcome's label.
  - A click sends the outcome, the changed editable values and the record's
    `version` to the complete endpoint. On success it returns to the inbox.
  - A `403` shows a message that the task is assigned to someone else.
  - A `409` for a decided task shows a message that the task is already
    completed, and loads the task again.
  - A `400` maps its keys as the form widget does: `/values/<field>` under
    that field, and every other key, such as `/outcome`, above the form.
- **Locale.** The shell header has a locale switch next to the light/dark
  toggle. The chosen locale is kept in `localStorage` under `axis.locale`,
  like the theme mode under `axis.themeMode`. One key serves every site: a
  site starts in the stored locale when it offers it, and in its default
  locale otherwise. The shell keeps the current texts until the chosen
  locale's texts have loaded. If they fail to load, it stays in the current
  locale and shows an error message.
  - Ant Design components, such as picker placeholders, follow the UI
    locale. `en` maps to Ant Design's `en_US` and `vi` to `vi_VN`, ignoring
    letter case, and any other locale uses `en_US`. dayjs, which the pickers
    use, follows the same locale. The shell sets both through a nested
    `ConfigProvider` that inherits the theme tokens.
- **Test user picker.** In `Development` and `Testing`, the shell header of
  every site shows a [test user](../architecture.md#development-test-users)
  picker before the locale switch. Its button shows the signed-in user's
  display name, or a sign-in prompt when nobody is signed in.
  - The button opens a menu of the test users in configuration order. Each
    entry is a `menuitemradio`, and the current user has
    `aria-checked="true"`. With someone signed in, the menu ends with a sign
    out entry.
  - Picking a user signs in as that user and reloads the page. Signing out
    also reloads, so every view follows the new user.
  - The picker loads `GET /api/test-users` and `GET /api/me` each time a shell
    mounts. When the list is not available, as in Production, it shows
    nothing. When a sign-in or sign-out request fails, it logs a warning and
    changes nothing.
  - Its texts are the platform texts `shell.user.label`, `shell.user.signIn`
    and `shell.user.signOut`.
- **Text resolution.** The SPA resolves each key through catalogs in order.
  On the platform site the only catalog is the platform texts. Inside an
  application site, the site texts come first and the platform texts second,
  so shared shell texts such as the theme toggle keep working.
  - Inside a site, the platform texts load in the site's current locale when
    the platform offers it. Otherwise only the platform fallback-locale texts
    serve the shell.
  - So a key resolves from the site's current locale, then the site's
    fallback locale, then the platform's current locale, then the platform's
    fallback locale. A key no catalog has shows its key name.
  - Within one catalog, a key missing from the current locale but present in
    the fallback locale shows its key name in development builds, so it gets
    noticed, and the fallback-locale text otherwise. Only a key missing from
    both locales of a catalog moves on to the next catalog. Every locale of a
    catalog has the same keys, so the development rule never hides a
    platform text behind a site that lacks it.
