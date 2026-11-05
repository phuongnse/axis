# Actions and controls

Detailed reference for how the SPA draws buttons, menus and form fields, the same in every widget, page, dialog and
in the identity provider's pages. The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md). The tokens named here are in
[design tokens and previews](frontend.md).

## Buttons

A button's level follows its role, so the same kind of action always has the same style. Its place decides the rest.

- _Finish_: solid primary. The one action that finishes a task, such as Save or Next, one per screen.
- _Main_: outlined in `primaryText`, and a text button in an action row. The main action of a region: a table's first toolbar action that is shown, such
  as New request, Add row of a child collection, and a `startProcess` action on a form or a detail.
- _Secondary_: the default grey outline, and a text button in an action row, such as Cancel and Import.
- _Icon_: a text button with only an icon, for small actions such as Refresh. Choose and Clear of a `reference` field
  are bordered and joined.
- _Plain_: a borderless text button in the text colour, for header controls such as the account button.
- _Destructive_: in `dangerText` at any level, outlined or a text button in an action row, such as Remove row and Delete.
- Ant Design writes the text of text, outlined and danger buttons in the fill colours `primary` and `danger`, which
  fail contrast on a dark surface, so Axis writes them in `primaryText` and `dangerText`. One shared Axis button,
  `AxisButton` in `web/src/platform/`, takes the role, and each widget names a role, never an Ant Design button type.
  The table's toolbar, its icon buttons, the reference, set and file field buttons, the form widget's footer, the import
  dialog and Studio's publishing row, editor row and field questions use it.
- **Beside a solid button every other action is a text button**. In a dialog, a form's footer
  and a band, Cancel and every secondary, main or destructive action next to the finish button is a text button in its
  own colour, so the finish button is the one filled shape. Every Ant Design dialog draws Cancel this way through the
  theme, and `AxisButton`'s action row, `AxisActionRow`, does it for the form widget's footer when it draws Save or Next, the import
  dialog, Studio's publishing and editor rows and its field questions. A main action with no solid button beside
  it, such as a table's first toolbar action, stays outlined.
- **Text or an icon, never both**. An action shows its text or its icon. An icon goes with text
  only where it does what text cannot: navigation that collapses to icons, a status or a severity, and the mark that
  identifies an application or a site.
- **An action that asks for more ends in an ellipsis**, such as New application…, New resource…,
  Publish…, Import…, Invite…, Reassign… and Choose…, so the person knows a dialog follows. The dialog's own button
  keeps the bare verb, such as Invite.
- **Icon-only buttons.** Only for actions repeated on every row, or where space is tight. Main toolbar actions keep
  their text. Every icon-only button shows its name as a tooltip on hover and on keyboard focus, and has the same text
  as its accessible name, from the text resources. Configuration names no icon, except a site's navigation entry,
  which names one from Axis's own list, see [navigation icons](shell.md#navigation-icons). So the platform gives an
  icon only to an action whose meaning it knows. At narrow width row actions fold into a menu of text entries, so touch
  users lose nothing.
- **A tooltip goes with the pointer** _(planned for M31)_. A click on an icon-only button closes its tooltip until the
  pointer comes back, and focus that a click or a closing dialog gives the button opens none. Keyboard focus still
  shows it.

## Menus

- **Selected and current states.** The selected tab, the active menu entry and the current page of a pagination use
  `primaryText`. Destructive menu entries use `dangerText`. A menu that marks a choice, such as the open application
  in Studio's application menu, draws it in `primaryText` on the selected tint, never in Ant Design's selected colour,
  which is hard to read in dark mode _(planned for M31)_.
- **A text trigger ends in a caret**. A button with text that opens a menu, such as the account
  button and Studio's application switcher, ends in a caret. An icon-only trigger has none. `AxisButton`'s `caret`
  option draws it.
- **Actions come after the choices** _(planned for M31)_. In a menu that holds both, such as the application menu,
  the choices come first and an action, such as New application…, follows them.

## Fields

These rules hold for every Ant Design form in the SPA and in the identity provider's pages, through one setting on the
shared `ConfigProvider`, so no widget keeps its own copy.

- **A field's error keeps the gap.** The message sits 4 px under its control, in 14/20 text, and the field keeps its
  own gap below the message: `spacing-lg` in a form, none in a table cell or an inline form.
- **A box sits on its label's first line.** A checkbox or a radio keeps its box level with the first line of its
  label, so a label that wraps leaves the box at the top. A one-line label looks as before.
- **An error border keeps 3:1.** An input, number input, select, date picker, mentions, cascader or tree select with
  an error, and an unticked checkbox or radio in a field with an error, take `dangerText` for their border, the colour
  of the field's message. The `danger` fill stays for fills such as a danger button, whose white text needs it.
- **A label is above its input, everywhere** _(planned for M31)_, as the form widget has it, never beside it.

## Tab bars

A tab bar sits `spacing-md` under what precedes it _(planned for M31)_. The theme starts a tab's label at the top of its
bar, with the underline 12 px under the label, for every tab bar, because Ant Design's padding above the label reads as
a gap too wide.

## Text

These rules hold for every word the SPA shows
_(planned for M31)_.

- **No technical name.** What the UI shows is a text, never an internal code, a resource's file or a key: a label,
  else a name an author gave, see [names](studio-workspace.md#names).
- **No middle dot as a separator.** Peers join as words do, with a comma, as in "2 errors, 1 warning". A state is a
  sentence, as in "Release 7 is live". Facts of different kinds take a column or a line of their own, such as when a
  release was published and by whom. The "·" glues facts of different kinds into one line with no order, and a screen
  reader may read it out.
- **No value is a dash.** A table cell with no value shows the muted em dash, a form or a detail "Not set", see the
  empty values of [frontend](frontend.md). Never nothing, which cannot tell "not given" from "not loaded", and never
  N/A, which says "does not apply" and does not translate.

## Tables

Every table of the platform, the `table` widget, Studio's, the console's and the users page's, is drawn by one shared
table, `AxisTable` in `web/src/platform/`, on Ant Design's table _(planned for M31)_. Tables that each go their own
way cannot be kept right, so a lint rule refuses `Table` from `antd` anywhere but `AxisTable.tsx`.

- **Toolbar.** One toolbar holds for every table: the view controls, search and filters, on the left; Refresh, the
  secondary actions such as Import and Export, and the main action last, on the right. Their places and looks are
  fixed; whether a table has each depends on what it needs, so a short list has no search.
- **Columns.** A column whose values have a fixed form, such as a number, a date, a date and time, a status tag or the
  row's icon actions, is as wide as its content and never wraps. The columns of free text share the width left in
  equal parts, and their text wraps at 320 px. A table with no free text gives the width left to its last column.
  Every cell keeps `spacing-lg` on each side. Numbers sit on the right in tabular figures.
- **Rows.** Every row and the header are the `tableRow` size high, 40 px, whatever they hold, icon buttons included:
  `spacing-xs` above and below a `smallControl` line. A row of more lines grows and reads from the same first line.
- **Row actions.** An action on one row sits on the row. One whose meaning the platform knows, such as open, delete
  or activate a release, is an icon button; one an author configured keeps its text. From three actions on, or below
  992 px, they fold into one "…" menu of text entries.
- **Opening a row** _(planned for M31)_. A row opens from its identifying cell, a real link, so a row has no Edit icon,
  see [opening things](#opening-things).
- **An empty table keeps its header** _(planned for M31)_, with the [empty state](#showing-what-is-empty) in its body.
- **Paging and scrolling.** A long table pages, and one narrower than its columns' content scrolls sideways rather
  than squeezing them.
- **Bulk actions** _(Proposed)_. Selecting rows is for bulk actions alone: a table shows checkboxes only when it has a
  bulk action, and a selection shows an "N selected" bar that holds them.
  [D59](../decisions/d59-table-row-and-toolbar-actions.md) left bulk actions for later.

## Opening things

Three levels, chosen by what the person does, never by taste _(planned for M31)_.

- **Opening a record is a page.** Every thing with an identity of its own, such as a record, a Studio resource, a
  release, a user or a process instance, opens from its table's identifying cell, a real link, as a page with its own
  address, so a link can be shared, reloaded, gone back to and opened in a new tab, and the page has room to grow.
- **A side panel goes with a page and never opens a record.** It is for what accompanies the page: the problems panel,
  filters or help. A quick look would be an addition to the page, with the same content and Open full page, never in its
  place, and Axis has none for now.
- **A modal dialog is for a short task that must end before going on:** confirming, such as Activate… or Delete…, asking
  for a little, such as a reason, a name or an invitation, or choosing one thing. Never a record's detail or a long form,
  and never a dialog from a dialog. What needs much scrolling, or to be come back to, is a page.

## Dialogs

These rules hold for every dialog, through the shared `ConfigProvider` _(planned for M31)_.

- **Three sizes, by what a dialog holds.** `small`, 400 px, for a confirmation or one question; `medium`, 560 px, for a
  short form of two to six fields; and `large`, 800 px, for content that needs the width, such as a list to choose from
  or an import's preview. There is no larger size: what needs more is a page. Below 992 px every dialog takes the width
  less the page margin. The size is a token that a dialog names, never a width of its own.
- **Its height.** A dialog is as tall as its content, at most the window less its margins. Its body scrolls while its
  title and buttons stay.
- **Its body scrolls at its edge.** The body reaches the frame's edges and keeps the padding inside it, so the thin
  scrollbar runs at the dialog's right edge and a field's focus ring has room at its sides.
- **It draws on `surface`,** as panels do, so everything inside sits on the colour it was made for, and the mask behind
  sets it apart from the page.

## Showing what is empty

Information always shows rather than being hidden
_(planned for M31)_.

- **A field the object always has always shows.** Empty, it reads "Not set" on a form or a detail and the muted dash in
  a table cell, see [text](#text).
- **A list that is a standing part of the object always shows.** Empty, it says in one line what that means.
- **Hide only what does not apply or is not allowed.** A part that does not apply to the object's kind or state, or a
  field the person may not read, is left out.
- **Loading and failing are never empty.** A part that is still loading or failed to load says so, never Not set.
- **One empty state.** `EmptyState` draws every empty table and list: Ant Design's simple picture and one line in
  `textMuted`. The line is "No data yet." unless the emptiness says something the reader needs. Ant Design's own empty
  text fails 4.5:1.

## List order

Three rules, chosen by what a list is
_(planned for M31)_.

- **An order the author gives keeps it.** Process steps, wizard steps, a table's columns, a form's fields and navigation
  stay as written, and the author moves them. Sorting them would change their meaning.
- **Records in time are newest first.** Releases, activations and history.
- **A set sorts by name.** Modules, packages, relaxed points, Uses and Used by and the changes sort by the name a person
  reads, with the page language's collation, so Đ follows D in Vietnamese. A list that mixes kinds groups them first in
  the sidebar's order, then by name. Relaxed points sort by module, then by point.
- **Sorting is for display only.** The file keeps its order, so adding a module adds one line to its diff. Studio's small
  tables have no sort by column, as their order is already the one to expect. The `table` widget keeps its own rules.

## Undo rather than confirm

These rules follow from what a person can take back _(planned for M31)_.

- **What can be taken back is undone, not confirmed.** Removing a row changes only the draft, so it asks nothing. A
  notice names it, such as "Organization removed.", with Undo, for ten seconds.
- **Every editor keeps a history.** Undo and Redo, icons in the
  [save bar](studio-resource-form.md#save-bar) with Ctrl+Z and Ctrl+Shift+Z, step through every change of the draft, a
  label's texts among them, on every resource kind.
- **What cannot be taken back is confirmed.** Discard drops every unsaved change at once and no Undo brings them back, so
  a small dialog asks "Discard all unsaved changes?" with a red Discard. Deleting a resource keeps its dialog.
