# Container widgets

Detailed reference for how the SPA draws the container widgets `columns`, `tabs` and `section`. The
conventions of [architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are
not built yet, and Dn refers to [decisions.md](../decisions.md). The rest of the frontend is in
[frontend.md](frontend.md). The containers are agreed in
[D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md).

- **The tree.** A page draws its whole widget tree with `web/src/platform/WidgetTree.tsx`. A
  container shows its widgets in file order, and containers can nest. Each widget that shows
  something keeps its own loading, empty and error states, so a widget that fails to load does not
  affect the widgets next to it.
- **Columns.** A `columns` widget shows its columns side by side. Each column is as wide as its
  `width` in twelfths. At narrow width, below the platform's one breakpoint, the columns stack in
  file order. The breakpoint is 992 px, see
  [frontend](frontend.md) and [D96](../decisions/d96-rules-from-the-design-system-review.md).
- **Tabs.** A `tabs` widget shows one header per tab with its label.
  - The selected tab is kept in the URL as `?tab=<n>`, counted from 1.
  - The second `tabs` widget of a page, in document order, uses `tab2`, the third `tab3`, and so on.
    A `tabs` widget inside a tab counts whether or not its tab is selected.
  - The first tab is the default and is left out of the URL. A value that is not a tab position is
    ignored and the first tab shows, without a change to the URL.
  - Each click on a tab adds a history entry, so the back button returns to the previous tab.
  - The position comes from the metadata the user is served. A policy can remove a tab, so a shared
    link can open another tab. A page parameter named `tab` clashes with the tab state.
- **Sections.** A `section` shows its title above its widgets, as a region named by the title. A
  section marked `collapsed` starts closed, and the user can open and close it. The open state is
  not in the URL, so a reload resets it to the file's `collapsed` value.
- **Lazy loading.** Only the selected tab and open sections draw their widgets. A table in a hidden
  tab or a closed section asks for no rows until it is shown.
- **Routes.** The route checks of a page, such as `/new`, `/{id}` and the task and instance
  addresses, find their form, task inbox, process inspector or detail anywhere in the tree. The
  history panel's "Open instance" link does the same.
