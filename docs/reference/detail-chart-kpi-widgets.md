# Detail, chart and KPI widgets

Detailed reference for the `detail`, `chart` and `kpi` widgets of the SPA. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet,
and Dn refers to [decisions.md](../decisions.md). The rest of the frontend is in
[frontend.md](frontend.md).

- **Detail widget**. A page whose widget is a `detail`
  shows one record read-only, read through the record API. It writes
  nothing. See [D64](../decisions/d64-detail-chart-and-kpi-widgets.md).

  ```json
  { "type": "detail", "entity": "Ticket", "record": "ticket", "actions": [{ "type": "startProcess", "process": "TicketHandling", "label": { "textKey": "tickets.handle" } }] }
  ```

  - **Compile.** The widget names exactly one of `form` and `entity`, as a form
    widget does (`AXC0060`). A `form` that names no loaded form is `AXC0077`.
    `record` names a page parameter that is `required`, of type `reference`,
    and whose `target` is the widget's entity, ignoring letter case.
    Otherwise it is `AXC0239`, reported at `/widgets/{i}/record`. The page
    schema requires `record` on a `detail` and rejects it on any other
    widget, so a missing `record` is `AXC0004`. `actions` follow the form widget's rules, with
    `navigate` allowed. The labels join `AXC0028`.
  - **Metadata.** It reuses `entity`, `form`, `actions` and `access` of the
    form widget, with `"dataSource": null`, `"kpi": null` and `"chart": null`,
    and adds `detail`, written after `access` and before `kpi`:

    ```json
    { "type": "detail", "entity": { "name": "Ticket", "fields": [{ "name": "title", "type": "text" }] }, "dataSource": null, "form": null, "tasksPath": null, "process": null, "instancesPath": null, "deadLettersPath": null, "actions": [{ "type": "startProcess", "process": "TicketHandling", "labelKey": "tickets.handle", "instancesPath": "/api/apps/ServiceDesk/processes/TicketHandling/instances", "visibleWhen": null }], "access": { "actions": ["read"], "createFields": [], "updateFields": [] }, "detail": { "record": "ticket" }, "kpi": null, "chart": null }
    ```

  - **Record.** The SPA reads the record that the page parameter `record`
    names, from the entity's `recordsPath`. A missing or malformed parameter
    and a record that is missing or unreadable show the not-found page inside
    the site shell. A `detail` inside `columns`, `tabs` or a `section` is
    drawn too, see [container widgets](container-widgets.md).
  - **Fields.** With a `form`, the widget shows its sections in order under
    their titles. Without one, it shows every readable field in declaration
    order. Every value is read-only and formatted as a table cell shows it. The
    widget ignores `visibleWhen`, `requiredWhen`, `readOnlyWhen` and `steps`.
  - **Actions.** A `startProcess` button follows the form widget's **Start
    process action**, without a save first. A `navigate` action sees the
    record's fields and `id`, as on a form
    ([D63](../decisions/d63-navigate-actions-and-page-parameters.md)).
- **Chart widget**. A page whose widget is a `chart` draws
  the groups of a grouped data source as a `bar`, a `line` or a `pie`. The
  compile checks, the page metadata and the chart in the SPA are built, and
  the chart binds page parameters. See
  [D64](../decisions/d64-detail-chart-and-kpi-widgets.md) and the
  [widget binding](data-sources.md#widget-binding).

  ```json
  { "type": "chart", "kind": "bar", "dataSource": "OpenTicketsByCategory", "category": "category", "measures": [{ "name": "openCount", "label": { "textKey": "dashboard.byCategory.open" } }], "title": { "textKey": "dashboard.byCategory.title" } }
  ```

  - **Compile.** The binding checks are in
    [data-sources.md](data-sources.md#widget-binding). `kind` is `bar`, `line`
    or `pie`. A `pie` has exactly one measure (`AXC0241`). A data source
    without exactly one `groupBy` field, or a `category` that is not that
    field, is `AXC0240`. A measure that the data source lacks, or that is not a
    number, is `AXC0241`. A measure is a number when it is a `count` or a `sum`,
    or a `min` or `max` over an integer, a decimal or a money field, because `min` and `max`
    keep the type of their field and a chart cannot plot a date. The `title`
    and the measure labels join `AXC0028`. A chart has no `entity`, `form` or
    `actions` (`AXC0060`). Configuration names no chart library and no library
    option. The page schema requires `dataSource`, `kind`, `category`,
    `measures` and `title`, so a missing one is `AXC0004`. Names match the data
    source ignoring letter case, and the compiled chart keeps the names the data
    source declares. A chart binds page parameters into its data source with
    `parameters`, the name map of a table, and a data source parameter that it
    leaves unbound must be optional (`AXC0060`).
  - **Metadata.** The widget has the `dataSource` object of the table
    binding, `"entity": null`, `"form": null`, `"actions": []` and
    `"access": null`, and adds `chart`, written after `kpi`. `parameters` holds the widget's name
    map, as on a table. A measure's
    `labelKey` is a text key, because a data source measure has no label. A
    user who cannot run the data source gets no widget, as with a table:

    ```json
    { "type": "chart", "entity": null, "dataSource": { "name": "OpenTicketsByCategory", "rowsPath": "/api/apps/ServiceDesk/data-sources/OpenTicketsByCategory/rows", "entity": "Ticket", "parameters": [], "pageSize": 20, "columns": [] }, "access": null, "detail": null, "kpi": null, "chart": { "kind": "bar", "titleKey": "dashboard.byCategory.title", "category": "category", "measures": [{ "name": "openCount", "labelKey": "dashboard.byCategory.open" }] } }
    ```

    The example leaves `columns` empty and the keys that stay `null` or `[]`
    out. A widget carries them all.
  - **Rows.** The chart sends one request to `rowsPath` with `pageSize=100`,
    the largest page size, and never pages. The request carries each bound
    page parameter's value under the data source parameter's name, and a
    parameter whose page parameter has no value is left out. The chart asks
    again when a page parameter changes. When `totalCount` is over 100, a
    note under the chart says that only the first 100 groups show. Each row is
    one category. A `reference` category shows its label from `labels`, and a
    `null` category shows a platform text.
  - **Values.** Decimals arrive as source text, as under **Number source
    text**. The SPA turns them into numbers only to draw. The tooltip and the
    data table keep the source text.
  - **Look.** Series colours come from the theme tokens, in light and dark
    mode. The SPA loads the library lazily in its own chunk of about 185 KB
    gzipped, as it loads React Flow, so a page without a chart never loads it.
    `scripts/build.sh` prints the chunk's size and fails when the chunk is
    missing or part of the main bundle. The shared chart component
    in `web/src/platform/Chart.tsx` is built and draws bar, line and pie charts
    from categories and labelled series. The widget that reads a data source
    draws a card with the chart's title above the chart.
  - **Accessibility.** The title is the chart's accessible name. Screen readers
    get the data as a table that only they reach, with the category and each
    measure as columns. The loading, empty, error, null category and
    more-than-100-groups texts are platform texts.
- **KPI widget**. A page whose widget is a `kpi` shows one
  measure of a data source as a tile, with its label. The compile checks, the
  page metadata and the tile in the SPA are built. See
  [D64](../decisions/d64-detail-chart-and-kpi-widgets.md).

  ```json
  { "type": "kpi", "dataSource": "OpenTicketTotals", "measure": "openCount", "label": { "textKey": "dashboard.open.label" }, "comparison": { "measure": "urgentCount", "label": { "textKey": "dashboard.urgent.label" } }, "navigate": { "type": "navigate", "page": "AllTickets", "label": { "textKey": "dashboard.open.show" } } }
  ```

  - **Compile.** The binding checks are in
    [data-sources.md](data-sources.md#widget-binding). `comparison` and
    `navigate` are optional. A navigate action follows D63's checks, and its
    expressions see only the page's parameters. A KPI binds page parameters
    into its data source with `parameters`, the name map of a table, and a data
    source parameter that it leaves unbound must be optional (`AXC0060`). The
    labels join `AXC0028`. A KPI has no `entity`, `form` or `actions` (`AXC0060`). The page schema
    requires `dataSource`, `measure` and `label`, and `comparison` has both a
    `measure` and a `label`.
  - **Metadata.** The widget has the `dataSource` object, `"entity": null`,
    `"form": null`, `"actions": []` and `"access": null`, and adds `kpi`.
    `parameters` holds the widget's name map, as on a table:

    ```json
    "kpi": {
      "measure": "openCount",
      "labelKey": "dashboard.open.label",
      "comparison": { "measure": "urgentCount", "labelKey": "dashboard.urgent.label" },
      "navigate": { "type": "navigate", "page": "AllTickets", "labelKey": "dashboard.open.show", "parameters": {}, "record": null, "opensForm": false, "paths": {}, "visibleWhen": null }
    }
    ```

    `measure` and the comparison's `measure` are the names the data source
    declares. `comparison` and `navigate` are `null` when the widget has none.
    The `navigate` is `null` too when the user cannot read its page. It has
    the keys of every `navigate` action, see
    [frontend.md](frontend.md). A user who cannot run the data source gets no
    widget, as with a table.
  - **Value**. The SPA reads the one total row of the data source. The request
    carries each bound page parameter's value under the data source
    parameter's name, and the tile reads again when a page parameter changes.
    The value
    is formatted as a table cell shows it, and a `null` measure shows a
    platform text for an empty value. The comparison is a second labelled
    value. The SPA computes no difference and no percentage.
  - **Click and reading**. With a `navigate`, the tile is a link to the target
    page. The navigate label is the tile's last line, inside the link. The
    tile reads as `<label>: <value>`, and a comparison adds
    `<comparison label>: <comparison value>`. Loading and error states use
    platform texts.
