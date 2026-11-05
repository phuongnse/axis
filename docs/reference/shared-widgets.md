# Shared widgets

Which shared widget does which job in the SPA, and the working rule behind it. The tokens are in
[design tokens and previews](frontend.md), the buttons in [actions and controls](actions-and-controls.md#buttons).

## The rule

One concern has one mechanism. Before you add a prop, a context, a helper or a local variant, search for what already does
the job, in `web/src/platform/`, `web/src/designSystem/` and the server, and extend that one.

- Never copy a component or its logic, fork it with a second way to the same result, or work round it in the caller.
- A caller that must build what its shared widget should know, such as the label of an option, shows that the widget or
  its data is incomplete. Fix it there, so every caller gets it.
- If a second mechanism already exists, merge the two in the same change.

## Which widget

- **A button** is `AxisButton` with a role, never Ant Design's `Button`.
- **A table** is `AxisTable`, never Ant Design's `Table`.
- **A tag's colour** comes from the colour tokens, never from a preset name such as `blue` or `success`.
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

The `tag` rule refuses a `Tag` whose `color` is an Ant Design colour name, with or without `-inverse` (such as `blue` or
`blue-inverse`), or a status preset (`success`, `error`, `warning`, `processing` or `default`). It counts a preset written
as a string or inside a `color={…}` expression, even when Prettier wrapped the props onto later lines. It cannot see a
colour picked from a lookup table. The `button` and `table` rules match within one line, so they miss a tag whose name ends
its line.
