# Detail, chart and KPI widgets

Detailed reference for the `detail`, `chart` and `kpi` widgets of the SPA, all
_(planned for M12)_. The conventions of [architecture.md](../architecture.md) apply: sections
marked _(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md). The
rest of the frontend is in [frontend.md](frontend.md).

- **Detail widget** *(planned for M12)*. A page whose widget is a `detail`
  shows one record read-only, read through the record API. It writes
  nothing. See [D64](../decisions/d64-detail-chart-and-kpi-widgets.md).

  ```json
  { "type": "detail", "entity": "Ticket", "record": "ticket", "actions": [{ "type": "startProcess", "process": "TicketHandling", "label": { "textKey": "tickets.handle" } }] }
  ```

  - **Compile.** The widget names exactly one of `form` and `entity`, as a form
    widget does (`AXC0060`). A `form` that names no loaded form is `AXC0077`.
    `record` names a page parameter that is `required`, of type `reference`,
    and whose `target` is the widget's entity. Otherwise it is a new check,
    with no code yet. `actions` follow the form widget's rules, with
    `navigate` allowed. The labels join `AXC0028`.
  - **Metadata.** It reuses `entity`, `form`, `actions` and `access` of the
    form widget, with `"dataSource": null` and `"chart": null` and
    `"kpi": null`, and adds `detail`:

    ```json
    { "type": "detail", "entity": { "name": "Ticket", "fields": [{ "name": "title", "type": "text" }] }, "formPage": null, "dataSource": null, "form": null, "tasksPath": null, "process": null, "instancesPath": null, "actions": [{ "type": "startProcess", "process": "TicketHandling", "labelKey": "tickets.handle", "instancesPath": "/api/apps/ServiceDesk/processes/TicketHandling/instances" }], "access": { "actions": ["read"], "createFields": [], "updateFields": [] }, "detail": { "record": "ticket" }, "chart": null, "kpi": null }
    ```

  - **Record.** The SPA reads the record that the page parameter `record`
    names, from the entity's `recordsPath`. A missing or malformed parameter
    and a record that is missing or unreadable show the not-found page inside
    the site shell.
  - **Fields.** With a `form`, the widget shows its sections in order under
    their titles. Without one, it shows every readable field in declaration
    order. Every value is read-only and formatted as a table cell shows it. The
    widget ignores `visibleWhen`, `requiredWhen`, `readOnlyWhen` and `steps`.
  - **Actions.** A `startProcess` button follows the form widget's **Start
    process action**, without a save first. A `navigate` action sees the
    record's fields and `id`, as on a form
    ([D63](../decisions/d63-navigate-actions-and-page-parameters.md)).
- **Chart widget** *(planned for M12)*. A page whose widget is a `chart` draws
  the groups of a grouped data source as a `bar`, a `line` or a `pie`. See
  [D64](../decisions/d64-detail-chart-and-kpi-widgets.md) and the
  [widget binding](data-sources.md#widget-binding).

  ```json
  { "type": "chart", "kind": "bar", "dataSource": "OpenTicketsByCategory", "category": "category", "measures": [{ "name": "openCount", "label": { "textKey": "dashboard.byCategory.open" } }], "title": { "textKey": "dashboard.byCategory.title" } }
  ```

  - **Compile.** The binding checks are in
    [data-sources.md](data-sources.md#widget-binding). `kind` is `bar`, `line`
    or `pie`. A `pie` has exactly one measure. The `title` and the measure
    labels join `AXC0028`. A chart has no `entity`, `form` or `actions`
    (`AXC0060`). Configuration names no chart library and no library option.
  - **Metadata.** The widget has the `dataSource` object of the table
    binding, `"entity": null`, `"form": null`, `"actions": []` and
    `"access": null`, and adds `chart`. A measure's `labelKey` is a text key,
    because a data source measure has no label:

    ```json
    { "type": "chart", "entity": null, "dataSource": { "name": "OpenTicketsByCategory", "rowsPath": "/api/apps/ServiceDesk/data-sources/OpenTicketsByCategory/rows", "entity": "Ticket", "parameters": [], "pageSize": 20, "columns": [] }, "chart": { "kind": "bar", "titleKey": "dashboard.byCategory.title", "category": "category", "measures": [{ "name": "openCount", "labelKey": "dashboard.byCategory.open" }] }, "detail": null, "kpi": null, "access": null }
    ```

    The example leaves `columns` empty and the keys that stay `null` or `[]`
    out. A widget carries them all.
  - **Rows.** The chart sends one request to `rowsPath` with `pageSize=100`,
    the largest page size, and never pages. When `totalCount` is over 100, a
    note under the chart says that only the first 100 groups show. Each row is
    one category. A `reference` category shows its label from `labels`, and a
    `null` category shows a platform text.
  - **Values.** Decimals arrive as source text, as under **Number source
    text**. The SPA turns them into numbers only to draw. The tooltip and the
    data table keep the source text.
  - **Look.** Series colours come from the theme tokens, in light and dark
    mode. The SPA loads the library lazily in its own chunk, as it loads React
    Flow, so a page without a chart never loads it.
  - **Accessibility.** The title is the chart's accessible name. Screen readers
    get the data as a table, with the category and each measure as columns.
    Loading, empty and error states use platform texts.
- **KPI widget**. A page whose widget is a `kpi` shows one
  measure of a data source as a tile, with its label. The compile checks and
  the page metadata are built. The tile in the SPA is *(planned for M12)*. See
  [D64](../decisions/d64-detail-chart-and-kpi-widgets.md).

  ```json
  { "type": "kpi", "dataSource": "OpenTicketTotals", "measure": "openCount", "label": { "textKey": "dashboard.open.label" }, "comparison": { "measure": "urgentCount", "label": { "textKey": "dashboard.urgent.label" } }, "navigate": { "type": "navigate", "page": "AllTickets", "label": { "textKey": "dashboard.open.show" } } }
  ```

  - **Compile.** The binding checks are in
    [data-sources.md](data-sources.md#widget-binding). `comparison` and
    `navigate` are optional. A navigate action follows D63's checks, and its
    expressions see only the page's parameters. The labels join `AXC0028`. A
    KPI has no `entity`, `form` or `actions` (`AXC0060`). The page schema
    requires `dataSource`, `measure` and `label`, and `comparison` has both a
    `measure` and a `label`.
  - **Metadata.** The widget has the `dataSource` object, `"entity": null`,
    `"form": null`, `"actions": []` and `"access": null`, and adds `kpi`:

    ```json
    "kpi": {
      "measure": "openCount",
      "labelKey": "dashboard.open.label",
      "comparison": { "measure": "urgentCount", "labelKey": "dashboard.urgent.label" },
      "navigate": { "type": "navigate", "page": "AllTickets", "labelKey": "dashboard.open.show", "parameters": {}, "record": null, "opensForm": false }
    }
    ```

    `measure` and the comparison's `measure` are the names the data source
    declares. `comparison` and `navigate` are `null` when the widget has none.
    The `navigate` is `null` too when the user cannot read its page. It has
    the keys of every `navigate` action, see
    [frontend.md](frontend.md). A user who cannot run the data source gets no
    widget, as with a table.
  - **Value** *(planned for M12)*. The SPA reads the one total row of the data source. The value
    is formatted as a table cell shows it, and a `null` measure shows a
    platform text for an empty value. The comparison is a second labelled
    value. The SPA computes no difference and no percentage.
  - **Click and reading** *(planned for M12)*. With a `navigate`, the tile is a link to the target
    page. The tile reads as `<label>: <value>`, and a comparison adds
    `<comparison label>: <comparison value>`. Loading and error states use
    platform texts.
