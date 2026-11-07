# Process designer

The process designer is the Design tab of a `process` resource in Studio, where an author builds a
process as a graph of its steps and transitions. It sits next to the Form and JSON tabs, a process opens
on it, and the three tabs edit one draft. It reuses the property panel, the undo and the drag and drop
of the [page designer](studio.md#page-designer).

## Canvas

The canvas is the process graph: React Flow, `@xyflow/react`, draws it and dagre, `@dagrejs/dagre`,
lays it out. The designer stores no position, and every edit lays the graph out again, so the process
file holds no layout. The canvas and the step panel read the same unsaved draft, so an edit in the
panel, the Form tab or the JSON view redraws the graph at once.

- **Selecting.** Clicking a node selects its step, and the step's panel shows beside the graph. Clicking
  an edge, or pressing Enter on it, selects that edge and the step it leaves, because that step's panel
  holds the picker of the transition. The selected step's node has `aria-current`, and the selected
  edge is drawn in the primary colour.
- **Not in the address.** The selected step and edge are not written to the URL, because a path in the
  URL opens the Form tab. A path that is `/steps/{i}` or inside it opens the Form tab and also selects
  step `{i}`, so the Design tab shows it on return.
- **Names.** Each node has a text label with the step's name and type.
- **No invented parts.** A transition to a step that does not exist draws no edge, and the graph never
  invents a node.

## Loading and redraw

The Design tab is the lazy chunk `assets/StudioProcessDesign-*.js`, which loads, with React Flow and
dagre, when the tab first opens. The build fails when that chunk is missing or is part of the main
bundle, as the [bundle budget](../development.md#scripts) describes. On a 40-step process the canvas
shows a panel edit within 2 s, and
[`studio-process-redraw.spec.ts`](../../tests/e2e/specs/studio-process-redraw.spec.ts) fails at 2 s or
more.

## Palette

The palette sits at the top of the panel beside the canvas and lists every step type and built-in
operation that `process.schema.json` defines: `decision`, `task`, the operations `updateRecord`,
`callConnector`, `raiseEvent`, `createRecord` and `notify`, one Package operation entry, `subProcess`,
`timer`, `waitEvent` and `end`. A Vitest test reads the schema and fails when a step type or a built-in
operation is missing from the palette.

- **A new step.** A new step goes at the end of `steps`, with a unique name such as `updateRecord1`: the
  operation's name, or the type's, with the first free number, ignoring letter case. It has its required
  keys empty and no transitions, becomes the selected step, and its diagnostics mark it until the author
  fills it in. A new `createRecord` step starts with an empty `entity` and an empty `set`, a new `notify`
  step with an empty `to`, an empty `channels` and a `message` with an empty subject and body key, and a
  new `waitEvent` step with an empty `event` and `next`.
- **Package operation.** The entry adds an operation step with an empty `operation`, which the author
  types as `<package>/<operation>` in the panel, as the operation picker does.
- **Dropping on the canvas** adds an unconnected step.
- **Dropping on an edge** inserts the step into that transition. The drop target is the edge's midpoint,
  a small area that takes no clicks, so clicking an edge still selects it. The transition names the new
  step, and the new step's `next` names the old target. A type with no `next`, such as `decision`, gets
  the old target as its `otherwise`. A `task` gets one outcome with an empty name and label whose `next`
  is the old target, and the diagnostics mark the empty name and label until the author fills them in.
  An `end` step cannot be dropped on an edge, and nothing changes.
- **By keyboard.** A palette button pressed with Enter or Space adds its step by the same rule: into the
  selected edge, else after the selected step's `next`, else unconnected. A `decision`, `task` or `end`
  step has no `next`, so a step added while one is selected is unconnected. Enter on an edge selects it,
  so the keyboard reaches every transition.
- **One Undo.** Each add is one change of `steps`, so one Undo removes it.

## Transitions

Every step except an `end` step has a handle at its bottom edge, and dragging from it and dropping
anywhere on another step sets a transition. A step with one transition, such as an operation, sets its
`next` at once. A step with several opens a small menu at the drop point: a decision lists its branches,
`otherwise` and New branch, and a task lists its outcomes and New outcome. The handle has no keyboard
way, and the panel's pickers set the same transitions by keyboard.

- **Same step.** Dropping on the step the drag started from does nothing. The panel's pickers can still
  set a transition to the same step.
- **Closing the menu.** Escape, or a click outside the menu, closes it and changes nothing.
- **New branch and New outcome.** New branch adds a branch with the first free name `branch1`, `branch2`
  and so on, an empty `when` and the target. New outcome adds an outcome with the first free name
  `outcome1`, `outcome2` and so on, an empty label key and the target. The diagnostics mark both until
  the author fills them in.
- **One Undo.** Each drag, or each menu choice, is one edit and so one Undo.

## Remove

Removing a step removes it from `steps`, and removing an edge clears that transition: its target becomes
empty, and its branch or outcome stays. A transition left naming a missing step is `AXC0070`, and the
graph draws no edge for it. The designer never invents a step or a transition, and one Undo reverses each
removal.

- **A step** is removed by its panel's Remove step button, or by Delete or Backspace in the canvas.
  Removing the selected step clears the selection.
- **An edge.** With an edge selected, the panel shows a Remove transition button above the step's panel,
  and Delete or Backspace in the canvas clears that transition instead of removing the step.
- **The selection, not the focus.** Delete acts on the selection, not on the focused element, and a key
  pressed outside the canvas, such as in the panel's text fields, removes nothing.

## Step panel

Selecting a node opens the step's card of the Form tab beside the canvas, with Move up, Move down and
Remove. Each move is one change of `steps`, so one Undo reverses it, and an edit in the panel redraws the
graph. With no step selected, the panel says "Select a step on the canvas."

The type picker in the panel and the Add step choice list every type in
`$defs.step.properties.type.enum` of the process schema. Changing a step's type removes the keys that the
schema's conditional for the new type forbids and adds its required keys, empty, while keys that the
schema allows stay. The conditionals that also test the operation are left to the operation panel. A
type with no panel shows its keys as "Edit in JSON" markers.

The step types and operations below have a panel of their own, beside the operation panels that
[Studio](studio.md) describes for the Form tab.

### createRecord

The operation picker offers `createRecord`. Its panel has an entity picker and one `set` row for each
field of the picked entity, as the `updateRecord` panel has for the subject entity.

### Notify

The operation picker offers `notify`. Its recipients are rows of a user expression, a role picker or an
email expression, with Add recipient and Remove. Its `inApp` and `email` channels are two checkboxes, and
its subject and body go through the label editor, so the author writes the texts in every locale.
`values` are rows of a typed placeholder name and an expression. An Open a page checkbox adds `open` with
a page picker and the page's parameters as rows, keyed by the parameters of the picked page.

Switching a step from `notify` to another operation removes `to`, `channels`, `message` and `open`, and
adds no empty key. The editor takes the pointer of the object that holds these keys and never shows
`next`, so the escalations of a task use it too.

### Connector input

The `input` of a `callConnector` step is rows of a typed input name, a Value, Object or List switch and an
expression. The names route lists no input types, so the panel reads a row's kind from the draft: text
is a Value, an object with `from` a List, and any other object an Object.

- **Object.** An `object` shows nested field rows, each a typed field name and an expression over the
  subject.
- **List.** A `list` shows a From picker of the subject's child collections, then nested field rows with
  an expression over a row of that collection. The expression editor offers that child entity's fields
  and `id`, not the subject's.
- **Nesting.** Nested rows nest further. The switch writes `{ fields }`, `{ from, fields }` or an empty
  expression and keeps the nested `fields`, and the compiler reports what is missing.
- **Package operations.** A package operation's `input` stays scalar rows, because package fields stay
  scalar.

### Task deadline and escalations

A task's panel has a Deadline section and an Escalations list, after Due in. Add deadline writes `deadline` with an empty `after` and `next`, and the author types the duration and
picks the next step from the step names. Remove deadline removes the key.

Add escalation appends an escalation with the first free name `escalation1`, `escalation2` and so on, and
an empty `after`, and Remove escalation removes it. Each escalation has a Reassign switch, which adds
`reassign` with the task's assignee editor, and a Notify switch, which adds `notify` with the
[notify](#notify) editor. Turning a switch off removes its key, and removing the last escalation removes
`escalations`. A new escalation has neither key yet, so the compiler marks it until one switch is on.

### Timer

A timer's panel has a Wait or Until choice, a duration box for `wait`, an expression editor for `until`,
and a Next step picker. Switching removes the other key and puts the chosen key, empty, in its place, and
one Undo brings the old key back. A timer with neither key or both opens in Wait.

### Sub-process

A sub-process's panel has a process picker, then the child's inputs, then a Next step picker. A child
process with a subject entity shows a `subject` expression editor, checked against the parent
process. Clearing it removes the key, because `subject` may be left out when the child's entity is the
parent's. A child without a subject shows `MappingRows`, with one row per child parameter, each mapped to
an expression.

Picking a child of the other kind removes the key that no longer applies in the same edit, so one Undo
restores it. Picking a child without a subject writes every child parameter, keeping a value that the
draft already holds for that name. A child that is not in the names list changes only `process`.

### Wait for event

A wait for event panel has an event picker, an optional `when` expression editor, an optional timeout
and a Next step picker. The event picker lists only the events whose entity is the process's subject entity. The `when` editor
suggests the payload fields of the picked event, with rules and functions, and not the subject record's
fields, and clearing it removes the key. Add timeout adds a `timeout` with an After duration and a Next
step on timeout picker, both empty until filled. Remove timeout takes the whole `timeout` out, and one
Undo brings it back.

## Undo

The page designer's undo and redo cover every edit of a process on all three tabs, with 100 steps,
cleared by a reload, see [undo](studio.md#page-designer). They do not apply to a module process.

## Keyboard and screen readers

Every add, move and remove has a keyboard way, and a hidden live region says what changed.

- **Tab order.** Edges and then nodes are reached with Tab, because the edges come first in the page,
  and nodes in step order. Enter selects a node's step and opens its panel, and Enter or Space on an edge
  selects that edge. Delete or Backspace removes the selection.
- **Edge names.** Each edge has a name made of its two steps and its branch or outcome.
- **What the live region says.** "Removed step remind", "Removed transition financeCheck to
  markApproved", "Step remind added after resolve", or "Step remind added" for an unconnected step, "An
  end step cannot be inserted into a transition", and "submit now goes to done". A choice from the
  transition menu also names its branch or outcome. A move names the step now before the moved step, such
  as "Step financeCheck moved after submit", and a step moved to the first place in `steps` says "Step
  financeCheck moved to the start". That place is the top of the list, not the process's `start` step.
- **Parts of a name join with a comma** _(planned for M31)_. A node's name and type, an edge's steps and
  its branch or outcome, and a transition menu's choice join as peers, as in "financeCheck to
  markApproved, otherwise" and "managerApproval, approve now goes to done", see
  [text](actions-and-controls.md#text).

## Diagnostics

A step of the open process is marked when a diagnostic of the open resource names its path, `/steps/{i}`
or a path inside it. A marked node has an error-coloured border, "has diagnostics" in its text label, and
`data-marked`. The marks come from the Problems panel's last loaded list, so they change on a load, a
save or a request, not on every edit.

## Module processes

A module process, such as `checklists/ChecklistReview`, opens in this designer with the application's
[customization layer](customization-layers.md#in-studio) applied, through the module editor that module
entities use. It shows only the Design view, with no Form tab, JSON tab, Undo, Delete or Used by.

- **Marks.** A step that the layer added or changed is marked Added or Changed on its graph node, in the
  node's accessible name and on its panel, which also has Reset to module. A change inside a branch,
  outcome or escalation marks the step that holds it as Changed. A step that the layer removed is a muted
  row under the graph, marked Removed, with Reset to module. Reset to module is off while the draft has
  unsaved edits.
- **Save.** Save writes the layer file, and then a Customized tag and Reset all show in the header.
