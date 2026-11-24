# Tables

This page says how every table of the platform is drawn and what its controls do. The rules that every control shares,
such as buttons and fields, are in [actions and controls](actions-and-controls.md), and [shared widgets](shared-widgets.md)
names the table that draws them.

## Tables

Every table of the platform follows one set of rules, because tables that each go their own way cannot
be kept right.

- **One table.** The `table` widget and the tables of Studio, the console and the
  users page are all drawn by one shared table, `AxisTable` in `web/src/platform/`, on Ant Design's
  table, and the shared-widgets check refuses an import of `Table` from `antd` anywhere but
  `AxisTable.tsx`, see [what the build checks](shared-widgets.md#what-the-build-checks).
- **Toolbar.** One toolbar holds for every table: the view controls, search and filters on the left,
  and Refresh, the secondary actions such as Import and Export, and the main action last on the right.
  Their places and looks are fixed, and whether a table has each depends on what it needs, so a short
  list has no search. An action on the page as a whole, such as New entity… on a kind's list, sits on the
  [page heading](studio-workspace.md#page-heading), not the toolbar. A `table` widget keeps the toolbar
  actions its author configures.
- **A title on the toolbar**. A table may have a `title` at the left end of its toolbar, before the view
  controls, such as an editable child collection's label, required mark and help.
- **Every control in one table.** `AxisTable` has every control below, and each view
  turns on what it needs. Search, filter, sort, page and columns live in the address, except in a dialog's
  table, which keeps them in the dialog. A view is only a
  filter kept there, so a link opens the same rows for anyone who follows it. A second table on a page puts a
  prefix of its own before each address name, as the console's Dead letters view does with `deadLetters.filter`.
- **Sort.** A column sorts from its header when its values have an order a person uses: names, people,
  statuses, dates and numbers. Long text, files, lists and actions do not. A header only sorts, as there is
  one place to filter. A table that holds all its rows sorts them by the value its column gives through
  `sortValue`, and a table paged on the server sends the sort to the server. A new sort starts at page 1.
- **Search.** A column is searchable when it says so, through `searchText`, as a field marked `searchable` is
  in the record API, and search looks in all of them. `AxisTable` draws the box, which says Search, and a
  table that holds all its rows searches them by itself, while a table paged on the server sends the text to
  the server. The text applies on Enter, on the search button and when the box is cleared, never on each
  key, and a new search starts at page 1. Search, filter, sort, page and page size live in the address. A table in a
  dialog, such as the [lookup](form-widget.md#lookup), keeps them in the dialog instead.
- **Filter.** Filter is an icon button that carries the count of conditions in force, and
  opens a builder of conditions in groups of All or Any, with no limit on depth. A column can be filtered
  on when it gives its kind of value through `filter`, and conditions follow that kind, text, enum, number
  or date, each with empty and not empty. Apply puts it in force, and a condition with no value is left
  out. A chip stands for each condition and group, with Clear all. A table that holds all its rows filters
  them itself. A table paged on the server sends its filter to the server as the record API's `filter`. The
  filter lives in the address as `filter` except in a dialog's table, in the shape of
  [the record API's filter](record-api.md#paging-and-sorting).
  A second table on a page keeps its view under a prefix, as `fields.filter`. In a table that edits a draft, a filter
  only hides rows, and an edit to a hidden row stays in the draft. The table's `keep` option names the rows the
  filter never hides, such as a row added since the last save, so the author always sees what they just added. Only
  a table that holds all its rows reads `keep`, and only for the filter. A column whose header is not plain text
  names itself in the builder and its chips through `filter`'s `title`, and without one they show the header text.
- **Filter on every table.** Each column that shows a row's value can be filtered on, in a table with a view and
  in a data source table. Some columns and tables have no Filter, as the lines below name.
  - **Kinds in a `table` widget.** The widget leaves out boolean, reference, file and date-time columns, and a
    measure column of a grouped data source. [Its filter](table-widget.md#filter) gives the reasons.
  - **A list column.** A list column can have no filter, such as the Steps column of a cell job table.
  - **A column of a table with a view.** The Event column of the webhook deliveries and the Task column of the
    task inbox have no Filter. [The task inbox](task-inbox-widget.md) names the columns it filters on. The Request
    column of the [AI requests table](ai-authoring-in-studio.md#the-request-panel) has no Filter either.
  - **A table with no Filter.** A child collection on a [form or detail](form-widget.md#child-collections) and a
    list of objects in a [Studio form](studio-form-editors.md) have none. So do the publishing releases table of the
    [operator console](operator-console.md#views), and the Hosts and Usage tables of the
    [users page](platform-pages.md#export-data-and-usage). The DNS records of a host request dialog have none too.
    A [reference lookup](form-widget.md#lookup) on a target with no display field has none. The cell console's job
    table, on a tenant's view and in Create a tenant, has none, see [the cell console](cell-console.md).
- **Columns control.** A table whose view turns it on has Columns, an icon button after Search and Filter. It
  opens a dialog that shows, hides and orders the columns, with Reset, Cancel and Apply. Reset sets the dialog
  back to every column in its first order, and nothing takes effect until Apply. The identifying column, the
  first, stays first and always shows. A hidden column is still searched and filtered on. The choice lives in
  the address as `columns`, the keys of the shown columns in order, and is left out when every column shows in
  its first order. A change of columns keeps the page.
- **Each person's columns.** The choice of columns is kept on the server for the person and the
  table. Apply keeps it, and Reset followed by Apply forgets it. A table opened with no `columns` in its address puts
  the kept choice into the address, without a history entry. An address with `columns` always wins, and a kept key
  of a column the table no longer has is dropped.
- **Saved views.** A person saves a table's search, filter and sort under a name, and the view is kept on the server
  for that person and table. Views is an icon button after Columns, and it shows once the person's views have
  loaded. It opens a dialog that lists the person's views of the table by name, in the page language's collation,
  with a Name field and Save view. A view's name opens it. A view is renamed in its own line, overwritten and
  deleted in its own line after a question, so the dialog opens no dialog of its own.
- **Opening a view.** It writes the view's search, filter and sort into the address at page 1, with a history entry,
  so Back returns. The columns, the page size and the data source values stay. An empty part of the view clears that
  part. The values are checked as the address's are, so a filter that names a field the table no longer has is
  dropped. No view applies by itself, and there is no default view.
- **A refused save.** A name that is empty or that another of the person's views has, ignoring letter case, is
  refused before anything is sent. Any other save the server refuses leaves the dialog open with "The view could not
  be saved.", and the address and the list stay as they were.
- **Overwrite a view.** Overwrite replaces a view's search, filter and sort with the address's current ones, after
  the question "Overwrite the view {name} with the current search, filter and sort?" in the view's own line. The
  view keeps its name and its place in the list. The columns, the page size and the data source values stay out, as
  they do on Save view. After the overwrite the line shows the view again and the dialog stays open. A write the
  server refuses leaves the dialog open with "The view could not be overwritten.", and the list stays as it was.
- **Views in the saved items dialog.** A table's Views opens the one
  [dialog that keeps saved items](actions-and-controls.md#fields). Each line is one split button, Overwrite with Rename and Delete in the
  menu of its joined caret.
- **Which tables keep a choice.** A table whose view `useTableView` keeps in the address, and that
  has a key, keeps its columns and views on the server. A table in a dialog keeps its view in the dialog and keeps
  nothing. The cell console's tables pass no key, because its host has no tenant database. Studio's page canvas
  passes no key either. The key format and the
  routes are in [table views and columns](frontend.md#table-views-and-columns).
- **Columns.** A column whose values have a fixed form, such as a number, a date, a date and time, a
  status tag or the row's icon actions, is as wide as its content and never wraps. The columns of free
  text share the width left in equal parts, and their text wraps. A table with no free text gives the
  width left to its last column. Every cell keeps `spacing-lg` on each side. Numbers sit on the right
  in tabular figures.
- **Rows.** Every row and the header are of the `tableRow` height, whatever they hold, icon buttons
  included: `spacing-xs` above and below a `smallControl` line. A row of more lines grows and reads
  from the same first line.
- **Compact tables**. A table in a side panel is `compact`, with `spacing-sm` on each side of a cell.
- **A table with no header row**. A table of names and values is `headless`, with no header row, see
  [text](actions-and-controls.md#text).
- **Row actions.** An action on one row sits on the row. One whose meaning the platform knows, such as
  open, delete or activate a release, is an icon button, and one an author configured keeps its text.
  From three actions on, or at narrow width from two on, they fold into one "…" menu of text entries. A table
  set up for three or more actions folds each row that has two or more. A row with one action shows it as a
  button, even at narrow width. A known Remove or Delete is the trash icon in `dangerText`, named by a tooltip.
  An action that opens another site, such as Open in Studio, is a text button with a real address, so Ctrl+click
  opens it in a new tab. See [menus](actions-and-controls.md#menus).
- **A known action is its icon wherever its row is**. A row's Remove or Delete that is a button is the
  trash icon in `dangerText`, named by a tooltip, in a list's row as in a table's.
- **A column of row controls has no header text** _(planned, #5087)_. The column of a row's "…" or icon buttons, and the
  column of drag handles, show no header words, as their icons say what they are. Each header still has a name for
  screen readers alone, such as Actions or Drag to reorder, because a header with no name fails the accessibility
  check. The controls show on every row, never only on hover, and each has its name as a tooltip.
- **Opening a row.** A row opens from its identifying cell, a real link, so a row
  has no Edit icon, see [opening things](actions-and-controls.md#opening-things).
- **An empty table keeps its header**, with the
  [empty state](actions-and-controls.md#showing-what-is-empty) in its body.
- **Paging.** A long table pages. A table that edits a draft, such as Studio's seed grid and the entity designer's
  Fields, turns paging off with the view's `paging` option and shows every row with no pager, because a pager would
  move the row an author is editing. Studio's policy matrix, the tables of a module contract, the operator console's Tenant
  table, the two licences lists, the account page's Authenticators and Sessions, the users page's Connected
  apps, the tables of a process instance's page, apart from its History timeline, and the History of an AI request's
  page also show every row with no pager. Only a table that holds all its rows reads it.
- **How many.** A paged table says what it shows of how many, as "1–20 of 143", with the numbers in the
  language's format.
- **Too little room.** A table measures its own width, as it changes. Its text columns
  narrow to 160 px first. Then columns move under their row one at a time, from the right or by a column's
  `priority`, `low` first and `high` last, as "Title: value" on the row's own surface, with no line between
  the row and them. The identifying column never moves. A moved cell with no value is left out, while a
  column that fits still shows its dash. Only when what stays does not fit does the table scroll sideways,
  with the first column fixed. So the data stays in sight, and every table gains with no setting, as SAP
  Fiori's pop-in has it.
- **Bulk actions**. Selecting rows is for bulk actions alone: a table shows checkboxes only when it has a
  bulk action. Selecting rows of the page shows an "N selected" bar above the rows that holds the bulk
  actions, and running one clears the selection. An action on one row stays on the row.
- **Selection** _(planned for M42)_. A selection holds rows of the current page only. A new page, sort, search or filter
  clears it, so a bulk action runs only on rows the person can see on the screen.
- **Bulk results** _(planned for M42)_. After a bulk action, a summary above the table says how many rows succeeded.
  Each row that failed is listed by its identifying value and its reason. The list sits in an `aria-live` region, and
  a full success shows only the summary.
- **Inline edit** _(planned for M42)_. An editable cell shows its input on click or Enter. It saves on Enter or when it
  loses focus, and it cancels on Escape. A refused save keeps the old value with the reason. A cell that cannot be
  edited shows its value as text, with no input.
- **The growth test**. A table whose rows grow as people use the application, such as
  records, requests or releases, has the full standard toolbar above. A table whose rows do not grow may
  show fewer of its controls, as the toolbar bullet says.
- **Named exceptions**. A table that is part of one thing is the exception to the growth
  test. Its rows belong to that one thing, such as the lines of a child collection on a form, so a search or
  a filter of its own would split them from it.
- **No control of its own**. A table gets no control for one view. A view is a filter
  kept in the address, so the page offers it beside the table and a link opens the same rows.
- **View controls on one line** _(planned for M35)_. The view controls sit on one line beside the search box,
  which keeps its own width.
- **Group rows.** A row that heads a group shows its heading and leaves its other cells blank, with no dash,
  as `AxisTable`'s `blankRow` draws them.
- **Folding group rows**. A row with children folds, closed at first, behind a subtle `AxisButton`
  with `Chevron`, named "Show the rows in it" or "Hide the rows in it". The fold sits in the
  first column's padding, every row's words start where the heading does, and each level of rows starts one fold
  further in. A table that groups rows uses this, and never a list inside a cell.
- **A row's details**. A row with more to show than its columns, such as a call's attempt, folds open to
  its details from a fold in its first cell, drawn as a group row's fold, named "Show the details" or "Hide the details"
  while open, with no column of its own. `AxisTable`'s `details` and its group rows open a row one way.
- **Reordering by dragging** _(planned, #5049)_. A table whose rows have an order that means something takes `reorder` in
  `AxisTable`, on `@dnd-kit/sortable`. Each row starts with a handle named Drag to reorder, and the rows between make
  room while a row is dragged. The keyboard uses the same handle: Space picks the row up, the arrow keys move it, Space
  drops it and Escape cancels. Move up and Move down stay in the row's menu, as the way to move a row without a drag.
  While a search or a filter hides rows, the handles are off, with the tooltip "Clear the search and the filter to
  reorder", as the rows shown are then not the list's order. What a screen reader hears names the item and its new
  place, in the page's language. A form's child collection rows take the same.

The reasons for the table standard are in [D151](../decisions/d151-tables-whose-rows-grow-have-the-full-toolbar-and-a-view-is-a-filter-kept-in-the-address.md),
and the reasons for each person's columns and saved views are in [D153](../decisions/d153-a-persons-table-views-and-columns-are-kept-on-the-server-and-the-address-still-wins.md),
and the reasons for bulk actions, inline edit and the column chooser of an application's table are in [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).

