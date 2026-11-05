# Form designer

The form designer is the Design tab of a `form` resource in Studio, where an author lays out a form by
drag and drop. It sits next to the Form and JSON tabs, a form opens on it, and the three tabs edit one
draft. It reuses the canvas, the property panel, the undo and the `@dnd-kit` drag and drop of the page
designer, whose undo covers every edit of the form on all three tabs, with 100 steps, cleared by a
reload. The [page designer](studio.md#page-designer) and the rest of Studio are in [Studio](studio.md).

## Canvas

The canvas draws the unsaved draft with the site's form widget, in its sections and steps, so an author
sees the form as a user will. Above it sit the [Used on](#used-on) list and the [step switch](#step-switch),
and beside it the field palette, the layout outline and the [property panel](#property-panel). It has no
width switch.

- **Placeholder data.** The canvas opens a placeholder record, so each field shows a placeholder value
  made from its type and a child collection shows placeholder rows, and no request goes to `/api/apps/`.
- **Drawn from the server.** The canvas asks [the draft page metadata route](draft-page-metadata.md)
  300 ms after the last edit. For a form it answers the metadata of a form widget, with a `path` on
  each section, field and step. A form the route cannot draw shows a short notice.
- **Nothing runs.** Save, Next, Back and Cancel do nothing, because the canvas cancels every click and
  form submit. A click only selects.
- **Loaded when needed.** The canvas is mounted only while the Design tab is active. The tab's code is
  the lazy chunk `StudioFormDesign`, which loads when the tab first opens.
- **Many steps warn.** A form with more than six steps shows a warning above the canvas, which never
  blocks a save.

## Step switch

The step switch shows one step of a form with `steps` at a time. It is a segmented control above the
canvas, with the step titles in order, and the canvas shows the sections of the chosen step. A step
that a diagnostic names is marked on its entry. The form widget's wizard bar and its Next and Back
buttons still draw, but do nothing. A form without `steps` has no switch.

## Conditions and marks

The canvas has no record, so it evaluates no condition and shows every field and section. An element
with `visibleWhen`, `requiredWhen` or `readOnlyWhen` carries a mark that names each condition and shows
its text from the draft, such as "Visible when: status == 'resolved'".

A section or field that a diagnostic of the form names is marked as in the page designer: a frame with
an error-coloured border, "has diagnostics" in its text label, and `data-marked`.

## Field palette

The field palette lists the entity's fields that no section lists, child collections included, so an
author places each field once. It sits in the panel column above the layout outline and the property
panel, with Add section and Add step below the field list. The names come from Studio's names list, so
an unsaved edit of the entity shows only after a save. The palette never offers a field already on the
form, and a drop never copies one.

- **Insert by keyboard or click.** Enter, Space or a click inserts a field at the end of the selected
  section, after the selected field, or at the end of the selected step's first section. With no usable
  selection it goes to the end of the first section. The new field is selected, and a live region names
  it, its section and its position.
- **Drag onto the outline.** A pointer drag from the palette onto a section row of the outline appends
  the field to that section, and onto a field row inserts it after that field. The innermost row wins.
  A section with no fields shows a "Drop a field here" area in the outline, where a palette field can
  land.
- **Remove field.** The Remove field button in a field's property panel takes the field out of its
  section. The field returns to the palette, and the section stays selected. A live region says
  "Removed" with the field and its section. A field with no name is named by its pointer, such as
  `/sections/0/fields/0`. One Undo puts the field back. Conditions that name the removed field are not
  rewritten, and the diagnostics flag them.
- **The announcement joins with a comma** _(planned for M31)_. The field and its section join as peers,
  as in "Removed priority, Section 2", see [text](actions-and-controls.md#text).

## Layout outline

The layout outline lists the steps, sections and fields of the draft, and a click selects a row. A
section is named by its `name`, or `Section N`, because its title loads only inside the canvas.

## Moves

Every move goes through the layout outline or the step switch, by a pointer drag or by the keyboard, and
a live region says where the element lands. A move is one change of the draft, so one Undo reverses it,
and the moved element keeps all its keys and stays selected.

- **Fields.** A field row moves within its section or into another one. On the keyboard, Space picks the
  focused row up, the arrow keys move it, Space drops it and Escape cancels. The moved field takes the
  place of the field it is dropped on, so in another section it lands just before that field. An empty
  section takes it too, and a section that has fields is not a target. The live region names the field,
  its section and its position.
- **Sections.** A section row moves the same way, by a pointer drag from its title line or by the
  keyboard. It takes the place of the section row it is dropped on, and a drop on another step's heading
  in the outline, or on a step in the step switch, appends it to that step. On a form with steps, the
  section's property panel also has a Move to step picker, which appends the section to the chosen step.
  The live region names the section, its step and its position.
- **Targets.** While a section moves, field rows are not targets, and while a field moves, step headings
  and the switch's entries are not. While a section moves, the switch's entries show an outline under
  the pointer. A section that no step lists is not a target, and a section without a `name` cannot move
  between steps.
- **Order.** On a form with steps, `sections` follows the step order after a move: the first step's
  sections, then the second step's and so on, then the sections that no step lists.

## Sections and steps

The palette's Add section and Add step build the form's structure, and each section's and step's panel
can remove it. Each add or remove is one Undo step, and a live region names what was added, or says
"Removed" with the section's name or the step's title.

- **Add section** appends a section named with the lowest unused `section{n}`, ignoring letter case, and
  a new title key, such as `forms.ticketResolution.sections.1.title`, which its panel's label editor
  writes. The new section is selected. It joins the selected step, the step of the selected section or
  field, or else the last step.
- **Add step.** The first Add step names every unnamed section and puts all sections into one step, so
  the form stays valid (`AXC0230`). A later Add step adds an empty, selected step. Each new step, the
  first one included, gets the first free name `step1`, `step2` and so on, ignoring letter case.
- **A new part draws once it holds something.** Until a field goes into an empty section or step and its
  texts are written, the canvas shows its cannot-be-drawn notice.
- **Remove section,** in a section's panel, takes it out of `sections` and of every step that lists it,
  and its fields return to the palette. The step that listed it is selected. Conditions that name the
  removed section's fields are not rewritten.
- **Remove step,** in a step's panel, keeps the step's sections in `sections`. No step then lists them,
  which the diagnostics flag (`AXC0230`), and nothing stays selected. Removing the last step removes
  `steps`, so the form goes back to one page.

## Property panel

The property panel edits the selected element. A click, Enter or Space selects a field or a section,
and a click on a step selects it and shows its sections. The selected element has `aria-current` and an
outline in the primary colour. With nothing selected, the panel asks the author to select something.
The selection is kept across the Design, Form and JSON tabs while the resource stays open.

| Element | The panel shows                                                                                                                                                                                                 |
| ------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Field   | `readOnly`, its three conditions in the expression editor, its `width` and a Remove field button. The width is a whole number from 1 to 12, and the canvas draws the fields in their [columns](form-widget.md). |
| Section | Its name, title and `visibleWhen`, a Remove section button and, on a form with steps, the Move to step picker                                                                                                   |
| Step    | Its title and a Remove step button                                                                                                                                                                              |

- **Conditions offer the form's fields.** The expression editor of a condition offers only the fields
  that the form lists, because a condition reads only those.
- **Entity flags.** A field that the entity marks `required` has no `visibleWhen` box, even when it
  already has a `visibleWhen`, which the author removes in the Form or JSON tab. A computed, sequence or creator field
  shows `readOnly` on and locked. The panel reads these flags from the canvas's latest answer, so it
  shows nothing about a field until the form is drawn.
- **Section name.** The Name box applies on Enter or when it loses focus. It renames the step entries
  that list the section in the same edit, so one Undo reverses it. It refuses an empty name and another
  section's name, ignoring letter case, and a message under the box says why.

## Used on

The Used on list above the canvas shows the pages and process steps that use the form, because one edit
changes every one of them. It is the Used by list filtered to the `widget form` and `task form`
relations, and shows each entry's relation, name and path. It loads once when the form opens, so it can
be stale like the `usedBy` counts. An entry opens the resource at its path.

## Module forms

A form of a module opens in this designer with the application's layer applied, see
[customization layers](customization-layers.md#in-studio). It has no Form or JSON tab and no Used on
list. The canvas asks the [module form draft route](draft-page-metadata.md), so a moved or added field
shows before Save.

The outline rows carry the layer's marks. A step or a section, keyed by `name`, and a field, keyed by
`field`, shows an Added or Changed tag with Reset to module. A removed field is a muted row inside its
section's row, and a removed section or step is at the end of the outline. Reset to module is off while
the draft has unsaved edits.
