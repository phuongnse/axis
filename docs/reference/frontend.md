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
- **The UI library is a detail of the SPA.** The SPA builds on Ant Design for
  its base components
  ([D58](../decisions/d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md)).
  Configuration, page metadata, configurable theme tokens and the custom widget
  API never name an Ant Design component, prop or token. A custom widget builds
  on Axis widgets and Axis tokens and never imports the UI library directly.
  Specialised libraries such as React Flow, CodeMirror and Apache ECharts
  ([D64](../decisions/d64-detail-chart-and-kpi-widgets.md)) follow the same rule.
  No layer renders UI from a schema on top of Ant Design.
- **Widgets expose their state to tests and assistive technology.** They are
  found by role, accessible name and ARIA state, never by a class of the UI
  library. Where the library leaves a state out, such as loading (`aria-busy`)
  or the current page (`aria-current`, as on the active navigation entry), the
  widget adds it.
- **Design tokens and previews.** The token file `web/src/platform/designTokens.ts`
  holds every colour, type, spacing and radius value of the theme, in Axis
  names. The colours are primary, success, warning, danger, info, canvas,
  surface, text, textMuted, border, borderSubtle and divider, each with a light
  and a dark value. Type has the body and code fonts and the font size, spacing
  runs from xs to xl, and radius has the base corner radius.
  `web/src/platform/antTheme.ts` is the only file that maps them to Ant Design
  tokens. A component that needs a theme value reads Axis tokens through
  `useDesignTokens`, never through Ant Design's `theme.useToken`. A Vitest test
  scans `web/src` and fails on a hard-coded colour, font family or radius
  outside those two files. The shared chart component in
  `web/src/platform/Chart.tsx` maps the Axis tokens to chart colours, font and
  grid, and loads the library in its own chunk. Widget previews draw the
  `table`, `form`, `taskInbox` and `processInspector` widgets, the `columns`,
  `tabs` and `section` containers and the input of
  each field type, from fixture metadata, in light and dark mode. They are at
  `/design-system.html` on the development server only, and
  `scripts/design-system.sh` saves each as static HTML, see
  [Development](../development.md#widget-previews). Previews of the tokens and
  of the other M12 widgets are still *(planned for M12)*. The local agent session
  publishes the saved files as the Design System artifact. See
  [D57](../decisions/d57-a-design-system-published-from-the-repository.md)
  and [Design changes](../delivery.md#design-changes).
- **How pages and widgets grow.** A page draws its whole tree of widgets.
  Containers show their widgets in file order and can nest. A form binds an entity
  or a `form` resource, which lays the entity's fields out in sections, and a
  table binds an entity or a data source. The container widgets `columns`,
  `tabs` and `section` hold other widgets, see
  [D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md).
  Data sources are agreed in
  [D18](../decisions/d18-data-sources.md) and described in
  [data-sources.md](data-sources.md). Shared `form` resources, the task inbox
  and the `startProcess` action are agreed in
  [D20](../decisions/d20-human-tasks-task-inbox-and-forms.md). The
  `form` resource, the task inbox, the task page and the `startProcess` action
  are built. The `navigate` buttons on table rows, the table toolbar and saved
  forms are built. A table opens and creates records through them: the table's
  `formPage` link is removed; see [D63](../decisions/d63-navigate-actions-and-page-parameters.md).
- **Container widgets.** `columns` stack below 768 px, `tabs` keep the
  selected tab in `?tab=`, and a `section` can start collapsed. See
  [container widgets](container-widgets.md).
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
    "parameters": [],
    "widgets": [
      {
        "type": "table",
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
        "rowActions": [
          { "type": "navigate", "page": "ItemForm", "labelKey": "items.open", "parameters": {}, "record": "id", "opensForm": true, "visibleWhen": null }
        ],
        "toolbarActions": [
          { "type": "navigate", "page": "ItemForm", "labelKey": "items.new", "parameters": {}, "record": null, "opensForm": true, "visibleWhen": null }
        ],
        "parameters": {},
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
  - `type` of a widget is `table`, `form`, `taskInbox`,
    `processInspector`, `kpi`, `chart` or `detail`.
    On a `table` or `form`, exactly one of `entity` and `dataSource` is set, and
    the other is `null`. A `taskInbox` has both `null`, and so does a
    `processInspector`. The data source
    binding of the table widget below describes `dataSource`.
  - A `kpi` sets `dataSource`, and has `"entity": null`, `"form": null`,
    `"actions": []` and `"access": null`.
  - A `detail` sets `entity`, `form` when it names a form, `actions` and
    `access`, and has `"dataSource": null`.
  - A `chart` sets `dataSource`, and has
    `"entity": null`, `"form": null`, `"actions": []` and `"access": null`.
  - Every widget also has `form`, never left out. It is the widget's form
    resource on a `form` widget that names one, and `null` otherwise. It is
    described under **Form metadata** of the form widget.
  - Every widget also has `tasksPath`, never left out. It is described under
    the task inbox widget, and is `null` on every other widget.
  - Every widget also has `process` and `instancesPath`,
    never left out. They are described under the process inspector widget, and
    are `null` on every other widget.
  - Every widget also has `detail`, `kpi` and `chart`, never left out. Each is
    described under its widget, and is `null` on every other widget. `detail`
    is written after `access`, `kpi` after `detail` and `chart` after `kpi`. The
    examples on this page leave out all three, which are `null` on all of them.
  - Every widget also has `actions`, never left out. It
    is described under the form widget, and is `[]` on a widget it does not
    apply to. A table's `startProcess` row actions are left out without
    `start`, and its `delete` row action without `delete`.
  - Every widget also has `rowActions` and `toolbarActions`, never left out.
    A table lists its `navigate`, `startProcess` and `delete` row actions and
    its `navigate` toolbar actions in them. A `delete` is
    `{ "type": "delete", "labelKey": "…", "recordsPath": "…", "visibleWhen": null }`.
    `recordsPath` is the record API path of the table's entity, or of its data
    source's root entity, so the SPA never builds it. They are `[]` elsewhere. Every action
    also has `visibleWhen`, never left out: the expression text of a row
    action's condition, or `null` when it has none. It is always `null` outside
    `rowActions`.
  - Every widget also has `parameters`, never left out: on a table over a data
    source, the name of the page parameter that gives each data source
    parameter its value, as `{ "<data source parameter>": "<page parameter>" }`,
    in the declared names. It is `{}` elsewhere and on a table that binds none.
  - A page also has `parameters`, never left out: the typed values it reads
    from its URL, in file order, or `[]`. Each is
    `{ "name", "type", "required", "values", "target" }`. `type` is the type
    name as page files write it. `values` is the list for an `enum` and `null`
    otherwise. `target` is `{ "entity", "displayField", "recordsPath" }` for a
    `reference`, as a data source parameter has it, and `null` otherwise. The
    SPA reads them to check the values in the address.
  - Every widget also has `access`, never left out. It is described under
    **Policies** below, and is `null` on a `taskInbox`, on a
    `processInspector`, on a `kpi` and on a `chart`.
  - A page may hold a tree of widgets. A container widget
    carries only `type` and its own keys. A leaf widget keeps every key above.
    A tab carries `labelKey`, and a `section` carries `titleKey` and
    `collapsed`. A page with two columns, one holding a table and the other a
    section with a task inbox:

    ```json
    {
      "name": "Overview",
      "titleKey": "pages.overview.title",
      "widgets": [
        {
          "type": "columns",
          "columns": [
            { "width": 8, "widgets": [{ "type": "table", "entity": { "name": "PurchaseRequest", "fields": [{ "name": "title", "type": "text" }] }, "dataSource": null, "form": null, "tasksPath": null, "process": null, "instancesPath": null, "actions": [], "rowActions": [], "toolbarActions": [], "parameters": {}, "access": { "actions": ["read"], "createFields": [], "updateFields": [] } }] },
            {
              "width": 4,
              "widgets": [
                {
                  "type": "section",
                  "titleKey": "pages.overview.tasks",
                  "collapsed": false,
                  "widgets": [{ "type": "taskInbox", "entity": null, "dataSource": null, "form": null, "tasksPath": "/api/apps/PurchaseRequests/tasks", "process": null, "instancesPath": null, "actions": [], "rowActions": [], "toolbarActions": [], "parameters": {}, "access": null }]
                }
              ]
            }
          ]
        }
      ]
    }
    ```
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
  - `computed` is `true` for a [computed field](resource-files.md#entity-logic).
    The server sets its value on every write, so the form shows it read-only
    and never sends it.
  - `sequence` is `true` for a text field that names a
    [sequence](resource-files.md#sequences). The server numbers it on create,
    so the form shows it read-only and never sends it.
  - `creator` is `true` for a text field that declares
    [`creator`](roles-and-policies.md#record-creator). The server sets it to the
    creating user's id on create, so the form shows it read-only and never
    sends it.
  - `target` names the referenced entity, its display field and the path of
    its record API, so the SPA never builds a record URL itself. It is only
    for a reference. A child collection's `target` is `null`, because a child
    entity has no record API of its own.
  - `fields` lists a child collection's child entity fields in declaration
    order, each in the same shape as an entity field. The form builds the
    columns and inputs of the collection's table from them.
  - Names in responses (page, entity, and the application in
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
    [Authorization](security.md#authorization) describes.
    This only shapes the UI. The server checks every request.
    - **Always shaped.** The endpoints need a session, so the metadata is
      always shaped by the user's grants.
    - **Readable pages.** A page is readable when at least one of its widgets
      is, see **Pages with a tree** below. A widget
      over an entity, or over a form of an entity, needs a `read` grant on
      that entity. A widget over a data source needs what the
      [data source endpoint](data-sources.md#authorization) needs. A
      `taskInbox` is always readable. A
      `processInspector` is readable when the user holds an `inspect` grant on
      its process. A `kpi` whose data source the user cannot run is left out
      of the page, as a table is. A `detail` needs a `read` grant on its
      entity. A `chart` whose data source the user cannot
      run is left out too.
    - **Pages with a tree.** An unreadable widget is
      left out. A column or tab whose widgets are all left out is left out,
      and the other columns keep their declared widths. A container left with
      no widget is left out. A page is readable when at least one of its
      widgets is.
    - **Unreadable pages.** The page endpoint answers `403` for a page the
      user cannot read. Site navigation leaves out entries whose page the
      user cannot read.
    - **Fields.** `entity.fields` and the form metadata leave out the
      fields the user may not read. A form section with no field left is
      left out. A child collection field covers its rows as a whole, so it
      is left out, or kept with all its `fields`. `id` and `version` are not
      affected. The task API's form does the same, see
      [policies on tasks](task-api.md). A `detail` leaves out unreadable fields
      as its form metadata does, and an empty section too. A `kpi` measure over
      a field the user may not read follows the data source's field check,
      which leaves the widget out. A `chart` does the
      same.
    - **Actions.** A `startProcess` action is left out of `actions` when the
      user has no `start` grant on its process. The same holds for a `table`
      row `startProcess` action, and its `delete` row action is left out when
      the user has no `delete` grant on the table's entity, or on the data
      source's root entity, which is what `access.actions` lacks. A `navigate` action whose target
      page the user cannot read is left out too. A
      `kpi`'s `navigate` to such a page is `null`.
    - **Access.** Every `table`, `form` and `detail` widget gets an `access`
      object.
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
      - A table shows a toolbar action to a form without `record` only with `create`. A form shows a
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
  | `/{site}/{page}` | the page inside the site shell, with the page title. A table page also takes `?page=&pageSize=&sort=`. A table bound to a data source also takes its parameter values. A page with `tabs` widgets also takes `?tab=`, `?tab2=` and so on |
  | `/{site}/{page}?ticket=<id>` | a page with parameters: the values sit in the query under the parameter names |
  | `/{site}/{page}/new` | the form of a form page, to create a record |
  | `/{site}/{page}/{id}` | the form of a form page, to edit the record with that id |
  | `/{site}/{inboxpage}/{taskId}` | the task page of the task with that id, under the page of a task inbox |
  | `/{site}/{inspectorpage}/{instanceId}` | the instance page of the process instance with that id, under the page of a process inspector |

  - A page holds at most one `table` and at most one
    `form`, `taskInbox` or `processInspector`, so every part of the address
    has one owner. A `kpi` owns no part of the address and reads only page
    parameters, so it does not count. A `detail` owns none either, and so does a
    `chart`. The compiler checks this. `?page=&pageSize=&sort=` and the data source parameter
    values belong to the page's one `table`. `/new` and `/{id}` belong to its
    one `form`. `/{taskId}` and `/{instanceId}` belong to its one `taskInbox`
    or `processInspector`.
  - `{site}` is the site path. `{page}` is the page name in lower case, as
    navigation links write it. The server matches both ignoring letter case.
  - A required page parameter that is missing from the URL, or has the wrong
    form, shows the not-found page inside the site shell. The SPA checks this
    on every page that declares parameters, before any widget renders, so no
    rows are requested. A value is missing when it is absent or empty, and has
    the wrong form when it is repeated or not in the plain text form of its type
    in [data-sources.md](data-sources.md#query-parameters). An optional
    parameter with a missing or malformed value counts as not given.
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
    - **Bound parameters.** A data source parameter that the widget's
      `parameters` binds to a page parameter has no filter input and is not
      written to the URL by the table. The table sends the page parameter's
      value from the URL under the data source parameter's name, and leaves
      the page parameter in the URL as it is. A page parameter that is not
      given is not sent.
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
      the id of its root record, so a row action with `record: id` opens its
      form as for an entity table. A group row of a grouped data source has
      `id: null` and no row actions.
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
  - **Links.** The table has no open link and no create button of its own. A
    sample opens a record with a `rowActions` navigate with `record: id`, and
    creates one with a `toolbarActions` navigate without `record`, still gated
    by `access` `create`. Both pass the table's address, with its paging and
    sorting, to the form in history state.
  - **Row and toolbar actions.** See
    [D59](../decisions/d59-table-row-and-toolbar-actions.md). Page metadata
    lists the `navigate` actions in `rowActions` and `toolbarActions`,
    without those to pages the user cannot read, each with its `parameters`,
    `record` and `opensForm`. It lists a `startProcess` in `rowActions` only
    with a `start` grant on its process. The `navigate` and `startProcess`
    buttons, the `visibleWhen` condition, the delete button and the phone menu
    are built.
    - **Where.** Toolbar actions sit above the table. Row actions
      are buttons in the row. Below 768 px, a row's actions fold into one
      menu button labelled by the platform text `shell.table.rowActions`. A
      `navigate` action may sit in either list. `startProcess` and `delete`
      are row actions only. A row of a data source acts on its root record,
      and a group row has no actions.
    - **Navigate.** A button that opens another page and sets its
      parameters. A My tickets row opens a TicketDetail page and sets its
      `ticket` parameter:

      ```json
      {
        "type": "table",
        "entity": "Ticket",
        "rowActions": [
          {
            "type": "navigate",
            "page": "TicketDetail",
            "label": { "textKey": "tickets.open" },
            "parameters": { "ticket": "id" }
          }
        ]
      }
      ```

      The target page declares
      `"parameters": [{ "name": "ticket", "type": "reference", "target": "Ticket", "required": true }]`.
      The click goes to `/{site}/ticketdetail?ticket=<id>`. See [D63](../decisions/d63-navigate-actions-and-page-parameters.md).
      With `record`, the button opens `/{site}/{page}/{id}`. Without it, an
      action to a form page, one with `opensForm`, opens `/{site}/{page}/new`.
      A toolbar action to a form page without `record` shows only when the
      table's `access.actions` holds `create`. A
      click passes the table's address, with its paging and sorting, to the
      page in history state, so a form returns to it after save or cancel.
    - **Values.** The SPA has no full expression engine. It works out a bare
      name (`id`, a column or a field, matched ignoring case) and a literal
      (text in single quotes, a number, `true`, `false` or `null`). A button
      whose expression uses anything else is hidden. The one exception is
      `visibleWhen`, which works out a larger part of the language, see below.
      A toolbar has no row, so only its literals resolve. A parameter whose
      value is `null` is left out of the query, and a button whose `record` is
      `null` is hidden. The target page does not read its parameters from the
      URL or bind them yet _(planned for M12)_.
    - **`visibleWhen`.** A row action may have a boolean expression over the
      row. The compiler checks it against the row's names, as it checks a
      `navigate` parameter. The SPA works out only part of the language:
      names, literals, `==`, `!=`, `is null`, `is not null`, `in (…)` with
      literal items, `and`, `or`, `not` and parentheses. Keywords ignore
      letter case. The button shows only when the result is `true`. It hides
      when the result is `false` or `null`. It also hides when the condition
      uses syntax outside that part, such as `<`, `+`, a path or a function
      call, or reads a name the row does not hold. A field the user may not
      read is not in the row, so it counts as a missing name. A missing name
      hides the button even where `and` or `or` could stop early. Comparison
      is null safe: `null == null` is `true` and `null in (…)` is `false`. In
      `and`, `or` and `not`, `null` means unknown, so `not null` and
      `true and null` are `null` and hide the button, while `false and null`
      is `false`. A number literal compares as a number with a value, so `5`
      equals a stored `5.00`. Two names compare by their text. The compiler
      does not refuse a well-typed condition the SPA cannot work out, such as
      `amount > 100`, so the author must stay inside this part. It only shapes
      the UI. The server decides, as in
      [D12](../decisions/d12-authorization-is-server-side-and-default-deny.md).
    - **Delete.** A row has a delete button only when the widget's
      `access.actions` holds `delete`. Without it there is no button. A click
      opens a confirmation dialog. On confirm the SPA sends
      `DELETE {recordsPath}/{id}` with `X-Axis-Csrf: 1`. `recordsPath` comes
      from the action's metadata. A delete action may have `visibleWhen`, such
      as to show Delete only on drafts.
    - **After a delete.** On `204` it shows a success message and loads the
      current page of rows again. A page past the last page becomes the last
      page, as for an invalid `page`. A `403` shows a permission message,
      such as when the record filter denies the row. A `404` shows a message
      that the record no longer exists. A `409` shows one 'in use' text, for
      a record that another record references and for a record held by a
      running process. In each case the row stays. Any other answer shows
      the generic platform error. The success and the failure messages show
      above the table.
    - **Start process.** A row `startProcess` follows the rules of the form
      widget's **Start process action**, with the row as the subject and
      the same `Idempotency-Key` and `409` text. On `201` it loads the
      current page of rows again, with no success message. A failure shows
      above the table.
    - **Later.** Actions on selected rows come after M12.
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
    checks them. The SPA does not use them yet *(planned for M12)*. See
    [Form conditions and steps](resource-files.md#form-conditions-and-steps).
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
  - **Conditions and steps** *(planned for M12)*. The widget asks the server
    which fields to show, require and lock, and which messages to show. It
    has no evaluator of its own
    ([D62](../decisions/d62-conditional-form-states-validation-as-you-type-and-wizard-steps.md)).
    - **Calls.** It calls the [form state endpoint](record-api.md#form-state)
      with the draft values and the touched fields. The call goes out at once
      after a choice: a select, a checkbox, a date or a picked record. It goes
      out 300 ms after typing stops in a text or number input.
    - **Pending answers.** While an answer is pending, the widget keeps the
      last answered states. It drops an answer that a newer call has
      overtaken.
    - **States.** Per the answer, a field is hidden, marked required or made
      read-only. A hidden field is not sent. The messages in `errors` show
      under the touched fields.
    - **Touched.** A field becomes touched when it loses focus or changes.
    - **Save.** The widget always sends `?form=` with the form's name on
      create and update, so the server enforces `requiredWhen`.
    - **Steps.** A form with `steps` shows one step at a time, with Back and
      Next. Next sends every visible field of the step as touched and stays
      on the step when any key of `errors` names one of them. Back never
      checks. A step whose sections are all hidden is skipped.
    - **Last step.** Save and the `startProcess` actions are on the last
      step. After a failed save, the form opens the first step that has an
      error. The current step stays out of the URL, because a draft does not
      survive a reload.
  - **Form metadata**. Page metadata gives the widget's
    `entity` with all its fields, so the SPA knows every field's type. It
    adds:
    - `form`: `{ "name", "sections": [ { "name", "titleKey", "visibleWhen", "fields": [ { "name", "readOnly", "visibleWhen" } ] } ], "steps": [ { "titleKey", "sections" } ] }`,
      or `null` when the widget names an `entity`. `readOnly` already
      includes computed, sequence and creator fields. Field names are as the entity
      declares them.
    - `actions`: `[ { "type", "process", "labelKey", "instancesPath", "visibleWhen" } ]`,
      or `[]`. `instancesPath` is the start endpoint, such as
      `/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances`,
      so the SPA never builds it itself. A `navigate` action is
      `{ "type": "navigate", "page", "labelKey", "parameters", "record", "opensForm", "visibleWhen" }`, with
      the target page's declared name. `parameters` maps each parameter name,
      as the target page declares it, to its expression, or is `{}`. `record`
      is the expression of the record id, or `null`. The expressions go to the
      browser as written. `opensForm` is `true` when the target page holds a
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
- **Detail widget**. A page whose widget is a `detail` shows one record
  read-only. See [detail-chart-kpi-widgets.md](detail-chart-kpi-widgets.md).
- **Chart widget**. A page whose widget is a `chart` draws a data source.
  See [detail-chart-kpi-widgets.md](detail-chart-kpi-widgets.md).
- **KPI widget**. A page whose widget is a `kpi` shows one value. See
  [detail-chart-kpi-widgets.md](detail-chart-kpi-widgets.md).
- **Task inbox widget**. A page whose widget is a `taskInbox` lists the signed-in user's open
  tasks. See [task-inbox-widget.md](task-inbox-widget.md).
- **Task page**. The address `/{site}/{inboxpage}/{taskId}` shows one task, under the page of its
  task inbox. See [task-inbox-widget.md](task-inbox-widget.md).
- **Process inspector widget**. A page whose widget is a `processInspector` lists the instances of
  one process and opens one. See [process-inspector-widget.md](process-inspector-widget.md).
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
  [sign-in and the session](security.md#sign-in-and-the-session).
  The header also reads `studio` from it, and shows the [Studio](studio.md)
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

- **Phone width** *(planned for M12)*. Below a fixed 768 px breakpoint, Ant
  Design's `md`, columns stack in file order, which is built. Tab headers scroll
  and a table scrolls sideways inside its widget. A table's row actions fold
  into one menu per row, which is built. The site shell's navigation collapses
  into a menu. There is no per-widget setting until a sample needs one, see
  [D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md).

## Studio

The Studio page is in [studio.md](studio.md).
