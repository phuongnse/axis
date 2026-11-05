# D65. A visual page designer on a canvas drawn by the site's widgets

Status: **Agreed**

## Context

An author who cannot write JSON must be able to lay out a [page](d15-presentation-model.md). A
designer that draws pages with renderers of its own drifts from what the site shows. Judging a draft,
which widgets compile and what metadata they get, takes the compiler, which runs on the server. And a
page is a file in git, so the designer must write the same format as the Form and JSON tabs.

## Decision

- **A canvas drawn by the site's own widgets.** A page opens on a Design tab, next to its Form and JSON
  tabs, and the three tabs edit one draft. The canvas draws it with the site's own widget components
  and placeholder data made from the field types, and its actions run nothing.
- **The server judges the draft.** A draft page metadata route compiles the draft in memory against
  the saved folder and answers its metadata, each widget with its JSON Pointer, and its diagnostics.
  It takes no lock and reads no records, so the author's policies never filter the canvas. The SPA has
  no compiler of its own.
- **The designer writes only the format.** It writes the order of the widget tree and the properties
  the format has, never a position or a size in pixels, and the page limits of
  [D61](d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md) hold.
- **Everything is bound by drag and drop, and by keyboard.** A palette adds widgets, a data panel binds
  one to an entity or a data source as [D64](d64-detail-chart-and-kpi-widgets.md) allows, and each
  action opens in the property panel, which maps a navigate action's parameters as
  [D63](d63-navigate-actions-and-page-parameters.md) sets out. Every insert and move has a keyboard
  way that a screen reader announces.
- **Undo covers the draft** in the three tabs, from a history in memory that a save keeps and a reload
  clears.
- **Drag and drop comes from the stable `@dnd-kit` packages,** in a lazy chunk that the designers share.

The screens are in [Studio](../reference/studio.md) and the route in
[draft page metadata](../reference/draft-page-metadata.md).

## Consequences

- The author sees the page the user gets, from one set of widget renderers.
- The route compiles on each pause in editing, which a short delay and the in-memory compile keep
  cheap, under a speed target that a performance test holds.
- Placeholder data needs no tenant, but the canvas never shows real records.
- Pages from the three tabs are the same files, so the format and the committed samples are unchanged.

## Alternatives

- **Renderers of the canvas's own.** They would drift from what the site draws.
- **A compiler in TypeScript in the SPA.** It would be a second compiler to keep in step.
- **Real records on the canvas.** They would need a tenant, and the author's policies would filter
  them.
- **Native HTML drag and drop.** It has no keyboard or touch support. Pragmatic drag and drop leaves
  keyboard support to the application, and `@dnd-kit/react` is not yet at 1.0.
