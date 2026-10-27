# Process designer

Detailed reference for the Design tab of the Studio process editor. The rest of
Studio is in [studio.md](studio.md), including the process graph this designer
builds on and the page designer, whose property panel, undo and drag and drop
it reuses. The conventions of [architecture.md](../architecture.md) apply:
sections marked _(planned for Mx)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

The design follows [D74](../decisions/d74-a-visual-process-designer.md). The
Design tab, the canvas, the existing step panels, remove, undo, Tab and Enter,
the diagnostic marks and the live region for removals are _(built)_. The
palette, the transitions and the live region for adds and transitions are
_(built)_. The schema-driven type picker, the timer panel, the sub-process panel, the
`notify` panel, the deadline and escalations of a task and the wait for event
panel are _(built)_. The live region for moves is _(planned for M14)_.

- **Design tab** _(built)_. A process opens on a Design tab, next to its Form
  and JSON tabs. The three tabs edit one draft.
- **Loading and redraw** _(built)_. The Design tab is the lazy chunk
  `assets/StudioProcessDesign-*.js`, which loads when the tab first opens. The
  build fails when that chunk is missing or is part of the main bundle, as the
  [bundle budget](../development.md#scripts) describes. On a 40-step process the
  canvas shows a panel edit within 2 s.
  [`studio-process-redraw.spec.ts`](../../tests/e2e/specs/studio-process-redraw.spec.ts)
  fails at 2 s or more.
- **Canvas** _(built)_. The [process graph](studio.md) of
  [D53](../decisions/d53-typed-editors-and-the-process-graph-in-studio.md),
  drawn by React Flow and laid out by dagre, is the Design tab's canvas. The
  designer stores no position. Every edit lays the graph out again, so the
  process file holds no layout. Clicking a node selects its step. Clicking an
  edge, or pressing Enter on it, selects that edge and the step it leaves. The
  selected edge is drawn in the primary colour. The selected step and edge are
  not written to the URL. The palette and the transitions edit the canvas
  _(built)_.
- **Palette** _(built)_. It sits at the top of the panel beside the canvas and
  lists the step types and operations that `process.schema.json` defines today:
  `decision`, `task`, the operations `updateRecord`, `callConnector`,
  `raiseEvent`, `createRecord` and `notify`, one "Package operation" entry,
  `subProcess`, `timer`, `waitEvent` and `end`. A new `createRecord` step starts
  with an empty `entity` and an empty `set`, a new `notify` step with an empty
  `to`, an empty `channels` and a `message` with an empty subject and body key,
  and a new `waitEvent` step with an empty `event` and `next`. A Vitest test
  reads the schema and fails when a step type or a built-in operation is
  missing from the palette.
  - The package entry adds an operation step with an empty `operation`, which
    the author types as `<package>/<operation>` in the panel, as the operation
    picker does. One entry per package operation of the application _(planned)_
    needs the names endpoint to list them.
  - A new step is added at the end of `steps`, with a unique name such as
    `updateRecord1` (the operation's name, or the type's, with the first free
    number, ignoring letter case), its required keys empty and no
    transitions. It becomes the selected step, and its diagnostics mark it
    until the author fills it in.
  - Dropping a type on the canvas adds an unconnected step.
  - Dropping a type on an edge inserts the step into that transition. The drop
    target is the edge's midpoint, a small area that does not take clicks, so
    clicking an edge still selects it. The transition names the new step, and
    the new step's `next` names the old target. A type with no `next`, such as
    `decision`, gets the old target as its `otherwise`. A `task` gets one
    outcome with an empty name and label, whose `next` is the old target, and
    diagnostics mark the empty name and label until the author fills them in.
    `end` cannot be dropped on an edge, and nothing changes.
  - A keyboard user presses a palette button, with Enter or Space, and the step
    goes in by the same rule: into the selected edge, else after the selected
    step's `next`, else unconnected. A `decision`, `task` or `end` step has no
    `next`, so a step added with one selected is unconnected. Enter on an edge
    selects it, so the keyboard reaches every transition.
  - Each add is one change of `steps`, so one Undo removes it.
- **Transitions** _(built)_. Every step except an `end` step has a handle at
  its bottom edge. Dragging from it and dropping on another step sets a
  transition. A drop anywhere on the target step counts. A step with one
  transition, such as an operation, sets its `next` at once. A step with
  several, a decision or a task, opens a small menu at the drop point. A
  decision lists its branches, `otherwise` and "New branch". A task lists its
  outcomes and "New outcome". The panel's pickers set the same transitions by
  keyboard, and the handle has no keyboard way.
  - Dropping on the step the drag started from does nothing. The panel's
    pickers can still set a transition to the same step.
  - Escape, or a click outside the menu, closes it and changes nothing.
  - "New branch" adds a branch with an empty `when` that names the target.
    "New outcome" adds an outcome with the first free name `outcome1`,
    `outcome2` and so on, an empty label key and the target. The diagnostics
    mark both until the author fills them in.
  - Each drag, or each menu choice, is one edit and so one Undo.
- **Remove** _(built)_. Removing a step removes it from `steps`. Removing an
  edge clears that transition: its target becomes empty, and its branch or
  outcome stays. A transition left naming a missing step is `AXC0070`, and the
  graph draws no edge for it. The designer never invents a step or a
  transition.
  - The selected step is removed by the card's Remove step button, or by
    Delete or Backspace in the canvas.
  - With an edge selected, the panel shows a Remove transition button above the
    card, and Delete or Backspace in the canvas clears that transition instead.
    Delete acts on the selection, not on the focused element, and a key
    pressed outside the canvas, such as in the panel's text fields, removes
    nothing.
  - One Undo reverses each removal.
- **Property panel (existing panels)** _(built)_. Selecting a node opens the
  step's card of the Form tab beside the canvas, with Move up, Move down and
  Remove. Removing the selected step clears the selection. An edit in the
  panel redraws the graph. With no step selected, the panel says "Select a step
  on the canvas."
- **Property panel (`createRecord`)** _(built)_. The operation picker offers
  `createRecord`. Its panel has an entity picker and one `set` row for each
  field of the picked entity, as the `updateRecord` panel has for the subject
  entity.
- **Property panel (`notify`)** _(built)_. The operation picker offers
  `notify`. Its panel has the recipients as rows of a user expression, a role
  picker or an email expression, with Add recipient and Remove. It has the
  `inApp` and `email` channels as two checkboxes, and the subject and body
  through the label editor, so the author writes the texts in every locale.
  `values` are rows of a typed placeholder name and an expression. An Open a
  page checkbox adds `open` with a page picker and the page's parameters as
  rows, keyed by the parameters of the picked page. Switching a step from
  `notify` to another operation removes `to`, `channels`, `message` and `open`,
  and adds no empty key. The editor takes the pointer of the object that holds
  these keys and never shows `next`, so the escalations of a task use it.
- **Property panel (task deadline and escalations)** _(built)_. A task's card
  has a Deadline section and an Escalations list, after Due in. Add deadline
  writes `deadline` with an empty `after` and `next`, and the author types the
  duration and picks the next step from the step names. Remove deadline removes
  the key. Add escalation appends an escalation with an empty `after`, and
  Remove escalation removes it. Each escalation has a Reassign switch, which
  adds `reassign` with the assignee editor of the task, and a Notify switch,
  which adds `notify` with the notify editor. Turning a switch off removes its
  key. Removing the last escalation removes `escalations`. A new escalation has
  neither key yet, so the compiler marks it until one switch is on.
- **Step type picker** _(built)_. The picker in the card and the Add step
  choice list every type in `$defs.step.properties.type.enum` of the process
  schema, including `timer` and `subProcess`. Changing a step's type removes the
  keys that the schema's conditional for the new type forbids and adds its
  required keys, empty. Keys the schema allows stay. The conditionals that also
  test the operation are left to the operation panel. A type with no panel yet
  shows its keys as "Edit in JSON" markers.
- **Property panel (timer)** _(built)_. A timer's card has a Wait or Until
  choice, a duration box for `wait`, an expression editor for `until`, and a
  Next step picker. Switching removes the other key and puts the chosen key,
  empty, in its place, and one Undo brings the old key back. A timer with
  neither key or both opens in Wait.
- **Property panel (sub-process)** _(built)_. A sub-process card has a process
  picker, then the child's inputs, then a Next step picker. A child with a
  subject entity shows a `subject` expression editor, checked against the
  parent process. Clearing it removes the key, because `subject` may be left
  out when the child's entity is the parent's. A child without a subject shows
  `MappingRows` with one row per child parameter, each mapped to an expression.
  Picking a child of the other kind removes the key that no longer applies in
  the same edit, so one Undo restores it. Picking a child without a subject
  writes every child parameter, keeping a value the draft already holds for
  that name. A child that is not in the names list changes only `process`.
- **Property panel (wait for event)** _(built)_. A wait for event card has an
  event picker that lists only the events whose entity is the process's subject
  entity, an optional `when` expression editor, an optional timeout and a Next
  step picker. The `when` editor suggests the payload fields of the picked event,
  plus rules and functions, and not the subject record's fields. Clearing it
  removes the key. Add timeout adds a `timeout` with an After duration and a
  Next step on timeout picker, both empty until filled. Remove timeout takes the
  whole `timeout` out, and one Undo brings it back.
- **Undo** _(built)_. The undo and redo of
  [D65](../decisions/d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md)
  cover every edit of a process on all three tabs, with 100 steps, cleared by
  a reload.
- **Keyboard** _(built)_. Edges and then nodes are reached with Tab, because the
  edges come first in the page. Enter selects a node's step and opens its
  card. Enter or Space on an edge selects that edge. Delete or Backspace
  removes the selection. Each edge has a name made of its two steps and its
  branch or outcome, such as "financeCheck to markApproved · otherwise".
- **Screen readers** _(partly built)_. Every add, move and remove has a
  keyboard way, and a live region says what changed. Removals, adds and
  transitions are _(built)_: a hidden live region says "Removed step remind",
  "Removed transition financeCheck to markApproved", "Step remind added after
  resolve" (or "Step remind added" for an unconnected step), "An end step cannot
  be inserted into a transition" or "submit now goes to done". A choice from the
  transition menu names its branch or outcome, such as "managerApproval ·
  approve now goes to done". Moves are _(planned for M14)_.
- **Diagnostics** _(built)_. Marks work as on the graph: a marked node has an
  error-coloured border, "has diagnostics" in its text label, and
  `data-marked`.
- **Carried over from M13.** Pages and forms open on their Design tabs _(built)_.
  A form section moves to another step by a drop on the step switch, and the
  form designer removes a section or a step _(planned for M14)_. See
  [studio.md](studio.md) and [form-designer.md](form-designer.md).
