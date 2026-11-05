# Shared widgets

Which shared widget does which job in the SPA. Before a page, a designer or a widget draws something, it looks here and
extends the widget that does the job, as the [working rule](../../AGENTS.md#working-rules) "One concern has one mechanism"
says. A thing that a shared widget lacks is added to the widget, so every caller has it, and a second way to the same
result is merged into the first. The tokens are in [design tokens and previews](frontend.md), the buttons in
[actions and controls](actions-and-controls.md#buttons).

## Which widget

- **A button** is `AxisButton` with a role, never Ant Design's `Button`.
- **A table** is `AxisTable`, never Ant Design's `Table`.
- **A tag's colour** comes from the colour tokens, never from a preset name such as `blue`.
- **A choice of a name that an application defines** is `ReferencePicker`. Its choices come from `referenceOptions`, so
  each is named by its label.
- **A value with nothing to show** is `EmptyDash`.
- **Words** come from `useText`, and a size, a space or a colour from `useDesignTokens`.
- **The current page of a pagination** is marked by `markCurrentPage`.

## What the build checks

`scripts/check-shared-widgets.mjs`, tested by `scripts/check-shared-widgets.test.mjs`, fails `scripts/lint.sh` when a file
under `web/src` uses Ant Design's `Button` or `Table`, or a preset tag colour, more often than
`scripts/shared-widgets-baseline.json` allows. The baseline counts the code that predates the check, by file and rule. It
only goes down: a file that uses a rule less than its baseline must lower it. A test file and the shared widget itself are
not checked. A change that moves a file off the baseline writes the new one with
`node scripts/check-shared-widgets.mjs --write-baseline`.
