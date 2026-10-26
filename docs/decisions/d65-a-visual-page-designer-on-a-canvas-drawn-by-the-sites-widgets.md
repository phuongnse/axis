# D65. A visual page designer on a canvas drawn by the site's widgets

Status: **Agreed**

M13 adds a visual page designer to Studio. An author builds a page by drag and
drop on a canvas that the site's own widgets draw. This builds on
[D15](d15-presentation-model.md),
[D53](d53-typed-editors-and-the-process-graph-in-studio.md),
[D61](d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md),
[D63](d63-navigate-actions-and-page-parameters.md) and
[D64](d64-detail-chart-and-kpi-widgets.md). It is built in M13 and M14.
The owner confirmed these points on issue #1140 on 2026-10-24.

- **Canvas:**
  - The page editor gets a Design tab next to the Form and JSON tabs. The three
    tabs edit one draft. A page opens on the Design tab.
  - The canvas draws the unsaved draft with the site's own widget components.
    There is no second set of renderers, so the author sees what the user gets.
  - The SPA has no compiler of its own.
- **Draft metadata:**
  - A new authoring route takes the unsaved draft page and answers the metadata
    the compiled page would have, with every widget, and the diagnostics of the
    draft. It is `POST /api/studio/apps/{application}/resources/{id}/page-metadata`.
  - It compiles in memory against the saved folder, takes no lock and touches
    no database. It needs Studio access, like every authoring route.
  - Each widget carries `path`, its JSON Pointer in the draft.
  - The Design tab asks again 300 ms after the last edit. The route answers
    within 500 ms on a typical application.
- **Placeholders:**
  - The canvas shows no records. Widgets get placeholder rows and values made
    from the field types, as the widget previews do. Actions on the canvas run
    nothing.
  - The route reads no records, so the author's policies never filter the
    canvas. Its answer lists every action and every writable field.
  - A widget that does not compile yet draws as a placeholder with its
    diagnostics. The rest of the page draws as usual.
- **Width switch:** the canvas draws at desktop width, which fills the canvas
  area, or at phone width, which is 375 px. Phone width is below the 768 px
  breakpoint of D61.
- **Diagnostic marks:** a widget that a diagnostic of the draft names is marked
  on the canvas, as the process graph marks a step. The marks come from the
  route's latest answer, so they follow the unsaved draft.
- **Selection:** a click on the canvas, or a pick in the widget tree, selects a
  widget in both. The property panel beside the canvas shows the widget's
  existing panel, and an edit redraws the canvas.
- **Palette and moves:**
  - A palette lists the widget types. The author drags one onto the canvas,
    before or after a widget or into a container.
  - A keyboard user picks a type and inserts it after the selected widget or
    into the selected container.
  - The author drags a widget to reorder it, or to move it into or out of a
    container. Every move has a keyboard way, and a screen reader hears where
    the widget lands.
  - The designer writes only the order of the widget tree and the properties
    the format has. It never writes a position or a size in pixels. Column
    widths stay in twelfths.
- **Page limits:** the limits of D61 stay. The palette disables a type that the
  page cannot take more of, and says why. These are a second table, a second
  form, task inbox or process inspector, and a container that would go past
  4 levels.
- **Data binding:** a data panel lists the entities and the data sources of the
  application. Dropping an entity on the canvas offers a table, a form or a
  detail bound to it. Dropping a data source offers a table, and a chart or a
  KPI when its shape fits, as D64 sets out.
- **Action binding:** each action draws as its button on the canvas. Selecting
  a button opens its action in the property panel. The panel adds navigate,
  `startProcess` and `delete` actions, and lists the target page's parameters
  to map, as D63 sets out.
- **Undo:**
  - Undo and redo cover every edit of the draft, in the Design, Form and JSON
    tabs. They have buttons and the keys Ctrl+Z and Ctrl+Shift+Z, or Cmd on
    macOS.
  - The history is kept in memory for the open resource, up to 100 steps. A
    step ends when the author leaves a box or edits elsewhere, so typing in one
    box is one step.
  - Studio's undo replaces the browser's own undo inside the editor's boxes, so
    the two never drift apart.
  - A reload, a conflict reload or opening another resource clears the history.
    A save keeps it, so an undo after a save changes the draft and the next
    save writes it.
- **Library:** drag and drop uses `@dnd-kit/core`, `@dnd-kit/sortable` and
  `@dnd-kit/utilities` (MIT) in Studio's lazy chunk. The four `@dnd-kit` packages,
  with `@dnd-kit/accessibility` among them, measure 15.4 KB gzipped together in the lazy
  chunk `assets/DragAndDrop-*.js`, which both designers share. The lazy chunk
  `StudioPageDesign` holds the canvas and the palette. Both chunks load when the
  Design tab opens. `@dnd-kit/sortable` comes with the moves of existing widgets. The pre-1.0 `@dnd-kit/react` is not used. The library has a keyboard
  sensor and screen reader announcements. Native HTML drag and drop has no
  keyboard or touch support. Pragmatic drag and drop leaves keyboard support to
  the application.

_Why:_ a canvas drawn by the site's own widgets shows the author the page the
user gets, and a second set of renderers would drift from it. The browser
cannot judge a draft, because that takes the compiler, so a server route
answers the metadata and the SPA needs no second compiler in TypeScript. The
route reads no records, so the author's policies never filter the canvas, and
placeholder data needs no tenant. Writing only tree order and existing
properties keeps the format unchanged, so pages from the designer, the form and
the JSON tab are the same files. Keeping the undo history across a save lets an
author undo a change after saving it. A history in memory is simple, and a
reload clears it. A stable library with keyboard and screen reader support
keeps the designer usable without a pointer. The cost is a route that compiles
on each pause in typing, which the 300 ms delay and the in-memory compile keep
cheap.
