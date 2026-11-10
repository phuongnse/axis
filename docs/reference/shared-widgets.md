# Shared widgets

This page says which shared widget does which job in the SPA, the working rule behind them and the
check that holds the code to it. The tokens are in [design tokens and previews](frontend.md#design-tokens-and-previews), and how
buttons, tables and fields look is in [actions and controls](actions-and-controls.md).

## The rule

One concern has one mechanism, because two ways to the same result drift apart and each fix reaches
only one of them. Before you add a prop, a context, a helper or a local variant, search for what
already does the job, in `web/src/platform/`, `web/src/designSystem/` and the server, and extend that
one.

- Never copy a component or its logic, fork it with a second way to the same result, or work round it
  in the caller.
- A caller that must build what its shared widget should know, such as the label of an option, shows
  that the widget or its data is incomplete. Fix it there, so every caller gets it.
- If a second mechanism already exists, merge the two in the same change.
- A new shared widget or helper gets its row in [which widget](#which-widget) in the same change.
- **A shared widget keeps no space around itself** _(planned for M33)_. It draws no margin outside
  itself, and the screen that places it gives the space, so the widget sits in a card's gap as in a
  form's layout.

## Which widget

Each common job has one shared widget or helper:

| Job                                            | Shared widget                                                                                          |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| A button                                       | `AxisButton` with a role, never Ant Design's `Button`                                                  |
| A message on a page, a form or a dialog        | `AxisAlert` with a kind, which always shows the kind's icon, never Ant Design's `Alert`                |
| A form's messages from a refused request       | `placeProblemErrors`                                                                                   |
| A table                                        | `AxisTable`, never Ant Design's `Table`                                                                |
| An empty table or list                         | `EmptyState`, never Ant Design's `Empty`                                                               |
| A tag's colour                                 | The colour tokens, never a preset name such as `blue` or `success`                                     |
| A choice of a name that an application defines | `ReferencePicker`, whose choices come from `referenceOptions`, each named by its label                 |
| A value with nothing to show                   | `EmptyDash`                                                                                            |
| A field of a platform screen                   | `AxisField` and `AxisFieldGroup`, with `AxisCheckboxField`, `AxisChoiceField` and `AxisFieldRow` _(planned for M31)_, and a `status` for what the screen found about a field |
| A row of actions                               | `AxisActionRow`                                                                                        |
| A text in the author's language                | `textIn`, else `en` _(planned for M31)_                                                                |
| The address of a Studio resource               | `studioResourceAddress`                                                                                |
| Asking before unsaved changes are left         | `useUnsavedChanges`, which `SaveBar` calls                                                            |
| An expression in an editor                     | `ExpressionEditor`, through `ExpressionField` _(planned for M31)_                                      |
| Words                                          | `useText`                                                                                              |
| A size, a space or a colour                    | `useDesignTokens`                                                                                      |
| A dialog's width                               | A dialog size token, `small`, `medium` or `large`, through `useDesignTokens`, never a width of its own |
| The current page of a pagination               | `markCurrentPage`                                                                                      |
| A page that is one card, as wide as the `small` dialog size token and with no size of its own | `CardPage`, with the organization's mark and name, the language menu and the legal line _(planned for M33)_ |
| A required box that accepts terms, each document a link | `AcceptTermsField` _(planned for M33)_ |
| A sentence with parts in it, such as a name in bold or a link | `FilledText`, which fills a text's `{name}` placeholders so a sentence keeps its word order in every language |
| The steps of a wizard or a set-up | `StepsBar`, with `vertical` for one step on each line |
| The application a new tenant or a new application starts from | `StartFromField`, with Blank first _(planned for M33)_ |
| A message that only a screen reader hears      | `Announcement`, a polite status region built on `visuallyHidden`                                       |
| Asking again until a job ends                  | `useFollowJob`, which `useFollowCellJob` wraps                                                         |

## What the build checks

Oxlint in `npm run lint --prefix web`, which `scripts/lint.sh` runs, fails on each way round a shared
widget. The plugin rules are in `web/lint/axis-plugin.mjs` and are on in `web/.oxlintrc.json`. Test
files are not checked. The plugin's rule for the end-to-end tests is on in `tests/e2e/.oxlintrc.json`.

- **`axis/no-antd-button`** refuses a `<Button>` imported from `antd`. `AxisButton.tsx` is the one
  file that may use it.
- **`axis/no-antd-alert`** refuses an `<Alert>` imported from `antd`. `AxisAlert.tsx` is the one file
  that may use it.
- **`no-restricted-imports`** refuses a named `Table` import from `antd`. `AxisTable.tsx` is the one
  file that may use it. It does not see an import from a deep path such as `antd/es/table`.
- **`axis/no-preset-tag-colour`** refuses a `Tag` whose `color` is an Ant Design colour name, with or
  without `-inverse` (such as `blue` or `blue-inverse`), or a status preset (`success`, `error`,
  `warning`, `processing` or `default`). It sees a preset written as a string or inside a `color={…}`
  expression. It cannot see a colour picked from a lookup table.
- **An older use carries its reason.** Each `Button`, `Alert` and `Tag` that predates the rules has an
  `oxlint-disable-next-line <rule> -- <reason>` comment on the line before it. With
  `--report-unused-disable-directives`, a comment that its line no longer needs fails the lint, so the
  older uses only go down.
