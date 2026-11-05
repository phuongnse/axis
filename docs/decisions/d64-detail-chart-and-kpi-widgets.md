# D64. Detail, chart and KPI widgets

Status: **Agreed**

## Context

Beside a table and a form, a real application needs one record to read, a series to compare and a
single key figure. These views build on the widgets of [D15](d15-presentation-model.md), the grouped
data sources of [D18](d18-data-sources.md) and the page parameters of
[D63](d63-navigate-actions-and-page-parameters.md).

## Decision

- **Detail.** A `detail` shows one record read-only, in the sections of a `form` resource or with
  every readable field. A required `reference` page parameter gives its record, and it may have the
  actions a form has.
- **Chart.** A `chart` binds only a grouped data source with exactly one `groupBy` field, its
  category, and draws its measures as a `bar`, a `line` or a `pie`. It reads one page of at most 100
  groups and says when more exist.
- **KPI.** A `kpi` binds only a grouped data source with no `groupBy`, so it reads one total row, and
  shows one measure. An optional comparison is a second labelled value, from which the SPA computes
  no difference. A click may navigate.
- **Page parameters only.** A chart and a KPI bind page parameters with the name map of a table, and
  leave only optional data source parameters unbound, because they have no filter bar. The three
  widgets own no part of the address, so the one-table, one-form limit of
  [D61](d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md) does not count
  them.
- **Apache ECharts is a detail of the SPA,** as
  [D58](d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md) requires of a specialised
  library, loaded lazily in its own chunk.

The detail, accessibility and policies are in
[detail, chart and KPI widgets](../reference/detail-chart-kpi-widgets.md).

## Consequences

- The browser never groups or pivots rows, and a chart stays readable with no paging control.
- The compiler checks the record of a detail.
- The SPA carries a chart library, which its lazy chunk keeps out of pages without a chart.

## Alternatives

- **The SPA computes the change between two values.** It would need rounding and sign rules for
  decimals that arrive as source text.
- **A chart that pages through its groups.** A paging control would confuse readers.
