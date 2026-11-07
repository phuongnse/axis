# D66. A visual form designer for form resources

Status: **Agreed**

## Context

A [form resource](d20-human-tasks-task-inbox-and-forms.md) lays out an entity's fields in sections,
and in steps with conditions as
[D62](d62-conditional-form-states-validation-as-you-type-and-wizard-steps.md) sets out. An author
needs to lay it out as visually as a page in
[D65](d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md). But a canvas has no
record to evaluate a condition against, and one form can serve several pages and tasks, so one edit
reaches all of them.

## Decision

- **The page designer's way, for forms.** A form opens on a Design tab, next to its Form and JSON
  tabs, and reuses the canvas, property panel, undo and drag and drop of D65. The canvas draws the
  draft with the site's own form widget and placeholder values, one step at a time.
- **One draft route for pages and forms.** The draft page metadata route of D65 also answers a draft
  form, with the metadata of a `form` widget that names it and the JSON Pointer of each section, field
  and step.
- **Conditions show as text.** The canvas evaluates no condition, so every element shows, and one
  with a condition carries a mark with the condition's name and text.
- **Every edit keeps the form valid.** The field palette offers only the fields not on the form yet,
  and a drop never copies one, so no field shows twice. Adding the first step names every unnamed
  section and puts them all into it. The property panel keeps the rules of D62.
- **The author sees how far an edit reaches.** The Design tab lists the pages and process steps that
  use the form, from the [reference index](d141-the-reference-index-and-what-uses-a-resource.md).

The screen is in the [form designer](../reference/form-designer.md) and the route in
[draft page metadata](../reference/draft-page-metadata.md).

## Consequences

- The author sees the form the user gets, with no second compiler in the SPA.
- The marks need no tenant and no form state endpoint, but the canvas cannot show what a condition
  does to a record.
- The list of users of a form needs no new route, and it can be stale as the `usedBy` counts can.
- One route and one canvas serve both designers, at the cost of a route that answers two kinds of
  resource.

## Alternatives

- **Evaluate conditions on sample values.** The author would type values and the form state endpoint
  would answer the states, but that needs a tenant and a record.
