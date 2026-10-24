# D64. Detail, chart and KPI widgets

Status: **Agreed**

M12 adds three widgets: a read-only detail of one record, a chart over a
grouped data source and a KPI tile that shows one measure. This builds on
[D15](d15-presentation-model.md), [D18](d18-data-sources.md) and
[D63](d63-navigate-actions-and-page-parameters.md). The three widgets are
built and previewed in the design system. Chart and KPI page parameters are
_(planned for M13)_. The owner confirmed these points on issue #933 on
2026-10-21.

- **Detail:**
  - It shows one record read-only, either in the sections of a `form` resource
    or with every readable field.
  - The record id comes from `record`, which names a required `reference` page
    parameter whose target is the widget's entity.
  - It may have navigate and `startProcess` actions, as a form has.
- **Chart:**
  - It binds only a grouped data source with exactly one `groupBy` field. That
    field is its category column. Its measures are measures of that data
    source.
  - The kinds are `bar`, `line` and `pie`.
  - It reads one page of at most 100 groups, the largest page size, and says
    when more groups exist.
  - Its labels come from text resources, declared on the widget because a
    data source measure has no label. Its colours come from theme tokens.
- **KPI:**
  - It binds only a grouped data source with an empty `groupBy`, so it reads
    exactly one total row, and it shows one measure with a label.
  - An optional comparison is a second labelled value. The SPA computes no
    difference or percentage.
  - A click may navigate, with a navigate action of D63.
- **Parameters:** a chart and a KPI bind page parameters to data source
  parameters with the name map of a table in D63. Every data source parameter
  they leave unbound must be optional, because they have no filter bar.
- **No URL part:** a detail, a chart and a KPI read only page parameters and
  own no part of the URL. The limit of one table and one form per page in
  [D61](d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md)
  does not count them.
- **Chart library:** the chart library is Apache ECharts. No library name or
  option appears in configuration or page metadata, as
  [D58](d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md)
  requires of a specialised library. The SPA loads it lazily in its own
  chunk, as it loads React Flow.
- **Accessibility:** a chart offers its data as a table to screen readers. A
  KPI reads as its label and value.
- **Policies:** fields the user cannot read are left out, as on a table. A
  widget whose data source the user cannot run is left out.
- **Compile checks:** each new check is a diagnostic. It reuses an existing
  code where one fits, and a new code is assigned when the check is built.
- **Sample:** the dashboard and the ticket detail page of
  [D60](d60-the-service-desk-dashboard-ticket-detail-page-and-guided-form.md)
  are the sample that uses the three widgets.

_Why:_ a record, a grouped total and a series are the views that a real
application needs beside a table and a form. Binding the chart to a grouped
data source with one `groupBy` field gives one category per row, so the browser
never groups or pivots rows. One page of at most 100 groups keeps the chart
readable and avoids a paging control that would confuse readers. Reusing the
name map of D63 and the rule for unbound parameters keeps one way to bind a
page parameter. Page parameters let the compiler check the record of a detail
widget. Computing a change between two values would need rounding and sign
rules, and decimals arrive as source text, so the SPA shows both values and
leaves the reading to the user. A chart library is a detail of the SPA, so an
upgrade or a replacement never breaks a published application. The cost is a
library that the SPA did not have, which the lazy chunk keeps out of every page
that shows no chart.
