# Process designer

Detailed reference for the Design tab of the Studio process editor. The rest of
Studio is in [studio.md](studio.md), including the process graph this designer
builds on and the page designer, whose property panel, undo and drag and drop
it reuses. The conventions of [architecture.md](../architecture.md) apply:
sections marked _(planned for Mx)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

The design follows [D74](../decisions/d74-a-visual-process-designer.md). The
Design tab, the canvas, the existing step panels, undo, Tab and Enter, and the
diagnostic marks are _(built)_. The palette, the transitions, remove, the panels
of the new step types and the live region are _(planned for M14)_.

- **Design tab** _(built)_. A process opens on a Design tab, next to its Form
  and JSON tabs. The three tabs edit one draft.
- **Canvas** _(built)_. The [process graph](studio.md) of
  [D53](../decisions/d53-typed-editors-and-the-process-graph-in-studio.md),
  drawn by React Flow and laid out by dagre, is the Design tab's canvas. The
  designer stores no position. Every edit lays the graph out again, so the
  process file holds no layout. Clicking a node selects its step. Clicking an
  edge selects the step the edge leaves. The selected step is not written to
  the URL. _(Planned for M14)_ The canvas becomes editable.
- **Palette** _(planned for M14)_. It lists the step types: `decision`, `task`, the operations
  (`updateRecord`, `createRecord`, `callConnector`, `raiseEvent`, `notify` and
  the package operations of the application's packages), `timer`,
  `waitEvent`, `subProcess` and `end`.
  - Dropping a type on the canvas adds a step with a unique name and no
    transitions. Its diagnostics mark it until the author fills it in.
  - Dropping a type on an edge inserts the step into that transition: the
    transition names the new step, and the new step's `next` names the old
    target. A type with no `next`, such as `decision`, gets the old target as
    its `otherwise`, and `end` cannot be dropped on an edge.
  - A keyboard user picks a type and inserts it after the selected step, by
    the same rule.
- **Transitions** _(planned for M14)_. Each step has a handle. Dragging from it to another step
  sets a transition. A step with one transition sets it. A step with several,
  a decision or a task, opens a small menu that lists its branches,
  `otherwise`, its outcomes, or a new branch or outcome. The panel's pickers
  set the same transitions by keyboard.
- **Remove** _(planned for M14)_. Removing a step removes it from `steps`. Removing an edge clears
  that transition. A transition left naming a missing step is `AXC0070`, and
  the graph draws no edge for it. The designer never invents a step or a
  transition.
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
- **Keyboard** _(built)_. Nodes are reached with Tab and opened with Enter, as
  on the graph. Enter selects the step and opens its card.
- **Screen readers** _(planned for M14)_. Every add, move and remove has a
  keyboard way, and a live region says what changed, such as "Step remind added
  after resolve".
- **Diagnostics** _(built)_. Marks work as on the graph: a marked node has an
  error-coloured border, "has diagnostics" in its text label, and
  `data-marked`.
- **Carried over from M13.** Pages and forms open on their Design tabs _(built)_.
  A form section moves to another step by a drop on the step switch, and the
  form designer removes a section or a step _(planned for M14)_. See
  [studio.md](studio.md) and [form-designer.md](form-designer.md).
