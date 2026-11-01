# Actions and controls

Detailed reference for how the SPA draws buttons, menus and form fields, the same in every widget, page, dialog and
in the identity provider's pages. The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md). The tokens named here are in
[design tokens and previews](frontend.md).

## Buttons

A button's level follows its role, so the same kind of action always has the same style
([D96](../decisions/d96-rules-from-the-design-system-review.md)). Its place decides the rest
([D128](../decisions/d128-rules-from-the-studio-review.md)) _(planned for M31)_.

- _Finish_: solid primary. The one action that finishes a task, such as Save or Next, one per screen.
- _Main_: outlined in `primaryText`. The main action of a region: a table's first toolbar action that is shown, such
  as New request, Add row of a child collection, and a `startProcess` action on a form or a detail.
- _Secondary_: the default grey outline, such as Cancel and Import.
- _Icon_: a text button with only an icon, for small actions such as Refresh. Choose and Clear of a `reference` field
  are bordered and joined.
- _Destructive_: in `dangerText` at any level, such as Remove row and Delete.
- Ant Design writes the text of text, outlined and danger buttons in the fill colours `primary` and `danger`, which
  fail contrast on a dark surface, so Axis writes them in `primaryText` and `dangerText`. One shared Axis button,
  `AxisButton` in `web/src/platform/`, takes the role, and each widget names a role, never an Ant Design button type.
  The table's toolbar, its icon buttons and the reference, set and file field buttons use it.
- **Beside a solid button every other action is a text button** _(planned for M31)_. In a dialog, a form's footer
  and a band, Cancel and every secondary, main or destructive action next to the finish button is a text button in its
  own colour, so the finish button is the one filled shape. Every Ant Design dialog draws Cancel this way through the
  theme, and `AxisButton`'s action row does it for the form widget's footer. A main action with no solid button beside
  it, such as a table's first toolbar action, stays outlined.
- **Text or an icon, never both** _(planned for M31)_. An action shows its text or its icon. An icon goes with text
  only where it does what text cannot: navigation that collapses to icons, a status or a severity, and the mark that
  identifies an application or a site.
- **An action that asks for more ends in an ellipsis** _(planned for M31)_, such as New application…, New resource…,
  Publish…, Import…, Invite…, Reassign… and Choose…, so the person knows a dialog follows. The dialog's own button
  keeps the bare verb, such as Invite.
- **Icon-only buttons.** Only for actions repeated on every row, or where space is tight. Main toolbar actions keep
  their text. Every icon-only button shows its name as a tooltip on hover and on keyboard focus, and has the same text
  as its accessible name, from the text resources. Configuration never names an icon, so the platform gives an icon
  only to an action whose meaning it knows. At narrow width row actions fold into a menu of text entries, so touch
  users lose nothing.

## Menus

- **Selected and current states.** The selected tab, the active menu entry and the current page of a pagination use
  `primaryText`. Destructive menu entries use `dangerText`. A menu that marks a choice, such as the open application
  in Studio's application menu, draws it in `primaryText` on the selected tint, never in Ant Design's selected colour,
  which is hard to read in dark mode _(planned for M31)_.
- **A text trigger ends in a caret** _(planned for M31)_. A button with text that opens a menu, such as the account
  button and Studio's application switcher, ends in a caret. An icon-only trigger has none.
- **Actions come after the choices** _(planned for M31)_. In a menu that holds both, such as the application menu,
  the choices come first and an action, such as New application…, follows them.

## Fields

These rules hold for every Ant Design form in the SPA and in the identity provider's pages, through one setting on the
shared `ConfigProvider`, so no widget keeps its own copy _(planned for M31)_.

- **A field's error keeps the gap.** The message sits 4 px under its control, in 14/20 text, and the field keeps its
  own gap below the message: `spacing-lg` in a form, none in a table cell or an inline form.
- **A box sits on its label's first line.** A checkbox or a radio keeps its box level with the first line of its
  label, so a label that wraps leaves the box at the top. A one-line label looks as before.
- **An error border keeps 3:1.** An input, number input, select, date picker, mentions, cascader or tree select with
  an error, and an unticked checkbox or radio in a field with an error, take `dangerText` for their border, the colour
  of the field's message. The `danger` fill stays for fills such as a danger button, whose white text needs it.
