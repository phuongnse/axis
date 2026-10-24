# D66. A visual form designer for form resources

Status: **Agreed**

M13 adds a visual form designer to Studio. An author lays out a `form` resource
by drag and drop on a canvas that the site's own form widget draws. This builds
on [D20](d20-human-tasks-task-inbox-and-forms.md),
[D52](d52-reference-pickers-label-texts-and-the-expression-editor.md),
[D53](d53-typed-editors-and-the-process-graph-in-studio.md),
[D62](d62-conditional-form-states-validation-as-you-type-and-wizard-steps.md),
[D65](d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md) and
[D39](d39-reference-index-and-where-a-resource-is-used.md). All of it is
_(planned for M13)_. The owner confirmed these points on issue #1141 on
2026-10-24.

- **Canvas:**
  - A `form` resource opens on a Design tab, next to its Form and JSON tabs.
    The three tabs edit one draft.
  - It uses the canvas, property panel, undo and drag and drop of D65. Undo
    keeps the same 100 steps, and a reload clears it.
  - The canvas draws the draft with the site's form widget, in its sections and
    steps. Fields get placeholder values made from the field types, and a child
    collection gets placeholder rows. The Save button does nothing.
  - A step switch lists the steps in order and shows the sections of one step.
    A form without `steps` has no switch.
- **Draft metadata:**
  - The draft page metadata route of D65 also answers a draft form. It keeps
    its path, `.../resources/{id}/page-metadata`.
  - For a form it answers `{ widget, diagnostics }`. `widget` is the metadata a
    `form` widget that names this form would get.
  - Each section, field and step carries `path`, its JSON Pointer in the draft.
  - The route still sends no expressions. It compiles in memory, takes no lock,
    reads no records and answers within 500 ms.
- **Condition marks:**
  - The canvas evaluates no condition, because it has no record. Every field
    and section shows.
  - An element with `visibleWhen`, `requiredWhen` or `readOnlyWhen` carries a
    mark that names each condition and shows its text from the draft at the
    element's path.
  - Diagnostic marks work as in D65: an error-coloured border, "has
    diagnostics" in the text label, and `data-marked`.
  - Letting the author type sample values and asking the form state endpoint
    for the states is not done. It needs a tenant and a record.
- **Palette and moves:**
  - A field palette lists the entity's fields that no section lists, child
    collections included. The author drags one into a section, or picks one and
    inserts it into the selected section by keyboard.
  - The palette never offers a field already on the form, and a drop never
    copies one. A form that shows one field twice confuses users, and the
    format forbids it (`AXC0076`). Removing a field from the canvas puts it
    back in the palette.
  - The author drags fields within and between sections, and sections within
    and between steps. A section moves to another step by a drop on that step
    in the step switch. A Move to step picker does the same by keyboard.
  - Every move has a keyboard way, and a screen reader hears where the element
    lands.
  - The palette adds a section, with a unique `name` and a new title key that
    the label editor of
    [D52](d52-reference-pickers-label-texts-and-the-expression-editor.md)
    opens, and a step.
  - Adding the first step names every unnamed section and puts all sections into
    that step. A new section joins the selected step. With `steps` present
    every section needs a name and a step (`AXC0230`), so each add leaves the
    form valid. Removing the last step removes `steps`.
- **Property panel:**
  - A field shows `readOnly` and its three conditions in the expression editor,
    which offers only the fields the form lists.
  - A field that the entity marks `required` has no `visibleWhen` box
    (`AXC0226`). A computed, sequence or creator field shows `readOnly` on and
    locked.
  - A section shows its name, title and `visibleWhen`. A step shows its title.
  - The panel keeps the rules of D62.
- **Where it is used:** a form can serve several pages and tasks, so one edit
  changes all of them. The Design tab lists them in a Used on list. It is the
  Used by list of the form, filtered to the `widget form` relation (a page, at
  `/widgets/{i}/form`) and the `task form` relation (a process, at
  `/steps/{i}/form`). It comes from the reference index of D39, loads with the
  resource, and can be stale like the `usedBy` counts. Each entry opens the
  resource at its path, as a Used by entry does.
- **Width:** there is no width switch for desktop and phone in the form
  designer.

_Why:_ a canvas drawn by the site's form widget shows the author the form the
user gets, and the draft metadata route of D65 saves a second compiler in the
SPA. The canvas has no record, so marks that show the text of each condition
are enough, and they need no tenant and no form state endpoint. A palette that
offers only fields not yet on the form, and drops that never copy, keep a field
from showing twice. Naming the sections when the first step is added keeps the
form valid after every edit. Listing the pages and tasks that use a form tells
the author how far one edit reaches, with no new route, because the reference
index has the edges. The cost is a form route that answers for a second kind of
resource, which keeps one route and one canvas for both designers.
