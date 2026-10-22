# D61. Widget trees in columns, tabs and sections, and pages at phone width

Status: **Agreed**

A page holds a tree of widgets, laid out in columns, tabs and sections, and it
adapts to a phone. The owner agreed these points on #930 on 2026-10-21. They
build on [D15](d15-presentation-model.md), which makes layout a matter of
container widgets, and they change the page format in place, see
[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md).
The [frontend reference](../reference/frontend.md) and the
[resource files reference](../reference/resource-files.md) show the shapes. All
of it is planned for M12.

- **Widget tree:** the `widgets` of a page holds one or more widgets. The
  format changes in place, `formatVersion` stays 1 and today's pages stay
  valid.
- **Containers:** three container widgets hold other widgets. `columns` has
  columns, each with a width in twelfths and its `widgets`. `tabs` has tabs,
  each with a label and its `widgets`. `section` has a title, an optional
  collapsed state and its `widgets`. The widths of one `columns` widget are
  whole numbers from 1 to 12 and add up to 12.
- **Depth:** containers nest to at most 4 levels. The page's own `widgets` list
  is level 1. The compiler checks it.
- **URL state:** a page holds at most one `table` of any kind, and at most one
  of `form`, `taskInbox` and `processInspector`. So `?page=&pageSize=&sort=`
  and the data source parameters belong to the one table, and `/new`, `/{id}`,
  `/{taskId}` and `/{instanceId}` belong to the one form, task inbox or
  process inspector. Every part of the URL has one owner.
- **Policies:** a widget the user cannot read is left out of the page
  metadata. A column or tab whose widgets are all left out is left out, and the
  other columns keep their widths. A container left with no widget is left
  out. A page is readable when at least one of its widgets is.
- **Phone width:** below a fixed 768 px breakpoint for the whole platform,
  columns stack in file order, tab headers scroll, tables scroll sideways
  inside their widget and the site shell's navigation collapses into a menu.
  There is no per-widget setting until a sample needs one.
- **Studio:** the page editor shows the tree, with add, remove, reorder and
  move into a container, and one panel per container type, as
  [D53](d53-typed-editors-and-the-process-graph-in-studio.md) planned for M12.

Diagnostic codes for the new checks change with the issues that build them.

_Why:_ real pages need more than one widget, and containers keep layout in
configuration, not in code. Limiting depth and the number of tables and forms
keeps the URL state, the routes and the compile-time checks simple. A fixed
breakpoint gives every page one phone behaviour without more settings. The
cost is that a page cannot show two tables or two forms. A per-widget setting
for stacking can be added when a sample needs one.
