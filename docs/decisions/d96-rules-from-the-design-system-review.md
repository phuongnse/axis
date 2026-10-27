# D96. Rules from the design system review

Status: **Agreed**

The owner reviewed every widget preview of the Design System artifact, one
change at a time, and agreed these rules on 2026-10-28. The reference docs
linked from each point hold the detail. None of it is built yet: the reference
docs mark it _(planned)_, and one issue builds it.

- **Fields and forms:** a required field has a `*` after its label in
  `dangerText` and `aria-required` on its input. A field label is weight 500
  in `textMuted` wherever a field is labelled. An entity field may have `help`,
  a short text a form shows between the label and the input. A validation
  message sits 4px under its input, with a whole field gap before the next
  field. A read-only field shows plain text, not an input. A `form` resource
  field may have a `width` in twelfths, so fields sit in columns, and the
  wizard steps bar stays on the left and shows only numbers from seven steps.
  See the [form widget](../reference/form-widget.md) and
  [resource files](../reference/resource-files.md).
- **Shown values:** one date and date-time format per locale, to the minute,
  in inputs, read-only values, table cells and details. Numbers show in the
  locale's digit grouping wherever they are read. An empty value reads "Not
  set" where fields are read one by one, and a dash where values are compared,
  both in muted italics. No widget shows an identifier. See
  [frontend](../reference/frontend.md).
- **Detail:** a description list, each label above its value, with the form
  sections' widths. See the
  [detail widget](../reference/detail-chart-kpi-widgets.md).
- **Table:** the first `navigate` row action opens a row from its identifying
  column, which comes first and is a link. A delete row action is a red trash
  icon, and three or more row actions fold into one "…" menu. The toolbar has
  the view controls on the left and Refresh, the secondary actions and the
  main action on the right. Long text wraps at 320px. See
  [frontend](../reference/frontend.md).
- **Buttons and icons:** five button levels by role, finish, main, secondary,
  icon and destructive, and one shared Axis button that takes the role.
  Icon-only buttons only where an action repeats on every row or space is
  tight, each with a tooltip and an accessible name. Selected and current
  states use `primaryText`. See [frontend](../reference/frontend.md).
- **KPI:** the label above, the measure large below it, and a comparison that
  is a reference for the same measure. See the
  [KPI widget](../reference/detail-chart-kpi-widgets.md).
- **Process inspector:** the first column names the instance's record and
  opens it, the release shows by its number, and the list API adds
  `subjectLabel` and `releaseLabel`. See the
  [process inspector widget](../reference/process-inspector-widget.md) and
  [process inspection](../reference/process-inspection.md#instance-list).
- **One breakpoint:** the platform has one layout breakpoint at 992 px, Ant
  Design's `lg`. Below it is narrow width. It replaces the 768 px phone width
  of [D61](d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md).
  See [frontend](../reference/frontend.md).
- **Design system previews:** every card is laid out at 992 px, the states of
  the field, form, table and KPI previews change, and the cards come in six
  groups in a fixed order. This replaces those rows of the states table of
  [D95](d95-widget-previews-show-the-states-a-design-change-touches.md). See
  [widget previews](../development.md#widget-previews).
- **Design reviews:** a review prototypes each change in the real widgets on a
  local branch, the artifact may run ahead of the repository while it is open,
  and it ends in one docs change and one issue. See
  [design changes](../delivery.md#design-changes).

Open points: a number input that takes the locale's decimal comma, what a
widget shows where it has only an identifier today, and how the instances of a
process without a subject are named beyond a number within their process.

_Why:_ the previews showed the same action drawn in different styles, values
that read differently in a form, a table and a detail, inputs that looked
editable but were not, and text that failed contrast in dark mode. One rule
for each kind of thing makes every application look and read alike without
per-application styling. A single breakpoint at the smallest desktop keeps
the desktop layout on every desktop and the narrow layout everywhere else.
The cost is one larger change to the shared widgets, and more states for each
preview to keep.
