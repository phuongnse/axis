# Actions and controls

This page says how the SPA draws buttons, menus, form fields, tables and dialogs, and how its screens
word, open, order and empty what they show. The rules are the same in every widget, page and dialog,
and in the identity provider's pages, so a person learns a control once. The tokens named here are in
[design tokens and previews](frontend.md#design-tokens-and-previews), and which shared component draws each control is in
[shared widgets](shared-widgets.md).

## Buttons

A button's style follows its role, so the same kind of action always looks the same, and its place
decides the rest. One shared button, `AxisButton` in `web/src/platform/`, takes the role, and each
widget names a role, never an Ant Design button type.

| Role        | Look                                        | Used for                                                                                                                                                                         |
| ----------- | ------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Finish      | Solid primary                               | The one action that finishes a task, such as Save or Next, one per screen                                                                                                        |
| Main        | Outlined in `primaryText`                   | The main action of a region: a table's first toolbar action that is shown, such as New request, Add row of a child collection, and a `startProcess` action on a form or a detail |
| Secondary   | The default grey outline                    | Other actions, such as Cancel and Import                                                                                                                                         |
| Icon        | A text button with only an icon             | Small actions such as Refresh. Choose and Clear of a `reference` field are bordered and joined                                                                                   |
| Plain       | A borderless text button in the text colour | Header controls, such as the account button                                                                                                                                      |
| Destructive | `dangerText` at any level                   | Actions that remove, such as Remove row and Delete                                                                                                                               |

- **Text colours, not fill colours.** Ant Design writes the text of text, outlined and danger buttons
  in the fill colours `primary` and `danger`, which fail contrast on a dark surface, so Axis writes
  them in `primaryText` and `dangerText`.
- **Beside a solid button every other action is a text button.** In a dialog, a form's footer and a
  band, Cancel and every secondary, main or destructive action next to the finish button is a text
  button in its own colour, so the finish button is the one filled shape. The theme draws every Ant
  Design dialog's Cancel this way, and `AxisActionRow`, the action row of `AxisButton`, draws the
  other rows. A main action with no solid button beside it, such as a table's first toolbar action,
  stays outlined.
- **Text or an icon, never both.** An action shows its text or its icon. An icon goes with text only
  where it does what text cannot: navigation that collapses to icons, a status or a severity, and the
  mark that identifies an application or a site.
- **An action that asks for more ends in an ellipsis,** such as New application…, New resource…,
  Publish…, Import…, Invite…, Reassign… and Choose…, so the person knows a dialog follows. The
  dialog's own button keeps the bare verb, such as Invite.
- **Icon-only buttons** are only for actions repeated on every row, or where space is tight, and main
  toolbar actions keep their text. Every icon-only button shows its name as a tooltip on hover and on
  keyboard focus, and has the same text, from the text resources, as its accessible name. At narrow
  width row actions fold into a menu of text entries, so touch users lose nothing.
- **The platform picks the icons.** Configuration names no icon, except a site's navigation entry,
  which names one from Axis's own list, see [navigation icons](shell.md#navigation-icons). So the
  platform gives an icon only to an action whose meaning it knows.
- **A tooltip names the control,** with no "…", as Filter and Columns, because the "…" belongs to a button
  whose words show and that opens a dialog, such as New entity….
- **A tooltip goes with the pointer**. A click on an icon-only button closes its
  tooltip until the pointer comes back, and focus that a click or a closing dialog gives the button
  opens none. Keyboard focus still shows it.

## Menus

Menus mark the current choice the same way everywhere, and keep their actions apart from their
choices.

- **Selected and current states.** The selected tab, the active menu entry and the current page of a
  pagination use `primaryText`, and destructive menu entries use `dangerText`.
- **A marked choice on the selected tint.** A menu that marks a choice, such as the open application in
  Studio's application menu, draws it in `primaryText` on the selected tint, never in Ant Design's
  selected colour, which is hard to read in dark mode. The theme draws Ant Design's selected menu item
  this way, so a menu marks its choice by selecting it.
- **A text trigger ends in a caret.** A button with text that opens a menu, such as the account button
  and Studio's application switcher, ends in a caret, which `AxisButton`'s `caret` option draws. An
  icon-only trigger has none.
- **Actions come after the choices.** In a menu that holds both, such as the
  application menu, the choices come first and an action, such as New application…, follows them.

## Fields

These rules hold for every Ant Design form in the SPA and in the identity provider's pages. The shared
`ConfigProvider` carries the field look, so no widget keeps its own copy, and a Vitest check holds the layout.

- **A field's error keeps the gap.** The message sits just under its control, and the field keeps its
  own gap below the message: `spacing-lg` in a form, none in a table cell.
- **A box sits on its label's first line.** A checkbox or a radio keeps its box level with the first
  line of its label, so a label that wraps leaves the box at the top.
- **An error border keeps 3:1.** An input, number input, select, date picker, mentions, cascader or
  tree select with an error, and an unticked checkbox or radio in a field with an error, take
  `dangerText` for their border, the colour of the field's message. The `danger` fill stays for fills
  such as a danger button, whose white text needs it.
- **A label is above its input, everywhere**, as the form widget has it, never beside it. Each form
  names Ant Design's vertical layout, and a Vitest check refuses a form that does not.
- **One field for the platform's screens** _(planned for M31)_. `AxisField` draws every field of Studio
  and the console: its label, its help and its input, as a group that its help describes.
  `AxisCheckboxField` is a yes-or-no field with its help under its label, `AxisChoiceField` a choice among
  a few ways, each with its help, and `AxisFieldGroup` a titled group of fields.
- **Every field has help** _(planned for M31)_, as [platform field help](frontend.md#platform-field-help) says.
- **Nothing between help and input** _(planned for M31)_. Guidance goes into the help, never on a line of
  its own.
- **One field to a row** _(planned for M31)_. Two fields share a row only when they truly belong together,
  such as a range's minimum and maximum, through `AxisFieldRow`, and they stack at narrow width.
- **Every required field is marked** _(planned for M31)_. A platform field follows the form widget's
  [required mark](form-widget.md#field-look), in every form and dialog, even where every field is
  required, such as New resource. Only the sign-in page marks none, as everyone knows both its fields are
  needed. A field never says "(optional)".
- **A field is checked on click** _(planned for M31)_. A finish button is never disabled because the
  input is not valid: that says nothing of what is missing, has low contrast and cannot be focused. A
  click checks the fields, shows under each one that is wrong what to do, such as "Enter a name.", and
  focuses the first. A message goes once the field is put right. A button is disabled only while its
  action runs. This holds for every form and dialog, as it does for the form widget's Save.

## Tab bars

A tab bar sits `spacing-md` under what precedes it. The theme starts a tab's label at the top of its
bar, with the underline right under the label, for every tab bar, because Ant Design's padding above
the label reads as a gap too wide.

## Text

These rules hold for every word the SPA shows. How a text is worded is in [UI writing](ui-writing.md).

- **No technical name.** What the UI shows is a text, never an internal code, a resource's file or a
  key: a label, else a name an author gave, see [names](studio-workspace.md#names).
- **No middle dot as a separator.** Peers join as words do, with a comma, as in "2 errors, 1 warning".
  A state is a sentence, as in "Release 7 is live". Facts of different kinds take a column or a line
  of their own, such as when a release was published and by whom. A "·" glues facts of different
  kinds into one line with no order, and a screen reader may read it out.
- **No value is a dash.** A table cell with no value shows the muted em dash, and a form or a detail
  shows "Not set", see the [empty values](frontend.md#shown-values). It never shows nothing, which
  cannot tell "not given" from "not loaded", and never N/A, which says "does not apply" and does not
  translate. A No is a value, so it reads No, never a dash.

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
- **Every control in one table** _(planned for M31)_. `AxisTable` has every control below, and each view
  turns on what it needs. Search, filter, sort, page and columns live in the address.
- **Sort** _(planned for M31)_. A column sorts from its header when its values have an order a person uses:
  names, people, statuses, dates and numbers. Long text, files, lists and actions do not. A header only
  sorts, as there is one place to filter.
- **Search** _(planned for M31)_. A column is searchable when it says so, through `searchText`, as a field
  marked `searchable` is in the record API, and search looks in all of them. The box says Search.
- **Filter** _(planned for M31)_. Filter is an icon button that carries the count of conditions in force, and
  opens a builder of conditions in groups of All or Any, with no limit on depth. Every column can be
  filtered on, and conditions follow the kind of value, text, enum, number or date, each with empty and
  not empty. Apply puts it in force, and a condition with no value is left out. A chip stands for each
  condition and group, with Clear all.
- **Columns control** _(planned for M31)_. Columns is an icon button that opens a dialog that shows, hides and
  orders the columns, with Reset. The identifying column stays first and always shows. A hidden column is
  still searched and filtered on. The platform keeps each person's choice.
- **Saved views** _(planned for M31)_. A person saves a table's search, filter and sort as a view.
- **Columns.** A column whose values have a fixed form, such as a number, a date, a date and time, a
  status tag or the row's icon actions, is as wide as its content and never wraps. The columns of free
  text share the width left in equal parts, and their text wraps. A table with no free text gives the
  width left to its last column. Every cell keeps `spacing-lg` on each side. Numbers sit on the right
  in tabular figures.
- **Rows.** Every row and the header are of the `tableRow` height, whatever they hold, icon buttons
  included: `spacing-xs` above and below a `smallControl` line. A row of more lines grows and reads
  from the same first line.
- **Row actions.** An action on one row sits on the row. One whose meaning the platform knows, such as
  open, delete or activate a release, is an icon button, and one an author configured keeps its text.
  From three actions on, or at narrow width, they fold into one "…" menu of text entries.
- **Opening a row.** A row opens from its identifying cell, a real link, so a row
  has no Edit icon, see [opening things](#opening-things).
- **An empty table keeps its header**, with the
  [empty state](#showing-what-is-empty) in its body.
- **Paging.** A long table pages.
- **How many** _(planned for M31)_. A paged table says what it shows of how many, as "1–20 of 143".
- **Too little room** _(planned for M31)_. A table measures its own width, as it changes. Its text columns
  narrow to 160 px first. Then columns move under their row one at a time, from the right or by a column's
  `priority`, `low` first and `high` last, as "Title: value" on the row's own surface, with no line between
  the row and them. The identifying column never moves. A moved cell with no value is left out, while a
  column that fits still shows its dash. Only when what stays does not fit does the table scroll sideways,
  with the first column fixed. So the data stays in sight, and every table gains with no setting, as SAP
  Fiori's pop-in has it.
- **Bulk actions** _(planned for M31)_. Selecting rows is for bulk actions alone: a table shows
  checkboxes only when it has a bulk action, and a selection shows an "N selected" bar that holds them.
  An action on one row stays on the row.

## Opening things

A thing opens at one of three levels, chosen by what the person does, never by taste.

- **Opening a record is a page.** Every thing with an identity of its own, such as a record, a Studio
  resource, a release, a user or a process instance, opens from its table's identifying cell, a real
  link, as a page with its own address. So a link can be shared, reloaded, gone back to and opened in a
  new tab, and the page has room to grow.
- **A side panel goes with a page and never opens a record.** It is for what accompanies the page: the
  problems panel, filters or help.
- **A modal dialog is for a short task that must end before going on:** confirming, such as Activate…
  or Delete…, asking for a little, such as a reason, a name or an invitation, or choosing one thing. It
  never holds a record's detail or a long form, and never opens a dialog of its own. What needs much
  scrolling, or to be come back to, is a page.

## Dialogs

These rules hold for every dialog, through the shared `ConfigProvider`.

- **Three sizes, by what a dialog holds:** `small` for a confirmation or one question, `medium` for a
  short form of two to six fields, and `large` for content that needs the width, such as a list to
  choose from or an import's preview. There is no larger size, because what needs more is a page. At
  narrow width every dialog takes the window's width less its `spacing-xl` margins. The size is a
  token that a dialog names, never a width of its own. A Vitest test fails on a dialog that names no
  size token.
- **Its place and height.** A dialog opens centred in the window and is as tall as its content, at
  most the window less its `spacing-xl` margins. Its body scrolls while its title and buttons stay.
- **Its body scrolls at its edge.** The body reaches the frame's edges and keeps the padding inside
  it, so the thin scrollbar runs at the dialog's right edge and a field's focus ring has room at its
  sides.
- **A dialog holds only what it edits** _(planned for M31)_. A table may show what its row's dialog does not
  edit, as long as the cell leads to where it is edited, so each thing is seen in one place and edited
  in one place.
- **It draws on `surface`,** as panels do, so everything inside sits on the colour it was made for,
  and the mask behind sets it apart from the page.

## Showing what is empty

Information always shows rather than being hidden, so a person can tell an empty value from a missing
one.

- **A field the object always has always shows.** Empty, it reads "Not set" on a form or a detail and
  the muted dash in a table cell, see [text](#text).
- **A list that is a standing part of the object always shows.** _(planned for M31)_ Empty, it says in
  one line what that means.
- **Hide only what does not apply or is not allowed.** _(planned for M31)_ A part that does not apply to
  the object's kind or state, or a field the person may not read, is left out.
- **Loading and failing are never empty.** _(planned for M31)_ A part that is still loading or failed to
  load says so, never Not set.
- **One empty state.** _(planned for M31)_ `EmptyState` draws every empty table and list: Ant Design's
  simple picture and one line in `textMuted`, because Ant Design's own empty text fails 4.5:1. The line
  is "No data yet." unless the emptiness says something the reader needs.

## List order

A list takes one of three orders, chosen by what the list is.

- **An order the author gives keeps it.** Process steps, wizard steps, a table's columns, a form's
  fields and navigation stay as written, and the author moves them, because sorting them would change
  their meaning.
- **Records in time are newest first,** such as releases, activations and history.
- **A set sorts by name.** Modules, packages, relaxed points, Used by and the changes sort by
  the name a person reads, with the page language's collation, so Đ follows D in Vietnamese. A list
  that mixes kinds groups them first in the sidebar's order, then by name. Relaxed points sort by
  module, then by point.
- **Sorting is for display only.** The file keeps its order, so adding a module adds one line to its
  diff. A table that lets a person sort follows [sort](#tables). The `table` widget keeps its own rules.

## Undo rather than confirm

_(planned for M31)_

Whether an action asks first follows from whether a person can take it back.

- **What can be taken back is undone, not confirmed.** Removing a row changes only the draft, so it
  asks nothing. A notice names it, such as "Organization removed.", with Undo, for ten seconds.
- **Every editor keeps a history.** Undo and Redo, icons in the
  [save bar](studio-resource-form.md#save-bar) with Ctrl+Z and Ctrl+Shift+Z, step through every change
  of the draft, a label's texts among them, on every resource kind.
- **What cannot be taken back is confirmed.** Discard drops every unsaved change at once and no Undo
  brings them back, so a small dialog asks "Discard all unsaved changes?" with a red Discard. Deleting
  a resource keeps its dialog.
