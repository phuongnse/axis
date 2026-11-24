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
`</script` or `<!--`, no font, at most 6 MB, and no HTML file in the folder. It warns when the bundle is within
256 KB of the 6 MB cap, so a shrinking margin is seen before a change fails. It needs no Playwright browser and
is not a NexKit check. The local agent session publishes the bundle as the Design System artifact.
The bundle carries its own React and never uses one that the page provides, because React 19 ships no
UMD build.

- **What it leaves out.** The bundle loads none of the JSON Schema validators that Studio's JSON view
  checks a resource against, since no preview state needs them. The JSON tab of a Studio preview
  therefore shows JSON syntax problems only, while the SPA checks the schema too.
- **Mounting.** The bundle sets `window.Axis`. `window.Axis.<name>(element)` mounts one preview into an
  element and returns a function that unmounts it. A preview draws each of its states under its caption
  in one card, except a preview whose states answer the same fixture paths, or whose several states
  are each a screen in a frame. That preview draws one state at a time under the State control, see
  [D142](../decisions/d142-widget-previews-show-the-states-a-design-change-touches-one-at-a-time-where-they-share-fixtures.md)
  for the states that share fixtures.
- **The State control.** A preview that draws one state at a time has a State control above the state.
  The control picks the state, and it wraps its captions onto more lines when they do not fit.
  `&state=<slug>` opens one state. `shell`, `signIn`, `identitySignIn` and `studio` use the control.
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
  The dev server also loads the app's fonts, `web/src/fonts.css`, so its previews show IBM Plex Sans. Opened
  from disk, the harness shows the reviewer's system font, because the bundle carries no fonts.

## The cards

The header line names every card once, in the order of the groups in
`web/src/designSystem/cardOrder.ts`. A `field-<type>` preview's card is `<Type>Field`, a
`chart-<kind>` preview's is `Chart<Kind>`, and every other card takes its preview's PascalCase name,
such as `Table` or `TaskInbox`. Every preview has at least one state, and a preview with only one shows
it as "Typical". A state can bring its own fixtures, on its own paths in the fixture application, or fixtures on
paths that its preview's other states answer too, which makes the preview show one state at a time. A
state's fixtures may also answer a path that the shared routes answer, such as `/api/me`. While the state
shows, its answer replaces the shared one, and every other state gets the shared answer. A change that adds a
state to a widget adds that state to the widget's preview in the same change.

| Group | Previews | States | What they show |
| --- | --- | --- | --- |
| Field inputs | `field-text`, `field-integer`, `field-decimal`, `field-boolean`, `field-date`, `field-date-time`, `field-enum`, `field-reference` | "Editable", "Read-only", "Required, help text and error" | The input the form widget uses for each type, with its label and the fixture value, laid out as on a form. The required state shows the required mark, a help text and a fixed message at once. A field preview has no Save, Cancel or action buttons and no history. Its input works as on a form, and nothing is saved. |
| Field inputs | `field-child-collection` | "Reference and computed cells", "Cell error", "Read-only", "Empty" | Line items with a reference cell and a computed cell. Like the other field previews, it has no Save, Cancel or action buttons and no history, and nothing is saved. |
| Data widgets | `table` | "With data", "Empty", "Many row actions" | With data has row and toolbar actions and a long-text row. Many row actions folds a row's actions into the "…" menu. |
| Data widgets | `form` | "Validation errors", "Read-only by a condition", "Wizard steps", "Fields in columns, with errors", "Many steps" | Validation errors submits the form once when it mounts, to a fixture that answers with a validation problem. Fields in columns, with errors has fields of several widths that share rows, and some show messages. Many steps is a wizard with seven steps, so the steps bar shows numbers. |
| Data widgets | `detail` | "With data", "Empty" | The detail widget of a record. With data draws the fixture record, and Empty draws a blank one. |
| Charts and KPIs | `chart-bar`, `chart-line`, `chart-pie`, `kpi` | "With data", "Empty" | The bar, line and pie charts, and the KPI, whose comparison is last month's total. |
| Process widgets | `taskInbox` | "With data", "Empty" | The task inbox. With data has an overdue and a claimed task. |
| Process widgets | `processInspector` | "Typical" | The process inspector, with a failed instance and an open dead letter. |
| Containers | `columns`, `tabs` | "Typical" | A columns layout and a tabs layout, each with its fixture widgets. |
| Containers | `section` | "Typical" | A section that does not fold, then one marked collapsed. |
| Shared states | `states` | "Loading", "Load error" | Drawn once, the states every widget shares: a table while it loads and a table whose request fails. |
| Shared states | `statusTag` | "Typical" | Every tone of the [status tag](shared-widgets.md#which-widget), on `surface` and on `canvas`. |
| Shell | `shell`, `signIn`, `identitySignIn` | see [shell](shell.md#design-system-previews) | Each draws its states in a frame that fills the window down to the canvas's padding and is never shorter than the state's height, one state at a time under the State control. The page scrolls inside the frame, as it does in a window, so the state's menus and dialogs open in the middle of what shows. |
| Studio | `studio` | "Typical", "Empty", "Loading", "Failed", "Many rows", "Long names", "Access denied", "Overview with errors and changes not live", "Overview of a new application", "Entity", "Entity with expressions", "Policy matrix", "Releases", "History", "Revision", "Release page", "Package file revision", "Authoring request", "Publish page" | Studio in the shell, in nineteen states that share the `/api/studio/apps` path, so it shows one state at a time. Entity with expressions opens an entity whose computed field and validation read their expressions back in words, over Typical's fixtures. Revision opens one revision's page over Typical's fixtures, and Release page opens release 2 over them. Package file revision opens revision r4 of Typical, an outside change to the files of the `approval-rules` package. Authoring request opens a ready AI request of the signed-in author, whose draft changes the same files. Publish page opens the publish page for a person who may publish, with the files that differ from the live release. Each framed state fills the window down to the canvas's padding and is never shorter than the state's height. |
| Studio | `studioPageDesign`, `studioFormDesign`, `studioProcessDesign` | "Typical" | Each opens a page, a form or a process in its designer, over the fixtures of `studio`'s Typical state. |
| Sign-up | `signUp` | "Typical" | The [sign-up page](sign-up.md#the-page) over fixtures, in a frame that fills the window down to the canvas's padding and is never shorter than the state's height. |

## The spec

`tests/e2e/specs/design-system.spec.ts` mounts each preview from the bundle in the harness, in both
modes, in a 992 px frame sandboxed without `allow-forms`, as the artifact draws it. It fails on a
console error, on a request other than the page that holds the frame, the harness and the bundle, and
on sideways scroll. It also proves that each card's states show, that the cards are named in order,
that a mounted widget follows `data-theme` and `--primary` and a validation message `--dangerText`,
that a form submits inside the sandbox, and that no fixture reaches the server's web root. It clicks
through Studio's sidebar in the `studio` preview, opens a state with `&state=` and picks each framed state in the
State control. It also checks that this control wraps its captions onto more lines in a 360 px frame.
It proves that each framed state's screen is as tall as its frame, with the page scrolling inside it. It proves
that a framed `studio` state fills the window down to the canvas's padding, is never shorter than its height and
follows a resize. It also proves that a dialog opened in the scrolled Entity state of `studio` shows inside the
frame. It also proves that the `studio` preview's Settings page and its Entity state draw no panel, and that each
part of them has no background, border, padding or radius.
A unit test compares the groups, the previews and the state captions of the cards table with `cardOrder.ts`
and `previews.tsx`. The Shell group's rows link to [shell](shell.md#design-system-previews) for their states, which
that page lists.

`tests/e2e/specs/design-system-contrast.spec.ts` opens every preview in light and in dark mode, and sweeps each of its
states. It checks text at 4.5:1, or 3:1 when the text is large: 24 px, or 18.66 px and bold. The background is the
nearest opaque layer behind the text, with any translucent layer above it blended in. It checks the border of each
input, select, checkbox and radio, and each icon, at 3:1. It skips disabled controls, `aria-hidden` elements, and text
or icons that draw nothing: fully transparent colours and visually hidden text. The arithmetic is in
`web/src/test/contrast.ts`, the helper the Vitest colour tests use too. Its unit test is
`tests/e2e/contrast.test.ts`.

`tests/e2e/contrast-baseline.json` lists the known misses, by preview, state, mode and element. The list only goes
down. A new miss fails the test. A listed miss that no longer misses also fails the test, until its entry is removed.
