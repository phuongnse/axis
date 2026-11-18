# Container widgets

The container widgets `columns`, `tabs` and `section` hold other widgets, so a page lays out its widgets without a
template of its own. This page says how the SPA draws them. The rest of the frontend is in [frontend](frontend.md), and
the reasons for widget trees are in
[D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).

## The tree

A page draws its whole widget tree with `web/src/platform/WidgetTree.tsx`. A container shows its widgets in file order,
and containers can nest. Each widget that shows something keeps its own loading, empty and error states, so a widget
that fails to load does not affect the widgets next to it.

## Columns

A `columns` widget shows its columns side by side, each as wide as its `width` in twelfths. At
[narrow width](frontend.md#narrow-width) the columns stack in file order. The columns sit `spacing-md` apart, with no
rule between them, and a columns widget has no heading, so it is not a part.

## Tabs

A `tabs` widget shows one header per tab with its label, and keeps the selected tab in the URL, so a reload, a shared
link and the back button keep it. The tab bar sits straight on `surface`, with its line in `borderSubtle`. A tabs
widget has no heading, so it is not a part, and no rule sits above it.

- **The parameter.** The selected tab is kept in the URL as `?tab=<n>`, counted from 1. The second `tabs` widget of a
  page, in document order, uses `tab2`, the third `tab3`, and so on. A `tabs` widget inside a tab counts whether or not
  its tab is selected.
- **The default.** The first tab is the default and is left out of the URL. A value that is not a tab position is
  ignored and the first tab shows, without a change to the URL.
- **History.** Each click on a tab adds a history entry, so the back button returns to the previous tab.
- **Positions.** The position comes from the metadata the user is served. A policy can remove a tab, so a shared link
  can open another tab.
- **A page parameter named `tab`** clashes with the tab state.

## Sections

_(planned)_

A `section` is a part of the page, as [shell layout](shell.md#layout) sets out. Its title is a heading drawn at the
`heading5` size, straight on `surface`, with its widgets under it, and no panel, border or Ant Design `Collapse`. The
section is a region named by its title. A `borderSubtle` rule sits above a section that follows another widget in the
same list, and none above the first.

A section that is not `collapsed` always shows its widgets and has no fold. A section marked `collapsed` starts closed,
and its fold sits before its title: an icon button drawn with `FoldIcon`, named "Show section {title}" or "Hide section
{title}", with the texts `shell.section.show` and `shell.section.hide` ("Hiện phần {title}" and "Ẩn phần {title}" in
Vietnamese). The button has `aria-expanded` and `aria-controls`. A closed section draws none of its widgets. The open
state is not in the URL, so a reload resets it to the file's `collapsed` value.

## Lazy loading

Only the selected tab and open sections draw their widgets, so a page asks only for what it shows. A table in a hidden
tab or a closed section asks for no rows until it is shown.

## Routes

The route checks of a page, such as `/new`, `/{id}` and the task and instance [addresses](frontend.md#routing), find
their form, task inbox, process inspector or detail anywhere in the tree. The history panel's "Open process" link does
the same.
