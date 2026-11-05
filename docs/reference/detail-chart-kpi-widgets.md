# Detail, chart and KPI widgets

The `detail`, `chart` and `kpi` widgets show what a page reads without editing it: one record, the groups of a data
source, or one value. This page says how a page names each of them, what the compiler checks, what page metadata
carries and how the SPA draws them. The rest of the frontend is in [frontend](frontend.md).

## Detail widget

A `detail` widget shows one record read-only, read through the record API, and writes nothing.

```json
{ "type": "detail", "name": "ticket", "entity": "Ticket", "record": "ticket", "actions": [{ "type": "startProcess", "name": "handle", "process": "TicketHandling", "label": { "textKey": "tickets.handle" } }] }
```

- **Compile.** The widget names exactly one of `form` and `entity`, as a form widget does (`AXC0060`). A `form` that
  names no loaded form is `AXC0077`. `record` names a page parameter that is `required`, of type `reference`, and
  whose `target` is the widget's entity, ignoring letter case. Otherwise it is `AXC0239`, reported at
  `/widgets/{i}/record`. The page schema requires `record` on a `detail` and refuses it on any other widget, so a
  missing `record` is `AXC0004`. `actions` follow the form widget's rules, with `navigate` allowed. The labels join
  `AXC0028`.
- **Metadata.** The widget carries `entity`, `form`, `actions` and `access` as the form widget does, with the other
  [widget keys](frontend.md#widget-keys), and adds `detail`:

  ```json
  { "type": "detail", "entity": { "name": "Ticket", "fields": [{ "name": "title", "type": "text" }] }, "dataSource": null, "form": null, "tasksPath": null, "process": null, "instancesPath": null, "deadLettersPath": null, "actions": [{ "type": "startProcess", "process": "TicketHandling", "labelKey": "tickets.handle", "instancesPath": "/api/apps/ServiceDesk/processes/TicketHandling/instances", "visibleWhen": null }], "access": { "actions": ["read"], "createFields": [], "updateFields": [] }, "detail": { "record": "ticket" }, "kpi": null, "chart": null }
  ```

- **Record.** The SPA reads the record that the page parameter `record` names, from the entity's `recordsPath`. A
  missing or malformed parameter, and a record that is missing or unreadable, show the not-found page inside the site
  shell. A `detail` inside `columns`, `tabs` or a `section` is drawn too, see
  [container widgets](container-widgets.md).
- **Fields.** With a `form`, the widget shows its sections in order under their titles. Without one, it shows every
  readable field in declaration order. Every value is read-only and formatted as a table cell shows it. A `references`
  field shows its labels in order as tags, or a record's id where it has no label. An empty value, an empty set of
  references and a file that is not set among them, shows "Not set" in muted italics, see
  [shown values](frontend.md#shown-values). The widget ignores `visibleWhen`, `requiredWhen`, `readOnlyWhen` and
  `steps`.
- **Layout.** The fields are a description list (`dl`, `dt`, `dd`). Each label sits above its value, as a read-only
  form field reads, with `spacing-md` between fields, and values are in `text`. The labels and the values follow the
  field look of the [form widget](form-widget.md), and the fields follow the form sections' widths, as fields in
  columns do on a form.
- **Actions.** A `startProcess` button follows the form widget's start process action, without a save first. It is the
  region's main action, outlined at the main level of [buttons](actions-and-controls.md#buttons). A `navigate` action
  sees the record's fields and `id`, as on a form.

## Chart widget

A `chart` widget draws the groups of a grouped data source as a `bar`, a `line` or a `pie`, and binds page parameters
as the [widget binding](data-sources.md#widget-binding) says.

```json
{ "type": "chart", "name": "by-category", "kind": "bar", "dataSource": "OpenTicketsByCategory", "category": "category", "measures": [{ "name": "openCount", "label": { "textKey": "dashboard.byCategory.open" } }], "title": { "textKey": "dashboard.byCategory.title" } }
```

- **Compile.** The binding checks are in [widget binding](data-sources.md#widget-binding).
  - `kind` is `bar`, `line` or `pie`, and a `pie` has exactly one measure (`AXC0241`).
  - A data source without exactly one `groupBy` field, or a `category` that is not that field, is `AXC0240`.
  - A measure that the data source lacks, or that is not a number, is `AXC0241`. A measure is a number when it is a
    `count`, a `countDistinct`, a `sum` or an `avg`, or a `min` or `max` over an integer, a decimal or a money field,
    because `min` and `max` keep the type of their field and a chart cannot plot a date.
  - The `title` and the measure labels join `AXC0028`. A chart has no `entity`, `form` or `actions` (`AXC0060`).
  - The page schema requires `dataSource`, `kind`, `category`, `measures` and `title`, so a missing one is `AXC0004`.
  - Names match the data source ignoring letter case, and the compiled chart keeps the names the data source declares.
  - A chart binds page parameters into its data source with `parameters`, the name map of a table, and a data source
    parameter that it leaves unbound must be optional (`AXC0060`).
  - Configuration names no chart library and no library option.
- **Metadata.** The widget has the `dataSource` object of the table binding and the other
  [widget keys](frontend.md#widget-keys), and adds `chart`. `parameters` holds the widget's name map, as on a table. A
  measure's `labelKey` is a text key, because a data source measure has no label. A user who cannot run the data
  source gets no widget, as with a table. This example leaves `columns` empty and the keys that stay `null` or `[]`
  out, which a widget carries all of:

  ```json
  { "type": "chart", "entity": null, "dataSource": { "name": "OpenTicketsByCategory", "rowsPath": "/api/apps/ServiceDesk/data-sources/OpenTicketsByCategory/rows", "entity": "Ticket", "parameters": [], "pageSize": 20, "columns": [] }, "access": null, "detail": null, "kpi": null, "chart": { "kind": "bar", "titleKey": "dashboard.byCategory.title", "category": "category", "measures": [{ "name": "openCount", "labelKey": "dashboard.byCategory.open" }] } }
  ```

- **Rows.** The chart sends one request to `rowsPath` with `pageSize=100`, the largest page size, and never pages. The
  request carries each bound page parameter's value under the data source parameter's name, and a parameter whose
  page parameter has no value is left out. The chart asks again when a page parameter changes. When `totalCount` is
  over 100, a note under the chart says that only the first 100 groups show. Each row is one category. A `reference`
  category shows its label from `labels`, and a `null` category shows a platform text.
- **Values.** Decimals arrive as [source text](frontend.md#number-source-text). The SPA turns them into numbers only to
  draw, and the tooltip and the data table keep the source text.
- **Look.** The widget draws a card with the chart's title above the chart. The shared chart component in
  `web/src/platform/Chart.tsx` draws bar, line and pie charts from categories and labelled series, with series colours
  from the theme tokens in light and dark mode.
- **Loading the library.** The SPA loads the chart library lazily in its own chunk, as it loads React Flow, so a page
  without a chart never loads it. `scripts/build.sh` prints the chunk's size and fails when the chunk is missing or
  part of the main bundle.
- **Accessibility.** The title is the chart's accessible name. Screen readers get the data as a table that only they
  reach, with the category and each measure as columns. The loading, empty, error, null category and
  more-than-100-groups texts are platform texts.

## KPI widget

A `kpi` widget shows one measure of a data source as a tile, with its label.

```json
{ "type": "kpi", "name": "open", "dataSource": "OpenTicketTotals", "measure": "openCount", "label": { "textKey": "dashboard.open.label" }, "comparison": { "measure": "openLastMonth", "label": { "textKey": "dashboard.open.lastMonth" } }, "navigate": { "type": "navigate", "page": "AllTickets", "label": { "textKey": "dashboard.open.show" } } }
```

- **Compile.** The binding checks are in [widget binding](data-sources.md#widget-binding). `comparison` and `navigate`
  are optional. A `navigate` follows the checks of every `navigate` action, and its expressions see only the page's
  parameters. A KPI binds page parameters into its data source with `parameters`, the name map of a table, and a data
  source parameter that it leaves unbound must be optional (`AXC0060`). The labels join `AXC0028`. A KPI has no
  `entity`, `form` or `actions` (`AXC0060`). The page schema requires `dataSource`, `measure` and `label`, and
  `comparison` has both a `measure` and a `label`.
- **Metadata.** The widget has the `dataSource` object and the other [widget keys](frontend.md#widget-keys), and adds
  `kpi`. `parameters` holds the widget's name map, as on a table:

  ```json
  "kpi": {
    "measure": "openCount",
    "labelKey": "dashboard.open.label",
    "comparison": { "measure": "openLastMonth", "labelKey": "dashboard.open.lastMonth" },
    "navigate": { "type": "navigate", "page": "AllTickets", "labelKey": "dashboard.open.show", "parameters": {}, "record": null, "opensForm": false, "paths": {}, "visibleWhen": null }
  }
  ```

  `measure` and the comparison's `measure` are the names the data source declares. `comparison` and `navigate` are
  `null` when the widget has none, and `navigate` is `null` too when the user cannot read its page. It has the keys of
  every `navigate` action, see [navigate](frontend.md#navigate). A user who cannot run the data source gets no widget,
  as with a table.
- **Value.** The SPA reads the one total row of the data source. The request carries each bound page parameter's value
  under the data source parameter's name, and the tile reads again when a page parameter changes. The value is
  formatted as a table cell shows it. A `null` measure shows a muted "—", not bold, with the platform text
  `shell.kpi.empty` hidden for everyone but assistive technology, see [shown values](frontend.md#shown-values).
- **Comparison.** A `comparison` is a reference for the same measure, such as last month's total or a target, shown as
  a second labelled value. The SPA computes no difference and no percentage. Two different metrics are two tiles side
  by side in `columns`, at the same size.
- **Look.** The label sits above, in the field label style of the [form widget](form-widget.md)'s field look. The
  measure is below it, large and semibold, in tabular figures. The comparison sits under it at body size, with its
  label muted, no colon between the label and the value, and the value in tabular figures.
- **Click and reading.** With a `navigate`, the tile is a link to the target page, and the navigate label is the tile's
  last line, inside the link. The tile reads as `<label>: <value>`, and a comparison adds
  `<comparison label>: <comparison value>`. Loading and error states use platform texts.
