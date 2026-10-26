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
palette and the live region for adds are _(built)_. The transitions, the
panels of the new step types and the live region for moves are _(planned for
M14)_.

- **Design tab** _(built)_. A process opens on a Design tab, next to its Form
  and JSON tabs. The three tabs edit one draft.
- **Canvas** _(built)_. The [process graph](studio.md) of
  [D53](../decisions/d53-typed-editors-and-the-process-graph-in-studio.md),
  drawn by React Flow and laid out by dagre, is the Design tab's canvas. The
  designer stores no position. Every edit lays the graph out again, so the
  process file holds no layout. Clicking a node selects its step. Clicking an
  edge, or pressing Enter on it, selects that edge and the step it leaves. The
  selected edge is drawn in the primary colour. The selected step and edge are
  not written to the URL. The palette edits the canvas _(built)_.
  _(Planned for M14)_ Dragging transitions edits it too.
- **Palette** _(built)_. It sits at the top of the panel beside the canvas and
  lists the step types and operations that `process.schema.json` defines today:
  `decision`, `task`, the operations `updateRecord`, `callConnector` and
  `raiseEvent`, one "Package operation" entry, `subProcess` and `end`. A
  Vitest test reads the schema and fails when a step type or a built-in
  operation is missing from the palette. `timer`, `waitEvent`, `createRecord`
  and `notify` join when the schema defines them, because their steps would
  always be invalid before that.
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
- **Transitions** _(planned for M14)_. Each step has a handle. Dragging from it to another step
  sets a transition. A step with one transition sets it. A step with several,
  a decision or a task, opens a small menu that lists its branches,
  `otherwise`, its outcomes, or a new branch or outcome. The panel's pickers
  set the same transitions by keyboard.
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
- **Property panel (new step types)** _(planned for M14)_. The new step types
  and keys get panels too: timer, wait for event, sub-process, `createRecord`,
  `notify`, and the escalations and deadline of a task.
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
  keyboard way, and a live region says what changed. Removals and adds are
  _(built)_: a hidden live region says "Removed step remind", "Removed
  transition financeCheck to markApproved", "Step remind added after resolve"
  (or "Step remind added" for an unconnected step), or "An end step cannot be
  inserted into a transition". Moves are _(planned for M14)_.
- **Diagnostics** _(built)_. Marks work as on the graph: a marked node has an
  error-coloured border, "has diagnostics" in its text label, and
  `data-marked`.
- **Carried over from M13.** Pages and forms open on their Design tabs _(built)_.
  A form section moves to another step by a drop on the step switch, and the
  form designer removes a section or a step _(planned for M14)_. See
  [studio.md](studio.md) and [form-designer.md](form-designer.md).
