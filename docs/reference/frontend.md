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
  `form` resource, the task inbox, the task page and the `startProcess` action
  are built. Navigate actions are expected to replace the table's
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
  Application sites below) to list the tenant's sites. When it answers `401`,
  the home page shows a sign-in prompt in place of the list.
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
  | `GET /api/sites/{path}/texts/{locale}` | `{ "locale", "texts" }`, the active release's texts for one locale, in the shape of the platform texts. Optional `?release={releaseId}` serves any stored release of the site's application. An unknown id, a non-UUID or another application's release answers `404`. |
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
              "creator": false, "maxLength": 100,
              "precision": null, "scale": null, "values": null, "target": null,
              "fields": null
            },
            {
              "name": "department", "type": "reference", "labelKey": "item.department",
              "required": false, "unique": false, "computed": false, "sequence": false,
              "creator": false, "maxLength": null,
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
        "form": null,
        "tasksPath": null,
        "process": null,
        "instancesPath": null,
        "actions": [],
        "access": {
          "actions": ["read", "create", "update", "delete"],
          "createFields": ["name", "department"],
          "updateFields": ["name", "department"]
        }
      }
    ]
  }
  ```

  - `titles` in the site list has one entry per available locale of the site,
    from the locale as the site declares it to the title text. The home page
    can show each site without one more request per site.
  - `type` of a widget is `table`, `form` or `taskInbox` or
    `processInspector`. `formPage` is the
    page holding the form for a table's records, and `null` otherwise. On a
    `table` or `form`, exactly one of `entity` and `dataSource` is set, and
    the other is `null`. A `taskInbox` has both `null`, and so does a
    `processInspector`. The data source
    binding of the table widget below describes `dataSource`.
  - Every widget also has `form`, never left out. It is the widget's form
    resource on a `form` widget that names one, and `null` otherwise. It is
    described under **Form metadata** of the form widget.
  - Every widget also has `tasksPath`, never left out. It is described under
    the task inbox widget, and is `null` on every other widget.
  - Every widget also has `process` and `instancesPath`,
    never left out. They are described under the process inspector widget, and
    are `null` on every other widget.
  - Every widget also has `actions`, never left out. It
    is described under the form widget, and is `[]` on a widget it does not
    apply to.
  - Every widget also has `access`, never left out. It is described under
    **Policies** below, and is `null` on a `taskInbox` and on a
    `processInspector`.
  - `entity` is the widget's entity: its name, its label key, its display
    field, the path of its record API in `recordsPath`, and its fields in
    declaration order. A `child-collection` field is listed too. Tables show
    no column for it, and forms show its rows.
  - Each field has its name, its `type` as written in entity files (such as
    `date-time`), its label key, `required`, `unique`, `computed`, `sequence` and `creator`,
    `maxLength` for text, `precision` and `scale` for decimal, `values` for enum, `target`
    for reference, and `fields` for child collection. A property the field's
    type does not have, or a label the file leaves out, is `null`; it is
    never left out.
  - `computed` is `true` for a [computed field](configuration.md#entity-logic).
    The server sets its value on every write, so the form shows it read-only
    and never sends it.
  - `sequence` is `true` for a text field that names a
    [sequence](configuration.md#sequences). The server numbers it on create,
    so the form shows it read-only and never sends it.
  - `creator` is `true` for a text field that declares
    [`creator`](configuration.md#record-creator). The server sets it to the
    creating user's id on create, so the form shows it read-only and never
    sends it.
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
    for. Without `release`, it uses the active release. Optional
    `?release={releaseId}` serves the texts of any stored release of the
    site's application. An unknown id, a non-UUID or another application's
    release answers `404`.
  - The page endpoint serves any page of the site's application, not only
    pages in the site's navigation, because form pages opened from a table
    are not in navigation.
  - `{path}`, `{locale}` and `{page}` match ignoring letter case. A path
    outside the pattern (an ASCII letter, then ASCII letters, digits or
    hyphens, at most 60 characters, matched ignoring letter case) is a 404
    without a query. A reserved path such as `api` is a 404 because no site
    can hold it.
  - An unknown site, page or locale is a 404 problem with a fixed title. A
    site active only in another tenant does not exist for the request.
  - The endpoints need a session. Without one they answer a `401` problem,
    before any `404`, so a signed-out caller cannot learn which sites or
    pages exist. So every application page starts sign-in when nobody is
    signed in. The record API and the data source endpoint also need a
    grant, so a page's data fails to load for a user without one.
  - **Policies**. Policies shape the site and page metadata, as
    [Authorization](../architecture.md#authorization) describes.
    This only shapes the UI. The server checks every request.
    - **Always shaped.** The endpoints need a session, so the metadata is
      always shaped by the user's grants.
    - **Readable pages.** A page is readable when its widget is. A widget
      over an entity, or over a form of an entity, needs a `read` grant on
      that entity. A widget over a data source needs what the
      [data source endpoint](data-sources.md#authorization) needs. A
      `taskInbox` is always readable. A
      `processInspector` is readable when the user holds an `inspect` grant on
      its process.
    - **Unreadable pages.** The page endpoint answers `403` for a page the
      user cannot read. Site navigation leaves out entries whose page the
      user cannot read.
    - **Fields.** `entity.fields` and the form metadata leave out the
      fields the user may not read. A form section with no field left is
      left out. A child collection field covers its rows as a whole, so it
      is left out, or kept with all its `fields`. `id` and `version` are not
      affected. The task API's form does the same, see
      [policies on tasks](processes.md#task-api).
    - **Actions.** A `startProcess` action is left out of `actions` when the
      user has no `start` grant on its process.
    - **Access.** Every `table` and `form` widget gets an `access` object.
      It says which entity actions the user may take, and which fields each
      write action covers:

      ```json
      "access": {
        "actions": ["read", "update"],
        "createFields": [],
        "updateFields": ["title", "department", "supplier", "currency", "lineItems", "status"]
      }
      ```

      - `actions` lists the entity actions of the user's grants on the
        widget's entity, in the order `read`, `create`, `update`, `delete`.
        On a data source widget it is about the root entity. Record filters
        are not evaluated here, so `update` means that some record may be
        updated, not this one.
      - `createFields` and `updateFields` list the fields the user's
        `create` and `update` grants cover, in declaration order. Each is
        `[]` when the action is not in `actions`.
      - A table shows its create button only with `create`. A form shows a
        field it may not write as read-only, as on a computed field, and
        never sends it. On a new record it writes the `createFields`, and on
        a saved record the `updateFields`.
      - A form has no save button without the action it needs: `create` on a
        new record and `update` on a saved one. Cancel and the start process
        buttons stay.
      - A `taskInbox` has `"access": null`, and so does a `processInspector`.
- **Routing.** The SPA routes on the client with `react-router` v7. The
  server answers every other path with the SPA, so each address below
  answers 200.

  | Address | Shows |
  | --- | --- |
  | `/` | the platform home page: the server status and the tenant's sites as links, each titled in the current locale, or in the site's first locale when it lacks the current one. With nobody signed in, a sign-in prompt shows in place of the sites, and the page does not start sign-in |
  | `/{site}` | the site's first navigation entry, by redirect |
  | `/{site}/{page}` | the page inside the site shell, with the page title. A table page also takes `?page=&pageSize=&sort=`. A table bound to a data source also takes its parameter values |
  | `/{site}/{page}/new` | the form of a form page, to create a record |
  | `/{site}/{page}/{id}` | the form of a form page, to edit the record with that id |
  | `/{site}/{inboxpage}/{taskId}` | the task page of the task with that id, under the page of a task inbox |
  | `/{site}/{inspectorpage}/{instanceId}` | the instance page of the process instance with that id, under the page of a process inspector |

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
  - `new` on a task inbox page, and a task id that is not
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
    `/{site}/{formpage}/new`, if the widget's `access` has `create`. Each row
    has an open link to
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
  - Computed, sequence and creator fields are always read-only, whatever
    `readOnly` says.
  - On a page, `readOnly` only shapes the UI. The record API enforces the
    user's field write grants, so it still accepts a read-only field that an
    `update` grant covers. In a task, the server
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
    | any type with `computed`, `sequence` or `creator`, and any field but a child collection that the form makes read-only | a read-only text input with the value the server returned, formatted as a table cell shows it. It changes only when the record is saved and loaded again |

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
    from the value it started from. It never sends a computed, sequence, creator
    or read-only field, nor a field its form does not show. A new record starts with every field
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
    names a [sequence](configuration.md#sequences), so the form shows it
    read-only. It is empty on a new record, and the form never sends it.
  - **Creator fields**. Page metadata marks a field that declares
    [`creator`](configuration.md#record-creator), so the form shows it
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
    next to Save and Cancel, with its label. For a signed-in user, page
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
  - **Form metadata**. Page metadata gives the widget's
    `entity` with all its fields, so the SPA knows every field's type. It
    adds:
    - `form`: `{ "name", "sections": [ { "titleKey", "fields": [ { "name", "readOnly" } ] } ] }`,
      or `null` when the widget names an `entity`. `readOnly` already
      includes computed, sequence and creator fields. Field names are as the entity
      declares them.
    - `actions`: `[ { "type", "process", "labelKey", "instancesPath" } ]`,
      or `[]`. `instancesPath` is the start endpoint, such as
      `/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances`,
      so the SPA never builds it itself.
- **Task inbox widget**. A page whose widget is a
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

  - **Compile.** A `taskInbox` names no `entity`, `dataSource` or `form`,
    otherwise it is `AXC0060` at that property. It names no `formPage`,
    otherwise it is `AXC0022` at `/widgets/{i}/formPage`.
  - **Metadata.** The widget has `"entity": null`, `"dataSource": null`,
    `"form": null`, `"formPage": null` and a `tasksPath`, such as
    `"/api/apps/PurchasingApp/tasks"`. `tasksPath` is `null` on every other
    widget and never left out.
  - **Order.** Rows keep the task API's order: the earliest due date first,
    and tasks with no due date last. The SPA does not sort them again.
  - **Columns.** The task's label, from the step's label text key, the
    subject's label and the due date, formatted as a `date-time` cell. An
    empty subject label or due date is an empty cell.
    The inbox loads the texts of each release its rows name through
    `releaseId`, once per release. Tasks from several releases each show
    their own release's labels. The table stays loading until those texts
    have arrived, and a failed load shows the inbox's load error.
  - **Paging.** `page` and `pageSize` live in the URL, as for a table. There
    is no sort.
  - **Links.** Each row opens the task page at
    `/{site}/{inboxpage}/{taskId}`, with the inbox page name in lower case.
  - **Signed out.** With nobody signed in, the task API answers `401`, and
    the widget starts sign-in like any other call. The platform texts
    `shell.tasks.*` hold the column headers, the empty text and the load
    error.
- **Task page**. The address `/{site}/{inboxpage}/{taskId}`
  shows one task, under the page of its task inbox.
  - It loads the texts of the task's `releaseId` with
    `GET /api/sites/{path}/texts/{locale}?release={releaseId}`, for the
    current and the fallback locale. The step, form and outcome labels use
    those texts first, then the site texts and the platform texts. If they
    fail to load, the page shows its load error.
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
  - A `409` that names keys, such as a stale `/version` or a duplicate
    unique value, is mapped the same way. There is no reload button.
  - A task that is not `open`, when the page loads or after a `409` loads it
    again, shows only the already completed message, with no form or
    outcome buttons. So nobody retries a decision that cannot succeed.
  - With nobody signed in, the task read answers `401`, and the page starts
    sign-in like any other call.
  - Any other failure to load the task or its subject record, or to
    complete it, shows a generic error message.
  - On success the page goes to the inbox page's plain address, without the
    paging the person came from.
  - The `version` is sent even when no field changed. The task API ignores
    it then.
  - The task page has no history panel.
  - The platform texts `shell.task.*` hold the assigned elsewhere and
    already completed messages, and the load and complete errors.
- **Process inspector widget**. A page whose widget is a
  `processInspector` names one process. It lists the process's instances and
  opens one, through the [inspection API](processes.md#process-inspection).

  ```json
  { "type": "processInspector", "process": "PurchaseRequestApproval" }
  ```

  - **Compile.** The `process` must name a loaded process, otherwise it is
    `AXC0192` at `/widgets/{i}/process`. The widget names none of `entity`,
    `dataSource` and `form` (`AXC0060` at that property), and no `formPage`
    (`AXC0022`).
  - **Metadata.** The widget has `"entity": null`, `"dataSource": null`,
    `"form": null`, `"formPage": null` and `"tasksPath": null`. It adds
    `process` and an `instancesPath`, such as
    `"/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances"`,
    the list endpoint. The SPA never builds the path itself. It uses `process` to link the record history to the page, see
    **History** under the form widget. The widget has `"access": null` and
    `"actions": []`. The page endpoint answers `403` to a user without an
    `inspect` grant on the process, and site navigation leaves the page out.
  - **List.** The page lists the instances with their state, step, subject id,
    release and times. Rows keep the API's order, newest first. A clearable
    select above the list filters by state, and offers the five states with
    platform texts. `state`, `page` and `pageSize` live in the URL, as for a
    table. Changing the state goes back to page 1. A `state` in the URL that is
    not one of the five is dropped. The step column shows the step's name,
    because a list row carries no label key. Each row has an Open link.
  - **Instance page.** A row opens `/{site}/{page}/{instanceId}`, with the
    page name in lower case. `new` and an id in the wrong form show the
    not-found page, and the instance is not requested. The page reads the
    instance detail and shows the steps as a list in graph order, each with
    its type and its transitions, with no diagram. Steps the instance has run
    are marked as taken, and the step it is at is marked as current, whatever
    the instance state. Below it are the history rows with the step, the
    times, the decision and the error, then the tasks, then the calls with
    their attempts. A call whose `inputHidden` is `true` shows "hidden" in
    place of its input. A shown input is a list of its top-level properties,
    where a number keeps its source digits.
  - **Commands.** The page has a Cancel instance and a Retry button for each
    of those commands that the instance detail's `commands` lists. With none
    listed, it has no button, and a command name this build does not know is
    left out. A button opens a dialog that asks for a reason. The reason is
    required, must not be blank and has at most 500 characters, and Send is
    blocked until it is valid. On success, the dialog closes and the page reads
    the instance detail again, so it shows the new state, tasks, calls and
    `commands`. On a `400`, `403`, `404` or `409`, the dialog shows the
    problem's title. Any other failure shows a generic text, and a `401`
    starts sign-in. The Retry dialog first warns when a call on the instance's
    current step has `failed`, and that call is a package call whose
    `idempotent` is not `true`. A failed connector call gets no warning. The
    server still checks every command, so the page never decides who may act.
  - **Resolve.** A call whose `resolvable` is `true` has a Resolve button,
    whether it is a connector or a package call. A call that is not resolvable
    has none, because the server decides what may be resolved and by whom. The
    button opens a dialog that asks for a reason, with the rules of the other
    commands, and an outcome, `succeeded` or `failed`. The outcome has no
    default, and Send is blocked until one is chosen. For `succeeded`, the
    dialog shows one input for each declared output of the call, labelled with
    its name and type, with the required ones marked and checked before Send.
    A `boolean` output is a true/false choice, and every other type is text,
    so a date or date-time is typed as the API takes it. The values go out
    typed: an integer or decimal keeps every digit, a boolean is `true` or
    `false`, and an empty optional output is left out. A `failed` outcome sends
    no output. On success, the dialog closes and the page reads the instance
    again. On a `400`, the message of each `/output/{name}` error shows under
    that output's field, and the problem's title shows in the dialog. Any other
    refusal shows its title, and any other failure shows a generic text.
  - **Labels.** Step labels come from the texts of the instance's `releaseId`,
    loaded with `GET /api/sites/{path}/texts/{locale}?release={releaseId}`, as
    the inbox does. A step without a label shows its name.
  - **Errors.** With nobody signed in, the API answers `401` and the widget
    starts sign-in like any other call. A `403` shows a message that the user
    may not inspect the process. A `404` on the instance page shows a message
    that the instance does not exist, inside the page. Any other failure shows
    a generic load error. The server checks every request, so the page never
    decides who may inspect.
  - The platform texts `shell.inspector.*` hold the column headers, the
    section titles, the instance states, the "hidden" text, the empty texts
    and the messages. The texts `shell.inspector.commands.*` hold the command
    buttons, the dialog's labels, the reason errors, the retry warning and the
    resolve labels and errors.
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
- **Account button.** The shell header of every site shows an account
  button first among its actions. It loads `GET /api/me` each time a shell
  mounts, and shows nothing while it loads or when the load fails. See
  [sign-in and the session](../architecture.md#sign-in-and-the-session).
  The header also reads `studio` from it, and shows the [Studio](#studio)
  link beside the button when it is `true`.
  - With nobody signed in, it is a **Sign in** button. It sends the browser
    to `/api/auth/sign-in` with the current path and query string as
    `returnUrl`, so the person comes back to the same page.
  - With someone signed in, it shows their display name. Its menu holds
    **Sign out**, which ends the session and then sends the browser to the
    identity provider's sign-out page.
  - A `401` from any API call starts the same sign-in, with the current path
    and query string as `returnUrl`. Two calls are exempt. `GET /api/me`
    answers `401` when nobody is signed in, so the header shows the button.
    The home page's `GET /api/sites` answers `401` too, so the home page
    shows a sign-in prompt and stays open to a signed-out person.
  - Its texts are the platform texts `shell.account.signIn`,
    `shell.account.signOut` and `shell.account.label`, the menu's label.
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

## Studio

Studio is where authors edit an application's resources. It uses the
[authoring API](authoring.md) and writes the same JSON files that git keeps, as
[D38](../decisions.md#d38-studio-edits-application-folders-through-an-authoring-api--agreed)
agrees. The route, the link, the application picker, New application, New resource, the resource list with its
search, the editor, the reference pickers, the Uses and Used by lists, the delete
button and its warning, the diagnostics panel, Validate, Preview and Publish with
the request status and the preview links, the Releases list with Roll back, Run tests,
Run and the Test results list, and the reconnect message are built.

- **Route.** `/studio` is a platform route, outside every site. The header
  shows a Studio link only when `GET /api/me` answers `studio: true`. A person
  who opens the route without the author role gets a `403` from the API and
  sees a forbidden page. A signed-out person is sent to sign in.
- **Address.** The picked application and the selected resource are in the
  address, as `/studio?app=PurchaseRequests&resource=<id>`, so a reload keeps
  them. A Used by entry or a diagnostic that opens a resource at a path adds
  `&path=<path>`.
- **New application.** A **New application** button shows beside the picker
  when `GET /api/me` answers `studioCreate: true`. It also shows when Studio
  lists no application. The dialog asks for a name and checks it with the API's
  rule before it sends anything. It offers Blank or a template from
  `GET /api/studio/templates`, with the title and description in the UI locale
  and `en` as the fallback. A `409` or a `400` shows next to the name. A success
  adds the application to the picker, picks it, and loads its resources and
  diagnostics. See
  [creating an application](authoring.md#creating-an-application) and
  [D51](../decisions.md#d51-creating-applications-and-resources-from-studio--agreed).
- **New resource.** A **New resource** button above the search box opens a
  dialog that asks for the kind and the name, or the locale for a `text`
  resource. The kind picker lists the 18 kinds that the create route accepts,
  which is every kind except the manifest.
  - It sends the kind's starter with an id from `crypto.randomUUID()`. A starter
    is the smallest content that passes the kind's schema. A reference the
    author must still choose is an empty string, so it shows up as a diagnostic.
    A new `site` gets the kebab-case name as its `path`, by the same rule as
    file names, because a path is a value and not a reference.
  - The starters live in the SPA, in `web/src/platform/studioStarters.json`.
    Each lists the pointers of its empty references. A Vitest test checks each
    one against its bundled schema, so a schema file without a starter fails,
    and every error must be at a listed pointer. A server test reads the same
    file, sends each starter through the create route, and checks that its
    diagnostics are at the listed pointers only.
  - A new text resource copies every key and text of the default locale of the
    first site in file order, or `en` when there is no site, so every label still
    resolves. The author then translates. Its name is `Texts` and the locale,
    such as `TextsVi` for `vi` and `TextsPtBr` for `pt-BR`.
  - A `409`, when the id or the file exists, and a `400`, when the name or the
    locale is refused, show under the field and keep the dialog open.
  - After a create, the new resource joins the list, the diagnostics panel shows
    the diagnostics that the create answered, and the editor opens the resource.
  - Its texts are platform texts under `shell.studio.*`, in `en` and `vi`.
- **Application picker.** The first step picks one of the applications that
  `GET /api/studio/apps` lists. With only one application, it is picked at
  once.
- **Resource list.** The list groups the resources by kind. The kind shows as
  written in the files, such as `entity`, without translation. A search box
  filters it by name and `id`, ignoring letter case. Selecting a resource shows
  its name, kind, `id` and file, and opens it in the editor below.
- **Editor.** The editor shows one resource in two views of the same draft.
  - A form is built from the JSON Schema of the resource's kind. The SPA
    bundles the schemas in `src/Axis.Configuration/Schemas`, the files the
    server embeds, and loads the one it needs when a resource is selected. The
    form shows text, number, boolean and enum values, nested objects, and
    arrays with add and remove. `id`, `kind` and fixed values such as
    `formatVersion` are read-only. Labels are the property names as written in
    the schema, without translation. A part the form cannot show, such as
    `oneOf`, `anyOf` or a map, points to the JSON view. A text resource's
    `texts` map shows in the Text editor, see **Editors by kind** below. An
    `allOf` whose items hold only `if`, `then` and `else` shows its
    properties, and the form does not show the conditions.
    A label, an object whose only property is `textKey`, has its own editor, see
    Label texts below. Until the names load, a label shows as a reference picker
    over the application's text keys, and a key that the application does not
    define is tagged unknown.
  - A JSON view shows the draft as text. A valid edit there updates the form.
    Invalid JSON shows an error and turns Save off.
  - Save sends only the changes, as a JSON Patch with the version the editor
    loaded in `If-Match`. It stays off while nothing has changed. An array
    whose length changed is replaced as a whole, and an array of the same
    length is compared item by item. Clearing an optional value removes its
    key. After a save the editor carries on from the new version.
  - A stale version is a `409`. The editor keeps the author's draft on screen
    and offers Reload. Reload reads the resource again and drops the draft.
    Selecting another resource also drops an unsaved draft, without a warning.
  - A kind with a typed editor shows it in place of the form, and keeps the
    JSON view as the second view of the same draft. The typed editor saves
    through the same JSON Patch, with the same `If-Match` rules. It never drops
    a value it does not show, so unknown keys stay as they are. See
    **Editors by kind** below. The seed editor, the scenario editor, the
    connector editor, the page editor, the endpoint mapping rows, the text
    editor and the process editor's step list, with its decision, end and
    task and operation panels, are built.
- **Uses and Used by.** Each resource shows a **Uses** list and a **Used by**
  list, from the [references routes](authoring.md#references). Each entry shows
  its relation, the other resource, and the file and path of the reference.
  - Selecting a **Uses** entry opens the other resource at its top.
  - Selecting a **Used by** entry opens the other resource at the path of the
    reference. The path is in the address and the details, and the editor
    scrolls to the form field for that path. Nothing is highlighted. A path
    the form cannot show, such as one inside a map, opens the JSON view instead.
  - An expand control on a resource or text key entry loads one more step, the
    entry's own lists. It loads them once. Collapsing and expanding again shows
    the same lists.
  - A package or a module entry is plain text. It cannot be opened or expanded.
  - Uses and Used by stay lists. The **Process graph** below is the only
    drawn graph *(planned for M11)*.
- **Text keys.** A text resource's **Uses** list holds its keys, each as an
  entry. Expanding a key lists the labels that use it and the locales that
  define it, as its **Used by** list.
- **Delete.** The editor has a **Delete** button, which is hidden for the
  application manifest. It always opens a confirm dialog first. When a
  resource's `usedBy` count is above 0, the dialog warns and names the count. The
  author can still confirm. The delete sends the loaded version in `If-Match`. A
  stale version shows the conflict message, and nothing is deleted. After a
  delete, the resource leaves the list and the diagnostics panel shows the
  diagnostics that the delete answered, which report what broke.
- **Stale counts.** The `usedBy` counts and the lists load with the selected
  resource. A save does not reload them, so a count can be out of date until the
  page is reloaded. The names, and so the counts that labels show, load again
  after a save and after a text write.
- **Diagnostics panel.** The panel lists the diagnostics of the folder, with
  each one's code, severity, message, file and `path`. It loads them from
  `GET /api/studio/apps/{application}/diagnostics` when an application is
  picked. After a save, it shows the diagnostics that the save answered
  instead. A diagnostic that names a resource selects it, clears the search, and
  opens the resource at the diagnostic's `path`, as a Used by entry does. A
  diagnostic with an empty path opens the resource at its top. A diagnostic that
  names no resource cannot be selected. A diagnostic never blocks a save.
- **Publishing.** Up to three buttons sit under the application picker. They use the
  [publishing routes](authoring.md#publishing).
  - **Validate** sends a validate request. **Publish** opens a dialog with an
    optional label, up to 100 characters, and an optional note, up to 2000.
    Confirming sends the publish request. Empty values are left out of the body.
  - **Publish** shows only when `GET /api/me` answers `studioPublisher: true`.
    When that answer is `false` or fails, **Publish** does not show.
  - **Preview** sits next to **Validate** and shows only when `GET /api/me`
    answers a `studioPreviewHost` that is not null. It does not need
    `studioPublisher`. It sends `POST .../preview` at once, with no dialog, and
    the status line shows **queued**, **running** and the outcome, as for Publish.
    A succeeded preview shows **Previewed** and a link to each of the
    application's sites on the preview host. Studio builds a link from the page's
    protocol and port, the `studioPreviewHost` and the site's `path`, and it reads the
    paths from the folder's `site` resources when the preview succeeds. A site
    renamed after the request was sent can give a wrong link. The preview host
    must be served on the same port and protocol as the page. A failed preview
    shows **Preview failed** and puts its diagnostics in the diagnostics panel.
    A preview shows no release number and does not change the Releases list,
    because its release belongs to the preview tenant's own history.
  - Studio then checks `GET .../requests/{id}` every second until the status is
    `succeeded` or `failed`, with no time limit. A status line shows **queued** or
    **running** meanwhile. It shows the outcome only once the publisher has finished
    the request.
  - A succeeded publish or rollback shows its release number. Studio reads it from
    `GET .../releases`, by matching the request's `releaseId`. A failed publish shows
    the failure and no release number, even when the request names a release, so a
    failure never looks like a success. A rollback shows its own texts, **Rolled back**
    and **Rollback failed**.
  - When a request ends, the diagnostics panel shows the request's diagnostics in
    place of the loaded ones, for a failed request and for a succeeded one that holds
    warnings. They open at their paths as before.
  - The buttons are off while a request runs and while the reconnect message
    shows. If the server does not answer during a request, Studio checks it again
    after the reconnect.
- **Running tests.** Studio runs the application's
  [test scenarios](configuration.md#test-scenarios) through the
  [test route](authoring.md#test), as
  [D55](../decisions.md#d55-running-test-scenarios-from-studio--agreed) agrees.
  - **Run tests** sits next to **Validate**. It sends `POST .../test` with no
    body, so every scenario runs.
  - **Run** sits in the details of a selected `scenario` resource. It sends
    `{ "scenarios": ["<name>"] }`, so only that scenario runs.
  - Both run the saved files, not an unsaved draft in the editor, as Validate does.
  - Both show only when `GET /api/me` answers `studioTest: true`. When that
    answer is `false` or fails, they do not show. They are off while a request
    runs and while the reconnect message shows.
  - The status line works as for Validate. It shows **queued**, **running** and
    the outcome, and Studio checks `GET .../requests/{id}` every second until the
    status is `succeeded` or `failed`. A test shows **Tests passed** or
    **Tests failed**, and never a release number.
  - The **Test results** list shows each scenario of the request's `scenarios`
    field, with **passed** or **failed**, its time and, for a failure, the failing
    step and its message. Steps count from 1, as the step cards of the editor do.
    Selecting a failure opens the scenario resource at the item's `path`, as a
    diagnostic does.
  - While the request is queued or running, the list shows a note instead of rows,
    so an empty list never reads as a pass. A failed run with no results says that
    no scenario ran, and points to the diagnostics. Compile errors, a missing
    setting and an unknown scenario name fail a run this way.
  - The list is not kept across a page reload. The request's diagnostics go in the
    diagnostics panel, as before.
  - The buttons are off while a request runs and while the reconnect message
    shows.
- **Releases.** A list under the publishing buttons shows the open application's
  release history from `GET .../releases`.
  - Each row is one activation: its release number, label, note, who activated it
    and when. Rows are sorted by time, newest first.
  - Exactly one row is marked **Active**. A release that was activated more than
    once has a row for each activation, and only its newest row is marked.
  - **Roll back** shows only when `GET /api/me` answers `studioPublisher: true`. It
    sits on every row of a release that is not the active one, and is off while a
    request runs and while the reconnect message shows.
  - **Roll back** opens a dialog that names the release number and label. Confirming
    sends `POST .../releases/{id}/rollback`. The status line then shows **queued**,
    **running** and the outcome, as for Publish. A failed rollback puts its
    diagnostics in the diagnostics panel, and the list still marks the previous
    release **Active**.
  - A warning shows when the folder's content hash differs from the active release,
    for example after a save or a rollback. It does not show without an active
    release, or while the folder has errors, because the server then returns no
    content hash for the folder.
  - The list loads when the application is picked, and again after a request ends,
    after a save, a text write or a delete, and after a reconnect.
- **Reconnect.** Under `scripts/dev.sh` a saved file restarts the server.
  Studio shows a reconnect message at the top of the page when a request gets
  no answer, or answers `502`, `503` or `504`. The Vite dev proxy answers `502`
  while the server is down.
  - Studio then calls `GET /api/me` with a back-off of 0.5, 1, 2 and then 4
    seconds, with no limit, until the server answers with any other status.
  - Then the resource list, the diagnostics, the open resource and its Uses
    and Used by lists load again, and the message goes away. The names load again
    too.
  - An unsaved draft stays on screen, with the version it was based on. If the
    file changed in the meantime, the next save gets a `409`. Studio does not
    resend a save that got no answer, so the author clicks Save again.
  - Save and Delete are off while the message shows.
  - A `401` from a reload starts sign-in, as elsewhere.
- **Reference pickers.** A property that the schema marks with
  [`x-axis-reference`](configuration.md#schema-keywords) shows as a select over
  the names that the [names route](authoring.md#names) lists, in place of a text
  box.
  - The select is searchable, and it matches a typed name without regard to
    letter case. A field or step part lists the fields of the entity, or the
    steps of the process, that the draft names.
  - A value that is not one of the names stays in the select as the first
    option, tagged **unknown**, so nothing is lost. The select takes only the
    listed names. To set a name that is not listed, the author edits the value
    in the JSON view.
  - Until the names load, the property shows a text box.
  - The names load with the application. They load again after a save, a text
    write, a delete and a reconnect.
- **Label texts.** A label is an object whose only property is `textKey`. It
  shows the key and one text box for each locale of the names, in place of the
  key alone. The **Write texts** button writes every locale in one call through
  the [text write route](authoring.md#text-writes). The label shows the texts
  of the key from the names, and the boxes load again when the author types
  another known key. Until the names load, the label keeps the reference picker.
  - A new label gets the key
    `<kind folder in camelCase>.<name in camelCase>.<path segments without the final textKey, joined with '.'>`,
    such as `pages.myTasks.title`. It adds `2`, `3` and so on when the key is
    taken. The author can change it. An empty `textKey`, as a new resource's
    starter leaves it, counts as no key and gets the same suggestion.
  - The text is written at once, when the author clicks **Write texts**, and
    not on Save. Then the key goes into the draft and is saved with the next
    Save. An unused key does no harm. A write that fails shows a message and
    leaves the draft as it was. The button is off while it writes, while the
    reconnect message shows, and while the key is empty or holds `/`.
  - A text write counts as a change of the folder, as a save does. The
    diagnostics panel shows the diagnostics that it answered, and the names and
    the Releases list load again.
  - A key that other labels use shows how many, from `usedBy` in the names
    answer, so the author knows the edit changes them too.
- **Expression editor.** A string property that the schema marks with
  `x-axis-expression` shows a code editor in place of a text box.
  - It is CodeMirror 6, about 100 KB gzipped, in a chunk that loads only when
    Studio first shows an expression. It highlights the
    [expression grammar](expressions.md#grammar).
  - It checks the text 300 ms after the last keystroke, through the
    [expression check route](authoring.md#expression-check). Only the latest
    answer is shown. It underlines the range from `start` to `end`, shows the
    error code and message under the editor, and shows the result `type` when
    there is no error.
  - **Completion.** Typing a word, or Ctrl+Space, opens a list of the names in
    scope. The arrow keys and Enter pick an option. The names come from the
    [names route](authoring.md#names), loaded with the application and again
    after a save, a delete and a reconnect.
    - The fields of the entity at the place: the entity being edited, a policy
      grant's `entity`, or a data source's or process's `entity`.
    - A data source filter offers its parameters, and a rule offers its own.
    - The application's rules, with module rules as `<module>:<name>`, and the
      functions and aggregates of the [expression language](expressions.md#functions).
    - `currentUser()` only in a policy grant's filter. Other expressions, such
      as triggers, migrations and endpoints, offer the rules and the functions only.
    - A function or rule inserts with its brackets and puts the cursor inside
      them. `now()` and `currentUser()` put the cursor after them.
    - When the names do not load, the list offers the functions only. Typing and
      Save keep working.
  - The check uses the saved resource, so a draft path that is not saved yet may
    have no answer. When there is no answer, or the check answers `400` or
    `401`, the last result stays on screen. A `401` does not start sign-in, so
    the draft is kept. The editor never blocks Save.
- **Entity designer** *(planned for M11)*. An `entity` resource opens in a
  designer in place of the generic form. The JSON view stays. See
  [D54](../decisions.md#d54-the-entity-designer-and-the-policy-matrix--agreed).
  - The fields are a table. Its columns are name, label, type, required,
    unique and the type's own settings: `maxLength`, `precision` and `scale`,
    `values`, `target` and the child entity of a `child-collection`. The
    settings follow the
    [field types](configuration.md#entity-field-types-and-constraints), so a
    setting a type does not take cannot be edited.
  - A computed field's `expression` and each validation use the
    [expression editor](../decisions.md#d52-reference-pickers-label-texts-and-the-expression-editor--agreed).
  - Fields can be added, removed and reordered. A display field picker lists
    the entity's fields.
  - A module entity is read-only, as it is in the generic form.
- **Migrations from the designer** *(planned for M11)*. The designer compares
  with the entity as Studio loaded it. "Loaded" means as Studio read the
  entity, not the active release. It asks a question for a change to a loaded
  field and adds the migration change that goes with the answer:

  | Change to a loaded field | Question | Migration added |
  | --- | --- | --- |
  | Rename | Keep its data? | `renameField` with `from` and `to`. |
  | Remove | Drop its data? | `dropField`. Answering No keeps the field. |
  | Type change | A `convert` expression | `convert` with the author's expression. |
  | Narrower `maxLength` | A `convert` expression | `convert`. |
  | Removed enum values | A `convert` expression that maps them to kept values | `convert`. |
  | Now required, or a new required field | Fill the nulls? A `fill` expression | `fill`. |
  | To or from `enum`, or `unique` added | None | None. The designer warns, and Validate reports `AXC0016`. |
  | Rename or remove of a `child-collection` field | None | None, because it has no column. |

  - The migration resource is named `<Entity>Change<UTC yyyyMMddHHmmss>`, for
    example `PurchaseRequestChange20261020093000` in
    `migrations/purchase-request-change20261020093000.json`. It holds the
    changes of one save in edit order. It never edits an existing migration.
  - The save sends the entity patch and the migration create in one
    [`changes` call](authoring.md#changes). A `409` keeps the draft, as for a
    single save.
  - Validate still runs every activation check, so a missing migration shows
    before a publish.
  - See [migrations](storage.md#migrations) and the
    [resource file shape](configuration.md#resource-file-shape).
- **Policy matrix** *(built)*. One view per application shows who
  may do what.
  - Roles are the rows. The columns are the entities without child entities,
    the data sources, the processes and the endpoints.
  - A cell shows the OR of the role's actions from all its policies, as in
    [combining grants](../architecture.md#combining-grants).
  - A cell opens its grants. Each shows its actions, its filter in the
    expression editor and its fields in the field picker.
  - An edit writes to `<Role>Access`, the PascalCase role name plus
    `Access`. So `department-manager` writes `DepartmentManagerAccess`. The
    policy is created when it is missing, with `roles` set to that role only.
  - A grant from a policy that names several roles shows as shared and opens
    that policy. So does a grant from another policy of this role. A
    `<Role>Access` that names other roles counts as shared too. Its row is
    read-only, with a message.
  - Only the actions a target takes can be chosen. An entity takes `read`,
    `create`, `update` and `delete`. A data source takes `read`. A process
    takes `start`, `inspect`, `cancel`, `retry` and `resolve`. An endpoint
    takes `invoke`. Filter and fields are only on entity grants.
  - The view reads the [policy matrix route](authoring.md#policy-matrix).
  - A Resources / Policy matrix switch sits under the application picker. The
    matrix view is kept in the address as `&view=policies`, so a reload or a
    shared link reopens it. The diagnostics panel shows in both views.
  - Selecting a cell opens its grants under the table. A cell lists the
    role's combined actions. A data source cell that lacks the `read` of its
    root entity warns and names that entity. A read-only row shows its reason.
    A shared grant is tagged Shared, and its Open policy link opens that policy
    in the Resources view.
  - In an editable row, the cell's own grants are editable. They are the
    grants of `<Role>Access` that name exactly this target. The panel reads
    that policy with the resource route, so every edit fits the file that a
    save patches.
    - Each grant has checkboxes for the actions its target takes, and nothing
      else. An action the target does not take stays in the file and shows as
      a warning tag, and only Validate reports it.
    - An entity grant also edits its record filter in the expression editor
      and its fields in a field picker. An empty field list means all fields,
      so clearing the picker removes the `fields` key.
    - Add grant adds a grant for the cell's target. Remove deletes a grant,
      but not the policy's last one.
    - Save is off while a grant has no ticked action, and while nothing
      changed.
  - A save writes one file. It sends one patch with the version the panel
    loaded, and the patch changes only what the author edited. When the role
    has no `<Role>Access`, Save creates `policies/<role>-access.json` with
    `roles` set to that role only. A stale version shows a conflict message,
    keeps the draft and offers Reload, which discards it.
  - After a save, the diagnostics panel, the matrix and the names load again,
    and the selected cell stays open. Selecting another cell, or a matrix that
    loads again after another write, discards an unsaved draft.
  - A shared grant, a grant of a read-only row and a grant in any other policy
    stay read-only. Splitting a shared policy is not done here.
  - See [roles and policies](configuration.md#roles-and-policies).
- **Editors by kind.** Studio uses a typed editor for six kinds, the entity
  designer for `entity`, and the generic form for the rest. The seed editor,
  the connector editor, the scenario editor, the page editor, the endpoint mapping rows, the text
  editor and the process editor's step list, with its decision, end, task and operation panels, are built. The other typed
  editors and the entity designer are *(planned for M11)*, and until they are built those kinds use the generic
  form. The last
  column lists what still needs the JSON view, because the form cannot show it.

  | Kind          | Editor                                | Still needs the JSON view                                             |
  | ------------- | ------------------------------------- | --------------------------------------------------------------------- |
  | `application` | Generic form                          | Nothing                                                               |
  | `entity`      | Entity designer                       | Nothing                                                               |
  | `page`        | Typed editor                          | A widget or layout that M12 adds, until M12 gives it a panel          |
  | `form`        | Generic form                          | Nothing                                                               |
  | `site`        | Generic form                          | Nothing                                                               |
  | `process`     | Typed editor with the process graph   | Nothing                                                               |
  | `rule`        | Generic form                          | Nothing                                                               |
  | `sequence`    | Generic form                          | Nothing                                                               |
  | `role`        | Generic form                          | Nothing                                                               |
  | `policy`      | Generic form                          | Nothing                                                               |
  | `dataSource`  | Generic form                          | Nothing                                                               |
  | `connector`   | Typed editor                          | Nothing                                                               |
  | `endpoint`    | Generic form, typed rows for `values` | Nothing                                                               |
  | `event`       | Generic form                          | Nothing                                                               |
  | `trigger`     | Generic form                          | The `parameters` map of a data source trigger                         |
  | `migration`   | Generic form                          | The `changes` list, because each change's fields depend on its `type` |
  | `seed`        | Typed editor                          | Nothing                                                               |
  | `scenario`    | Typed editor                          | Nothing                                                               |
  | `text`        | Typed editor                          | Nothing                                                               |

  The generic form sends a part to the JSON view when it holds `oneOf` or
  `anyOf`, when it holds `allOf` with an item that has more than `if`, `then`
  and `else`, is an object with no `properties`, such as a map, or has no type. The last column follows that rule. A new kind or property that the
  form cannot show goes to the JSON view until an editor covers it.

  - **Page editor** *(built)*. The Form tab shows the page's name, its title
    with a text box for each locale, and a list of its widgets, each with its
    type and a summary such as `table · PurchaseRequestList`. A panel per
    widget type, `table`, `form`, `taskInbox` and `processInspector`, shows the
    properties the widget has today. A `table` panel has entity, data source
    and form page pickers. A `form` panel has entity and form pickers and its
    `startProcess` actions, each with a process picker and a label. A
    `taskInbox` panel has no properties. A `processInspector` panel has a
    process picker. The JSON view stays as a second view of the same draft.
    - **Type change.** Choosing another type removes the known properties the
      new type does not take, because the compiler rejects them. A switch to
      `processInspector` adds an empty `process`, which the schema requires.
    - **Exclusive bindings.** The compiler accepts exactly one of a table's
      entity and data source, and one of a form's entity and form. Picking one
      clears the other.
    - **Pickers.** The form page picker lists every page except the current
      one. The action's process picker lists every process. Neither checks
      what the choice holds, so a wrong choice shows as a diagnostic after the
      save.
    - **Widgets.** A page holds one widget today, so the list has no add,
      remove or reorder.
    - **Unknown keys.** A key the editor does not show, on the page or on a
      widget, is saved unchanged. A widget of a type with no panel, such as one
      M12 adds, shows its type and points to the JSON view.
  - **Process step editor.** The Form tab opens the process with its name,
    entity and optional start condition, and its start step. The JSON view
    stays as a second view of the same draft.
    - **Step list** *(built)*. The steps show as cards in file order. Each card
      shows the step's name and type, with move up, move down and remove. Add
      step appends a step of the chosen type, with an empty name.
    - **Type change** *(built)*. Changing a step's type removes the keys the
      new type forbids and adds the keys it requires, empty. The other keys,
      including unknown keys, stay.
    - **Decision and end panels** *(built)*. A decision shows its branches,
      each with a `when` expression and a target, and its `otherwise`. A branch
      can be added and removed. An end step shows only its name and type.
    - **Transitions** *(built)*. The start step, `next`, a branch's `next`,
      `otherwise` and an outcome's `next` pick their target from the step names
      of the unsaved draft. A target that is not a step stays visible and is
      tagged unknown. Removing a step does not rewrite the transitions that
      name it, so the compiler reports them.
    - **Task panel** *(built)*. A task shows its label and its form as pickers
      over the application's text keys and forms. The assignee has a User and
      Role switch. User shows the expression editor and Role shows a role
      picker. A switch removes the other key, so the step never holds both.
      `dueIn` is a text box, and clearing it removes the key. The outcomes are
      rows of name, label and target step, with add and remove. A key the
      panel does not show stays as it is. A diagnostic path inside a task's
      label, assignee, form, `dueIn` or outcomes opens the Form tab at that
      field.
    - **Operation panel** *(built)*. An `operation` step shows an operation
      choice, `updateRecord`, `callConnector`, `raiseEvent` or **Package
      operation**, above its transition. Changing the operation removes only
      the keys the new one forbids. It adds no empty key, so the compiler
      reports what is still missing. Every other key, including unknown keys,
      stays.
      - `updateRecord` shows `set` as rows of a field of the process's entity
        and an expression. `callConnector` shows the connector and its
        operation as pickers, `input` as rows of a typed input name and an
        expression, `output` as rows of a typed connector output name and an
        entity field, and `retry`. `raiseEvent` shows the event as a picker
        and `fields` as rows of a payload field of that event and an
        expression.
      - A package operation is typed in a text box as
        `<package>/<operation>`, because no route lists the installed
        operations. It shows `input`, `output` and `retry` as `callConnector`
        does. The names route lists no connector input or output names, so
        those are typed too.
      - `retry` is attempts and backoff. Clearing both removes `retry`.
      - While the application's names are not loaded, every picker is a name
        box. A key the step's operation forbids is not shown as rows. It says
        to edit it in the JSON view, and a diagnostic path inside it opens the
        JSON view.
  - **Connector editor** *(built)*. The Form tab shows the base URL binding,
    the auth type with its secret binding, `timeout`, `idempotency`, and the
    operations as a list. The JSON view stays as a second view of the same
    draft.
    - **Bindings.** A binding is a name box, labelled as a URL or a secret
      binding. It is not a pick from a list, because no route lists a tenant's
      binding names. No value of a binding is shown or stored: the editor
      reads and writes only the resource and schema routes, and the values stay
      in tenant settings.
    - **Auth.** Choosing a type removes the keys the new type forbids, such as
      `token` for `header`, and adds the keys it requires, with an empty name.
      Clearing the type removes `auth`.
    - **Operations.** A card per operation shows its name, method, path and
      `lookup`, which is picked from the connector's other operations. Its
      input and output fields are rows of name, type and required. Operations
      and rows can be added and removed. A `GET` with input rows keeps them and
      shows a hint, and the compiler reports the error.
    - **Unknown keys.** A key the editor does not show, in the file, in an
      operation or in `auth`, is saved unchanged.
  - **Scenario editor** *(built)*. The Form tab shows the scenario's name and
    `now`, then its users with their roles, its stubs, and its steps as a list
    with a panel per step type. The JSON view stays as a second view of the
    same draft.
    - **Steps.** Each step has a type. Changing the type keeps the properties
      the new type takes and drops the others. Keys the editor does not show
      stay in the file. A step can be removed or moved up or down.
    - **Pickers.** Entities, processes, roles and connectors are picked by
      name. A `record` is picked from the aliases that earlier `createRecord`
      steps save. A `step` and its `outcome` come from the processes that the
      record started.
    - **Values.** The fields of the step's entity show as typed cells. A value
      the entity does not name stays as a text row. A step whose entity is not
      in the folder edits its `values` as a JSON object, and the text is saved
      only when it is one.
  - **Seed editor** *(built)*. The Form tab shows the seed's name, entity and
    `sync`, then its records as a grid, with a row per record and a column per
    field of the seed's `entity`. The JSON view stays as a second view of the
    same draft.
    - **Columns.** The input fits the field type: a text box for `text`,
      `date`, `date-time` and `reference`, a text box for `integer` and
      `decimal` that stores a number, a true/false choice for `boolean` and a
      choice of `values` for `enum`. A `reference` is a text box that checks
      the UUID form, and the record picker is separate work. Each input follows
      the [record API's value rules](record-api.md#request-bodies-and-values).
    - **No column.** A computed field, a `sequence` field, a `creator` field
      and a `child-collection` field get no column, because seeding rejects a
      value for the first three, and child collections stay as the file has
      them. A value the grid does not show, including an unknown key, is saved
      unchanged.
    - **Rows.** **Add record** adds a row with a new record id, and **Remove**
      deletes the record from the draft. Clearing a cell removes its key from
      the record's `values`. The grid never writes `null`.
    - **Save.** A cell whose value would fail seeding, because it has the wrong
      type, is out of bounds or is missing a required value, shows an error
      and turns Save off, also when the value comes from the JSON view.
    - **Fallback.** The entity is found by name among the folder's `entity`
      resources, ignoring letter case. When it is not there, for example a
      module entity or a typo, the seed uses the generic form and the JSON view.
  - **Text editor** *(built)*. A `text` resource opens as one table of keys
    with a column per locale, and a search box.
    - **Columns.** One row per key, sorted by key, and one column per locale in
      the order of the folder's `text` resources. Only the open resource's
      column is editable. The other locales show beside it as read-only text,
      read from their files. To edit another locale, open its `text` resource.
    - **Missing keys.** A key that the open locale lacks shows an empty cell
      marked missing. Typing in it adds the key to this locale. A locale that
      the file lacks the key in shows a missing mark in place of the text.
    - **Search.** The box keeps the rows whose key, or text in any locale,
      contains the search, ignoring letter case.
    - **Cells.** A cell edit goes into the draft, and **Save** sends one JSON
      Patch for the open resource only. Clearing a cell saves an empty string
      and keeps the key, because every locale holds the same keys
      (`AXC0027`).
    - **Add key.** The row below the table takes a key and a text for each
      locale. It writes the key to every locale through the
      [texts route](authoring.md#text-writes) at once. The key cannot be empty,
      cannot contain `/`, and cannot already be in the table. The editor takes
      the open file's new version from the route's answer and adds the key to
      its loaded copy and draft, so unsaved cell edits stay and the next Save
      does not meet a stale version. The diagnostics and the names reload.
    - **JSON view.** It stays as the second view of the same draft.
    - **Removing a key** is not built. No route removes a key from every
      locale yet.
  - **Endpoint `values`** *(built)*. The endpoint keeps the generic form for
    its entity, process, input and output. Its `values` show as rows of a field
    and an expression in the expression editor. The field is picked from the
    fields of the endpoint's entity, found by name among the application's
    names, and is typed while those are unknown. **Add mapping** lists the
    fields that have no row. Renaming a row's field keeps its place in the
    file, and a value that is not an object is left to the JSON view.
  - **Page editor and M12.** M12 adds layouts and widgets while this editor is
    built, see
    [D46](../decisions.md#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed).
    The page editor changes again then.
- **Process graph** *(planned for M11)*. A process shows a drawn graph of its
  steps and transitions above its editor, see
  [D53](../decisions.md#d53-typed-editors-and-the-process-graph-in-studio--agreed).
  It is read-only. The author edits steps in the list.
  - Dagre, `@dagrejs/dagre`, lays out the graph, and React Flow,
    `@xyflow/react`, draws it. Their sizes are about 30 KB and 50 KB min+gzip.
    Neither is in the SPA yet. M14's process designer builds on both.
  - The graph and the step editor stay in step. Both read the same unsaved
    draft, so an edit in the list redraws the graph at once.
  - Selecting a node selects that step in the list. Selecting an edge opens its
    transition: the outcome, the branch, `otherwise` or `next`. Selecting a step
    in the list highlights its node.
  - A transition to a step that does not exist draws no edge. The graph never
    invents a node.
  - A step that a diagnostic in the diagnostics panel names, by a path such as
    `/steps/{i}`, is marked. The marks come from the panel's last loaded list,
    so they change on a save or a request, not on every edit.
  - Nodes are reached with Tab and opened with Enter. Each node has a text label
    with the step's name and type.
- **Designers.** Visual page, form and process designers are not built yet.
  They are planned for M13 and M14, see
  [D46](../decisions.md#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed).
  The process graph is read-only, and not a designer.
- **Texts.** Its texts, including those of the lists above, are the platform
  texts under `shell.studio.*`, in `en` and `vi`, like the other shell texts.
