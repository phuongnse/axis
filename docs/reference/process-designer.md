# Process designer

Detailed reference for the Design tab of the Studio process editor. The rest of
Studio is in [studio.md](studio.md), including the process graph this designer
builds on and the page designer, whose property panel, undo and drag and drop
it reuses. The conventions of [architecture.md](../architecture.md) apply:
sections marked _(planned for Mx)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md).

Everything in this file is _(planned for M14)_. The design follows
[D74](../decisions/d74-a-visual-process-designer.md).

- **Design tab.** A process opens on a Design tab, next to its Form and JSON
  tabs. The three tabs edit one draft.
- **Canvas.** The [process graph](studio.md) of
  [D53](../decisions/d53-typed-editors-and-the-process-graph-in-studio.md),
  drawn by React Flow and laid out by dagre, becomes editable. The designer
  stores no position. Every edit lays the graph out again, so the process
  file holds no layout.
- **Palette.** It lists the step types: `decision`, `task`, the operations
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
- **Transitions.** Each step has a handle. Dragging from it to another step
  sets a transition. A step with one transition sets it. A step with several,
  a decision or a task, opens a small menu that lists its branches,
  `otherwise`, its outcomes, or a new branch or outcome. The panel's pickers
  set the same transitions by keyboard.
- **Remove.** Removing a step removes it from `steps`. Removing an edge clears
  that transition. A transition left naming a missing step is `AXC0070`, and
  the graph draws no edge for it. The designer never invents a step or a
  transition.
- **Property panel.** Selecting a node opens the step's existing panel beside
  the canvas. The new step types and keys get panels too: timer, wait for
  event, sub-process, `createRecord`, `notify`, and the escalations and
  deadline of a task. An edit in the panel redraws the graph.
- **Undo.** The undo and redo of
  [D65](../decisions/d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md)
  cover every edit, with 100 steps, cleared by a reload.
- **Keyboard and screen readers.** Nodes are reached with Tab and opened with
  Enter, as on the graph. Every add, move and remove has a keyboard way, and
  a live region says what changed, such as "Step remind added after
  resolve".
- **Diagnostics.** Marks work as on the graph: a marked node has an
  error-coloured border, "has diagnostics" in its text label, and
  `data-marked`.
- **Carried over from M13.** Pages and forms open on their Design tabs, a form
  section moves to another step by a drop on the step switch, and the form
  designer removes a section or a step. See [studio.md](studio.md) and
  [form-designer.md](form-designer.md).
