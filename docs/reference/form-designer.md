# Form designer

Detailed reference for the Design tab of the Studio form editor. The rest of Studio is in
[studio.md](studio.md), including the page designer, whose canvas, property panel, undo and drag and
drop this designer reuses. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet, and
Dn refers to [decisions.md](../decisions.md).

- **Form designer** _(partly built, planned for M13)_. The form editor gets a Design tab that
  lays out a form by drag and drop, per
  [D66](../decisions/d66-a-visual-form-designer-for-form-resources.md). It sits
  next to the Form and JSON tabs, is the default tab, and the three tabs edit
  one draft. It reuses the canvas, property panel, undo (100 steps, cleared by
  a reload; built for forms) and `@dnd-kit` drag and drop of the page designer.
  - **Canvas.** It draws the unsaved draft with the site's form widget, in its
    sections and steps. Fields get placeholder values made from the field
    types, and a child collection gets placeholder rows. The canvas asks
    [the draft page metadata route](authoring.md#draft-page-metadata) 300 ms
    after the last edit. For a form it answers the metadata of a form widget,
    with a `path` on each section, field and step.
    _Partly built:_ the Design tab is there, and Form stays the default tab
    until the designer is complete. The canvas opens the placeholder record, so
    each field shows a placeholder value, and no request goes to `/api/apps/`.
    Save, Next, Back and Cancel do nothing, because the canvas cancels every
    click and form submit. A click only selects. A form the route cannot draw
    shows a short notice. The tab shows the Used on list above the step switch
    and the canvas, with the field palette, the layout outline and a property
    panel beside the canvas, and no width switch. The canvas is mounted only while the Design tab is active.
    The tab's code is the lazy chunk `StudioFormDesign`, which loads when the tab first opens.
  - **Step switch.** It lists the steps in order and shows the sections of one
    step. A form without `steps` has no switch.
    _Partly built:_ the switch is a segmented control above the canvas, with
    the step titles in order. A step that a diagnostic names is marked on its
    entry. The wizard bar and the Next and Back buttons of the form widget still
    draw, but do nothing.
  - **Condition marks.** No condition is evaluated, because the canvas has no
    record. Every field and section shows. An element with `visibleWhen`,
    `requiredWhen` or `readOnlyWhen` carries a mark that names each condition
    and shows its text from the draft. A diagnostic of the form marks an
    element as in the page designer: an error-coloured border, "has
    diagnostics" in the text label, and `data-marked`. _Partly built:_ a
    section or field has a frame with that border, label and `data-marked`. A
    mark shows the name of each condition and its text, such as "Visible when:
    status == 'resolved'".
  - **Field palette.** It lists the entity's fields that no section lists,
    child collections included. The author drags one into a section, or picks
    one and inserts it into the selected section by keyboard. Removing a field
    puts it back in the palette. The palette never offers a field already on
    the form, and a drop never copies one.
    _Partly built:_ the panel column lists the fields above the layout outline
    and the property panel. Add section and Add step sit below the field list.
    The names come from Studio's names list, so an unsaved edit of the entity
    shows only after a save. Enter, Space or a click
    inserts a field at the end of the selected section, after the selected
    field, or at the end of the selected step's first section. With no usable
    selection it goes to the end of the first section. The new field is
    selected, and a live region names it, its section and its position. A
    section is named by its `name`, or `Section N`, because its title loads
    only inside the canvas. The outline lists the steps, sections and fields of
    the draft. A click selects a row. A pointer drag from the palette onto a
    section row appends to it, and onto a field row inserts after it. The
    innermost row wins. The Remove field button in a field's property panel
    takes the field out of its section. The field returns to this palette,
    and the section stays selected. A live region says "Removed" with the field
    and its section, such as "Removed priority · Section 2". A field with no
    name is named by its pointer, such as `/sections/0/fields/0`. One undo puts
    the field back. Conditions that name the removed field are not rewritten.
    The diagnostics flag them.
  - **Moves.** The author drags fields within and between sections, and
    sections within and between steps. To move a section to another step, drop
    it on that step in the switch, or use the Move to step picker. Every move
    has a keyboard way, and a screen reader hears where the element lands.
    _Partly built:_ a field row of the layout outline moves within its section
    or into another one, by a pointer drag, or by the keyboard: Space picks the
    focused row up, the arrow keys move it, Space drops it and Escape cancels.
    The moved field takes the place of the field it is dropped on, so in
    another section it lands just before that field. An empty section takes it
    too, and a section that has fields is not a target. The field keeps all its
    keys and stays selected, and the live region names the field, its section
    and its position. A section row moves the same way: by a pointer drag from
    its title line, or by the keyboard. It takes the place of the section row it
    is dropped on, and a drop on another step's heading in the outline appends
    it to that step. While a section moves, field rows are not targets, and
    while a field moves, step headings are not. The section property panel of a
    form with steps has a Move to step picker, which appends the section to the
    chosen step. On a form with steps, `sections` follows the step order after a
    move: the first step's sections, then the second step's and so on, then the
    sections that no step lists. A section that no step lists is not a target,
    and a section without a `name` cannot move between steps. The live region
    names the section, its step and its position, and the section stays
    selected. A move is one change of the draft, so one undo reverses it. A drop
    on a step in the step switch is not built yet.
  - **Sections and steps.** The palette adds a section, with a unique `name`
    and a new title key that the label editor opens, and a step. A new section
    joins the selected step. Adding the first step names every unnamed section
    and puts all sections into it, so the form stays valid (`AXC0230`).
    Removing the last step removes `steps`.
    _Partly built:_ the palette has Add section and Add step below the field
    list. Add section appends a section named with the lowest unused
    `section{n}`, ignoring letter case, and a title key such as
    `forms.ticketResolution.sections.1.title`. The new section is selected. It
    joins the selected step, the step of the selected section or field, or the
    last step. The first Add step names every unnamed section and puts all
    sections into one step. A later Add step adds an empty, selected step. A
    section with no fields shows a "Drop a field here" area in the outline,
    where a palette field can land. Each add is one undo step, and a live
    region names what was added. Until a field goes into an empty section or
    step and the texts are written, the canvas shows its cannot-be-drawn
    notice. Removing a section or a step is not built yet.
  - **Property panel.** A field shows `readOnly` and its three conditions in
    the expression editor, which offers only the fields the form lists. A field
    that the entity marks `required` has no `visibleWhen` box. A computed,
    sequence or creator field shows `readOnly` on and locked. A field also has
    a Remove field button below its conditions, described in the field palette
    bullet. A section shows its title and `visibleWhen`. A step shows its title.
    _Partly built:_ a click, Enter or Space selects a field or a section. A
    click on a step selects it and shows its sections. The selected element has
    `aria-current` and an outline in the primary colour. The panel asks the
    author to select something when nothing is selected. The selection is kept
    across the Design, Form and JSON tabs while the resource stays open. The
    panel reads the entity flags from the canvas's latest answer, so it shows
    nothing about a field until the form is drawn. A required field that
    already has a `visibleWhen` shows no box for it. Remove it in the Form or
    JSON tab.
  - **Used on.** A list of the pages and process steps that use the form. It is
    the Used by list filtered to the `widget form` and `task form` relations,
    loads with the resource, and can be stale like the `usedBy` counts. An
    entry opens the resource at its path. One edit changes every one of them.
    _Built:_ the list sits above the canvas, loads once when the form opens,
    and shows each entry's relation, name and path.
