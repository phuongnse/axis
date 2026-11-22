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
| Link        | Text in `primaryText` with no border        | An action that reads as a link but goes to no address, such as the first cell of a [list of objects](studio-resource-form.md#lists-of-objects)                                   |
| Destructive | `dangerText` at any level                   | Actions that remove, such as Delete row and Delete                                                                                                                               |

- **An inline link** _(planned)_. An `AxisButton` link with `inline` takes the height of a line of text and no padding,
  for a link inside a value, such as a reference in a table cell.
- **Text colours, not fill colours.** Ant Design writes the text of text, outlined and danger buttons
  in the fill colours `primary` and `danger`, which fail contrast on a dark surface, so Axis writes
  them in `primaryText` and `dangerText`.
- **Beside a solid button every other action is a text button.** In a dialog, a form's footer and a
  band, Cancel and every secondary, main or destructive action next to the finish button is a text
  button in its own colour, so the finish button is the one filled shape. The theme draws every Ant
  Design dialog's Cancel this way, and `AxisActionRow`, the action row of `AxisButton`, draws the
  other rows. A main action with no solid button beside it, such as a table's first toolbar action,
  stays outlined.
- **An action row folds into a menu.** An `AxisActionRow` with `fold` keeps one line. When its content does not
  fit, the actions named in `fold` become the entries of one "…" icon menu, More actions, in their order, each
  keeping its disabled state. They are buttons again when the room comes back. A row without `fold` wraps.
- **An action bar folds into More.** An `ActionBar`, a bar of actions at the foot of a page on `surface`
  with a `borderSubtle` rule above it, folds the actions it names into one More actions menu through
  `AxisActionRow`'s `fold`, as a table's row actions fold at narrow width. A bar that stays at the foot of the window as the page
  scrolls draws in the shell's [bar slot](shell.md#layout), above the footer, instead of at the foot of its page.
- **Actions of equal weight are one group**. A screen keeps one main action. Actions of equal weight that
  together are a region's main action, such as a task's outcomes, are one joined group in the `main` role, in the order
  the process gives them. Actions of equal weight in any other role, such as a package's Build, Run tests and Run…,
  are one joined group in their role. Each button of the group is as wide as the widest, stays a button of its own
  with no selected state, and the group has a name for assistive technology. A group of one is a plain button of its
  role.
- **Which actions go together** _(planned)_. Do the actions change or remove the same thing? Then they manage that
  thing. If not, they are tools, which stay side by side.
- **Actions that manage one thing fold into a split button** _(planned)_. From three on, the one used most stays the
  button and the others are the entries of a menu on its joined caret, in their order, a destructive one in
  `dangerText`. It is one button in one shape, as Ant Design joins one with Space.Compact: its text does the action
  and its caret opens the menu. `AxisSplitButton` draws it.
- **Tools stay side by side** _(planned)_. Find, Format, Copy, Zoom in, Zoom out, Move up, Move down, Undo and Redo
  stay as buttons, because a menu would cost a click each time. They fold into "…" only when their row runs out of
  room.
- **All text or all icons** _(planned)_. A group of actions is all text or all icons. The "…" or the caret that opens
  a menu is no action of its own, so it stays an icon beside text or icons alike.
- **A page's actions and a tree's rows keep their own rules**. A page's actions keep the page's rules, and
  a file tree's node keeps its actions on a right click, Shift+F10 or the menu key.
- **Text or an icon, never both.** An action shows its text or its icon. An icon goes with text only
  where it does what text cannot: navigation that collapses to icons, a status or a severity, and the
  mark that identifies an application or a site.
- **An action that asks for more ends in an ellipsis,** such as New application…, New resource…,
  Publish…, Import…, Invite…, Reassign… and Choose…, so the person knows a dialog or a page follows. The
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
  opens none. Keyboard focus still shows it. A copy button is the one exception: right after the click its tooltip says
  Copied, see [shown values](frontend.md#shown-values).

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
- **A select's own actions end its list**. Creating or managing a select's choices, such as New
  application… or Manage saved inputs…, comes after the choices and a divider, in the look of the list's own items,
  as actions come after the choices in a menu. `ChoiceActions` draws them.
- **One look for a list's items** _(planned for M35)_. Every list of things to pick or open draws its
  items one way, whether Ant Design draws the list or the page does, such as the sidebar's menu, a select
  or the notification bell. An item sits inset from its list's edge, with rounded corners. Groups are set
  apart by their heading, with no divider between them.
- **A menu's minimum width** _(planned for M35)_. Every dropdown menu is at least as wide as the `menu`
  size token, and wider when an item needs the room.

## Fields

These rules hold for every Ant Design form in the SPA and in the identity provider's pages. The shared
`ConfigProvider` carries the field look, so no widget keeps its own copy, and a Vitest check holds the layout.

- **A field keeps no gap of its own.** The message sits just under its control. In a form, the form's
  layout gives `spacing-lg` between fields, so the next field stays `spacing-lg` below a message. In a table
  cell there is none.
- **A box sits on its label's first line.** A checkbox or a radio keeps its box level with the first
  line of its label, so a label that wraps leaves the box at the top.
- **An error border keeps 3:1.** An input, number input, select, date picker, mentions, cascader or
  tree select with an error, and an unticked checkbox or radio in a field with an error, take
  `dangerText` for their border, the colour of the field's message. The `danger` fill stays for fills
  such as a danger button, whose white text needs it.
- **A warning takes the warning text colour.** A field's warning message and the
  border of a field with a warning take `warningText`, as an error's take `dangerText`, so both pass
  contrast.
- **A label is above its input, everywhere**, as the form widget has it, never beside it. Each form
  names Ant Design's vertical layout, and a Vitest check refuses a form that does not. In a table cell whose row
  and column name the control, such as a switch in a row of settings, the label is visually hidden and still
  names the input, through `hideLabel` on a field the screen holds the value of.
- **One field for the platform's screens.** `AxisField` draws a field: its label, its help and its input, as a
  group that its help describes. Inside a vertical form it takes the form's value and checks. Outside an
  Ant Design form, where a screen keeps its draft itself, the caller gives the input its value and change
  handler, and `AxisField` still ties the label to the input and makes the help its description. Such a field
  takes `required`, which draws the shared required mark after its label and sets `aria-required` on the input,
  while a field inside a form is marked by its `requiredMessage` alone.
  It makes its select, number, date or text input, or a select joined with its action, as wide as the field.
  `AxisFieldGroup` draws a titled group of fields with its help, such as an object's fields or a table, as a
  group that its title names and its help describes. It takes an `action` on its title row, drawn as a light text
  button, and the group is still named by its title alone. A field that holds one input uses `AxisField`, and a
  field that holds several inputs, or rows, uses `AxisFieldGroup`, because a label names only one input.
  `AxisCheckboxField`, `AxisChoiceField` and `AxisFieldRow` are built on `AxisField` and `AxisFieldGroup`.
  `AxisCheckboxField` and `AxisChoiceField` take the same two modes: with a `name` inside a form, or with the
  value and `onChange` the screen holds. A choice the screen holds gives its radio group a name of its own, so
  two choices on one panel never join.
- **An action on an input's value**. It is an icon inside the input, at its end, such as Clear, Copy, Show
  password or a label's Translations.
- **An action on a select's value**. It is an icon joined after the select, such as a reference's Open, the
  two as wide as the field.
- **One dialog keeps saved items.** Saved items that a person names, such as a table's views and a package's saved run
  inputs, are kept in one dialog, `SavedItemsDialog`. Each name opens its item, its line holds one split button,
  Overwrite with Rename and Delete in its menu, and Name with Save saves what is shown now as a new item. A table opens
  it from Views, and a select of saved items from the end of its list.
- **A field's status.** A field inside a form takes a `status`, what the screen found about the
  field beyond its rules: a check run while the person types, such as whether a subdomain is free, or
  choices that could not load. Running shows the input busy. A status that passed leaves the field as it
  is, with no tick and no text, since no error is all the person needs, and a status region tells a
  screen reader its message. A refused one shows its message as an error, and something that could not
  be done shows its message as a warning, under the input where an error shows, never in an alert of its
  own. While a status is shown it takes the place of the field's own errors. Before any check runs, the
  status is `idle`, which shows nothing, so the input stays in place as the status changes and keeps its
  focus. A field outside a form takes no status, because the form item is what draws the message, the
  border and the spinner.
- **Every Studio and console field is drawn through them**, with `AxisCheckboxField` as
  a yes-or-no field with its help under its label and `AxisChoiceField` as a choice among a few ways, each
  with its help.
- **Every field has help**, as [platform field help](frontend.md#platform-field-help) says.
- **Nothing between help and input**. Guidance goes into the help, never on a line of
  its own.
- **One field to a row**. Two fields share a row only when they truly belong together,
  such as a range's minimum and maximum, through `AxisFieldRow`, and they stack at narrow width. A key and its
  value in one row of a titled list are one entry, so they share a row too.
- **One fact to a row**. A list of facts, such as a page's first part through `TermList`, gives each fact
  a row of its own. Two facts share a row only when they belong together, such as a start and an end date.
- **A strip of facts**. Facts that head a view in use, such as a process instance's page, sit in one line
  through `TermList`'s `row`. Each fact has its own width, `spacing-xl` apart, wraps when the line is full, and each
  value stays on one line. It is the one layout the rule above does not cover.
- **Every required field is marked**. A platform field follows the form widget's
  [required mark](form-widget.md#field-look), in every form and dialog, even where every field is
  required, such as New resource. The sign-in pages mark none, since everyone knows their fields are needed.
  A field never says "(optional)". Each form names the shared `requiredMark`, and a Vitest check refuses a form
  outside the sign-in pages that does not.
- **A field is checked on click**. A finish button is never disabled because the
  input is not valid: that says nothing of what is missing, has low contrast and cannot be focused. A
  click checks the fields, shows under each one that is wrong what to do, such as "Enter a name.", and
  focuses the first. A message goes once the field is put right. A button is disabled only while its
  action runs, while there is nothing to save, or while the screen cannot send it, as during
  [Studio's reconnect](studio.md#reconnect). This holds for every form and dialog, as it does for the form widget's Save. A confirmation
  that asks the user to type a name before a destructive action keeps its button off until the name
  matches, as [the cell console's Delete](cell-console.md#actions) does.
- **A group's title lines up with its fields** _(planned)_. A group's title, such as Entity label, starts at the inset
  of a field's label, so the fields of a section line up whether they are one field or a group. The group's help sits
  under its title, and its action, such as Add validation, sits to the right of the title and the help together, so
  the action does not push the help away.
- **Help sits close to its label** _(planned)_. A field's help sits just under the label's line and `spacing-xs` above
  the input. The gap is judged in IBM Plex Sans, the app's font, as the space between lines differs from font to font.
  Every help line is one size, in `textMuted`, and a choice's option help is one too.
- **A box in error looks it**. A field a screen holds, whose value the screen refuses, is drawn in its error look, with
  its error in `dangerText` under it and `aria-invalid` on the input. `AxisField` takes `error` for it, `AxisFieldGroup`
  takes `error` and `LabelTexts` takes `invalid`. An error shows from the refused action on, and while the value stays
  wrong. It goes as soon as the value is right.
- **A box in error looks it in a form** _(planned)_. A form's field whose value the form refuses gets the same error look
  and `aria-invalid`. That covers the record form's fields and the forms that draw their own error line. `AxisFieldGroup`
  also takes `required` and `fold`, for #3264's build.
- **One way to the first error**. A screen that holds its fields calls `focusFirstError` after a refused action, such as
  Done, so the focus moves to the first box in error and scrolls it into view. The resource form's item dialog does this.
- **Every Ant Design form takes the focus to its first error** _(planned)_. Each Ant Design form sets
  `scrollToFirstError={{ focus: true }}`, so a refused Save, Add, OK or Next moves the focus to the first box in error.
- **One look for yes or no** _(planned)_. A yes-or-no setting is a checkbox with its label on its right and its help under
  the label, the same in a form as on a screen that holds its fields. A switch is for a setting that takes effect as it
  is turned on, and no other.
- **A pair's help** _(planned)_. A pair of fields in `AxisFieldRow` takes one title and one help, and its fields take no
  help of their own, so their boxes line up.

## Tab bars

A tab bar sits `spacing-md` under what precedes it, except a page's tab row, see below. The theme starts a
tab's label at the top of its bar, with the underline right under the label, for every tab bar, because Ant
Design's padding above the label reads as a gap too wide.

- **One tab row look.** Every tab row of a workspace has the same padding and a rule the region's width,
  whether it holds a file's tabs or a view switch.
- **A page's tab row.** A tab row on a page, such as a Studio resource's Design, Form, JSON and Used by, has the
  same look. Its rule reaches the page's edges, past the content area's padding. The row sits `spacing-sm` into the
  gap above it, and its content starts `spacing-md` under its rule.

## Text

These rules hold for every word the SPA shows. How a text is worded is in [UI writing](ui-writing.md).

- **No technical name.** What the UI shows is a text, never an internal code, a resource's file or a
  key: a label, else a name an author gave, see [names](studio-workspace.md#names).
- **No middle dot as a separator.** Peers join as words do, with a comma, as in "2 errors, 1 warning".
  A state is a sentence, as in "Release 7 is live". Facts of different kinds take a column or a line
  of their own, such as when a release was published and by whom. A "·" glues facts of different
  kinds into one line with no order, and a screen reader may read it out.
- **No dash between facts either** _(planned)_. Neither a middle dot nor a dash joins facts. A fact of a different
  kind takes its own term or line, such as a run's Branch beside Went to, or Started via under Started by. What
  qualifies one thing sits in parentheses, with peers in them joined by a comma, such as "(run 2)" or an
  operation's "(ERP)".
- **Names and values as a table**. Names and their values, such as a request's headers or a run's input,
  are a table of two columns with no header row. A name in `textMuted` takes 5 of 12 columns and its value 7, so the
  tables a view stacks line up at any width. A nested value is a row of its own, named by its path, such as
  `lines[0].description`. A number keeps its source digits. A word written in place of a value, such as `null`, Empty,
  a count of fields or items, or Hidden ("Đã ẩn") for a secret or a value the reader may not read, takes the
  [empty value's](frontend.md#shown-values) style, as Not set does. The server's `[secret]` stays its placeholder in
  logs, JSON and the API, and reads Hidden in the table. A value in the table has no copy button. A JSON body is a table
  by default, with Table and JSON as line tabs right above it, see [tab bars](#tab-bars). Other text shows as it is in
  the code font.
- **A person by name** _(planned)_. A person is always named, so a page read by someone else, or seen in a screenshot,
  still says who. The signed-in person's own name is followed by "(you)" ("(bạn)") in `textMuted`, and is never
  replaced by You or Me. "Me" stays for the reader's own voice, such as a filter Assigned to me. A name is a link only
  to a page about the person that the reader may open, such as a user's page for an administrator, and plain text for
  anyone else.
- **No value is a dash.** A table cell with no value shows the muted em dash, and a form or a detail
  shows "Not set", see the [empty values](frontend.md#shown-values). It never shows nothing, which
  cannot tell "not given" from "not loaded", and never N/A, which says "does not apply" and does not
  translate. A No is a value, so it reads No, never a dash.
- **A reference is a link, and only the reference.** A pointer to a resource, a record or a page is a link,
  and only the pointer is. Its message and any other words are text, and no whole row is a button. The link
  names the thing as people read it: its title in the reader's language, with its kind muted after it, such
  as "Purchase requests" then "Entity". It never names a file or a path. It opens the thing at its path. A
  pointer to nothing a person can open is its file, in muted text. The link is an `AxisButton` with the `link`
  role, see [buttons](#buttons).
- **A status is a `StatusTag` with five tones, and any other enum value is text** _(planned for M35)_. A
  status shows as a tag in one of five tones. Any other value of an enum shows as plain text.
- **Every tag is a `StatusTag`**. Any tag on any screen is a `StatusTag`: a status, a mark or a value's
  name. Nothing else draws a tag, neither Ant Design's `Tag` outside `StatusTag` nor a span drawn as one. Its tone says
  what it asks of the reader:
  - `attention` for what a person should look at: Off-standard, "Missing read on …", "Unaccepted action …", "Unknown
    name", a text marked Missing, and a problem's warning.
  - `danger` for a problem's error.
  - `muted` for a mark that needs nothing: a role that is Read-only, and the module a resource comes from in a kind's list.
  - `neutral` for a value's name or a mark that only informs: a policy's actions and fields, a grant's module and Shared,
    a layer's mark and Customized, a condition's mark on the form canvas, a field's properties, an expression's kind,
    a choice's mark, and a lifecycle state's Initial and Deletable.
- **A tag is never a link**. A references field on a record's page shows its links in a row, with no tag
  round each.
- **A tag that stands for more than its words names itself**. A layer's mark takes `role="note"` and an
  accessible name such as "supplier, added by this application", which `StatusTag` passes to its tag.
- **A removable filter chip is a control**. The table filter's chip is no tag and keeps Ant Design's
  closable `Tag`.

## What comes next

_(planned for M35)_

Every screen says where things stand in a few plain words, and has at most one main action.

- **Where things stand.** A screen names its state in a short sentence, such as "Release 7 is live.", so a
  person reads it before any control.
- **One main action.** A screen has at most one solid Finish action, as [buttons](#buttons) says. Every
  other action beside it is a text button.

The reasons are in [D152](../decisions/d152-every-screen-says-where-things-stand-with-at-most-one-main-action.md).

## Layout shows structure

_(planned for M35)_

How a list is drawn shows what it is: its items, its groups and its order.

- **A list is drawn as a list.** Items that a person reads one by one sit in a list, not in a line joined
  by a middle dot, as [text](#text) says.
- **Bulleted lists use one component.** `useBulletList` draws every bulleted list, so each keeps one look.
- **Order.** A list keeps the order that [list order](#list-order) gives it.

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
    task inbox have no Filter. [The task inbox](task-inbox-widget.md) names the columns it filters on.
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
  [dialog that keeps saved items](#fields). Each line is one split button, Overwrite with Rename and Delete in the
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
  [text](#text).
- **Row actions.** An action on one row sits on the row. One whose meaning the platform knows, such as
  open, delete or activate a release, is an icon button, and one an author configured keeps its text.
  From three actions on, or at narrow width, they fold into one "…" menu of text entries.
- **Opening a row.** A row opens from its identifying cell, a real link, so a row
  has no Edit icon, see [opening things](#opening-things).
- **An empty table keeps its header**, with the
  [empty state](#showing-what-is-empty) in its body.
- **Paging.** A long table pages. A table that edits a draft, such as Studio's seed grid and the entity designer's
  Fields, turns paging off with the view's `paging` option and shows every row with no pager, because a pager would
  move the row an author is editing. Studio's policy matrix, the tables of a module contract, the operator console's Tenant
  table, the two licences lists, the account page's Authenticators and Sessions, the users page's Connected
  apps and the tables of a process instance's page, apart from its History timeline, also show every row with no pager. Only a table that holds all its
  rows reads it.
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
- **The growth test** _(planned for M35)_. A table whose rows grow as people use the application, such as
  records, requests or releases, has the full standard toolbar above. A table whose rows do not grow may
  show fewer of its controls, as the toolbar bullet says.
- **Named exceptions** _(planned for M35)_. A table that is part of one thing is the exception to the growth
  test. Its rows belong to that one thing, such as the lines of a child collection on a form, so a search or
  a filter of its own would split them from it.
- **No control of its own**. A table gets no control for one view. A view is a filter
  kept in the address, so the page offers it beside the table and a link opens the same rows.
- **View controls on one line** _(planned for M35)_. The view controls sit on one line beside the search box,
  which keeps its own width.
- **Group rows.** A row that heads a group shows its heading and leaves its other cells blank, with no dash,
  as `AxisTable`'s `blankRow` draws them.
- **Folding group rows**. A row with children folds, closed at first, behind an `AxisButton`
  of the role `icon` with `Chevron`, named "Show the rows in it" or "Hide the rows in it". The fold sits in the
  first column's padding, every row's words start where the heading does, and each level of rows starts one fold
  further in. A table that groups rows uses this, and never a list inside a cell.
- **A row's details**. A row with more to show than its columns, such as a call's attempt, folds open to
  its details from a fold in its first cell, drawn as a group row's fold, named "Show the details" or "Hide the details"
  while open, with no column of its own. `AxisTable`'s `details` and its group rows open a row one way.
- **Reordering by dragging** _(planned)_. A table whose rows have an order that means something takes `reorder` in
  `AxisTable`, on `@dnd-kit/sortable`. Each row starts with a handle named Drag to reorder, and the rows between make
  room while a row is dragged. The keyboard uses the same handle: Space picks the row up, the arrow keys move it, Space
  drops it and Escape cancels. Move up and Move down stay in the row's menu, as the way to move a row without a drag.
  While a search or a filter hides rows, the handles are off, with the tooltip "Clear the search and the filter to
  reorder", as the rows shown are then not the list's order. What a screen reader hears names the item and its new
  place, in the page's language. A form's child collection rows take the same.

The reasons for the table standard are in [D151](../decisions/d151-tables-whose-rows-grow-have-the-full-toolbar-and-a-view-is-a-filter-kept-in-the-address.md),
and the reasons for each person's columns and saved views are in [D153](../decisions/d153-a-persons-table-views-and-columns-are-kept-on-the-server-and-the-address-still-wins.md),
and the reasons for bulk actions, inline edit and the column chooser of an application's table are in [D159](../decisions/d159-pages-show-related-lists-boards-and-calendars-forms-take-context-and-drafts-and-each-site-has-its-brand.md).

## Opening things

A thing opens at one of three levels, chosen by what the person does, never by taste.

- **Opening a record is a page.** Every thing with an identity of its own, such as a record, a Studio
  resource, a release, a user or a process instance, opens from its table's identifying cell, a real
  link, as a page with its own address. So a link can be shared, reloaded, gone back to and opened in a
  new tab, and the page has room to grow.
- **A side panel goes with a page and never opens a record.** It is for what accompanies the page: the
  problems panel, filters or help, and an item of a draft, as the rule below says.
- **An item of a draft opens in the side panel** _(planned)_. A field, validation or index of a draft, such as an
  entity's, opens in the side panel. Its name is a real link to the panel's address, which opens the panel in place on
  a plain click and a new tab on Ctrl+click, and its address can be copied. One platform part draws every such link.
  An item with no address is a subtle button that opens the panel.
- **A modal dialog is for a short task that must end before going on:** confirming, such as Activate…
  or Delete…, asking for a little, such as a reason, a name or an invitation, or choosing one thing. It
  never holds a record's detail or a long form, and never opens a dialog of its own. What needs much
  scrolling, or to be come back to, is a page.
- **A reference opens what it names**. A record's label, shown read-only, is a real link to its record page,
  with an `href` so Ctrl+click opens a new tab. The link passes the page it leaves as `from`, see
  [shell layout](shell.md#layout). It never opens a dialog. A record the person cannot open is plain text.
- **A link in a value opens in the same site** _(planned)_. A reference in a value is an inline link only when the
  thing it names opens in the same site, such as a record, an instance, a sub-process or a step's run. A thing in
  another site, such as Studio, opens only from a button named for it, such as Open in Studio. So a call's connector
  and operation are plain text.
- **Studio's and the platform's references**. A user, a tenant, an application or a release that a Studio
  or platform page names is a link to its page where the reader may open it, and plain text otherwise. A Studio
  reference picker whose chosen resource exists has an Open button joined to it, which opens that resource in Studio.

## Dialogs

These rules hold for every dialog, through the shared `ConfigProvider`.

- **Three sizes, by what a dialog holds:** `small` for a confirmation or one question, `medium` for a
  short form of two to six fields, and `large` for content that needs the width, such as a list to
  choose from or an import's preview. There is no larger size, because what needs more is a page. At
  narrow width every dialog takes the window's width less its `spacing-xl` margins. The size is a
  token that a dialog names, never a width of its own. A Vitest test fails on a dialog that names no
  size token.
- **Its place and height.** A dialog opens centred in the window and is as tall as its content, at
  most the window less its `spacing-xl` margins. Its body scrolls while its title and buttons stay, and it
  stops at its end, as a [scroll area](#scroll-areas) does.
- **Its body scrolls at its edge.** The body reaches the frame's edges and keeps the padding inside
  it, so the thin scrollbar runs at the dialog's right edge and a field's focus ring has room at its
  sides.
- **The buttons sit `spacing-lg` below the body.** A dialog that ends in a form needs no space of its
  own, and a list or a table ends the same way.
- **A dialog holds only what it edits**. A table may show what its row's dialog does not edit, as long
  as the cell leads to where it is edited, so each thing is seen in one place and edited in one place.
- **It draws on `surface`,** as panels do, so everything inside sits on the colour it was made for,
  and the mask behind sets it apart from the page.

## Scroll areas

A scroll area is a part of a screen that scrolls on its own, such as a dialog's body, the shell's
navigation or a code editor. The page keeps the browser's own scroll, and each scroll area shares the
wheel with the page under two rules.

- **It stops at its end.** A scroll area passes no scroll on to the page. Otherwise the browser moves the
  page as soon as the area reaches its end, so a person who only meant to scroll a list loses their place.
- **One scroll area in each direction.** A scroll area never sits inside another that scrolls the same
  way, because two areas would share one wheel. So a list in a dialog grows, and the dialog's body scrolls
  it. A wide table may still scroll sideways inside a page or dialog that scrolls down.
- **One scroll per pane** _(planned)_. A pane scrolls as one from top to bottom, however many blocks it holds. A code
  editor in it is as tall as its text with no scroll of its own. One a person types in keeps a least height and reaches
  the pane's foot when its text is shorter. An action bar at the pane's foot stays in place.

## Showing what is empty

Information always shows rather than being hidden, so a person can tell an empty value from a missing
one.

- **A field the object always has always shows.** Empty, it reads "Not set" on a form or a detail and
  the muted dash in a table cell, see [text](#text).
- **A list that is a standing part of the object always shows.** Empty, it says in
  one line what that means.
- **Hide only what does not apply or is not allowed.** A part that does not apply to
  the object's kind or state, or a field the person may not read, is left out.
- **Loading and failing are never empty.** A part that is still loading or failed to
  load says so, never Not set.
- **One empty state.** `EmptyState` draws every empty table and list: Ant Design's
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

Whether an action asks first follows from whether a person can take it back.

- **What can be taken back is undone, not confirmed.** Removing a row changes only the draft, so it
  asks nothing. A notice names it, such as "Organization removed.", with Undo, for ten seconds.
- **Every editor keeps a history.** Undo and Redo, icons in the
  [save bar](studio-resource-form.md#save-bar) with Ctrl+Z and Ctrl+Shift+Z, step through every change
  of the draft, on every resource kind.
- **What cannot be taken back is confirmed.** Discard drops every unsaved change at once and no Undo
  brings them back, so a small dialog asks "Discard all unsaved changes?" with a red Discard. Deleting
  a resource keeps its dialog.
