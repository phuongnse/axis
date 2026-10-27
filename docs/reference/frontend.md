# Frontend

Detailed reference for the frontend: the SPA, its rendering from metadata and
its widgets. The conventions of [architecture.md](../architecture.md) apply:
sections marked *(planned for Mx)* are not built yet, and Dn refers to
[decisions.md](../decisions.md).

- **One SPA for all applications.** The SPA renders from metadata served by
  the server: site navigation, pages and their widgets, data source schemas
  and text resources. Publishing an application never rebuilds the frontend.
- **Shared look and feel.** A page is a route in a site, and its widgets are
  its content. Each widget type, such as `table` and `form`, has one shared
  component, and layout comes from container widgets rather than per-page
  templates. Those components and a single token-based theme with light and
  dark modes live in `web/src/platform/`. Feature code assembles them and
  adds no styling of its own.
- **The UI library is a detail of the SPA.** The SPA builds on Ant Design for
  its base components ([D58](../decisions/d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md)).
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
- **Design tokens and previews.** The token file
  `web/src/platform/designTokens.ts` holds every colour, type, spacing and
  radius value of the theme, in Axis names. The colours are primary, success,
  warning, danger, info, primaryText, successText, warningText, dangerText,
  infoText, canvas, surface, text, textMuted, border, borderSubtle, divider and
  focusRing, each with a light and a dark value. The first five are seeds: they
  colour fills, icons and tints. The `*Text` tokens colour text in that state
  colour: state text, links, the selected tab, the active navigation entry and
  form validation messages. `focusRing` colours the solid 2px keyboard focus of
  every control. Every text colour reaches 4.5:1 and every border and focus
  ring reaches 3:1 on surface and canvas, in both modes. The shadow group
  holds `overlay`, the only shadow: dialogs, menus, pickers and popovers cast
  it, and widgets, cards, sections and buttons cast none. Type has the body and
  code fonts and the font size, spacing runs from xs to xl, and radius has the
  base radius. The body font is IBM Plex Sans, a variable font for weights 100
  to 700, and the code font is IBM Plex Mono at weight 400. The SPA ships the
  Google Fonts files from the Fontsource npm packages unchanged. Only the
  Latin, Latin Extended and Vietnamese subsets ship, each with a
  `unicode-range` in `web/src/fonts.css`, so a browser downloads only the
  subsets a page uses. The fonts are under the SIL Open Font License 1.1. Its
  texts ship at `/fonts/OFL-IBM-Plex-Sans.txt` and
  `/fonts/OFL-IBM-Plex-Mono.txt`, from `web/public/fonts`. The font files are
  part of the build output and load from the SPA's own origin (`font-src
  'self'`), so an install with no internet shows them too. The design-system
  bundle carries no fonts. `web/src/platform/antTheme.ts` is the only file that maps them to Ant
  Design tokens. A component that needs a theme value reads Axis tokens through
  `useDesignTokens`, never through Ant Design's `theme.useToken`. A Vitest test
  fails on a hard-coded colour, font family or radius in `web/src` outside those
  two files. The shared chart component in `web/src/platform/Chart.tsx` maps the
  Axis tokens to chart colours, font and grid, and loads the library in its own
  chunk. Widget previews draw every widget, container and field input from
  fixture metadata, in light and dark mode. `scripts/design-system.sh` builds
  them into one classic script that sets `window.Axis` with a mount per preview.
  A mounted widget follows `data-theme` on the page's root element. For the
  current mode it also reads the custom properties `--primary` to `--focusRing`
  for the colours (named as above, so `--primaryText` to `--infoText` and
  `--focusRing` too), `--shadowOverlay`, `--spacing-xs` to `--spacing-xl`,
  `--radius-base`, `--font-sans` and `--font-code` from that element, and
  redraws when one changes. A property the page leaves out, or sets to a value
  the theme cannot use, keeps its token file value. A shadow value is usable
  when it is not empty and has no `;`, `{` or `}`. Only the design-system
  bundle reads them. See [Development](../development.md#widget-previews),
  [D67](../decisions/d67-live-widget-previews-in-the-design-system.md),
  [D57](../decisions/d57-a-design-system-published-from-the-repository.md) and
  [Design changes](../delivery.md#design-changes).
- **How pages and widgets grow.** A page draws its whole tree of widgets.
  Containers show their widgets in file order and can nest. A form binds an entity
  or a `form` resource, which lays the entity's fields out in sections, and a
  table binds an entity or a data source. The container widgets `columns`,
  `tabs` and `section` hold other widgets, see
  [D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md).
  Navigate actions with page parameters, and the detail, chart and KPI widgets,
  are built, see [D63](../decisions/d63-navigate-actions-and-page-parameters.md)
  and [D64](../decisions/d64-detail-chart-and-kpi-widgets.md).
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
  | `GET /api/sites/{path}` | `{ "path", "titleKey", "locales": { "default", "fallback", "available" }, "navigation": [ { "page", "labelKey" } ], "notificationsPath" }` |
  | `GET /api/sites/{path}/texts/{locale}` | `{ "locale", "texts" }`, the active release's texts for one locale, in the shape of the platform texts. Optional `?release={releaseId}` serves any stored release of the site's application. An unknown id, a non-UUID or another application's release answers `404`. |
  | `GET /api/sites/{path}/pages/{page}` | the page metadata shown below |

  At typical size, a page with a three-level widget tree loads in under 2 s: its
  metadata, then the rows of its table, chart and KPIs read in parallel over 100,000
  records. `PageLoadPerformanceTests` fails when the best of three loads takes 2 s or more.

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
              "creator": false, "maxLength": 100, "minLength": null,
              "precision": null, "scale": null, "currency": null, "currencyField": null, "min": null, "max": null, "values": null, "valueLabelKeys": null, "target": null, "fields": null
            },
            {
              "name": "department", "type": "reference", "labelKey": "item.department",
              "required": false, "unique": false, "computed": false, "sequence": false,
              "creator": false, "maxLength": null, "minLength": null, "precision": null,
              "scale": null, "currency": null, "currencyField": null, "min": null, "max": null, "values": null, "valueLabelKeys": null, "target": {
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
        "deadLettersPath": null,
        "actions": [],
        "rowActions": [
          { "type": "navigate", "page": "ItemForm", "labelKey": "items.open", "parameters": {}, "record": "id", "opensForm": true, "paths": {}, "visibleWhen": null }
        ],
        "toolbarActions": [
          { "type": "navigate", "page": "ItemForm", "labelKey": "items.new", "parameters": {}, "record": null, "opensForm": true, "paths": {}, "visibleWhen": null }
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
    described under **Form metadata** of the [form widget](form-widget.md).
  - Every widget also has `tasksPath`, never left out. It is described under
    the task inbox widget, and is `null` on every other widget.
  - Every widget also has `process`, `instancesPath` and `deadLettersPath`,
    never left out. They are described under the process inspector widget, and
    are `null` on every other widget.
  - Every widget also has `detail`, `kpi` and `chart`, never left out. Each is
    described under its widget, and is `null` on every other widget. `detail`
    is written after `access`, `kpi` after `detail` and `chart` after `kpi`. The
    examples on this page leave out all three, which are `null` on all of them.
  - Every widget also has `actions`, never left out. It
    is described under the [form widget](form-widget.md), and is `[]` on a widget it does not
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
  - Every widget also has `parameters`, never left out: on a table, a chart or a
    KPI over a data source, the name of the page parameter that gives each data
    source parameter its value, as `{ "<data source parameter>": "<page parameter>" }`,
    in the declared names. It is `{}` elsewhere and on a widget that binds none.
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
            { "width": 8, "widgets": [{ "type": "table", "entity": { "name": "PurchaseRequest", "fields": [{ "name": "title", "type": "text" }] }, "dataSource": null, "form": null, "tasksPath": null, "process": null, "instancesPath": null, "deadLettersPath": null, "actions": [], "rowActions": [], "toolbarActions": [], "parameters": {}, "access": { "actions": ["read"], "createFields": [], "updateFields": [] } }] },
            {
              "width": 4,
              "widgets": [
                {
                  "type": "section",
                  "titleKey": "pages.overview.tasks",
                  "collapsed": false,
                  "widgets": [{ "type": "taskInbox", "entity": null, "dataSource": null, "form": null, "tasksPath": "/api/apps/PurchaseRequests/tasks", "process": null, "instancesPath": null, "deadLettersPath": null, "actions": [], "rowActions": [], "toolbarActions": [], "parameters": {}, "access": null }]
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
    declaration order. A `child-collection` field and a `references` field are
    listed too. Tables show no column for either. Forms show a child
    collection's rows and a `references` field's records as a list, and a
    detail shows a `references` field's labels.
  - Each field has its name, its `type` as written in entity files (such as
    `date-time`), its label key, `required`, `unique`, `computed`, `sequence` and `creator`,
    `maxLength` and `minLength` for text, `min` and `max` for integer, decimal, date and
    date-time, `precision` and `scale` for decimal and money, `currency` (the fixed code) or `currencyField` (the field
    holding the code) for money, `values` for enum, `valueLabelKeys` for an enum with `valueLabels`, `maxSize`,
    `accept`, `uploadPath` and `downloadPath` for file, `target` for reference and references, and `fields` for
    child collection. `min` and `max` are strings in the
    entity file's literal form, such as `"0.5"` or `"2026-01-01"`. A property the field's
    type does not have, or a label the file leaves out, is `null`; it is
    never left out. `valueLabelKeys` maps each enum value to the text key of
    its label, and is `null` when the enum has no labels.
  - A file field's `maxSize` (bytes) and `accept` (media types) are the
    entity file's, or `null` when it declares none. `uploadPath` is the path
    of the field's [upload route](file-fields.md#upload), and for a field of a
    child entity it names the collection, such as
    `/api/apps/{app}/entities/{entity}/fields/attachments/file/files`. It is
    `null` for a field of a module's entity, which has no upload route yet, so
    the input shows no upload button. `downloadPath` is `/api/apps/{app}/files`,
    and the SPA appends a file's id to it. The SPA never builds a file URL
    itself.
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
    for a reference and a references field. A child collection's `target` is `null`, because a child
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
      `kpi`'s `navigate` to such a page is `null`. A `navigate` action whose
      parameters or `record` use a path is also left out when the user may
      not read a field along the path, the first field on the widget's
      entity included, or the entity that field belongs to. This is the
      data source field check applied to a path.
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
        A `table` adds `export` after them, when the user may export its
        data source, or its entity when it has no data source
        (`PolicyEvaluator.AllowsExport`). A form and a detail never list it.
        A `table` over an entity adds `import` last, when the user holds
        `import` and `create` on the entity. A form, a detail and a table over
        a data source never list it. Without a user, as in Studio's draft
        metadata, every table lists `export` and every table over an entity
        lists `import`.
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
  | `/` | the platform home page: the server status and the tenant's sites as links, each titled in the current locale, or in the site's first locale when it lacks the current one. With nobody signed in, a sign-in prompt shows in place of the sites, and the page does not start sign-in. The shell's sign-in page is `/?signIn=failed`, where the server sends the browser after a refused sign-in. It shows the same prompt and, above the page, an alert that screen readers announce, with the platform text `shell.home.signInFailed` and no reason. `/` without the marker shows no message |
  | `/{site}` | the site's first navigation entry, by redirect |
  | `/{site}/{page}` | the page inside the site shell, with the page title. A table page also takes `?search=&page=&pageSize=&sort=`. A table bound to a data source also takes its parameter values. A page with `tabs` widgets also takes `?tab=`, `?tab2=` and so on |
  | `/{site}/{page}?ticket=<id>` | a page with parameters: the values sit in the query under the parameter names |
  | `/{site}/{page}/new` | the form of a form page, to create a record |
  | `/{site}/{page}/{id}` | the form of a form page, to edit the record with that id |
  | `/{site}/{inboxpage}/{taskId}` | the task page of the task with that id, under the page of a task inbox |
  | `/{site}/{inspectorpage}/{instanceId}` | the instance page of the process instance with that id, under the page of a process inspector |

  - A page holds at most one `table` and at most one
    `form`, `taskInbox` or `processInspector`, so every part of the address
    has one owner. A `kpi` owns no part of the address and reads only page
    parameters, so it does not count. A `detail` owns none either, and so does a
    `chart`. The compiler checks this. `?search=&page=&pageSize=&sort=` and the data source parameter
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
    child collection and a `references` field have no column.
  - **URL state.** Paging, sorting and search live in the URL as the record
    API's own `page`, `pageSize`, `sort` (`-` and the field name for
    descending) and `search`, so reload, sharing and the back button keep
    them. The widget writes them after every other parameter, always in the
    order `search`, `page`, `pageSize`, `sort`, and leaves out defaults (no
    search, page 1, 20 a page or the data source's page size, no sort). It
    changes only its own four parameters and, for a data source, the
    parameter values described below: any other parameter, such as `x=1`,
    stays as it is.
  - **Search.** A table whose rows can be searched shows a search box above
    the rows, on the left, with the toolbar buttons on the right. An entity
    table has one when the entity's display field is among the fields the
    user may read, that is in the widget's `entity.fields`. A data source
    table has one when the data source's metadata has `"search": true`. The
    text applies on Enter, on the search button and when the box is cleared,
    so typing does not request rows and add a history entry for every key.
    It lives in the URL as `search`. A new search starts again at page 1 and
    adds a history entry, and paging and sorting keep it. The text is sent to
    the rows request as typed, without trimming, and an empty text means no
    search, so clearing the box removes `search` from the URL. The box takes
    at most 200 characters, the limit of the rows routes. On a table that
    cannot search, a `search` in the URL is ignored and removed without a
    history entry, as for an invalid `sort`, and is not sent. `search` is a
    reserved page parameter name, because the table owns it in the URL.
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
          "values": ["draft", "submitted", "approved", "rejected"],
          "valueLabelKeys": { "draft": "request.status.draft", "submitted": "request.status.submitted", "approved": "request.status.approved", "rejected": "request.status.rejected" },
          "target": null
        }
      ],
      "pageSize": 20,
      "search": false,
      "columns": [
        {
          "name": "status", "type": "enum", "labelKey": "request.status",
          "values": ["draft", "submitted", "approved", "rejected"],
          "valueLabelKeys": { "draft": "request.status.draft", "submitted": "request.status.submitted", "approved": "request.status.approved", "rejected": "request.status.rejected" },
          "target": null
        }
      ]
    }
    ```

    - **Parameters.** A parameter's `labelKey` is the text key of its label, or
      `null` when it has none. `values` and `target` are `null` when the type
      has none. `valueLabelKeys` maps each enum value to the text key of its
      label. It is `null` without `valueLabels`. The SPA resolves the keys with
      the site texts, while rows keep the raw value.
    - **Columns.** There is one column per projected field, in order. For a
      grouped data source, the columns are the group fields in `groupBy`
      order, then the measures. The header is the label of the field the
      path ends at, else the projected name. A measure has no `labelKey`, so
      its header is its name. A `count` is an `integer` column, a `sum` is a
      `decimal` column, and a `min` or `max` has its field's type. A column
      over a labelled enum field has the same `valueLabelKeys` as the field,
      and `null` otherwise. Sorting, values and labels follow the rules below,
      by the column's type.
    - **Page size.** The data source's `pageSize` is the table's default page
      size. It is offered with 10, 20, 50 and 100 when it is not one of them,
      and the URL leaves it out.
    - **Search.** `search` is `true` when the data source declares
      `"search": true`. The table then shows the search box described under
      Search above. Otherwise it shows none.
    - **Filter inputs.** A filter bar above the rows has one input per
      parameter, in declaration order. It shows the parameter's label, or its
      name when `labelKey` is `null`. Each input is the kind the [form widget](form-widget.md)
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
      writes them after every other parameter and before `search`, `page`,
      `pageSize` and `sort`, in declaration order. A new value starts again at page 1
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
  - **Sorting.** Every column but a reference and a file is sortable. The record API
    sorts a reference by the stored id, which does not match the label people
    see, and cannot sort a file. Repeated clicks on a header sort ascending, then descending, then
    not at all.
  - **Values.** Values are formatted for display and never changed:

    | Field type | Shown as |
    | --- | --- |
    | `date-time` | date and time to the second in the UI locale and the browser's time zone. The fraction is dropped |
    | `date` | the date in the UI locale, without a time-zone shift |
    | `integer`, `decimal` | exactly as the API wrote them, right-aligned |
    | `money` | the amount with its currency in the UI locale, such as `$1,250.50` in `en` and `1.250.000 ₫` in `vi`, right-aligned. It shows the field's `scale` in decimal places, or more when the API wrote more, so it never rounds. The currency is `currency`, or the value of `currencyField` in the same row, or of `currencyColumn` in a data source row. Without a code of three letters it shows the number as the API wrote it |
    | `boolean` | a localized yes or no |
    | `reference` | the label from `labels` |
    | `text`, `enum` | the value. An enum with `valueLabels` shows the label |
    | `file` | the file's name as a download link to `downloadPath` and the file's id. A data source row holds a file as its bare id, so a data source column shows the id and no link |
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
    number, so the [form widget](form-widget.md) sends numbers back as JSON numbers.
  - **Links.** The table has no open link and no create button of its own. A
    sample opens a record with a `rowActions` navigate with `record: id`, and
    creates one with a `toolbarActions` navigate without `record`, still gated
    by `access` `create`. Both pass the table's address, with its paging and
    sorting, to the form in history state.
  - **Row and toolbar actions.** See
    [D59](../decisions/d59-table-row-and-toolbar-actions.md). Page metadata
    lists the `navigate` actions in `rowActions` and `toolbarActions`,
    without those to pages the user cannot read, each with its `parameters`,
    `record`, `opensForm` and `paths`. It lists a `startProcess` in `rowActions` only
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
      `null` is hidden. The target page reads its parameters from the URL and
      binds them. A reference path such as `customer.parent.name` is resolved
      on click: the SPA reads each record along it through the record API,
      using `paths`. A `null` reference gives `null`. A 403, a 404, a missing
      field or a failed read opens no page and shows `shell.navigate.failed`.
      A button whose first name is not in scope or lacks a `paths` entry hides.
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
    - **Import.** A table over an entity has an Import button next to its
      toolbar actions only when `access.actions` holds `import`. Without it
      there is no button. The canvas draws it and opens nothing. A click opens
      a dialog with one file picker for a `.csv` or `.xlsx` file, a Check
      button and a Close button. Check sends `POST {entity}/imports` with the
      file as the raw body and `X-Axis-Csrf: 1`. The imports path is the
      entity's `recordsPath` with its last `/records` swapped for `/imports`.
      The `Content-Type` comes from the file extension, `text/csv` or the
      `xlsx` type, because browsers often report a CSV differently. Another
      extension is refused in the dialog and sends nothing. The dialog reads
      the import at once and then every second, with no time limit, until it
      is `checked` or `failed`. Closing the dialog stops the checking. It
      lists each error as the row and the field's label, for
      `/rows/{n}/values/{field}`, as a row error for `/rows/{n}`, as a column
      error for `/columns/{n}`, and as a file error for any other key. A field
      the table lacks shows its name. Commit shows only when the import is
      `checked`. It sends `POST {entity}/imports/{id}/commit` and checks
      every second until the import is `committed` or `failed`. On
      `committed` the dialog closes, a success message shows above the table
      and the current page of rows loads again. A `failed` commit lists its
      errors in the dialog. Picking another file starts a new import. A `403`,
      `413` or `415` on the upload each shows its own message, and any other
      failure shows the generic one. The progress and the errors sit in an
      `aria-live` region, and the picker and every button take the keyboard.
    - **Export.** A table has an Export dropdown button next to its toolbar
      actions only when `access.actions` holds `export`. Without it there is
      no button. The canvas draws it and exports nothing. The dropdown offers
      CSV and Excel, and it opens and selects with the keyboard. A choice
      sends `GET` to the export route: the entity's `recordsPath` with
      `/export` added, or the data source's `rowsPath` with its last `/rows`
      swapped for `/export`. The address holds the table's data source
      parameter values, bound and filter, its `search` and its `sort`, as the
      rows request has them, and `format` as `csv` or `xlsx`. It holds no
      `page` or `pageSize`, so the file has every matching row. The SPA saves
      the file under the name in the response's `Content-Disposition`, through
      a temporary download link. A success shows no message. When the response
      carries `Axis-Export-Truncated: true`, a notice above the table says the
      file stops at the row limit. A `403`, `409` or `503` each shows its own
      message above the table, and any other failure, a network error
      included, shows the generic one.
    - **Start process.** A row `startProcess` follows the rules of the form
      widget's **Start process action**, with the row as the subject and
      the same `Idempotency-Key` and `409` text. On `201` it loads the
      current page of rows again, with no success message. A failure shows
      above the table.
    - **Later.** Actions on selected rows come later.
  - **States.** Loading, empty and error states use the shared table and
    alert with platform texts. While the next page loads, the current rows
    stay under the loading overlay.
- **Form resource**. A `form` resource lays out the fields of one entity in sections. See [form-widget.md](form-widget.md).
- **Form widget.** A page whose widget is a `form` creates and edits one record through the record API. See [form-widget.md](form-widget.md).
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
  toggle, in the Menu panel at phone width. The chosen locale is kept in `localStorage` under `axis.locale`,
  like the theme mode under `axis.themeMode`. One key serves every site: a site starts in the stored locale
  when it offers it, and in its default locale otherwise. The shell keeps the current texts until the chosen
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
  - With nobody signed in, it is a **Sign in** button. It sends the browser to
    `/api/auth/sign-in` with the current path and query string as `returnUrl`, so
    the person comes back there, but to `/` from the sign-in page `/?signIn=failed`.
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

- **Phone width.** Below a fixed 768 px breakpoint, Ant Design's `md`, columns stack in file order, tab headers scroll and a row's
  actions fold into one menu. Every platform table, including form line items and the reference lookup,
  scrolls sideways inside its widget, so the page never does. The shell header keeps the site title, the
  account button and a **Menu** button (`shell.navigation.menu`) with `aria-expanded`. It opens a panel with
  the navigation, locale switch and theme toggle. Escape or following a link closes it, and Escape returns
  focus to the button. There is no per-widget setting until a sample needs one, see
  [D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md).

## Studio

The Studio page is in [studio.md](studio.md).
