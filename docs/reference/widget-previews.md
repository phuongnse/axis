# Widget previews

The design system has a live preview of every widget, drawn by the real widget components in light
and dark mode, so the Design System artifact in Claude shows what the SPA draws, see
[D67](../decisions/d67-live-widget-previews-in-the-design-system.md) and
[design changes](../delivery.md#design-changes). The previews are one bundle, built from
`web/src/designSystem/`. It feeds fixture page metadata to the widgets and answers their API calls
from fixtures, only for the API paths of the fixture application, so it needs no server, database or
sign-in. Every other request goes to the page's own fetch, unchanged.

## The bundle

`scripts/design-system.sh` builds the bundle into `artifacts/design-system/axis-design-system.js` and
checks it with `scripts/check-design-system.mjs`: one classic script, no `import`, no literal
`</script` or `<!--`, at most 6 MB, and no HTML file in the folder. It needs no Playwright browser and
is not a NexKit check. The local agent session publishes the bundle as the Design System artifact.
The bundle carries its own React and never uses one that the page provides, because React 19 ships no
UMD build.

- **Mounting.** The bundle sets `window.Axis`. `window.Axis.<name>(element)` mounts one preview into an
  element. It draws every state of the preview in one card, each under its caption, and returns a
  function that unmounts them all. A preview whose states answer the same fixture paths draws one
  state at a time, under a Fixtures control that picks it, see
  [D142](../decisions/d142-widget-previews-show-the-states-a-design-change-touches-one-at-a-time-where-they-share-fixtures.md).
- **Style sheets.** The bundle is one file, so the style sheet of a library it imports, such as React
  Flow's, becomes a style element that the script adds to the page.
- **Theme.** A mounted widget follows `data-theme` on the root element of the page: `dark` is dark
  mode, and any other value or none is light mode. It also follows the token custom properties of the
  root element, such as `--primary`, see [frontend](frontend.md).
- **Forms in a sandbox.** The Design System artifact draws each card in a frame sandboxed without
  `allow-forms`, where the browser blocks a form's own submission before its submit event fires. The
  bundle therefore fires a form's submit event itself, for a click on a submit button, Enter in an
  input and `requestSubmit()`, and cancels the browser's submission.
- **The header line.** The bundle's first line is
  `/* @ds-bundle: {"format":4,"namespace":"Axis","components":[{"name":"TextField"},...]} */`, and the
  Design System artifact reads it. The namespace is `Axis`, and the artifact reads each card from
  `window.Axis`. `scripts/design-system.sh` writes the line with `scripts/design-system-header.mjs`
  after the build, so a sync keeps it. `scripts/check-design-system.mjs` rejects a bundle whose line
  has no format 4, another namespace, or a card missing, named twice or matching no preview.
- **Card width.** Each card's own marker in the artifact sets `width=992`, the
  [narrow width](frontend.md) breakpoint. The artifact scales the card to fit its frame, so
  every card shares one scale and shows the desktop layout. A card never scrolls sideways at that
  width. The states of a card share one `minmax(0, 1fr)` column, so a wide table scrolls inside its
  widget, as it does on a page.
- **The harness.** `web/design-system.html` is the harness. After `scripts/design-system.sh`, open it
  from disk with the built bundle. Under the Vite dev server, the same page loads `bundle.ts` from
  source, see [widget previews](../development.md#widget-previews). With no query it mounts every
  preview, and `?preview=<name>&mode=light|dark` mounts one preview in that mode.
  `&state=<slug>` opens a preview that shows one state at a time on that state, the caption in kebab
  case, such as `?preview=studio&state=access-denied`. With no `state`, it opens on the first state.

## The cards

The header line names every card once, in the order of the groups in
`web/src/designSystem/cardOrder.ts`. A `field-<type>` preview's card is `<Type>Field`, a
`chart-<kind>` preview's is `Chart<Kind>`, and every other card takes its preview's PascalCase name,
such as `Table` or `TaskInbox`. Every preview has at least one state, and a preview with only one shows
it as "Typical". A state can bring its own fixtures, on its own paths in the fixture application, or fixtures on
paths that its preview's other states answer too, which makes the preview show one state at a time. A
change that adds a state to a widget adds that state to the widget's preview in the same change.

| Group           | Previews and their states                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Field inputs    | `field-<type>` for each field type: the input the form widget uses for that type, with its label and the fixture value, laid out as on a form, in three states: Editable, Read-only, and "Required, help text and error", which shows the required mark, a help text and a fixed message at once. `field-child-collection` draws line items with a reference cell and a computed cell, in four states: Reference and computed cells, Cell error, Read-only, and Empty. A field preview has no Save, Cancel or action buttons and no history. Its input works as on a form, and nothing is saved |
| Data widgets    | `table`: With data, with row and toolbar actions and a long-text row, Empty, and Many row actions, which folds a row's actions into the "…" menu. `form`: Validation errors, which submits the form once when it mounts to a fixture that answers with a validation problem, Read-only by a condition, Wizard steps, "Fields in columns, with errors", where fields of several widths share rows and some show messages, and Many steps, a wizard with seven steps, so the steps bar shows numbers. `detail`: With data and Empty                                                               |
| Charts and KPIs | `chart-<kind>` for `bar`, `line` and `pie`, and `kpi`, whose comparison is last month's total: each With data and Empty                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Process widgets | `taskInbox`: With data, with an overdue and a claimed task, and Empty. `processInspector`: one state with a failed instance and an open dead letter                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| Containers      | `columns`, `tabs`, `section`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Shared states   | `states`: drawn once, the states every widget shares, a table while it loads and a table whose request fails                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Shell           | `shell`, `signIn` and `identitySignIn`, see [shell](shell.md#design-system-previews). Each draws its states in a frame at least as tall as its height, which grows to hold the whole shell, so the state's menus and dialogs open inside it                                                                                                                                                                                                                                                                                                                                                     |
| Studio          | `studio`, see [shell](shell.md#design-system-previews). It draws Studio in the shell, in seven states that share the `/api/studio/apps` path, so it shows one state at a time: Typical, Empty, Loading, Failed, Many rows, Long names and Access denied                                                                                                                                                                                                                                                                                                                                         |

- **More Studio previews** _(planned for M31)_. The Studio group also gets `studioPageDesign`,
  `studioFormDesign` and `studioProcessDesign`.

## The spec

`tests/e2e/specs/design-system.spec.ts` mounts each preview from the bundle in the harness, in both
modes, in a 992 px frame sandboxed without `allow-forms`, as the artifact draws it. It fails on a
console error, on a request other than the page that holds the frame, the harness and the bundle, and
on sideways scroll. It also proves that each card's states show, that the cards are named in order,
that a mounted widget follows `data-theme` and `--primary` and a validation message `--dangerText`,
that a form submits inside the sandbox, and that no fixture reaches the server's web root. It clicks
through Studio's sidebar in the `studio` preview, opens a state with `&state=` and picks one in the
Fixtures control. A unit test compares the groups of the cards table with the groups in `cardOrder.ts`.
