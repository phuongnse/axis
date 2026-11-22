# D159. Pages show related lists, boards and calendars, forms take context and drafts, and each site has its brand

Status: **Agreed**

## Context

A page held one table, a form page took no values, a chart had no filter a reader could set, a text
field had one line, and the only views were tables, forms, details and charts. An organization reads a
customer with its orders and tickets on one page, opens a new ticket with the customer filled in,
works a board of tickets, plans leave on a calendar, closes thirty requests at once, and shows its
own logo to its customers. Every comparable platform has these.
[D154](d154-every-capability-an-organization-needs-has-a-home-and-a-capability-map-keeps-it-so.md)
puts them among the foundations. This decision replaces
[D61](https://github.com/phuongnse/axis/blob/40b46c2ab7011e6d68dcd5364a4d122c9384b8e9/docs/decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md),
[D63](https://github.com/phuongnse/axis/blob/40b46c2ab7011e6d68dcd5364a4d122c9384b8e9/docs/decisions/d63-navigate-actions-and-page-parameters.md) and [D64](https://github.com/phuongnse/axis/blob/40b46c2ab7011e6d68dcd5364a4d122c9384b8e9/docs/decisions/d64-detail-chart-and-kpi-widgets.md), and
keeps every part of them that still holds.

## Decision

- **A tree of widgets.** The containers `columns`, `tabs` and `section` hold other widgets, nested to
  at most 4 levels, and column widths are twelfths that add up to 12.
- **One owner for each part of the address.** _(planned for M42)_ A page may hold several tables,
  such as a record's related lists, and each table owns its part of the address under a prefix of
  its own. A page holds at most one of `form`, `taskInbox` and `processInspector`.
- **Policies prune the tree.** A widget the user cannot read is left out, and so is a container left
  empty. A page is readable when one of its widgets is.
- **One breakpoint for the platform.** Below it, columns stack, tables scroll inside their widget and
  the navigation collapses into a menu, with no setting per widget.
- **Studio edits the tree,** as [D164](d164-typed-editors-and-one-process-graph-laid-out-by-elk.md) sets out.
- **Typed page parameters in the URL query.** A page declares `parameters`, each with a name, an
  entity field type and whether it is required. Their values live in the URL query under their names,
  as a data source's parameters do ([D18](d18-data-sources.md)), and a name a table's own query uses
  is reserved.
- **A name map binds them.** A table, a chart or a KPI binds a page parameter to a data source
  parameter by name, not by an expression.
- **A page may have a filter bar.** _(planned for M42)_ Its inputs set the page's parameters, so
  every table, chart and KPI bound to them follows, and a click on a chart's group opens a page with
  that group's value, for a reader to drill down.
- **Navigate actions.** A `navigate` action names a target page and gives each of its parameters an
  expression over the row or the record, which may follow a short reference path. It sits in a
  table's row and toolbar actions ([D59](d59-table-row-and-toolbar-actions.md)) and in a form's
  actions. To a form page it may set `record` to open that record.
- **A new record starts with its parent's values.** _(planned for M42)_ A `navigate` action to a form
  page without `record` may give a new record the first values of its fields, such as the customer of
  the page it came from, as [resource files](../reference/resource-files.md#first-values-of-a-new-record)
  set out.
- **One format.** A table opens and creates records only through navigate actions.
- **Checked when compiled.** The compiler checks each target page, parameter, field and value type,
  and indexes each target as a `navigate page` relation
  ([D141](d141-the-reference-index-and-what-uses-a-resource.md)). A required parameter missing from the
  URL, or malformed, shows the not-found page.
- **Policies shape the actions.** Page metadata leaves out an action whose target page, or a field on
  whose path, the user may not read, and the server still checks every request
  ([D12](d12-authorization-is-server-side-and-default-deny.md)).
- **Detail.** A `detail` shows one record read-only, in the sections of a `form` resource or with
  every readable field. A required `reference` page parameter gives its record, and it may have the
  actions a form has.
- **Chart.** A `chart` binds only a grouped data source with exactly one `groupBy` field, its
  category, and draws its measures as a `bar`, a `line` or a `pie`. It reads one page of at most 100
  groups and says when more exist.
- **KPI.** A `kpi` binds only a grouped data source with no `groupBy`, so it reads one total row, and
  shows one measure. An optional comparison is a second labelled value, from which the SPA computes
  no difference. A click may navigate.
- **Detail, chart and KPI own no part of the address,** so the limit of one form does not count them.
- **Apache ECharts is a detail of the SPA,** as
  [D58](d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md) requires of a specialised
  library, loaded lazily in its own chunk.
- **Text may have several lines, or be rich.** _(planned for M42)_ A `text` field may be multi-line,
  and a `richText` field keeps a cleaned subset of HTML, which a search reads as its plain words.
- **Forms take their context.** _(planned for M42)_ A form groups its sections in tabs and folds a
  section. A field's default may be the person signed in, today, or a value its page gives. A lookup
  lists only the records a filter over the form's other fields allows, and the server checks the
  same filter on save. A child collection keeps the order its rows are dragged into.
- **A new record keeps a draft.** _(planned for M42)_ A form for a new record saves a draft on the
  server as the person types, for that person only, until it is saved or 30 days pass.
- **Tables act on many rows and edit in place.** _(planned for M42)_ A table may have bulk actions
  on the selected rows, to start a process, set fields or delete, under the grants of each row and
  with a result for each row. A cell may be edited where its field may be written. An application's
  table has the column chooser of
  [D153](d153-a-persons-table-views-and-columns-are-kept-on-the-server-and-the-address-still-wins.md).
- **More views.** _(planned for M42)_ A calendar shows records by a date field and moves one by a
  drag where it may be updated. A board groups records by an enum or a reference field and moves one
  between groups by a drag, as a record write under every policy and contract. Cards show records as
  tiles, and a tree shows records under the record they refer to. A content widget shows text, an
  image, a link or a note, in each language of the site.
- **Navigation and a person's own places.** _(planned for M42)_ A site's menu entries may sit under
  a parent. Each role may have its own home page. Each person keeps favourites and recent records,
  on the server.
- **Each site has its brand.** _(planned for M42)_ A site names its logo and its main colour, the
  other colours follow from it, and the compiler refuses a colour whose text would not meet AA
  contrast.

The shapes are in [resource files](../reference/resource-files.md#resource-file-shape), the drawing
in [container widgets](../reference/container-widgets.md), the policies and the breakpoint in
[frontend](../reference/frontend.md), the editor in [Studio](../reference/studio-editors-by-kind.md#widget-tree), the
binding in [widget binding](../reference/data-sources.md#widget-binding) and the views in
[detail, chart and KPI widgets](../reference/detail-chart-kpi-widgets.md).

## Consequences

- Layout stays in configuration, and every table, form and filter keeps its state in the address, so
  a page works as a link and survives a reload.
- A record page reads as one place, with its related lists, and a new related record starts filled in.
- A row can open any page with any values, and the compiler checks the link. A reference path in an
  action is read on click, one record at a time.
- A board and a calendar change records through the same writes, grants and contracts as a form, so
  a drag can be refused for the same reasons a save can.
- Every page has the same phone behaviour, with nothing to set.
- The browser never groups or pivots rows, and a chart stays readable with no paging control.
- The SPA carries a chart library, which its lazy chunk keeps out of pages without a chart.

## Alternatives

- **Markdown for rich text.** People who are not developers would have to learn its marks, and the
  editors they know write HTML.
- **Drafts in the browser only.** A draft would be lost on another device or a cleared browser.
- **A responsive setting on each widget.** It would add a setting that no sample needs.
- **The SPA computes the change between two values.** It would need rounding and sign rules for
  decimals that arrive as source text.
- **A chart that pages through its groups.** A paging control would confuse readers.
