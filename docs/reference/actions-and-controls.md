# Actions and controls

This page says how the SPA draws buttons, menus, form fields, messages, [tables](tables.md#tables) and dialogs, and how its screens
word, open, order and empty what they show. The rules are the same in every widget, page and dialog,
and in the identity provider's pages, so a person learns a control once. The tokens named here are in
[design tokens and previews](frontend.md#design-tokens-and-previews), and which shared component draws each control is in
[shared widgets](shared-widgets.md).

## Buttons

A button's style follows its role, so the same kind of action always looks the same, and its place
decides the rest. One shared button, `AxisButton` in `web/src/platform/`, takes the role, and each
widget names a role, never an Ant Design button type.

| Role      | Look                                                                            | Used for                                                                                                                                                                         |
| --------- | ------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Finish    | Solid primary, or in the destructive tone a solid `danger` fill with white text | The one action that finishes a task, such as Save or Next, one per screen                                                                                                        |
| Main      | Outlined in `primaryText`                                                       | The main action of a region: a table's first toolbar action that is shown, such as New request, Add row of a child collection, and a `startProcess` action on a form or a detail |
| Secondary | The default grey outline                                                        | Other actions, such as Cancel and Import                                                                                                                                         |
| Subtle    | Borderless in the text colour, with a light fill on hover and focus             | Tools such as Refresh, Format and Undo, header controls such as the account button, and icon-only buttons. Choose and Clear of a `reference` field are bordered and joined       |

- **Four roles, by weight.** Finish is a solid fill, the one action that finishes a task, one a screen.
  Main is outlined in `primaryText`, the main action of a region, one a region. Secondary is the default grey outline,
  for most actions. Subtle is borderless in the text colour, with a light fill on hover and focus, for tools such as
  Refresh, Format and Undo. An icon-only button is a subtle button with only its icon.
- **An action with no address is a button, never a link.** A name that opens its item in place is a subtle button.
  An action such as Add condition is a secondary button, so it has a visible shape. See [opening things](#opening-things).
- **A tone apart from the role**. The role says how much an action weighs. The destructive tone says it removes or
  throws something away, such as Delete, Discard, Revoke or Drop, and any role takes it. A main, secondary or subtle
  button takes it in `dangerText`, and a menu entry too. A finish button takes it as a fill in `danger` with white text.
  Save or Next removes nothing, so it is never red.
- **A removal finishes in solid red.** A dialog that confirms a removal ends in a solid red Delete, drawn through
  `DialogFooter`, with a light Cancel beside it.
- **An open resource's Delete… is secondary**. An open resource's Delete… is a secondary button in the
  destructive tone.
- **A chosen subtle button is marked through `selected`.** `AxisButton`'s `selected` draws a subtle button on the
  selected tint in `primaryText` and sets the one `aria-pressed` itself, or `aria-current` for a page or a step, in place
  of any the caller passes.
- **No button looks like a link**. A borderless button is in the text colour, or in `dangerText` in the
  tone, even beside a finish button. Borderless text in `primaryText` is a link's look alone.
- **A region's main action keeps its border.** It keeps its border wherever it sits, a title row included.
  A section's Add, such as Add field, Add validation or Add index, is a secondary button in the title row, and a
  title row's tools stay subtle. `TitleRowActions` draws a title row's actions in their own roles.
- **One size to a group** _(planned, #5062)_. The usual size is the default, and the small size is for tables and dense rows.
  A group has one size.
- **An inline link**. A link inside a value, such as a reference in a table cell, takes the height of a
  line of text and no padding.
- **Text colours, not fill colours.** Ant Design writes the text of text, outlined and danger buttons
  in the fill colours `primary` and `danger`, which fail contrast on a dark surface, so Axis writes
  them in `primaryText` and `dangerText`.
- **Beside a solid button every other action is a subtle button.** Cancel and every secondary action next
  to the finish button is a subtle button, so the finish button is the one filled shape. A region's main action keeps
  its border beside it, as the rule above says. `AxisActionRow` draws them so.
- **An action row folds into a menu.** An `AxisActionRow` with `fold` keeps one line. When its content does not
  fit, the actions named in `fold` become the entries of one "…" icon menu, More actions, in their order, each
  keeping its disabled state. They are buttons again when the room comes back. A row without `fold` wraps, and so does
  a row whose `fold` names one action, as a menu of one entry is never drawn.
- **An action bar folds into More.** An `ActionBar`, a bar of actions at the foot of a page on `surface`
  with a `borderSubtle` rule above it, folds the actions it names into one More actions menu through
  `AxisActionRow`'s `fold`, as a table's row actions fold at narrow width. A bar that stays in view as the page
  scrolls draws in the shell's [bar slot](shell.md#layout), instead of at the foot of its page.
- **Actions of equal weight are one group**. A screen keeps one main action. Actions of equal weight that
  together are a region's main action, such as a task's outcomes, are one joined group in the `main` role, in the order
  the process gives them. Actions of equal weight in any other role, such as a package's Build, Run tests and Run…,
  are one joined group in their role. Each button of the group is as wide as the widest, stays a button of its own
  with no selected state, and the group has a name for assistive technology. A group of one is a plain button of its
  role.
- **Which actions go together**. Do the actions change or remove the same thing? Then they manage that
  thing. If not, they are tools, which stay side by side.
- **Actions that manage one thing fold into a split button**. From three on, the one used most stays the
  button and the others are the entries of a menu on its joined caret, in their order, a destructive one in
  `dangerText`. Its menu holds at least two entries and never repeats the button, and a destructive entry comes last,
  after a divider. It is one button in one shape, as Ant Design joins one with Space.Compact: its text does the action and
  its caret opens the menu. `AxisSplitButton` draws it.
- **Tools stay side by side**. Find, Format, Copy, Zoom in, Zoom out, Undo and Redo stay as
  buttons, because a menu would cost a click each time. They fold into "…" only when their row runs out of room.
- **All text or all icons**. A group of actions is all text or all icons. The "…" or the caret that opens
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
  width a row with two or more actions folds into a menu of text entries, so touch users lose nothing.
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

- **One builder.** Every menu of actions or choices is built by `menuOf` from one action type, `AxisAction`,
  in `web/src/platform/menuActions.tsx`. A row's actions, an action row's fold, a split button's menu and a
  menu of choices take the same actions. A choice with no address is a checked radio entry. A choice with an
  address is a link, marked `aria-current="page"` when it is the current page. A menu entry keeps the action's
  `attributes`, such as its `data-testid`. The shell's navigation tree lists
  pages, not actions or choices, so it is not one of these menus, see [layout](shell.md#layout).
- **Selected and current states.** The selected tab, the active menu entry and the current page of a
  pagination use `primaryText`, and destructive menu entries use `dangerText`.
- **A marked choice on the selected tint.** A menu that marks a choice, such as the open application in
  Studio's application menu, draws it in `primaryText` on the selected tint, never in Ant Design's
  selected colour, which is hard to read in dark mode. The theme draws Ant Design's selected menu item,
  and the title of a submenu that holds it, this way, so a menu marks its choice by selecting it.
- **A text trigger ends in a caret.** A button with text that opens a menu, such as the account button
  and Studio's application switcher, ends in a caret, which `AxisButton`'s `caret` option draws. An
  icon-only trigger has none.
- **Actions come after the choices.** In a menu that holds both, such as the
  application menu, the choices come first and an action, such as New application…, follows them behind one
  divider.
- **A select's own actions end its list**. Creating or managing a select's choices, such as New
  application… or Manage saved inputs…, comes after the choices and a divider, in the look of the list's own items,
  as actions come after the choices in a menu. `ChoiceActions` draws them.
- **One look for a list's items** _(planned for M35)_. Every list of things to pick or open draws its
  items one way, whether Ant Design draws the list or the page does, such as the sidebar's menu, a select
  or the notification bell. An item sits inset from its list's edge, with rounded corners. Groups are set
  apart by their heading, with no divider between them.
- **A menu's minimum width.** Every dropdown menu is at least as wide as the `menu`
  size token, and wider when an item needs the room.
- **One threshold everywhere**. One or two actions show as buttons in a table's row or a list's item. From three
  on, or when the row runs out of room, they fold into one "…" menu.
- **One threshold on a heading** _(planned, #5134)_. One or two actions show as buttons on a page's heading or a section's
  title row. From three on, the actions the heading marks as foldable fold into one "…" menu of two or more, as a row's
  actions do.
- **A destructive entry comes last.** In a menu of actions, a destructive entry comes after the others, behind one
  divider.
- **A menu never holds one entry**. A menu has at least two entries.

## Fields

These rules hold for every Ant Design form in the SPA and in the identity provider's pages. The shared
`ConfigProvider` carries the field look, so no widget keeps its own copy, and a Vitest check holds the layout.

- **A field keeps no gap of its own.** The message sits 4px under its control, in a 14px font on a 20px
  line, which the shared `ConfigProvider` sets on the `help` part of Ant Design's form. A field with a
  message grows by the message's height. In a form, `AxisForm` gives `spacing-lg` between fields, so the
  next field stays `spacing-lg` below a message. In a table cell there is none.
- **A box sits on its label's first line.** A checkbox or a radio keeps its box level with the first
  line of its label, so a label that wraps leaves the box at the top.
- **An error border keeps 3:1.** An input, number input, select, date picker, mentions, cascader or
  tree select with an error, and an unticked checkbox or radio in a field with an error, take
  `dangerText` for their border, the colour of the field's message. The `danger` fill stays for fills
  such as a danger button, whose white text needs it.
- **A warning takes the warning text colour.** A field's warning message and the
  border of a field with a warning take `warningText`, as an error's take `dangerText`, so both pass
  contrast.
- **A label is above its input, everywhere**, as the form widget has it, never beside it. Every form on
  a platform screen is an `AxisForm`, which is always vertical, and a Vitest check refuses Ant Design's `Form` outside
  `AxisForm`, the form widget and the filter bar, which name the vertical layout themselves. In a table cell whose row
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
  group that its title names and its help describes. It takes an `action` on its title row, drawn as [buttons](#buttons) says for a
  title row, and the group is still named by its title alone. A field that holds one input uses `AxisField`, and a
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
  A field never says "(optional)". `AxisForm` takes the shared `requiredMark`, and the sign-in pages
  pass `unmarked` to take it off. The form widget and the filter bar name the shared `requiredMark` themselves, and a
  Vitest check refuses a form outside the sign-in pages that does not.
- **A field is checked on click**. A finish button is never disabled because the
  input is not valid: that says nothing of what is missing, has low contrast and cannot be focused. A
  click checks the fields, shows under each one that is wrong what to do, such as "Enter a name.", and
  focuses the first. A message goes once the field is put right. A button is disabled only while its
  action runs, while there is nothing to save, or while the screen cannot send it, as during
  [Studio's reconnect](studio.md#reconnect). This holds for every form and dialog, as it does for the form widget's Save. A confirmation
  that asks the user to type a name before a destructive action keeps its button off until the name
  matches, as [the cell console's Delete](cell-console.md#actions) does.
- **A group's title lines up with its fields** _(planned, #5049)_. A group's title, such as Entity label, starts at the inset
  of a field's label, so the fields of a section line up whether they are one field or a group. The group's help sits
  under its title, and its action, such as Add validation, sits to the right of the title and the help together, so
  the action does not push the help away.
- **Help sits close to its label** _(planned, #5049)_. A field's help sits just under the label's line and `spacing-xs` above
  the input. The gap is judged in IBM Plex Sans, the app's font, as the space between lines differs from font to font.
  Every help line is one size, in `textMuted`, and a choice's option help is one too.
- **A box in error looks it**. A field a screen holds, whose value the screen refuses, is drawn in its error look, with
  its error in `dangerText` under it and `aria-invalid` on the input. `AxisField` takes `error` for it, `AxisFieldGroup`
  takes `error` and `LabelTexts` takes `invalid`. An error shows from the refused action on, and while the value stays
  wrong. It goes as soon as the value is right. A record form marks each field the server refuses the same way, and a
  cell of a child collection too, see [saving](form-widget.md#saving).
- **A box in error looks it in the forms that draw their own error line**. A form that draws its own error
  line, such as the text table, gives a box in error the same error look and `aria-invalid` too, as the text
  table's **New key** box does.
- **AxisFieldGroup's required and fold** _(planned, #5049)_. `AxisFieldGroup` also takes `required` and `fold`, for #3264's build.
- **One way to the first error**. A refused action that submits what a person typed, such as Save, Add, Done or Next,
  moves the focus to the first box in error and scrolls it into view. `AxisForm` sets
  `scrollToFirstError={{ focus: true }}`, as the form widget and the filter bar do, and a Vitest check refuses a form
  in `web/src` that does not, the sign-in pages included. A button that sends a dialog's form submits the form, so a refused click focuses the field. A file
  picker cannot take the focus, so its dialog focuses its Choose button.
- **A screen that holds its fields focuses its first error**. Such a screen calls `focusFirstError` after a refused
  action, such as Done, so the focus moves to the first box in error and scrolls it into view. The resource form's
  item dialog does this, and so does the record form after a refused save or Next, see [saving](form-widget.md#saving).
- **One look for yes or no** _(planned, #5049)_. A yes-or-no setting is a checkbox with its label on its right and its help under
  the label, the same in a form as on a screen that holds its fields. A switch is for a setting that takes effect as it
  is turned on, and no other.
- **A pair's help** _(planned, #5049)_. A pair of fields in `AxisFieldRow` takes one title and one help, and its fields take no
  help of their own, so their boxes line up.
- **A radio group is a value of a form**. A radio group holds one value of a form. It is kept with the form
  and saves with it, as the form's other values do.

## Messages

A message tells a person what happened or what to do next. This section says where each kind of message sits, so a
person reads it before the content it is about.

- **A message about a page, section, form or dialog comes first.** It sits above the content it is about, whatever its
  kind: an error, a warning, an info or a success. A person reads it before what it is about. The error summaries of
  GOV.UK and the CMS design system sit at the top in the same way.
- **A form's message goes to `AxisForm`.** `AxisForm`'s `message` draws it first, `spacing-lg` above the first field, as
  [which widget](shared-widgets.md#which-widget) says. A Vitest check refuses an `AxisAlert` among a form's children, so
  no form draws its message after its fields. A site's form shows its own message above the form, see
  [saving](form-widget.md#saving).
- **A dialog's message.** A dialog with a form gives its message to the form, as above. A dialog with no form draws its
  message first in its body, above the text.
- **A table's message goes through `AxisTable`.** `below` draws it under the toolbar and the filter chips, above the
  rows. `failure` draws it in place of the rows, and the toolbar stays. See [tables](tables.md#tables).
- **A field's and a row's message.** A field's message stays under its field, see [fields](#fields). A row's message
  stays in its row.
- **A section's or a widget's message.** It goes to `SectionPart`'s `message`, which draws it under the heading and
  `spacing-md` above the content. A chart's note about more groups is one such message. A Vitest check refuses an
  `AxisAlert` among a section's direct children, so no section draws its message after its content. A branch of
  `? :` is allowed, as it stands in place of the content. [Which widget](shared-widgets.md#which-widget) names
  `SectionPart`'s message.
- **An AI request's step.** The Result step puts the accept's status or its conflict first in its body, above the
  checks and the review.
- **A Studio editor's save messages** _(planned, #4253)_. Where they sit is part of the save bar's review.
- **The policy matrix's save messages** _(planned, #5058)_. Where they sit is part of the matrix's review.

## Tab bars

A tab bar sits `spacing-md` under what precedes it, except a page's tab row, see below. The theme starts a
tab's label at the top of its bar, with the underline right under the label, for every tab bar, because Ant
Design's padding above the label reads as a gap too wide.

- **One tab row look.** Every tab row of a workspace has the same padding and a rule the region's width,
  whether it holds a file's tabs or a view switch.
- **A page's tab row.** A tab row on a page, such as a Studio resource's Design, Form, JSON and Used by, has the
  same look. Its rule reaches the page's edges, past the content area's padding. The row sits `spacing-sm` into the
  gap above it, and its content starts `spacing-md` under its rule.
- **Tabs**. Tabs switch between areas or views of one content, such as Form, JSON and Used by.
- **A segmented control**. It is one choice of two to five options that changes what shows in place, at
  once, such as a chart's period. It never holds an action.
- **A group of buttons never shows a selection**. A chosen item of choices drawn as buttons is on the
  selected tint in `primaryText`, through `AxisButton`'s `selected`, never by a heavier role.
- **A form's steps on the steps bar**. A form's steps are picked on the form widget's own steps bar, on the
  canvas, not in a switch above it, see [steps bar](form-designer.md#steps-bar). The steps show once.

## Text

These rules hold for every word the SPA shows. How a text is worded is in [UI writing](ui-writing.md).

- **No technical name.** What the UI shows is a text, never an internal code, a resource's file or a
  key: a label, else a name an author gave, see [names](studio-workspace.md#names).
- **No middle dot as a separator.** Peers join as words do, with a comma, as in "2 errors, 1 warning".
  A state is a sentence, as in "Release 7 is live". Facts of different kinds take a column or a line
  of their own, such as when a release was published and by whom. A "·" glues facts of different
  kinds into one line with no order, and a screen reader may read it out.
- **No dash between facts either**. Neither a middle dot nor a dash joins facts. A fact of a different
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
- **A person by name**. A person is always named, so a page read by someone else, or seen in a screenshot,
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
  pointer to nothing a person can open is its file, in muted text. See [opening things](#opening-things).
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
  other action beside it is a subtle button, except a region's main action, which keeps its border.

The reasons are in [D152](../decisions/d152-every-screen-says-where-things-stand-with-at-most-one-main-action.md).

## Layout shows structure

How a list is drawn shows what it is: its items, its groups and its order.

- **A list is drawn as a list.** Items that a person reads one by one sit in a list, not in a line joined
  by a middle dot, as [text](#text) says.
- **Bulleted lists use one style.** `useBulletList` draws every bulleted list, so each keeps one look. An item
  that is a sentence gets a filled bullet in the `text` colour, at any depth. The bullets sit about `spacing-md` in
  from the start of the line above, the words start at `spacing-xl`, and a wrapped line sits under the words. An item
  led by a mark of its own, such as a problem's severity tag, keeps its bullet, and the mark follows it. A list of
  rows that are not sentences, such as notices, links or an outline, draws no bullet, as indenting alone never makes
  something an item of a list.
- **Order.** A list keeps the order that [list order](#list-order) gives it.

## Opening things

A thing opens at one of three levels, chosen by what the person does, never by taste.

- **Opening a record is a page.** Every thing with an identity of its own, such as a record, a Studio
  resource, a release, a user or a process instance, opens from its table's identifying cell, a real
  link, as a page with its own address. So a link can be shared, reloaded, gone back to and opened in a
  new tab, and the page has room to grow.
- **A side panel goes with a page and never opens a record.** It is for what accompanies the page: the
  problems panel, filters or help, and a part of what the page edits, as the rule below says.
- **A part of what the page edits opens in the side panel** _(planned, #5059)_. A part is a field, validation or index of an
  entity, an application's module or package, or any item of a [list of objects](studio-resource-form.md#lists-of-objects).
  Its name is a real link to the panel's address, which opens the panel in place on a plain click and a new tab on
  Ctrl+click, and its address can be copied. The panel holds its edits until Done, or Add for a new part, with Cancel
  beside it in the panel's bar. One platform part draws every such link.
- **A modal dialog is for a short task that must end before going on:** confirming, such as Activate…
  or Delete…, asking for a little, such as a reason, a name or an invitation, or choosing one thing. It
  never holds a record's detail or a long form, and never opens a dialog of its own. What needs much
  scrolling, or to be come back to, is a page.
- **A dialog never edits a part of the page** _(planned, #5059)_. A part opens in the side panel instead, so a dialog never
  edits a field, a validation, an index or a list's item.
- **A reference opens what it names**. A record's label, shown read-only, is a real link to its record page,
  with an `href` so Ctrl+click opens a new tab. The link passes the page it leaves as `from`, see
  [shell layout](shell.md#layout). It never opens a dialog. A record the person cannot open is plain text.
- **A link goes to an address.** A link is a real `<a href>` or the router's link, so it opens in a new tab and its
  address can be copied. It is in `primaryText`. An action with no address is a button, never a link.
- **A link is underlined**. A link in running text, which is a sentence that `FilledText` fills, is always underlined.
  Any other link stands alone and is underlined only on hover. A menu's entries and a button with an address are never underlined.
- **What opens a view gets an address.** A resource named in Problems, test results or a form's Used on is a
  real link to the resource at its path, and a shared grant's Open policy links to the policy. Ctrl+click opens
  either in a new tab. See [Studio's route and address](studio.md#route-and-address).
- **A field's name gets an address.** A field's name links to `&field=<name>`, and a plain click opens the field in
  place, as [entity designer](entity-designer.md#fields) says.
- **A list's first cell gets an address** _(planned, #5059)_. A list of objects' first cell links to `&item=<pointer>`, which
  opens the item.
- **A package file's place is a link.** A package file's place, such as `lib/x.py, line 3`, links to
  `&view=packages&package=<id>&file=<path>&line=<n>`, which opens the file at that line. See
  [Studio's route and address](studio.md#route-and-address).
- **A reference reads as words.** A resource's name and its muted kind are inline text, so a reference
  follows the words before it on their line and wraps as words do.
- **A link in a value opens in the same site**. A reference in a value is an inline link only when the
  thing it names opens in the same site, such as a record, an instance, a sub-process or a step's run. A thing in
  another site, such as Studio, opens only from a button named for it, such as Open in Studio. So a call's connector
  and operation are plain text.
- **Studio's and the platform's references**. A user or a tenant that a Studio or platform page names is a link to
  its page where the reader may open it, and plain text otherwise. On a page outside Studio, such as the console
  or the user page, an application or a release is not a link in a value: a reader who may use Studio opens it from
  an Open in Studio button, and anyone else sees plain text. A Studio reference picker whose chosen resource exists
  has an Open button joined to it, which opens that resource in Studio.

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
- **The shell's regions are scroll areas** _(planned, #5065)_. The window does not scroll. The sidebar's menu, the page's frame
  and a side panel each scroll on their own and stop at their end, see [layout](shell.md#layout).
- **One scroll per pane** _(planned, #5065)_. A pane scrolls as one from top to bottom, however many blocks it holds. A code
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
  diff. A table that lets a person sort follows [sort](tables.md#tables). The `table` widget keeps its own rules.

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
